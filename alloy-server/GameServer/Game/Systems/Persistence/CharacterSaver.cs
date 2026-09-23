using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Common;
using Common.Database.Models;
using Common.Structs;
using Common.Utilities;
using GameServer.Game.Network;
using GameServer.Game.Systems.Stats;

namespace GameServer.Game.Systems.Persistence;

// Copies the live entity back into the Character record (the reverse of PlayerExtensions.LoadCharacterStats / InitPlayerInventory).
public static class CharacterSnapshot {
    public static void CaptureStats(ref EntityStats stats, Character chr) {
        chr.Level = stats.GetInt(StatType.Level);
        chr.CurrentFame = stats.GetInt(StatType.CharFame);
        chr.XpPoints = stats.GetInt(StatType.Experience);
        chr.HealthPotions = stats.GetInt(StatType.HealthPotionStack);
        chr.MagicPotions = stats.GetInt(StatType.MagicPotionStack);

        chr.Stats ??= new CharacterStats();
        chr.Stats.MaxHp = stats.GetInt(StatType.MaxHP);
        chr.Stats.Hp = stats.GetInt(StatType.HP);
        chr.Stats.MaxMp = stats.GetInt(StatType.MaxMP);
        chr.Stats.Mp = stats.GetInt(StatType.MP);
        chr.Stats.Attack = stats.GetInt(StatType.Attack);
        chr.Stats.Defense = stats.GetInt(StatType.Defense);
        chr.Stats.Speed = stats.GetInt(StatType.Speed);
        chr.Stats.Dexterity = stats.GetInt(StatType.Dexterity);
        chr.Stats.Vitality = stats.GetInt(StatType.Vitality);
        chr.Stats.Wisdom = stats.GetInt(StatType.Wisdom);
    }

    // The saver serializes on another thread while the game keeps mutating the live record: hand it an independent copy.
    public static Character Clone(Character chr) => JsonSerializer.Deserialize<Character>(JsonSerializer.Serialize(chr));
}

// Sends character saves to the AccountServer (RPC SaveCharacter -> CharacterDb.SaveAsync, a targeted write of that one character)
// from a background worker, so the game thread only ever queues a snapshot. Only the newest snapshot per (account, character) is
// kept while one is waiting, so a burst of world switches does not queue a burst of writes. 2026-09-21 audit (C4): before this the
// game server never saved characters - equipment, potions, HP and stats were lost on disconnect or restart.
public static class CharacterSaver {
    private static readonly Logger _log = new(typeof(CharacterSaver));
    private const int RetryDelayMs = 5000;

    private static readonly ConcurrentDictionary<(int AccountId, int CharId), Character> _latest = new();
    private static readonly Channel<(int AccountId, int CharId)> _keys = Channel.CreateUnbounded<(int, int)>();
    private static Task _worker;
    private static int _inFlight;

    public static long Saved { get; private set; }
    public static long Failed { get; private set; }
    public static int Pending => _latest.Count + _inFlight;

    public static void Start() {
        _worker ??= Task.Run(WorkerAsync);
    }

    // Game thread: queue the latest state of a character. Safe to call often; only the newest copy per character is sent.
    public static void Enqueue(int accountId, Character chr) {
        if (chr == null || accountId <= 0)
            return;

        var key = (accountId, chr.CharId);
        var isNew = !_latest.ContainsKey(key);
        _latest[key] = CharacterSnapshot.Clone(chr);
        if (isNew)
            _keys.Writer.TryWrite(key);
    }

    // Snapshot a playing user's character (inventory + stats) and queue it. Game thread only.
    public static void SaveUser(User user) {
        var info = user.GameInfo;
        if (info.State != GameState.Playing || info.World == null || info.Char == null || info.Account == null)
            return;

        ref var inv = ref info.World.EntityInventories.Get(info.PlayerId);
        if (inv.Id != Common.Utilities.Collections.EntityId.Null)
            inv.Save(info.Char);

        ref var stats = ref info.World.EntityStats.Get(info.PlayerId);
        if (stats.Id != Common.Utilities.Collections.EntityId.Null)
            CharacterSnapshot.CaptureStats(ref stats, info.Char);

        Enqueue(info.Account.Id, info.Char);
    }

    // Waits until everything queued has been sent (used at shutdown). Returns false on timeout.
    public static async Task<bool> FlushAsync(int timeoutMs) {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Pending > 0 && Environment.TickCount64 < deadline)
            await Task.Delay(20);
        return Pending == 0;
    }

    private static async Task WorkerAsync() {
        var reader = _keys.Reader;
        while (await reader.WaitToReadAsync()) {
            while (reader.TryRead(out var key)) {
                if (!_latest.TryRemove(key, out var chr))
                    continue;

                Interlocked.Increment(ref _inFlight);
                try {
                    var rpc = Program.AccountServerRpc;
                    if (rpc == null)
                        throw new InvalidOperationException("AccountServer RPC is not connected");
                    await rpc.SaveCharacter(key.AccountId, chr);
                    Saved++;
                }
                catch (Exception ex) {
                    Failed++;
                    _log.Error($"Saving character #{key.CharId} of account {key.AccountId} failed: {ex.Message}");
                    // Keep the newest state (unless a newer one arrived meanwhile) and try again in a few seconds.
                    _latest.TryAdd(key, chr);
                    _ = Task.Delay(RetryDelayMs).ContinueWith(_ => {
                        if (_latest.ContainsKey(key))
                            _keys.Writer.TryWrite(key);
                    });
                }
                finally {
                    Interlocked.Decrement(ref _inFlight);
                }
            }
        }
    }
}
