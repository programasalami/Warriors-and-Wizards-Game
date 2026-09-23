using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace WarriorsAndWizards.Launcher;

// The website's floating embers (2026-09-23, by request): small glowing dots - mostly ember orange, some gold, a few purple - drifting slowly
// up the whole window with a little sway and flicker, behind everything and never in the way of a click. About 30 redraws a second of a few
// dozen soft circles, only while the window is shown.
public sealed class EmberField : Control {
    private const int Count = 46;
    private const double FrameSeconds = 1.0 / 30;

    private static readonly Color Ember = Color.Parse("#FF7A2F");
    private static readonly Color Gold = Color.Parse("#FFC857");
    private static readonly Color Violet = Color.Parse("#9B4DFF");

    private struct Spark {
        public double X, Y, Speed, Size, Phase, Sway, Flicker;
        public int Colour;
    }

    private readonly Spark[] _sparks = new Spark[Count];
    private readonly IBrush[] _glows;
    private readonly Random _random = new();
    private readonly DispatcherTimer _timer;
    private double _time;
    private bool _seeded;

    public EmberField() {
        IsHitTestVisible = false;
        _glows = [Glow(Ember), Glow(Gold), Glow(Violet)];
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(FrameSeconds), DispatcherPriority.Render, (_, _) => Step());
    }

    // a soft dot: a bright core fading to nothing at the edge of its circle
    private static IBrush Glow(Color c) => new RadialGradientBrush {
        GradientStops = {
            new GradientStop(Color.FromArgb(235, c.R, c.G, c.B), 0),
            new GradientStop(Color.FromArgb(120, c.R, c.G, c.B), 0.25),
            new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1)
        }
    };

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Seed(double w, double h) {
        for (var i = 0; i < Count; i++)
            _sparks[i] = NewSpark(w, _random.NextDouble() * h);
        _seeded = true;
    }

    private Spark NewSpark(double w, double y) {
        var roll = _random.NextDouble();
        return new Spark {
            X = _random.NextDouble() * w,
            Y = y,
            Speed = 7 + _random.NextDouble() * 16,           // pixels per second, upward
            Size = 1.2 + _random.NextDouble() * 1.8,          // core radius; the glow is four times as wide
            Phase = _random.NextDouble() * Math.PI * 2,
            Sway = 4 + _random.NextDouble() * 10,
            Flicker = 1.5 + _random.NextDouble() * 2.5,
            Colour = roll < 0.72 ? 0 : roll < 0.9 ? 1 : 2
        };
    }

    private void Step() {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;
        if (!_seeded)
            Seed(w, h);

        _time += FrameSeconds;
        for (var i = 0; i < Count; i++) {
            ref var s = ref _sparks[i];
            s.Y -= s.Speed * FrameSeconds;
            if (s.Y < -12)
                s = NewSpark(w, h + 12);                     // gone off the top: a new one rises from below
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context) {
        if (!_seeded)
            return;
        foreach (var s in _sparks) {
            var x = s.X + Math.Sin(_time * 0.6 + s.Phase) * s.Sway;
            var glow = s.Size * 4;
            var alpha = 0.55 + 0.45 * Math.Sin(_time * s.Flicker + s.Phase);
            using (context.PushOpacity(Math.Clamp(alpha, 0.1, 1) * 0.8))
                context.DrawEllipse(_glows[s.Colour], null, new Point(x, s.Y), glow, glow);
        }
    }
}
