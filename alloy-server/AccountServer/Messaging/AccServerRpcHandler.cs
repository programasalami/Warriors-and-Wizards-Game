using System;
using System.Threading.Tasks;
using Common;
using Common.Database;
using Common.Database.Models;
using Common.Messaging;
using Common.Structs;
using Common.Utilities;
using StreamJsonRpc;

namespace AccountServer.Messaging;

public class AccServerRpcHandler : IAccountServerHandler {
    private static readonly Logger _log = new Logger(typeof(AccServerRpcHandler));

    public Guid ServerId { get; set; }

    private IGameServerRpc _proxy;

    public void Attach(IGameServerRpc proxy) {
        _proxy = proxy;
    }

    public async Task Close() {
        IpcServer.Clients.TryRemove(ServerId, out _);
        await AccountLockManager.ReleaseAllForServerAsync(ServerId);
    }
    
    public Task GameServerConnected(Guid gameServerId) {
        if (!IpcServer.Clients.TryAdd(gameServerId, _proxy))
            throw new InvalidOperationException($"Failed to add GameServer to dictionary: {gameServerId}");
        ServerId = gameServerId;
        
        _log.Info($"[RPC] GameServer ({gameServerId}) connected");
        return Task.CompletedTask;
    }

    public async Task<GameInfoDto?> GetUserInfo(string name, int accountId) {
        return await _proxy.GetUserInfo(name, accountId);
    }

    public async Task<VerifyResultDto> VerifyAccount(string username, string password, Guid gameServerGuid) {
        var (acc, status) = await DbClient.VerifyAccount(username, password, gameServerGuid);
        return new VerifyResultDto(acc, status);
    }

    public async Task<BanRecord[]> GetActiveBans(int accountId) {
        return await DbClient.GetActiveBans(accountId);
    }

    public async Task FlushAccount(Account account) {
        await DbClient.FlushAsync(account);
    }

    public async Task<Character> GetCharacter(int accountId, int charId) {
        return await DbClient.GetCharacterAsync(accountId, charId);
    }

    public async Task<AccountBriefDto> FindAccount(string name) {
        var brief = await AdminDb.FindAsync(name);
        return brief == null ? default : new AccountBriefDto(true, brief.Value.Id, brief.Value.Name, brief.Value.Rank, brief.Value.IsBanned);
    }

    public async Task<ModerationResultDto> Moderate(ModerationRequestDto r) {
        TimeSpan? duration = r.DurationMinutes <= 0 ? null : TimeSpan.FromMinutes(r.DurationMinutes);
        var error = r.Action switch {
            "ban" => await AdminDb.BanAsync(r.ModeratorId, r.Target, r.Reason, duration),
            "unban" => await AdminDb.UnbanAsync(r.ModeratorId, r.Target),
            "mute" => await AdminDb.MuteAsync(r.ModeratorId, r.Target, r.Reason, duration),
            "unmute" => await AdminDb.UnmuteAsync(r.ModeratorId, r.Target),
            "setrank" => await AdminDb.SetRankAsync(r.ModeratorId, r.Target, r.Rank),
            _ => "Unknown moderation action."
        };
        if (error == null)
            _log.Info($"[MOD] {r.Action} {r.Target} by account {r.ModeratorId}" + (r.Action is "ban" or "mute" ? $" ({(duration == null ? "permanent" : duration.Value.TotalMinutes + " min")}) {r.Reason}" : r.Action == "setrank" ? $" -> {Ranks.Name(r.Rank)}" : string.Empty));
        return new ModerationResultDto(error);
    }

    public async Task<MuteStateDto> GetMuteState(int accountId) {
        var end = await AdminDb.ActiveMuteEndAsync(accountId);
        if (end == null)
            return new MuteStateDto(0);
        return new MuteStateDto(end.Value == DateTime.MaxValue ? long.MaxValue : new DateTimeOffset(DateTime.SpecifyKind(end.Value, DateTimeKind.Utc)).ToUnixTimeSeconds());
    }

    public async Task SaveVaultChests(int accountId, VaultChest[] chests) {
        await VaultDb.SaveAsync(accountId, chests);
    }

    public async Task<ModerationResultDto> SendMail(MailRequestDto r) {
        return new ModerationResultDto(await AdminDb.MailAsync(r.Target, r.From, r.Subject, r.Body, r.Gold, r.Fame));
    }

    public async Task<CreateCharacterResultDto> CreateCharacter(Account account, ushort objectType, ushort skinType) {
        var result = await DbClient.CreateCharacterAsync(account, objectType, skinType);
        return new CreateCharacterResultDto(account, result.Char, result.Status);
    }
}