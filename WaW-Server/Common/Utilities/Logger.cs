#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Channels;

#endregion

namespace Common.Utilities;

public enum LogLevel {
    Info,
    Debug,
    Warn,
    Error,
    Fatal
}

// Server-side logger. Formatting happens on the calling thread; the console and file WRITES happen on one background thread.
// 2026-09-21 audit: they used to happen synchronously on the caller (the game thread included) - Console.WriteLine under a lock plus
// File.AppendAllLines (open + close per line). Selecting text in the console window pauses console output on Windows, which froze the
// whole game server for as long as the selection lasted (a 7.7 s tick was measured). Debug lines are skipped unless the environment
// variable WAW_DEBUG_LOG is set (they used to print in every Debug build - 20 lines a second per player from the sight system).
public class Logger : ILogger {
    private const int PADDING = 18;
    private const int QueueCapacity = 20_000;
    public const string DebugEnvVar = "WAW_DEBUG_LOG";

    private static readonly string CurrentDir = Directory.GetCurrentDirectory();
    private static readonly string LogDir = $"/logs/{Process.GetCurrentProcess().ProcessName}/";

    public static readonly bool DebugEnabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DebugEnvVar));

    private readonly record struct Entry(LogLevel Level, string Text, bool SaveToFile);

    private static readonly Channel<Entry> _queue = Channel.CreateBounded<Entry>(new BoundedChannelOptions(QueueCapacity) {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest     // a flood never blocks the game thread; the oldest lines are lost instead
    });
    private static long _dropped;
    private static readonly Thread _writer;
    private static readonly Dictionary<LogLevel, StreamWriter> _files = new();
    private static readonly object _flushLock = new();
    private static volatile bool _idle = true;

    private readonly string _loggerName;

    static Logger() {
        // Create directories for the log files if they don't exist
        foreach (var level in Enum.GetValues(typeof(LogLevel))) {
            var path = $"{CurrentDir}{LogDir}{level.ToString().ToLower()}";
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        _writer = new Thread(WriterLoop) { IsBackground = true, Name = "Logger", Priority = ThreadPriority.BelowNormal };
        _writer.Start();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush(2000);
    }

    public Logger(Type type)
        : this(type.Name) { }

    public Logger(string name) {
        _loggerName = name;
    }

    public void Info(object obj, bool saveToFile = true) {
        Log(obj.ToString(), LogLevel.Info, saveToFile, _loggerName);
    }

    public void Debug(object obj, bool saveToFile = false) {
        if (!DebugEnabled) return;
        Log(obj.ToString(), LogLevel.Debug, saveToFile, _loggerName);
    }

    public void Warn(object obj, bool saveToFile = true) {
        Log(obj.ToString(), LogLevel.Warn, saveToFile, _loggerName);
    }

    public void Error(object obj, bool saveToFile = true) {
        Log(obj.ToString(), LogLevel.Error, saveToFile, _loggerName);
    }

    public void Fatal(object obj, bool saveToFile = true) {
        Log(obj.ToString(), LogLevel.Fatal, saveToFile, _loggerName);
        Flush(2000);    // the process is probably about to die: get the line out
    }

    public static void Info(object obj, string loggerName = "Logger", bool saveToFile = false) {
        Log(obj.ToString(), LogLevel.Info, saveToFile, loggerName);
    }

    public static void Debug(object obj, string loggerName = "Logger", bool saveToFile = false) {
        if (!DebugEnabled) return;
        Log(obj.ToString(), LogLevel.Debug, saveToFile, loggerName);
    }

    public static void Warn(object obj, string loggerName = "Logger", bool saveToFile = false) {
        Log(obj.ToString(), LogLevel.Warn, saveToFile, loggerName);
    }

    public static void Error(object obj, string loggerName = "Logger", bool saveToFile = false) {
        Log(obj.ToString(), LogLevel.Error, saveToFile, loggerName);
    }

    public static void Fatal(object obj, string loggerName = "Logger", bool saveToFile = false) {
        Log(obj.ToString(), LogLevel.Fatal, saveToFile, loggerName);
        Flush(2000);
    }

    public void Log(LogLevel level, object obj, bool saveToFile = false) {
        if (level == LogLevel.Debug && !DebugEnabled) return;
        Log(obj.ToString(), level, saveToFile, _loggerName);
    }

    // Waits (up to timeoutMs) until everything queued so far has been written. Used on Fatal and at process exit.
    public static void Flush(int timeoutMs) {
        var deadline = Environment.TickCount64 + timeoutMs;
        while ((_queue.Reader.Count > 0 || !_idle) && Environment.TickCount64 < deadline)
            Thread.Sleep(5);
        lock (_flushLock) {
            foreach (var file in _files.Values) {
                try { file.Flush(); } catch (IOException) { }
            }
        }
    }

    private static void Log(string text, LogLevel level, bool saveToFile, string loggerName) {
        var lvl = level.ToString().ToUpper();
        var lvlPad = lvl.Length + (7 - lvl.Length);
        const int maxLoggerLen = PADDING - 2;

        if (loggerName.Length > maxLoggerLen)
            loggerName = loggerName.Substring(0, maxLoggerLen - 3) + "...";

        var senderPad = loggerName.Length + (PADDING - loggerName.Length);
        text = $"{DateTime.Now.TimeOfDay}  {lvl.PadRight(lvlPad) + loggerName.PadRight(senderPad) + text}";

        if (!_queue.Writer.TryWrite(new Entry(level, text, saveToFile)))
            Interlocked.Increment(ref _dropped);
    }

    private static void WriterLoop() {
        var reader = _queue.Reader;
        while (true) {
            Entry entry;
            try {
                if (!reader.TryRead(out entry)) {
                    _idle = true;
                    entry = reader.ReadAsync().AsTask().GetAwaiter().GetResult();
                }
            }
            catch (Exception) {
                return;     // channel completed: process is exiting
            }

            _idle = false;
            Write(entry);

            var dropped = Interlocked.Exchange(ref _dropped, 0);
            if (dropped > 0)
                Write(new Entry(LogLevel.Warn, $"{DateTime.Now.TimeOfDay}  WARN   Logger            {dropped} log line(s) dropped: the log queue was full", true));
        }
    }

    private static void Write(Entry entry) {
        try {
            Console.BackgroundColor = GetBackColor(entry.Level);
            Console.ForegroundColor = GetForeColor(entry.Level);
            Console.WriteLine(entry.Text);
        }
        catch (IOException) { }

        if (!entry.SaveToFile)
            return;

        try {
            lock (_flushLock) {
                if (!_files.TryGetValue(entry.Level, out var file)) {
                    var path = $"{CurrentDir}{LogDir}{entry.Level.ToString().ToLower()}/log.txt";
                    file = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                    _files[entry.Level] = file;
                }
                file.WriteLine(entry.Text);
            }
        }
        catch (IOException) { } // uhhh, leave this here ok?
        catch (UnauthorizedAccessException) { }
    }

    private static ConsoleColor GetBackColor(LogLevel level) {
        switch (level) {
            case LogLevel.Info:
            case LogLevel.Debug:
            case LogLevel.Warn:
            case LogLevel.Fatal:
                return ConsoleColor.Black;
            case LogLevel.Error:
                return ConsoleColor.Red;
            default:
                throw new ArgumentException($"Invalid LogLevel '{level}'");
        }
    }

    private static ConsoleColor GetForeColor(LogLevel level) {
        switch (level) {
            case LogLevel.Info:
                return ConsoleColor.Gray;
            case LogLevel.Debug:
                return ConsoleColor.DarkGray;
            case LogLevel.Warn:
                return ConsoleColor.Yellow;
            case LogLevel.Error:
                return ConsoleColor.White;
            case LogLevel.Fatal:
                return ConsoleColor.White;
            default:
                throw new ArgumentException($"Invalid LogLevel '{level}'");
        }
    }
}
