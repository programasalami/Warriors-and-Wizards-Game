using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WaW.Engine;
using WaWClient.Assets.Libraries;
using WaWClient.Game.Objects;
using WaWClient.Networking.Structs.DataObjects;
using WaWClient.Rendering;
using WaW.UiLib;

using WaWClient.Game;

namespace WaWClient.Dev;

// A scripted, repeatable frame-cost measurement (2026-09-22; lives in Dev/ with nothing else of the game - developer tooling, switched off for players). OFF unless the environment variable WAW_PERFTEST names an output file, so a
// normal player never runs any of it. When on: the title screen presses PLAY, the book plays the last character, and once in the world the
// script runs fixed phases - standing, walking back and forth, then the same with 1 and with 10 FAKE remote players standing next to you
// (client-side Player objects that never touch the network: they cost exactly what a real remote player costs to update, draw, list and
// put on the minimap, minus 20 small stat packets a second). Every phase averages PerfSections, the frame timing, the GPU traffic and the
// garbage allocated per frame, and the table is written to the file. Nothing is sent to the server except your own normal movement.
public static class DevPerfTest {
    public static readonly string OutputPath = Environment.GetEnvironmentVariable("WAW_PERFTEST");
    public static bool Enabled => !string.IsNullOrEmpty(OutputPath);

    private sealed record Phase(string Name, double StartS, double EndS, int FakePlayers, bool Walk);

    // (seconds after the local player appeared; the first 2 s of each phase are skipped when sampling)
    // WAW_PERFTEST_MODE=enemies: real server traffic instead of fake players - /god on, then /spawn 10 Pirate next to you (owner commands;
    // the pirates follow and shoot, every tick brings their positions, projectiles and damage texts). Restart the local game server after.
    public static readonly bool EnemyMode = Environment.GetEnvironmentVariable("WAW_PERFTEST_MODE") == "enemies";

    // WAW_PERFTEST_MODE=timeline: enter the NEXUS and stand still for up to 15 minutes, appending one line every 5 s (other players in the
    // world, entities drawn, frame / CPU / GPU / swap, the biggest sections, allocation). Made for a REAL second player: walk another game
    // window in and out while it runs; the lines show the before / after. The output file is written as it goes.
    public static readonly bool TimelineMode = Environment.GetEnvironmentVariable("WAW_PERFTEST_MODE") == "timeline";
    private const double TimelineStepMs = 5000;
    private const double TimelineMaxMs = 15 * 60 * 1000;
    private static Totals _slice = new();
    private static double _sliceMs;

    private static readonly Phase[] Phases = EnemyMode ? [
        new("warm-up",          0,  12, 0, false),
        new("alone, standing",  12, 32, 0, false),
        new("alone, walking",   32, 52, 0, true),
        new("spawning",         52, 58, 0, false),
        new("10 pirates, standing", 58, 83, 0, false),
        new("10 pirates, walking",  83, 108, 0, true),
    ] : [
        new("warm-up",          0,  12, 0, false),
        new("alone, standing",  12, 32, 0, false),
        new("alone, walking",   32, 52, 0, true),
        new("1 other, standing",52, 72, 1, false),
        new("1 other, walking", 72, 92, 1, true),
        new("10 others, standing", 92, 112, 10, false),
        new("10 others, walking",  112, 132, 10, true),
        new("alone again, standing", 132, 152, 0, false),
    ];

    private static bool _spawnSent;

    private static void Say(string text) {
        var packet = Networking.Packets.Outgoing.PlayerText.CreatePacket();
        packet.Text = text;
        Networking.Client.QueuePacket(packet);
    }

    private sealed class Totals {
        public int Frames;
        public readonly double[] Sections = new double[(int)PerfSections.Section.Count];
        public double Work, Swap, Gpu, Frame;
        public int GpuSamples;
        public long Draws, Uploads, UiSprites, UiBatches, Entities;
        public long AllocStart = -1, AllocEnd, Gen0Start = -1, Gen0End;
    }

    private static readonly Dictionary<string, Totals> Results = [];
    private static readonly List<int> FakeIds = [];
    private static double _startMs = -1;
    private static bool _written;

    public static bool Walking { get; private set; }

