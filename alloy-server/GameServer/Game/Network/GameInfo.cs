using Common.Database.Models;
using Common.Structs;
using Common.Utilities;
using Common.Utilities.Collections;
using GameServer.Game.Entities;
using GameServer.Game.Entities.Extensions;
using GameServer.Game.Systems.Chat;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Worlds;

namespace GameServer.Game.Network;

public enum GameState {
    Idle, // User has established connection to server but hasn't loaded to any world yet
    Loading, // User has sent Hello packet, and now we're waiting for client to send Load packet
    Playing // User has established
}

public class GameInfo {
    private static readonly Logger _log = new(typeof(GameInfo));

    public readonly User User;
    public Account Account;
    public World World;
    public Character Char;
    public EntityId PlayerId;

    public ref Entity Player => ref World.Entities.Get(PlayerId);
    // When this account's mute ends (unix seconds, UTC): 0 = not muted, long.MaxValue = no end. Set at login and whenever a moderator mutes / unmutes them.
    public long MuteEndUnix;

    // Log-only plausibility checks (see Systems/Combat/PlausibilityRules.cs): when the last Move arrived, the fire-rate bucket,
    // and how many times each check tripped this session (logged sparingly, never enforced yet).
    public long LastMoveAtMs;
    public int MoveViolations;
    public int FameXpCarry;
    public bool God;                     // /god (owner only, 2026-09-22): this character takes no damage - for tests among enemies              // XP towards the next character fame point (1 per 1,000 XP, Progression)
    public FireRateBucket FireRate;
    public int FireRateViolations;

    public bool IsMuted => MuteEndUnix != 0 && (MuteEndUnix == long.MaxValue || MuteEndUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    public GameInfoDto Data => new GameInfoDto(Account.Id, World.Id, World.DisplayName, World.EntityStats.Get(PlayerId).Pos);

    public GameInfo(User user) {
        User = user;
    }

    public GameState State { get; private set; }

    public void SetWorld(Account acc, World world) {
        Account = acc;
        State = GameState.Loading;
        World = world;
    }

    public void Load(Character chr, World world) {
        State = GameState.Playing;
        LastMoveAtMs = 0;
        FireRate = default;
        Char = chr;

        var plr = new Entity(chr.ObjectType);
        ref var newPlr = ref world.EnterPlayer(ref plr, User);
        newPlr.InitPlayer(User, world, Account, Char);
        newPlr.MoveToSpawn(world);

        PlayerId = newPlr.Id;
    }

    public void Unload() {
        if (World == null) {
            State = GameState.Idle;
            PlayerId = EntityId.Null;
            return;
        }

        // Refresh the character record from the live entity and queue it for the AccountServer before the entity is destroyed.
        Systems.Persistence.CharacterSaver.SaveUser(User);

        World.LeaveWorld(PlayerId);
        State = GameState.Idle;
        PlayerId = EntityId.Null;
    }

    public void Reset() {
        State = GameState.Idle; // Change our state first
        World = null;
        Char = null;
        PlayerId = EntityId.Null;
        LastMoveAtMs = 0;
        MoveViolations = 0;
        FireRate = default;
        FireRateViolations = 0;
    }
}