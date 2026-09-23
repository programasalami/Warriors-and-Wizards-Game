using Common.Database.Models;

namespace GameServer.Game.Worlds.Logic;

// The pure parts of the Vault (your personal storage world), kept apart from the world so they can be tested: which spots get an open chest, and how a chest's
// contents are read from / written to the account.
public static class VaultRules {
    public const int Slots = 8;              // items per chest
    public const int MaxChests = 64;         // never more chests on an account than this, whatever a database row says

    public readonly record struct Spot(int X, int Y);

    // The chest spots split into the ones that get an open chest (the account's chests, nearest the middle of the map first) and the ones that stay closed.
    // Ties are broken by row, then column, so the same map always gives the same chests in the same places.
    public static (List<Spot> Open, List<Spot> Closed) Split(IEnumerable<Spot> spots, double centerX, double centerY, int openCount) {
        var ordered = spots
            .OrderBy(s => (s.X + 0.5 - centerX) * (s.X + 0.5 - centerX) + (s.Y + 0.5 - centerY) * (s.Y + 0.5 - centerY))
            .ThenBy(s => s.Y)
            .ThenBy(s => s.X)
            .ToList();
        openCount = Math.Clamp(openCount, 0, Math.Min(MaxChests, ordered.Count));
        return (ordered.Take(openCount).ToList(), ordered.Skip(openCount).ToList());
    }

    // Exactly `Slots` item types: anything missing is empty (-1), and a type the game no longer has is dropped to empty too (never a crash on login).
    public static int[] Clean(int[] types, Func<int, bool> exists) {
        var result = new int[Slots];
        for (var i = 0; i < Slots; i++) {
            var t = types != null && i < types.Length ? types[i] : -1;
            result[i] = t >= 0 && exists(t) ? t : -1;
        }

        return result;
    }

    // The account's chest list, grown to `count` chests (each with 8 empty slots at least) and never larger than MaxChests.
    public static List<VaultChest> Chests(List<VaultChest> saved, int count, Func<int, bool> exists) {
        count = Math.Clamp(count, 0, MaxChests);
        var list = new List<VaultChest>(count);
        for (var i = 0; i < count; i++) {
            var old = saved != null && i < saved.Count ? saved[i] : null;
            list.Add(new VaultChest { ChestId = i, ItemTypes = Clean(old?.ItemTypes, exists), ItemDatas = old?.ItemDatas ?? [] });
        }

        return list;
    }

    public static bool SameContents(IReadOnlyList<int[]> a, IReadOnlyList<int[]> b) {
        if (a == null || b == null || a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
            if (!a[i].AsSpan().SequenceEqual(b[i]))
                return false;
        return true;
    }
}
