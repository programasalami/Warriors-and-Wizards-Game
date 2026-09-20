// Logger that writes straight to the browser console (Console.WriteLine), replacing the desktop console logger whose background
// thread cannot exist in single-threaded WebAssembly.
using Microsoft.Extensions.Logging;

namespace WarriorsWeb;

public sealed class WebLoggerProvider : ILoggerProvider {
    public ILogger CreateLogger(string categoryName) => new WebLogger(categoryName);
    public void Dispose() { }

    private sealed class WebLogger(string category) : ILogger {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) {
            var tag = logLevel switch { LogLevel.Trace => "TRACE", LogLevel.Debug => "DEBUG", LogLevel.Information => "INFO", LogLevel.Warning => "WARN", LogLevel.Error => "ERROR", _ => "FATAL" };
            Console.WriteLine($"[{tag}] {category}: {formatter(state, exception)}" + (exception != null ? "\n" + exception : ""));
        }
    }
}