    // Called at the END of GameScreen.Update, every frame.
    public static void Update(in GameTime time) {
        if (!Enabled || _written || Map.LocalPlayer == null)
            return;

        if (_startMs < 0)
            _startMs = time.TotalMs;
        var t = (time.TotalMs - _startMs) / 1000d;

        if (TimelineMode) {
            Timeline(time, t);
            return;
        }

        Phase phase = null;
        foreach (var p in Phases)
            if (t >= p.StartS && t < p.EndS) { phase = p; break; }

        if (phase == null) {
            Write();
            return;
        }

        SetFakePlayers(phase.FakePlayers);
        if (phase.Name == "spawning" && !_spawnSent) {
            _spawnSent = true;
            Say("/god on");
            Say("/spawn 10 Pirate");
        }
        Walking = phase.Walk;
        if (Walking) {
            // back and forth along the screen's x axis, 3 s each way (the input code only sets movement on key events, so this wins)
            var dir = Math.Sin(t * Math.PI / 3) >= 0 ? 1 : -1;
            Map.LocalPlayer.SetRelativeMovement(0, dir, 0);
        } else {
            Map.LocalPlayer.SetRelativeMovement(0, 0, 0);
        }

        if (phase.Name is "warm-up" or "spawning" || t < phase.StartS + 2)
            return;

        if (!Results.TryGetValue(phase.Name, out var tot))
            Results[phase.Name] = tot = new Totals();

        tot.Frames++;
        for (var i = 0; i < tot.Sections.Length; i++)
            tot.Sections[i] += PerfSections.LastMs[i];
        tot.Work += FrameTiming.WorkMs;
        tot.Swap += FrameTiming.SwapMs;
        tot.Frame += time.ElapsedMs;
        if (FrameTiming.GpuMs >= 0) { tot.Gpu += FrameTiming.GpuMs; tot.GpuSamples++; }
        tot.Draws += PerfCounters.DrawCallsLastFrame;
        tot.Uploads += PerfCounters.UploadBytesLastFrame;
        tot.UiSprites += UiRender.LastRenderCount;
        tot.UiBatches += WaW.UiLib.Rendering.SpriteRender.LastBatchCount;
        tot.Entities += Render.LastDrawCountEntities;
        var alloc = GC.GetTotalAllocatedBytes();
        var gen0 = GC.CollectionCount(0);
        if (tot.AllocStart < 0) { tot.AllocStart = alloc; tot.Gen0Start = gen0; }
        tot.AllocEnd = alloc;
        tot.Gen0End = gen0;
    }

    private static void Timeline(in GameTime time, double t) {
        Map.LocalPlayer.SetRelativeMovement(0, 0, 0);
        if (t < 8)
            return;       // settle after entering

        var tot = _slice;
        tot.Frames++;
        for (var i = 0; i < tot.Sections.Length; i++)
            tot.Sections[i] += PerfSections.LastMs[i];
        tot.Work += FrameTiming.WorkMs;
        tot.Swap += FrameTiming.SwapMs;
        tot.Frame += time.ElapsedMs;
        if (FrameTiming.GpuMs >= 0) { tot.Gpu += FrameTiming.GpuMs; tot.GpuSamples++; }
        tot.Entities += Render.LastDrawCountEntities;
        var alloc = GC.GetTotalAllocatedBytes();
        if (tot.AllocStart < 0) tot.AllocStart = alloc;
        tot.AllocEnd = alloc;
        _sliceMs += time.ElapsedMs;
        if (_sliceMs < TimelineStepMs)
            return;

        var f = (double)tot.Frames;
        string S(PerfSections.Section s) => (tot.Sections[(int)s] / f).ToString("F3");
        var line = $"{t,6:F0}s players={Map.Players.Count,2} ents={tot.Entities / f,4:F0} fps={1000d * f / _sliceMs,6:F1} frame={tot.Frame / f:F2} work={tot.Work / f:F2} " +
                   $"gpu={(tot.GpuSamples > 0 ? tot.Gpu / tot.GpuSamples : -1):F2} swap={tot.Swap / f:F2} | net={S(PerfSections.Section.Net)} hud={S(PerfSections.Section.Hud)} " +
                   $"map={S(PerfSections.Section.MapUpdate)} stage={S(PerfSections.Section.StageUpdate)} models={S(PerfSections.Section.DrawModels)} " +
                   $"sprites={S(PerfSections.Section.DrawEntities)} ui={S(PerfSections.Section.Ui)} | alloc={(tot.AllocEnd - tot.AllocStart) / f:F0}B/f";
        try {
            File.AppendAllText(OutputPath, line + Environment.NewLine);
        } catch { }
        _slice = new Totals();
        _sliceMs = 0;

        if (time.TotalMs - _startMs > TimelineMaxMs)
            _written = true;
    }

