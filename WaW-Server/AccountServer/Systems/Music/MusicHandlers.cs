#region

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Xml.Linq;
using AccountServer.Systems.Board;
using Common.Music;
using Common.Resources.Config;
using Common.Utilities;

#endregion

namespace AccountServer.Systems.Music;

// The one shared in-game playlist (see MusicDirector). It lives here because there is a single account server for everyone, whichever game world they are in
// and whichever client they play on. Time is a monotonic stopwatch; the state starts fresh (a new shuffle) whenever the server restarts.
internal static class MusicService {
    private static readonly object Gate = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly Dictionary<int, long> LastByAccount = [];
    private static long _lastByAnyone = long.MinValue;
    private static MusicDirector _director;

    private static long Now => Clock.ElapsedMilliseconds;

    // Throws if the library file is missing or empty; the handlers turn that into an error answer.
    private static MusicDirector Director => _director ??= new MusicDirector(MusicConfig.Config.Tracks, Now, Random.Shared);

    public static MusicSnapshot Snapshot(out IReadOnlyList<MusicTrack> library) {
        lock (Gate) {
            var snapshot = Director.Snapshot(Now);
            library = Director.Tracks;
            return snapshot;
        }
    }

    // null = it worked; otherwise why not. `apply` returns an error text or null.
    public static string Change(int accountId, string by, Func<MusicDirector, long, string> apply) {
        lock (Gate) {
            var now = Now;
            var lastMine = LastByAccount.TryGetValue(accountId, out var t) ? t : long.MinValue;
            var problem = MusicRules.CheckCooldown(now, lastMine, _lastByAnyone);
            if (problem != null) {
                return problem;
            }

            var error = apply(Director, now);
            if (error != null) {
                return error;
            }

            LastByAccount[accountId] = now;
            _lastByAnyone = now;
            return null;
        }
    }
}

// What is playing and the whole library. Open to every client (no sign-in needed to listen).
public class MusicNow : RequestHandler {
    private static readonly Logger Log = new(typeof(MusicNow));

    public override string Path => "/music/now";

    public override Task<string> Handle(string ip, NameValueCollection query) {
        try {
            var snapshot = MusicService.Snapshot(out var library);
            var root = new XElement("Music",
                new XAttribute("serial", snapshot.Serial),
                new XAttribute("changedBy", snapshot.ChangedBy ?? string.Empty),
                new XAttribute("positionMs", snapshot.PositionMs.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("current", snapshot.Current.Id),
                new XAttribute("next", snapshot.Next.Id));
            foreach (var track in library) {
                root.Add(new XElement("Track",
                    new XAttribute("id", track.Id),
                    new XAttribute("file", track.File),
                    new XAttribute("title", track.Title),
                    new XAttribute("lengthMs", track.LengthMs)));
            }

            return Task.FromResult(root.ToString(SaveOptions.DisableFormatting));
        } catch (Exception e) {
            Log.Error($"The music library could not be read: {e.Message}");
            return Task.FromResult(WriteError("The music library is not set up on this server."));
        }
    }
}

// Next / previous song for everybody. Signed-in players only, rate limited (and later paid for in fame: MusicRules.SkipFameCost).
public class MusicSkip : RequestHandler {
    private static readonly Logger Log = new(typeof(MusicSkip));

    public override string Path => "/music/skip";

    public override async Task<string> Handle(string ip, NameValueCollection query) {
        var acc = await BoardAuth.VerifyAsync(query);
        if (acc == null) {
            return WriteError("Sign in to change the music.");
        }

        var direction = query["dir"];
        if (direction != "next" && direction != "prev") {
            return WriteError("Unknown direction.");
        }

        var problem = MusicService.Change(acc.Id, acc.Name, (director, now) => {
            director.Skip(now, direction == "next", acc.Name);
            return null;
        });
        if (problem != null) {
            return WriteError(problem);
        }

        Log.Info($"Music: {acc.Name} skipped {(direction == "next" ? "forward" : "back")}");
        return WriteSuccess();
    }
}

// Play a chosen song for everybody. Signed-in players only, rate limited (and later paid for in fame: MusicRules.PickFameCost).
public class MusicSet : RequestHandler {
    private static readonly Logger Log = new(typeof(MusicSet));

    public override string Path => "/music/set";

    public override async Task<string> Handle(string ip, NameValueCollection query) {
        var acc = await BoardAuth.VerifyAsync(query);
        if (acc == null) {
            return WriteError("Sign in to change the music.");
        }

        var track = query["track"];
        var problem = MusicService.Change(acc.Id, acc.Name, (director, now) => director.Set(now, track, acc.Name) ? null : "There is no such song.");
        if (problem != null) {
            return WriteError(problem);
        }

        Log.Info($"Music: {acc.Name} picked {track}");
        return WriteSuccess();
    }
}
