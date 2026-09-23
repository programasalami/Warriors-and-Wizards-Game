using System;
using System.Collections.Generic;
using WaWClient.Game.Objects;
using WaW.UiLib.Core;
using WaW.Common;
using WaW.Engine;
using WaWClient.Game;

namespace WaWClient.Ui.Character;

public record struct StatusData(Entity Owner, string Text, uint Color, int Lifetime, int OffsetTime);

// Floating status / damage numbers over entities. Bounded: at most MaxLive texts exist at once and at most MaxQueued wait to be
// shown; beyond that the OLDEST are dropped. Each text is a sprite with its own text mesh, so an unbounded list is exactly what
// froze the client when one hit produced hundreds of "-dmg" texts (2026-09-21 audit).
public class NotificationLayer : Sprite {
    public const int MaxLive = 48;
    public const int MaxQueued = MaxLive * 2;

    private static readonly Queue<StatusData> TextQueue = new();
    private readonly List<CharacterStatusText> _list = [];

    public static void AddStatusText(Entity en, string text, uint color, int lifetime, int offsetTime) {
        if (en == null) {
            return;
        }

        if (TextQueue.Count >= MaxQueued) {
            TextQueue.Dequeue();
            PerfCounters.StatusTextsDroppedTotal++;
        }

        TextQueue.Enqueue(new StatusData(en, text, color, lifetime, offsetTime));
    }

    public static void ClearQueued() => TextQueue.Clear();

    // Pure helper (unit tested): keeps `count` under `max` by returning how many of the oldest entries must go.
    public static int OverflowToDrop(int count, int max) => count > max ? count - max : 0;

    public void Update(in GameTime gameTime, in Camera camera) {
        while (TextQueue.TryDequeue(out var data)) {
            var child = new CharacterStatusText(data.Owner, data.Text, data.Color, data.Lifetime, data.OffsetTime + gameTime.TotalMs);
            AddChild(child);
            _list.Add(child);
        }

        var drop = OverflowToDrop(_list.Count, MaxLive);
        for (var i = 0; i < drop; i++) {
            RemoveChild(_list[i]);
        }
        if (drop > 0) {
            _list.RemoveRange(0, drop);
            PerfCounters.StatusTextsDroppedTotal += drop;
        }

        for (var i = _list.Count - 1; i >= 0; i--) {
            var status = _list[i];
            if (!status.Update(in gameTime, in camera)) {
                _list.RemoveAt(i);
                RemoveChild(status);
            }
        }

        PerfCounters.StatusTextsLive = _list.Count;
    }
}
