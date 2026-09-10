using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Common.Database.Models;
using Common.Resources.Config;
using Common.Resources.Xml;
using Common.Utilities;
using Dapper;
using Npgsql;

namespace Common.Database;

public static class DbClient {
    private const int MaxAccountsPerIp = 3000;

    public static NpgsqlDataSource DataSource;

    public static void Load() {
        var pg = PostgresConfig.Config;
        DataSource = NpgsqlDataSource.Create(pg.ConnectionString);

        using (var conn = DataSource.OpenConnection()) {
            var schemaPath = Path.Combine(AppContext.BaseDirectory, "Database/Schema.sql");
            using var cmd = new NpgsqlCommand(File.ReadAllText(schemaPath), conn);
            cmd.ExecuteNonQuery();
        }

        AccountLockManager.Init(RedisConfig.Config.ConnectionString);

        DbWriter<Account>.Init(UpsertAccountAsync);
        DbWriter<Login>.Init(UpsertLoginAsync);
        DbWriter<Guild>.Init(UpsertGuildAsync);
        DbWriter<MuteRecord>.Init(UpsertMuteAsync);
        DbWriter<BanRecord>.Init(UpsertBanAsync);
    }

    public static async Task<NpgsqlConnection> OpenConnectionAsync() => await DataSource.OpenConnectionAsync();

    public static async Task FlushAsync<T>(T model) where T : class {
        await DbWriter<T>.WriteAsync(model);
    }

    public static async Task Dispose() {
        await DbWriter<Account>.StopAsync();
        await DbWriter<Login>.StopAsync();
        await DbWriter<Guild>.StopAsync();
        await DbWriter<MuteRecord>.StopAsync();
        await DbWriter<BanRecord>.StopAsync();
        await DataSource.DisposeAsync();
    }

    #region Per-type upsert (used by DbWriter<T> - one INSERT-or-UPDATE per item,
    // batched together into one transaction per flush by DbWriter itself)

    private static async Task UpsertAccountAsync(NpgsqlConnection conn, Account acc) {
        // New account: insert a placeholder row first so we get a real id back,
        // then overwrite it below with the full, now-correctly-id'd payload.
        if (acc.Id == 0) {
            acc.Id = await conn.QuerySingleAsync<int>(
                "INSERT INTO accounts (name, guild_id, data) VALUES (@Name, @GuildId, '{}'::jsonb) RETURNING id",
                new { acc.Name, acc.GuildId });
        }

        await conn.ExecuteAsync(
            "UPDATE accounts SET name=@Name, guild_id=@GuildId, data=@Data::jsonb WHERE id=@Id",
            new { acc.Name, acc.GuildId, Data = JsonSerializer.Serialize(acc), acc.Id });
    }

    private static async Task UpsertLoginAsync(NpgsqlConnection conn, Login login) {
        if (login.Id == 0) {
            login.Id = await conn.QuerySingleAsync<int>(
                "INSERT INTO logins (name, password_hash, password_salt, ip_address, last_login_at) " +
                "VALUES (@Name, @PasswordHash, @PasswordSalt, @IpAddress, @LastLoginAt) RETURNING id",
                login);
        }
        else {
            await conn.ExecuteAsync(
                "UPDATE logins SET name=@Name, password_hash=@PasswordHash, password_salt=@PasswordSalt, " +
                "ip_address=@IpAddress, last_login_at=@LastLoginAt WHERE id=@Id",
                login);
        }
    }

    private static async Task UpsertGuildAsync(NpgsqlConnection conn, Guild guild) {
        if (guild.Id == 0) {
            guild.Id = await conn.QuerySingleAsync<int>(
                "INSERT INTO guilds (name, level, current_fame, total_fame, guild_board, created_at) " +
                "VALUES (@Name, @Level, @CurrentFame, @TotalFame, @GuildBoard, @CreatedAt) RETURNING id",
                guild);
        }
        else {
            await conn.ExecuteAsync(
                "UPDATE guilds SET name=@Name, level=@Level, current_fame=@CurrentFame, " +
                "total_fame=@TotalFame, guild_board=@GuildBoard WHERE id=@Id",
                guild);
        }
    }

    private static async Task UpsertBanAsync(NpgsqlConnection conn, BanRecord ban) {
        if (ban.Id == 0) {
            ban.Id = await conn.QuerySingleAsync<int>(
                "INSERT INTO bans (target_acc_id, moderator_acc_id, reason, created_at, expires_at, permanent) " +
                "VALUES (@TargetAccId, @ModeratorAccId, @Reason, @CreatedAt, @ExpiresAt, @Permanent) RETURNING id",
                ban);
        }
        else {
            await conn.ExecuteAsync(
                "UPDATE bans SET target_acc_id=@TargetAccId, moderator_acc_id=@ModeratorAccId, " +
                "reason=@Reason, expires_at=@ExpiresAt, permanent=@Permanent WHERE id=@Id",
                ban);
        }
    }

