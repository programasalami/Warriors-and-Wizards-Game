using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace WaWClient.Logging;

public static class Logging {

    public static readonly ILoggerFactory Factory;

    static Logging() {
        Factory = LoggerFactory.Create(builder => 
                builder.AddConsole(options => { options.FormatterName = SingleLineConsoleFormatter.FormatterName; })
                .AddConsoleFormatter<SingleLineConsoleFormatter, ConsoleFormatterOptions>()
                .SetMinimumLevel(MinimumLevel)
        );
    }

    // Information by default; set the environment variable WAW_LOG=debug (or =trace) for the chatty levels. Debug builds used to log
    // at Trace from the console sink on the render thread (2026-09-21 audit).
    private static LogLevel MinimumLevel =>
        Environment.GetEnvironmentVariable("WAW_LOG")?.ToLowerInvariant() switch {
            "trace" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            _ => LogLevel.Information
        };

    extension(ILogger logger) {
        public static ILoggerFactory Factory => Factory;
        
        public static ILogger CreateLogger(string name) => Factory.CreateLogger(name);
        
        public void Trace(string message) => logger.Log(LogLevel.Trace, message);
        public void Trace(string message, Exception exception) => logger.Log(LogLevel.Trace, message, exception);
        public void Debug(string message) => logger.Log(LogLevel.Debug, message);
        public void Debug(string message, Exception exception) => logger.Log(LogLevel.Debug, message, exception);
        public void Info(string message) => logger.Log(LogLevel.Information, message);
        public void Info(string message, Exception exception) => logger.Log(LogLevel.Information, message, exception);
        public void Warn(string message) => logger.Log(LogLevel.Warning, message);
        public void Warn(string message, Exception exception) => logger.Log(LogLevel.Warning, message, exception);
        public void Error(string message) => logger.Log(LogLevel.Error, message);
        public void Error(string message, Exception exception) => logger.Log(LogLevel.Error, message, exception);
        public void Panic(string message) => logger.Log(LogLevel.Critical, message);
        public void Panic(string message, Exception exception) => logger.Log(LogLevel.Critical, message, exception);
    }
}

#pragma warning disable CS9113 // required by AddConsoleFormatter<T, TOptions> DI registration, unused by this formatter
public sealed class SingleLineConsoleFormatter(IOptions<ConsoleFormatterOptions> options) : ConsoleFormatter(FormatterName) {
#pragma warning restore CS9113
    public const string FormatterName = "wawSingleline";

    private const string Ansi = "\e[";
    private const string AnsiStop = "m";
    private const string Reset = $"{Ansi}0{AnsiStop}";
    private const string Background = "40"; // Black

    private const string FontNorm = "0;";
    private const string FontBold = "1;";

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider scopeProvider, TextWriter textWriter) {
        var timestamp = DateTimeOffset.Now.ToString("HH:mm:ss.ffff");
        var level = logEntry.LogLevel switch {
            LogLevel.Trace       => $"{Ansi}{FontNorm}90;{Background}{AnsiStop}TRACE{Reset}",
            LogLevel.Debug       => $"{Ansi}{FontNorm}34;{Background}{AnsiStop}DEBUG{Reset}",
            LogLevel.Information => $"{Ansi}{FontNorm}32;{Background}{AnsiStop}INFO{Reset} ",
            LogLevel.Warning     => $"{Ansi}{FontBold}33;{Background}{AnsiStop}WARN{Reset} ",
            LogLevel.Error       => $"{Ansi}{FontBold}31;{Background}{AnsiStop}ERROR{Reset}",
            LogLevel.Critical    => $"{Ansi}{FontBold}35;{Background}{AnsiStop}CRIT{Reset} ",
            _                    => $"{Ansi}{FontNorm}37;{Background}{AnsiStop}NONE{Reset} "
        };

        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);

        textWriter.WriteLine($"[{timestamp}] {level} {logEntry.Category}[{logEntry.EventId.Id}]:    {message}");

        if (logEntry.Exception is not null) {
            textWriter.WriteLine(logEntry.Exception);
        }
    }
}