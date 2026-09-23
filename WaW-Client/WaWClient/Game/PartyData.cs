using System;
using System.Collections.Generic;
using WaWClient.Game.Objects;
using WaWClient.Networking;
using WaWClient.Networking.Packets.Outgoing;
using WaWClient.Utils;
using OpenTK.Mathematics;

namespace WaWClient.Game;

public static class PartyData {

    public const int MaxVisibleMembers = 6;

    private const int MaxDistance = 50 * 50;

    public static readonly HashSet<int> LockedPlayers = [];
    
    public static readonly HashSet<int> IgnoredPlayers = [];

    private static double _lastUpdateTime;

    private static readonly PartyInfo[] Members = new PartyInfo[1000];

    public static readonly ArraySegment<PartyInfo> PartyMembers = new(Members, 0, MaxVisibleMembers);

    private static readonly PartyComparison PartyComparer = new();

    private static readonly List<PartyInfo> Scratch = [];

    public static void Clear() {
        LockedPlayers.Clear();
        IgnoredPlayers.Clear();
        Array.Clear(Members);
    }

    public static void Update(double time) {
        if (time < _lastUpdateTime + 500)
            return;
        
        if (Map.LocalPlayer == null)
            return;
        
        _lastUpdateTime = time;
        
        Array.Clear(Members);

        var localPosition = Map.LocalPlayer.Position;

        Scratch.Clear();
        foreach (var player in Map.Players.Values) {
            Vector2.DistanceSquared(localPosition, player.Position, out var dist);
            if (dist < MaxDistance) {
                Scratch.Add(new PartyInfo(player, player.Locked, dist, player.ObjectId));
            }
        }

        // closest first (the nearby-players panel shows the first MaxVisibleMembers)
        Scratch.Sort(PartyComparer);
        for (var i = 0; i < Scratch.Count && i < Members.Length; i++) {
            Members[i] = Scratch[i];
        }
    }

    public static void SetData(int id, int[] list) {
        var set = id == 0 ? LockedPlayers : IgnoredPlayers;
        set.UnionWith(list);
    }

    public static void LockPlayer(Player player) {
        player.Locked = true;
        _lastUpdateTime = int.MinValue;
        LockedPlayers.Add(player.AccountId);

        var pkt = EditAccountList.CreatePacket();
        pkt.AccountListId = 0;
        pkt.Add = true;
        pkt.ObjectId = player.ObjectId;
        
        Client.QueuePacket(pkt);
    }
    
    public static void UnlockPlayer(Player player) {
        player.Locked = false;
        _lastUpdateTime = int.MinValue;
        LockedPlayers.Remove(player.AccountId);

        var pkt = EditAccountList.CreatePacket();
        pkt.AccountListId = 0;
        pkt.Add = false;
        pkt.ObjectId = player.ObjectId;
        
        Client.QueuePacket(pkt);
    }
    
    public static void IgnorePlayer(Player player) {
        player.Ignored = true;
        _lastUpdateTime = int.MinValue;
        IgnoredPlayers.Add(player.AccountId);

        var pkt = EditAccountList.CreatePacket();
        pkt.AccountListId = 1;
        pkt.Add = true;
        pkt.ObjectId = player.ObjectId;
        
        Client.QueuePacket(pkt);
    }
    
    public static void UnignorePlayer(Player player) {
        player.Ignored = false;
        _lastUpdateTime = int.MinValue;
        IgnoredPlayers.Remove(player.AccountId);

        var pkt = EditAccountList.CreatePacket();
        pkt.AccountListId = 1;
        pkt.Add = false;
        pkt.ObjectId = player.ObjectId;
        
        Client.QueuePacket(pkt);
    }

    public sealed record PartyInfo(Player Player, bool Locked = false, float Dist = float.MaxValue, int ObjectId = int.MaxValue);
    
    private class PartyComparison : IComparer<PartyInfo> {
        public int Compare(PartyInfo self, PartyInfo other) {
            if (self!.Locked != other!.Locked) {
                return self.Locked ? -1 : 1;
            }

            var byDistance = self.Dist.CompareTo(other!.Dist);
            return byDistance != 0 ? byDistance : self.ObjectId.CompareTo(other.ObjectId);
        }
    }
}