using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WaW.Common;
using WaW.Engine.Utils;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics;
using OpenTK.Platform;

namespace WaW.Engine;

public abstract partial class GameWindow {

    private static ILogger _logger;

    public readonly WindowHandle Window;
    public readonly OpenGLContextHandle Context;

    protected double TargetFrameTime = 1000d / 60;

    private bool _exitFlag;

    protected GameWindow(Version openglVersion, ILoggerFactory logFactory, LogLevel minOpenTkLogLevel = LogLevel.Warning) {
        _logger = logFactory.CreateLogger(nameof(GameWindow));

        var options = new ToolkitOptions {
            FeatureFlags = ToolkitFlags.EnableOpenGL,
            Logger = new CustomLogger(logFactory.CreateLogger(nameof(Toolkit)), minOpenTkLogLevel)
        };

        Toolkit.Init(options);

        var hints = new OpenGLGraphicsApiHints {
            Version = openglVersion
        };

        if (!TryCreateContext(hints, out Window, out Context)) {
            _exitFlag = true;
            return;
        }

        Toolkit.OpenGL.SetCurrentContext(Context);
        GLLoader.LoadBindings(Toolkit.OpenGL.GetBindingsContext(Context));

        _logger.Log(LogLevel.Information, "GL context: {0} ({1}, {2}), GLSL {3}",
            GL.GetString(StringName.Version), GL.GetString(StringName.Renderer),
            GL.GetString(StringName.Vendor), GL.GetString(StringName.ShadingLanguageVersion));

        EnableDebugOutput();

        Toolkit.Event.EventRaised += HandleEvents;
    }

    protected virtual void Initialize() { }

    protected virtual void LoadContent() { }

    protected abstract void Update(GameTime gameTime);

    protected abstract void Draw(GameTime gameTime);

    protected void Exit() => _exitFlag = true;

    protected virtual void Stop() { }

    protected abstract void HandleEvents(EventArgs args);

    public void Run() {
        if (_exitFlag) { // context creation failed
            return;
        }

        using (new TimedScope(_logger, null, "Initialize took {0}")) {
            Initialize();
        }

        using (new TimedScope(_logger, null, "LoadContent took {0}")) {
            LoadContent();
        }

        if (OperatingSystem.IsWindows()) {
            SetWindowsTimerResolution();
        }

        ReadRefreshRate();
        var gpuTimer = new GpuFrameTimer();

        var clockFrequency = 1000d / Stopwatch.Frequency;
        var previousTicks = Stopwatch.GetTimestamp();
        var totalMs = 0d;

        while (true) {
            Toolkit.Window.ProcessEvents(false);

            if (_exitFlag) {
                break;
            }

            var currentTicks = Stopwatch.GetTimestamp();
            var deltaMs = (currentTicks - previousTicks) * clockFrequency;
            previousTicks = currentTicks;
            totalMs += deltaMs;

            var gameTime = new GameTime(totalMs, deltaMs);

            // Where the frame's time goes (FrameTiming, read by the client's FPS readout): the game's own CPU work, the swap (VSync
            // wait / GPU backlog), the cap sleep, and the GPU's own time from a timer query.
            gpuTimer.Begin();
            Update(gameTime);
            Draw(gameTime);
            gpuTimer.End();
            var afterWork = Stopwatch.GetTimestamp();
            FrameTiming.WorkMs = (afterWork - currentTicks) * clockFrequency;

            Toolkit.OpenGL.SwapBuffers(Context);
            var afterSwap = Stopwatch.GetTimestamp();
            FrameTiming.SwapMs = (afterSwap - afterWork) * clockFrequency;
            FrameTiming.GpuMs = gpuTimer.CollectMs();

            FrameTiming.SleepMs = 0;
            if (TargetFrameTime > 0) {
                var workMs = (afterSwap - currentTicks) * clockFrequency;
                var remainingMs = TargetFrameTime - workMs;
                if (remainingMs > 0) {
                    OpenTK.Core.Utils.AccurateSleep(remainingMs / 1000.0d, 2);
                    FrameTiming.SleepMs = (Stopwatch.GetTimestamp() - afterSwap) * clockFrequency;
                }
            }
        }

        gpuTimer.Delete();

        if (OperatingSystem.IsWindows()) {
            RestoreWindowsTimerResolution();
        }

        Stop();

        Toolkit.OpenGL.DestroyContext(Context);
        Toolkit.Window.Destroy(Window);
    }

