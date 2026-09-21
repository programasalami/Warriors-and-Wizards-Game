using Common.Database.Models;
using Common.Resources.Config;
using Common.Resources.World;
using Common.Resources.Xml;
using Common.Utilities;
using Common.Utilities.Collections;
using GameServer.Game.Entities;
using GameServer.Game.Entities.Extensions;
using GameServer.Game.Network;

namespace GameServer.Game.Worlds.Logic;

// Your personal Vault: one world per account, reached through the Vault Portal in the Nexus. The map marks chest spots with the "Vault" region. The account's
// VaultCount decides how many of them get an OPEN Vault Chest (8 slots, only you can use it, filled from Account.VaultChests); the rest get a Closed Vault
// Chest (a spot for a chest you do not have yet). The contents are saved back to the account a few seconds after any change (targeted save: only the chests
// are written, nothing else on the account is touched).
public class Vault : World {
    public const ushort VaultChestType = 0x9f12;
    public const ushort ClosedVaultChestType = 0x9f13;
    private const int SaveCheckMs = 3000;

    private static readonly Logger _log = new(typeof(Vault));
    private static readonly Dictionary<int, Vault> _vaults = [];

    private readonly List<EntityId> _openChests = [];
    private Account _account;
    private int[][] _lastSaved = [];

    public Vault(int id, int mapId, WorldConfig config)
        : base(id, mapId, config) {
    }

    public override World GetInstance(User user) {
        if (user.GameInfo.Account == null)
            return null;

        if (!_vaults.TryGetValue(user.GameInfo.Account.Id, out var ret) || ret.Deleted) {
            ret = _vaults[user.GameInfo.Account.Id] = new Vault(0, MapId, Config);
            RealmManager.AddWorld(ret);
            ret.SetupChests(user.GameInfo.Account);
        }
        else {
            ret.Attach(user.GameInfo.Account);       // the player may have logged in again: their account object is a new one
        }

        return ret;
    }

    // Puts the chests on the map for this account. Called once, when the account's Vault world is created.
    public void SetupChests(Account account) {
        _account = account;
        var count = Math.Max(0, account.VaultCount);
        account.VaultChests = VaultRules.Chests(account.VaultChests, count, t => XmlLibrary.ItemDescs.ContainsKey((ushort)t));

        var spots = Map.Regions.TryGetValue(TileRegion.Vault, out var region)
            ? region.Select(p => new VaultRules.Spot(p.X, p.Y)).ToList()
            : [];
        var (open, closed) = VaultRules.Split(spots, Map.Data.Width / 2.0, Map.Data.Height / 2.0, count);
        if (open.Count < count)
            _log.Warn($"The Vault map has only {spots.Count} chest spots but account {account.Id} owns {count} chests.");

        if (!XmlLibrary.ContainerDescs.ContainsKey(VaultChestType)) {
            _log.Error($"The Vault Chest (0x{VaultChestType:x}) is missing from the game data: no chests were placed.");
            return;
        }

        for (var i = 0; i < open.Count; i++) {
            var en = new Entity(VaultChestType);
            ref var chest = ref EnterWorld(ref en);
            chest.Move(this, open[i].X + 0.5f, open[i].Y + 0.5f);       // the MIDDLE of the tile: the tile corner would draw the chest offset from the square it blocks

            ref var inv = ref EntityInventories.Get(chest.Id);
            inv.OwnerAccIds.Add(account.Id);       // nobody else can put things in or take things out
            inv.LoadItems(account.VaultChests[i].ItemTypes);
            _openChests.Add(chest.Id);
        }

        foreach (var spot in closed) {
            var en = new Entity(ClosedVaultChestType);
            ref var chest = ref EnterWorld(ref en);
            chest.Move(this, spot.X + 0.5f, spot.Y + 0.5f);
        }

        _lastSaved = Snapshot();
        ScheduleSave();
    }

    // The same world for a returning player: their (new) account object must carry the current chests, or a later save of the whole account would undo them.
    private void Attach(Account account) {
        _account = account;
        account.VaultChests = CurrentChests();
    }

    private int[][] Snapshot() => _openChests.Select(id => EntityInventories.Get(id).ItemTypes()).ToArray();

    private List<VaultChest> CurrentChests() {
        var list = new List<VaultChest>();
        var snapshot = Snapshot();
        for (var i = 0; i < snapshot.Length; i++)
            list.Add(new VaultChest { ChestId = i, ItemTypes = snapshot[i], ItemDatas = [] });
        return list;
    }

    private void ScheduleSave() {
        AddTimedAction(SaveCheckMs, _ => {
            if (Deleted)
                return;
            SaveIfChanged();
            ScheduleSave();
        });
    }

    // Saves the chests if anything changed since the last save. (Public so tests and shutdown can force it.)
    public void SaveIfChanged() {
        var now = Snapshot();
        if (VaultRules.SameContents(now, _lastSaved))
            return;

        _lastSaved = now;
        var chests = CurrentChests();
        _account.VaultChests = chests;
        var rpc = Program.AccountServerRpc;
        if (rpc == null)
            return;

        var accountId = _account.Id;
        _ = SaveAsync(rpc, accountId, chests);
    }

    private static async Task SaveAsync(Common.Messaging.IAccountServerRpc rpc, int accountId, List<VaultChest> chests) {
        try {
            await rpc.SaveVaultChests(accountId, chests.ToArray());
        }
        catch (Exception ex) {
            _log.Error($"Could not save the Vault of account {accountId}: {ex}");
        }
    }

    // What is in each open chest right now (for tests and the admin tools).
    public IReadOnlyList<int[]> ChestContents() => Snapshot();

    public int OpenChestCount => _openChests.Count;

    public EntityId OpenChestId(int index) => _openChests[index];
}
