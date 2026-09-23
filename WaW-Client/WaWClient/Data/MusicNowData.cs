using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace WaWClient.Data;

public sealed record MusicTrackInfo(string Id, string File, string Title, int LengthMs);

// What the account server's /music/now says: the shared in-game playlist. Everyone hears the same track; `Serial` changes whenever the track changes.
public sealed record MusicNowData(int Serial, string ChangedBy, long PositionMs, string CurrentId, string NextId, IReadOnlyList<MusicTrackInfo> Tracks) {

    public MusicTrackInfo Find(string id) => Tracks.FirstOrDefault(t => t.Id == id);

    // <Music serial="3" changedBy="Riigged" positionMs="12000" current="a" next="b"><Track id file title lengthMs/>...</Music>
    // Anything else (an <Error>, an empty answer from an older server, broken XML, a current track that is not in the list) is "no music info".
    public static bool TryParse(string response, out MusicNowData data) {
        data = null;
        if (string.IsNullOrWhiteSpace(response)) {
            return false;
        }

        try {
            var root = XElement.Parse(response);
            if (root.Name.LocalName != "Music") {
                return false;
            }

            var tracks = new List<MusicTrackInfo>();
            foreach (var element in root.Elements("Track")) {
                var id = (string)element.Attribute("id");
                var file = (string)element.Attribute("file");
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(file) || !int.TryParse((string)element.Attribute("lengthMs"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) || length <= 0) {
                    continue;
                }

                tracks.Add(new MusicTrackInfo(id, file, (string)element.Attribute("title") ?? id, length));
            }

            var current = (string)root.Attribute("current");
            var next = (string)root.Attribute("next");
            if (tracks.Count == 0 || tracks.All(t => t.Id != current) || !long.TryParse((string)root.Attribute("positionMs"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var position)) {
                return false;
            }

            var changedBy = (string)root.Attribute("changedBy");
            data = new MusicNowData(int.TryParse((string)root.Attribute("serial"), out var serial) ? serial : 0,
                string.IsNullOrEmpty(changedBy) ? null : changedBy, Math.Max(0, position), current, tracks.Any(t => t.Id == next) ? next : current, tracks);
            return true;
        } catch (Exception) {
            return false;
        }
    }

    // "3:27"
    public static string FormatTime(long ms) {
        var seconds = Math.Max(0, ms) / 1000;
        return $"{seconds / 60}:{seconds % 60:00}";
    }
}