    private static async Task UpsertMuteAsync(NpgsqlConnection conn, MuteRecord mute) {
        if (mute.Id == 0) {
            mute.Id = await conn.QuerySingleAsync<int>(
                "INSERT INTO mutes (target_acc_id, moderator_acc_id, reason, created_at, expires_at) " +
                "VALUES (@TargetAccId, @ModeratorAccId, @Reason, @CreatedAt, @ExpiresAt) RETURNING id",
                mute);
        }
        else {
            await conn.ExecuteAsync(
                "UPDATE mutes SET target_acc_id=@TargetAccId, moderator_acc_id=@ModeratorAccId, " +
                "reason=@Reason, expires_at=@ExpiresAt WHERE id=@Id",
                mute);
        }
    }

    #endregion

    private static async Task<Account> GetAccountByIdAsync(NpgsqlConnection conn, int id) {
        var data = await conn.ExecuteScalarAsync<string>("SELECT data FROM accounts WHERE id=@Id", new { Id = id });
        return data == null ? null : JsonSerializer.Deserialize<Account>(data);
    }

    private static async Task<Account> GetAccountByNameAsync(NpgsqlConnection conn, string name) {
        var data = await conn.ExecuteScalarAsync<string>("SELECT data FROM accounts WHERE name=@Name", new { Name = name });
        return data == null ? null : JsonSerializer.Deserialize<Account>(data);
    }

    public static bool IsValidUsername(string name) {
        return !string.IsNullOrWhiteSpace(name) && name.Length > 0 && name.Length < 11 && name.All(char.IsLetter);
    }

    public static bool IsValidPassword(string password) {
        return !string.IsNullOrWhiteSpace(password) && password.Length > 8;
    }

    public static async Task<RegisterStatus> RegisterAsync(string username, string password, string ip) {
        if (!IsValidUsername(username))
            return RegisterStatus.InvalidName;
        if (!IsValidPassword(password))
            return RegisterStatus.InvalidPassword;

        var status = RegisterStatus.Success;
        var lowerName = username.ToLower();

        await using var conn = await OpenConnectionAsync();

        var nameExists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM logins WHERE name=@Name)", new { Name = lowerName });

        if (nameExists) {
            status = RegisterStatus.NameInUse;
        }
        else {
            var ipCount = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM logins WHERE ip_address=@Ip", new { Ip = ip });
            if (ipCount >= MaxAccountsPerIp)
                status = RegisterStatus.MaxAccountsReached;
        }

        if (status == RegisterStatus.Success) {
            // Used for password encryption
            var salt = MathUtils.GenerateSalt();

            var acc = new Account {
                Name = username,
                MaxChars = NewAccountsConfig.Config.MaxChars,
                VaultCount = NewAccountsConfig.Config.VaultCount,
                Stats = new AccountStats {
                    CurrentCredits = NewAccountsConfig.Config.Credits,
                    TotalCredits = NewAccountsConfig.Config.Credits,
                    CurrentFame = NewAccountsConfig.Config.Fame,
                    TotalFame = NewAccountsConfig.Config.Fame,
                    ClassStats = NewAccountsConfig.CreateClassStats()
                }
            };
            var login = new Login {
                Name = lowerName, IpAddress = ip, PasswordHash = (password + salt).ToSHA1(),
                PasswordSalt = salt
            };

            await FlushAsync(acc);
            await FlushAsync(login);
        }