    private void ReadRefreshRate() {
        try {
            var hz = Toolkit.Display.GetRefreshRate(Toolkit.Window.GetDisplay(Window));
            FrameTiming.RefreshRate = (int)Math.Round(Convert.ToDouble(hz));
        } catch (Exception e) {
            _logger.Log(LogLevel.Debug, "Refresh rate unavailable: {0}", e.Message);
        }
    }

    // GL_TIME_ELAPSED queries around Update + Draw, a small ring so a result is only read once it is ready (never a stall). A query that
    // fails to create (no timer-query support) turns the whole thing off and FrameTiming.GpuMs stays -1.
    private sealed class GpuFrameTimer {
        private const int Ring = 4;
        private readonly int[] _queries = new int[Ring];
        private readonly bool[] _pending = new bool[Ring];
        private int _next;
        private bool _enabled;
        private double _lastMs = -1;

        public GpuFrameTimer() {
            try {
                GL.GenQueries(Ring, _queries);
                _enabled = _queries[0] != 0 && GL.GetError() == ErrorCode.NoError;
            } catch {
                _enabled = false;
            }
        }

        public void Begin() {
            if (!_enabled || _pending[_next])
                return;
            GL.BeginQuery(QueryTarget.TimeElapsed, _queries[_next]);
            _pending[_next] = true;
        }

        public void End() {
            if (!_enabled || !_pending[_next])
                return;
            GL.EndQuery(QueryTarget.TimeElapsed);
            _next = (_next + 1) % Ring;
        }

        // The newest finished query's time in ms, or the previous value while none has finished yet.
        public double CollectMs() {
            if (!_enabled)
                return -1;
            for (var i = 0; i < Ring; i++) {
                var idx = (_next + i) % Ring;      // oldest first
                if (!_pending[idx])
                    continue;
                GL.GetQueryObjecti64(_queries[idx], QueryObjectParameterName.QueryResultAvailable, out long ready);
                if (ready == 0)
                    break;
                GL.GetQueryObjecti64(_queries[idx], QueryObjectParameterName.QueryResult, out long ns);
                _pending[idx] = false;
                _lastMs = ns / 1_000_000d;
            }
            return _lastMs;
        }

        public void Delete() {
            if (!_enabled)
                return;
            for (var i = 0; i < Ring; i++)
                GL.DeleteQuery(_queries[i]);
            _enabled = false;
        }
    }

    private static bool TryCreateContext(OpenGLGraphicsApiHints hints, out WindowHandle window, out OpenGLContextHandle context) {
        window = Toolkit.Window.Create(hints);
        context = null;

        try {
            context = Toolkit.OpenGL.CreateFromWindow(window);
        } catch {
            Toolkit.Dialog.ShowMessageBox(window, "OpenGL creation failure", $"Application requires a minimum opengl version of {hints.Version.Major}.{hints.Version.Minor}", MessageBoxType.Information);
            return false;
        }

        return true;
    }

    [Conditional("DEBUG")]
    private static void EnableDebugOutput() {
        GL.DebugMessageCallback(OnDebugMessage, nint.Zero);
        GL.DebugMessageControl(DebugSource.DebugSourceApi, DebugType.DebugTypeOther, DebugSeverity.DontCare, 1, [131185], false);
        GL.Enable(EnableCap.DebugOutput);
        GL.Enable(EnableCap.DebugOutputSynchronous);
    }

    private static void OnDebugMessage(DebugSource source, DebugType type, uint id, DebugSeverity severity, int length, nint pmessage, nint userParam) {
        var message = Marshal.PtrToStringAnsi(pmessage, length);
        _logger.Log(LogLevel.Warning, "[{0} source={1} type={2} id={3}] {4}", severity, source, type, id, message);
    }

    #region Windows timer resolution

    /* Mix of opentk's GameWindow and improvements from PR #27 */

    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static partial uint TimeBeginPeriod(uint ms);

    [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static partial uint TimeEndPeriod(uint ms);

    [SupportedOSPlatform("windows")]
    // 1 ms timer resolution for the frame-pacing sleep. The render thread is deliberately NOT pinned to CPU core 0 any more
    // (2026-09-21 audit): on a 2-core laptop that also hosts the game servers it fought every other process for that one core.
    private void SetWindowsTimerResolution() {
        TimeBeginPeriod(1);
    }

    [SupportedOSPlatform("windows")]
    private void RestoreWindowsTimerResolution() {
        TimeEndPeriod(1);   // was TimeBeginPeriod again, so the resolution was never released
    }

    #endregion
}