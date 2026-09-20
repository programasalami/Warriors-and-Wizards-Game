// Browser version of Alloy.Engine.GameWindow: same protected API the client's Main derives from, but instead of owning a blocking
// loop, the page's requestAnimationFrame calls Tick(). Context creation happens in JavaScript (wwwroot/gl.js).
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTK.Platform;

namespace Alloy.Engine;

public abstract class GameWindow {
    private static ILogger _logger;
    internal static GameWindow Instance;

    public readonly WindowHandle Window = new();
    public readonly OpenGLContextHandle Context = new();

    protected double TargetFrameTime = 1000d / 60;

    private bool _running;
    private double _previousMs = -1;
    private double _totalMs;

    protected GameWindow(Version openglVersion, ILoggerFactory logFactory, LogLevel minOpenTkLogLevel = LogLevel.Warning) {
        _logger = logFactory.CreateLogger(nameof(GameWindow));
        Instance = this;
        Toolkit.Event.EventRaised += HandleEvents;
    }

    protected virtual void Initialize() { }
    protected virtual void LoadContent() { }
    protected abstract void Update(GameTime gameTime);
    protected abstract void Draw(GameTime gameTime);
    protected void Exit() { }
    protected virtual void Stop() { }
    protected abstract void HandleEvents(EventArgs args);

    // Called once by the host: runs Initialize/LoadContent, then the page starts driving Tick().
    public void Run() {
        Initialize();
        LoadContent();
        _running = true;
    }

    internal static void Tick(double nowMs) {
        var w = Instance;
        if (w == null || !w._running) return;
        if (w._previousMs < 0) w._previousMs = nowMs;
        var delta = Math.Min(nowMs - w._previousMs, 250);
        w._previousMs = nowMs;
        w._totalMs += delta;
        var gameTime = new GameTime(w._totalMs, delta);
        w.Update(gameTime);
        w.Draw(gameTime);
    }
}
