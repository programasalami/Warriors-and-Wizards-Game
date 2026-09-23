using Common.Resources.Xml.Descriptors;

namespace GameServer.Game.Systems.Chat.Commands;

// What "/spawn <count> <name>" accepts (pure, tested in SpawnRulesTests). Only object definitions are spawnable (tiles are not objects and never
// reach this), and never a player class. The name is forgiving: the exact id in any case, the id without a typed trailing "s" ("10 pirates"),
// the display name, or a single object whose id contains every word typed; several matches or none give a message with suggestions instead of
// the old "null object desc" (2026-09-22).
public static class SpawnRules {
    public const int MaxSuggestions = 8;

    public static ObjectDesc Resolve(string query, IEnumerable<ObjectDesc> objects, out string error) {
        error = null;
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0) {
            error = "Name what to spawn: /spawn <count> <entity>.";
            return null;
        }

        var spawnable = objects.Where(o => o != null && !o.Player).ToList();

        var exact = spawnable.FirstOrDefault(o => Same(o.ObjectId, q)) ?? spawnable.FirstOrDefault(o => Same(o.DisplayId, q));
        if (exact == null && q.Length > 1 && (q.EndsWith('s') || q.EndsWith('S'))) {
            var singular = q[..^1];
            exact = spawnable.FirstOrDefault(o => Same(o.ObjectId, singular)) ?? spawnable.FirstOrDefault(o => Same(o.DisplayId, singular));
        }
        if (exact != null)
            return exact;

        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var partial = spawnable.Where(o => words.All(w => Contains(o.ObjectId, w) || Contains(o.DisplayId, w))).ToList();
        if (partial.Count == 1)
            return partial[0];

        if (partial.Count == 0) {
            error = $"No object named '{q}'.";
            return null;
        }

        var names = partial.Take(MaxSuggestions).Select(o => o.ObjectId);
        error = $"{partial.Count} objects match '{q}': " + string.Join(", ", names) + (partial.Count > MaxSuggestions ? ", ..." : string.Empty) + ". Be more specific.";
        return null;
    }

    private static bool Same(string a, string b) => a != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string a, string b) => a != null && a.Contains(b, StringComparison.OrdinalIgnoreCase);
}
