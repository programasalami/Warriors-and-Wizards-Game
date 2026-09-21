namespace GameServer.Game.Systems.Chat.Commands;

// Finds items by (part of) their name for /give. Pure: it works on (type, name) pairs, so it can be tested without the game data loaded.
public static class ItemSearch {
    public readonly record struct Entry(ushort Type, string Name);

    // Best matches first: an exact name, then names that START with the search, then names that CONTAIN it (every word of the search, in any order).
    // Case, spaces and dashes are ignored ("ironsword" finds "Iron Sword"). Within a group the shorter name comes first, then alphabetical.
    public static List<Entry> Find(IEnumerable<Entry> items, string query, int max = 50) {
        var q = Normalize(query);
        if (q.Length == 0)
            return [];

        var words = (query ?? string.Empty).Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var scored = new List<(int Rank, Entry Item)>();
        foreach (var item in items) {
            if (string.IsNullOrWhiteSpace(item.Name))
                continue;

            var name = Normalize(item.Name);
            int rank;
            if (name == q)
                rank = 0;
            else if (name.StartsWith(q, StringComparison.Ordinal))
                rank = 1;
            else if (name.Contains(q, StringComparison.Ordinal))
                rank = 2;
            else if (words.Length > 1 && words.All(w => name.Contains(Normalize(w), StringComparison.Ordinal)))
                rank = 3;
            else
                continue;

            scored.Add((rank, item));
        }

        return scored.OrderBy(s => s.Rank).ThenBy(s => s.Item.Name.Length).ThenBy(s => s.Item.Name, StringComparer.OrdinalIgnoreCase).Take(max).Select(s => s.Item).ToList();
    }

    // If the first match is an exact name (or the only match) it is THE item; otherwise the caller lists the candidates.
    public static bool IsClearWinner(List<Entry> matches, string query) =>
        matches.Count == 1 || (matches.Count > 1 && Normalize(matches[0].Name) == Normalize(query) && Normalize(matches[1].Name) != Normalize(query));

    private static string Normalize(string text) => new string((text ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
