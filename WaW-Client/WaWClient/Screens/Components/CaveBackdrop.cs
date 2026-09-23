using System;
using System.Collections.Generic;
using WaWClient.Utils;
using WaW.UiLib.BuiltIn;
using WaW.UiLib.Core;
using WaW.UiLib.Data;

namespace WaWClient.Screens.Components;

// The main menu's background: one baked dark-cave picture (Content/Title/TitleMap.png, built from the RPGW Caves pack by
// Tools/BookUi/build_title_cave.py) stretched over the whole window, with a light layer of ambience on top. The picture is
// its own texture (TextureType.TitleBackground, registered by the startup plan in Main), not part of the UI atlas; the
// soft glow sprites used below live in a strip under the map in that same texture. Same Resize(width, height) contract as
// ForestBackdrop, which the character-select screen keeps using for its light forest map.
//
// Ambience (everything is generated from the same layout as the props, see CaveBackdropData):
//   - crystal glows that slowly pulse, faint purple glow under the mushroom clumps
//   - warm, flickering light on the lit amber rock
//   - a few slow drifts of mist
//   - glints twinkling on the pond, and spores that blink on and off at scattered spots
//   - Wizards and Warriors fighting waves of lightning-summoned monsters on both sides of the menu (CaveBattle)
// The ambience only animates alpha - the UI positions sprites in whole pixels, so anything that MOVES would step; fading in
// and out stays perfectly smooth. (The walkers do move, in whole pixels, which suits pixel-art sprites.) Tune with the constants below.
public sealed class CaveBackdrop : Container {

    // Overall strength of each kind of ambience (1 = as generated, 0 = off).
    private const float GlowStrength = 1.7f;
    private const float MistStrength = 1.3f;
    private const float SporeStrength = 1.0f;
    private const float GlintStrength = 1.0f;

    // How many spores are alive at once, and how long each blink lasts (ms, min..max).
    private const int SporeCount = 26;
    private const float SporeMinMs = 4200f;
    private const float SporeMaxMs = 9000f;
    private const int SporeSize = 20;

    private const int GlintCount = 14;
    private const int GlintSize = 15;

    // Glow strip order (see CaveBackdropData): teal, green, amber, purple, white, fog.
    private const int KindWhite = 4;
    private const int KindFog = 5;

    private readonly List<(ObjectRect Rect, CaveBackdropData.Glow Data)> _glows = [];
    private readonly List<(ObjectRect Rect, CaveBackdropData.Mist Data)> _mists = [];
    private readonly List<Spore> _spores = [];
    private readonly List<Glint> _glints = [];

    private sealed class Spore {
        public ObjectRect Rect;
        public float PeriodMs;
        public float Phase;
        public int Seed;
        public int Cycle = -1;
    }

    private sealed class Glint {
        public ObjectRect Rect;
        public float PeriodMs;
        public float Phase;
    }

    public CaveBackdrop() : base(new ContainerConfig { Width = Settings.DefaultScreenWidth, Height = Settings.DefaultScreenHeight }) {
        // The map itself: the top part of the texture, exactly the 1280x720 design canvas.
        AddChild(new ObjectRect(new ObjectRectConfig {
            Texture = Region(0, 0, CaveBackdropData.MapWidth, CaveBackdropData.MapHeight),
            Width = Settings.DefaultScreenWidth,
            Height = Settings.DefaultScreenHeight,
            OutlineEnabled = false,
            GlowEnabled = false
        }));

        // The skirmishes fought on the open floor either side of the menu, under all the glows and mist.
        AddChild(new CaveBattle());

        var rng = new Random(20260919);

        foreach (var m in CaveBackdropData.Mists) {
            var rect = MakeGlow(KindFog, m.X - m.Width / 2, m.Y - m.Height / 2, m.Width, m.Height);
            _mists.Add((rect, m));
        }

        foreach (var g in CaveBackdropData.Glows) {
            var rect = MakeGlow(g.Kind, g.X - g.Size / 2, g.Y - g.Size / 2, g.Size, g.Size);
            _glows.Add((rect, g));
        }

        var glints = CaveBackdropData.PondGlints;
        for (var i = 0; i < Math.Min(GlintCount, glints.Length); i++) {
            var (x, y) = glints[i];
            _glints.Add(new Glint {
                Rect = MakeGlow(KindWhite, x - GlintSize / 2, y - GlintSize / 2, GlintSize, GlintSize),
                PeriodMs = 2400f + (float)rng.NextDouble() * 2600f,
                Phase = (float)rng.NextDouble()
            });
        }

        // Spores come in a few tints (kind indexes: teal 0, green 1, amber 2, purple 3, white 4).
        int[] tints = [0, 1, 2, 3, KindWhite, 0, 2];
        for (var i = 0; i < SporeCount; i++) {
            _spores.Add(new Spore {
                Rect = MakeGlow(tints[i % tints.Length], 0, 0, SporeSize, SporeSize),
                PeriodMs = SporeMinMs + (float)rng.NextDouble() * (SporeMaxMs - SporeMinMs),
                Phase = (float)rng.NextDouble(),
                Seed = rng.Next(1, 100000)
            });
        }

        AddEventListener(Event.EnterFrame, OnFrame);
        OnFrame();
    }