    private static void SetFakePlayers(int wanted) {
        while (FakeIds.Count > wanted) {
            var id = FakeIds[^1];
            FakeIds.RemoveAt(FakeIds.Count - 1);
            Map.RemoveEntity(id);
        }

        var local = Map.LocalPlayer;
        while (FakeIds.Count < wanted) {
            var i = FakeIds.Count;
            var id = -100_000 - i;
            var angle = i * Math.PI * 2 / 10;
            var x = local.Position.X + (float)Math.Cos(angle) * 2.5f;
            var y = local.Position.Y + (float)Math.Sin(angle) * 2.5f;

            var player = new Player();
            player.Properties = ObjectLibrary.TypeToObjectProps[local.Type];
            player.SetObjectId(id);
            player.SetType(local.Type);
            player.Name = $"Tester{i + 1}";
            player.MaxHp = 100;
            player.Hp = 100;
            player.Level = 1;
            player.RenderBaseType.SetName(player.Name);
            Map.AddEntity(player, new Position(x, y));
            player.SetPos(x, y);
            player.OnTickPosition(x, y, 0, 0, true);
            FakeIds.Add(id);
        }
    }

    private static void Write() {
        _written = true;
        SetFakePlayers(0);
        Walking = false;
        Map.LocalPlayer?.SetRelativeMovement(0, 0, 0);

        var names = Enum.GetNames<PerfSections.Section>();
        var sb = new StringBuilder();
        sb.AppendLine($"# DevPerfTest {DateTime.Now:yyyy-MM-dd HH:mm:ss}  (ms per frame, averages; VSync {Core.Settings.VSync.Value}, cap {Core.Settings.FpsCap.Value})");
        sb.Append("phase".PadRight(24)).Append("frames".PadLeft(7)).Append("frame".PadLeft(8)).Append("work".PadLeft(8)).Append("swap".PadLeft(8)).Append("gpu".PadLeft(8));
        for (var i = 0; i < names.Length - 1; i++)
            sb.Append(names[i].PadLeft(i >= 5 ? 13 : 11));
        sb.Append("draws".PadLeft(7)).Append("upKB".PadLeft(7)).Append("uiSpr".PadLeft(7)).Append("uiBat".PadLeft(6)).Append("ents".PadLeft(6)).Append("allocB/f".PadLeft(10)).Append("gen0".PadLeft(6)).AppendLine();
        foreach (var p in Phases) {
            if (!Results.TryGetValue(p.Name, out var r) || r.Frames == 0)
                continue;
            var f = (double)r.Frames;
            sb.Append(p.Name.PadRight(24)).Append(r.Frames.ToString().PadLeft(7))
              .Append((r.Frame / f).ToString("F3").PadLeft(8)).Append((r.Work / f).ToString("F3").PadLeft(8))
              .Append((r.Swap / f).ToString("F3").PadLeft(8)).Append((r.GpuSamples > 0 ? r.Gpu / r.GpuSamples : -1).ToString("F3").PadLeft(8));
            for (var i = 0; i < r.Sections.Length; i++)
                sb.Append((r.Sections[i] / f).ToString("F4").PadLeft(i >= 5 ? 13 : 11));
            sb.Append((r.Draws / f).ToString("F1").PadLeft(7)).Append((r.Uploads / f / 1024).ToString("F0").PadLeft(7))
              .Append((r.UiSprites / f).ToString("F0").PadLeft(7)).Append((r.UiBatches / f).ToString("F2").PadLeft(6))
              .Append((r.Entities / f).ToString("F0").PadLeft(6)).Append(((r.AllocEnd - r.AllocStart) / f).ToString("F0").PadLeft(10))
              .Append((r.Gen0End - r.Gen0Start).ToString().PadLeft(6)).AppendLine();
        }
        try {
            File.WriteAllText(OutputPath, sb.ToString());
        } catch (Exception e) {
            Console.WriteLine($"[PERFTEST] could not write {OutputPath}: {e.Message}");
        }
        Console.WriteLine("[PERFTEST] done");
        Console.WriteLine(sb.ToString());
    }
}