        return status;
    }

    public static async Task<(Account Acc, VerifyStatus Status)> VerifyAccount(string username, string password, Guid gameServerGuid) {
        await using var conn = await OpenConnectionAsync();

        var login = await conn.QueryFirstOrDefaultAsync<Login>(
            "SELECT id AS Id, name AS Name, password_hash AS PasswordHash, password_salt AS PasswordSalt, " +
            "ip_address AS IpAddress, last_login_at AS LastLoginAt FROM logins WHERE name=@Username",
            new { Username = username });
        if (login == null)
            return (null, VerifyStatus.InvalidCredentials);

        var hash = (password + login.PasswordSalt).ToSHA1();
        if (login.PasswordHash != hash)
            return (null, VerifyStatus.InvalidCredentials);

        var acc = await GetAccountByNameAsync(conn, login.Name);
        if (acc == null)
            return (null, VerifyStatus.InternalError);

        // Atomic acquire-or-verify-already-mine against Redis - replaces the old
        // Account.LockOwner field entirely (that mutation was never actually
        // persisted, so the old in-DB duplicate-login check didn't reliably work).
        if (!await AccountLockManager.TryAcquireAsync(acc.Id, gameServerGuid))
            return (null, VerifyStatus.AccountInUse);

        return (acc, VerifyStatus.Success);
    }

    public static async Task<(Character Char, CreateCharacterStatus Status)> CreateCharacterAsync(Account acc, ushort objectType, ushort skinType) {
        Character chr = null;
        var status = CreateCharacterStatus.Success;

        if (acc == null) {
            status = CreateCharacterStatus.InternalError;
        }
        else if (acc.Characters.Count >= acc.MaxChars) {
            status = CreateCharacterStatus.MaxCharactersReached;
        }
        else if (skinType != 0 && !acc.OwnedSkins.Contains(skinType)) {
            status = CreateCharacterStatus.SkinNotOwned;
        }
        else // Success, create character here
        {
            var charId = acc.NextCharId;
            var classDesc = XmlLibrary.PlayerDescs[objectType];
            chr = new Character {
                CharId = charId,
                XpPoints = NewCharsConfig.Config.Experience,
                Level = NewCharsConfig.Config.Level,
                ObjectType = objectType,
                ItemTypes = Enumerable.Repeat(-1, 20).ToArray(),
                ItemDatas = Enumerable.Repeat((byte)0, 20).ToArray(),
                TextureOne = (ushort)NewCharsConfig.Config.Tex1,
                TextureTwo = (ushort)NewCharsConfig.Config.Tex2,
                SkinType = skinType,
                HealthPotions = NewCharsConfig.Config.HealthPotions,
                MagicPotions = NewCharsConfig.Config.MagicPotions,
                HasBackpack = NewCharsConfig.Config.HasBackpack,
                Stats = new CharacterStats {
                    Hp = classDesc.Stats[StatType.MaxHP].StartValue,
                    MaxHp = classDesc.Stats[StatType.MaxHP].StartValue,
                    Mp = classDesc.Stats[StatType.MaxMP].StartValue,
                    MaxMp = classDesc.Stats[StatType.MaxMP].StartValue,
                    Attack = classDesc.Stats[StatType.Attack].StartValue,
                    Defense = classDesc.Stats[StatType.Defense].StartValue,
                    Speed = classDesc.Stats[StatType.Speed].StartValue,
                    Dexterity = classDesc.Stats[StatType.Dexterity].StartValue,
                    Vitality = classDesc.Stats[StatType.Vitality].StartValue,
                    Wisdom = classDesc.Stats[StatType.Wisdom].StartValue
                },
                CombatStats = new CombatStats(),
                ExplorationStats = new ExplorationStats(),
                KillStats = new KillStats(),
                DungeonStats = new DungeonStats()
            };

            for (var i = 0; i < classDesc.Equipment.Length; i++) {
                var itemType = classDesc.Equipment[i];
                chr.ItemTypes[i] = itemType;
            }

            acc.NextCharId++;
            acc.Characters.Add(chr);
            await FlushAsync(acc);
        }

        return (chr, status);
    }

    public static async Task<bool> DeleteCharacterAsync(int accId, int charId) {
        await using var conn = await OpenConnectionAsync();
        var acc = await GetAccountByIdAsync(conn, accId);

        var success = true;
        if (acc == null || charId < 0 || charId >= acc.NextCharId) {
            success = false;
        }
        else {
            var chr = acc.Characters[charId];
            if (chr == null) {
                success = false;
            }
            else {
                // Perform a "soft" delete, doesn't actually delete from database, instead we mark it as deleted
                chr.IsDeleted = true;
                await FlushAsync(acc);
            }
        }

        return success;
    }

    public static async Task<BuyStatus> BuyCharSlotAsync(Account acc) {
        var cost = NewAccountsConfig.Config.CharSlotCost;
        if (acc.Stats.CurrentFame < cost)
            return BuyStatus.NotEnoughFame;

        acc.Stats.CurrentFame -= cost;
        acc.MaxChars++;

        await FlushAsync(acc);
        return BuyStatus.Success;
    }

    public static async Task<Character> GetCharacterAsync(int accId, int charId) {
        await using var conn = await OpenConnectionAsync();
        var acc = await GetAccountByIdAsync(conn, accId);
        if (acc == null)
            return null;

        if (charId < 0 || charId >= acc.NextCharId)
            return null;

        return acc.Characters[charId];
    }

    public static async Task<BanRecord[]> GetActiveBans(int accountId) {
        await using var conn = await OpenConnectionAsync();
        var bans = await conn.QueryAsync<BanRecord>(
            "SELECT id AS Id, target_acc_id AS TargetAccId, moderator_acc_id AS ModeratorAccId, reason AS Reason, " +
            "created_at AS CreatedAt, expires_at AS ExpiresAt, permanent AS Permanent FROM bans WHERE target_acc_id=@AccountId",
            new { AccountId = accountId });
        return bans.ToArray();
    }
}