    // One of the soft glow sprites in the strip under the map (teal 0, green 1, amber 2, purple 3, white 4, fog 5). The cell is read one texel
    // in from each edge: the texture is sampled LINEAR, the cells butt against the map's bottom row and against each other, so a fragment on
    // the quad's edge used to blend the neighbour in - a thin line the width of the sprite under every orb, a square round every crystal glow
    // (2026-09-22). The cells' own edge texels are fully transparent, so nothing is lost.
    internal static TextureInfo GlowTexture(int kind) => GlowRegion(kind);

    private static TextureInfo GlowRegion(int kind) =>
        Region(kind * CaveBackdropData.GlowSize + 1, CaveBackdropData.MapHeight + 1, CaveBackdropData.GlowSize - 2, CaveBackdropData.GlowSize - 2);

    // A region of the map texture, in texture pixels, pulled in by half a texel so a linear sample never straddles the region's edge.
    private static TextureInfo Region(int x, int y, int w, int h) => new(new AtlasPosition(
        (x + 0.5f) / CaveBackdropData.SheetWidth, (y + 0.5f) / CaveBackdropData.SheetHeight,
        (w - 1f) / CaveBackdropData.SheetWidth, (h - 1f) / CaveBackdropData.SheetHeight), TextureType.TitleBackground);

    private ObjectRect MakeGlow(int kind, int x, int y, int width, int height) {
        var rect = new ObjectRect(new ObjectRectConfig {
            Texture = GlowRegion(kind),
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Alpha = 0f,
            OutlineEnabled = false,
            GlowEnabled = false
        });
        AddChild(rect);
        return rect;
    }

    private static float Wave(double ms, float periodMs, float phase) =>
        0.5f + 0.5f * (float)Math.Sin((ms / periodMs + phase) * Math.Tau);

    // Two waves of different speeds added together: irregular like a flame, but still smooth.
    private static float Flicker(double ms, float periodMs, float phase) =>
        0.55f * Wave(ms, periodMs, phase) + 0.30f * Wave(ms, periodMs * 0.43f, phase * 1.7f) + 0.15f * Wave(ms, periodMs * 0.19f, phase * 2.9f);

    private void OnFrame() {
        var ms = (double)Stage.GameTime.TotalMs;

        foreach (var (rect, m) in _mists) {
            rect.Alpha = MistStrength * (m.Base + m.Swing * Wave(ms, m.PeriodMs, m.Phase)) * 0.6f;
        }

        foreach (var (rect, g) in _glows) {
            var wave = g.Flicker ? Flicker(ms, g.PeriodMs, g.Phase) : Wave(ms, g.PeriodMs, g.Phase);
            rect.Alpha = GlowStrength * (g.Base + g.Swing * wave);
        }

        foreach (var glint in _glints) {
            var w = Wave(ms, glint.PeriodMs, glint.Phase);
            glint.Rect.Alpha = GlintStrength * 0.85f * w * w * w * w;   // mostly dark, a brief bright twinkle
        }

        var spots = CaveBackdropData.SporeSpots;
        foreach (var spore in _spores) {
            var t = ms / spore.PeriodMs + spore.Phase;
            var cycle = (int)Math.Floor(t);
            var frac = (float)(t - cycle);
            if (cycle != spore.Cycle) {
                // A new blink: hop to a new spot (it is invisible right now, so the hop can't be seen).
                spore.Cycle = cycle;
                var (x, y) = spots[(int)(((long)spore.Seed * 7919 + (long)cycle * 104729) % spots.Length + spots.Length) % spots.Length];
                spore.Rect.X = x - SporeSize / 2;
                spore.Rect.Y = y - SporeSize / 2;
            }

            var s = (float)Math.Sin(frac * Math.PI);
            spore.Rect.Alpha = SporeStrength * 0.9f * s * s;
        }
    }

    // Stretches the fixed 1280x720 layout to exactly fill the real window (no letterbox bars).
    public void Resize(int width, int height) {
        ScaleX = width / (float)Settings.DefaultScreenWidth;
        ScaleY = height / (float)Settings.DefaultScreenHeight;
    }
}
