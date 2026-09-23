#region

using System;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using AccountServer.Messaging;
using AccountServer.Systems;
using Common.Database;
using Common.Messaging;
using Common.Resources.Config;
using Common.Resources.Xml;
using Common.Utilities;

#endregion

namespace AccountServer;

internal class Program {
    private static readonly Logger Log = new(typeof(Program));

    // Generous for our small form-encoded requests (username/password/charId etc.),
    // but bounds how much memory a single request body can force us to buffer.
    private const int MaxRequestBodyBytes = 64 * 1024;

    private static async Task Main(string[] args) {
        ThreadPool.SetMinThreads(1000, 1000);

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        Console.Title = $"Alloy Server v{version} - AccountServer";
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        AppDomain.CurrentDomain.UnhandledException += UnhandledException;

        var listener = new HttpListener();
        var config = AppEngineConfig.Config;
        using (var timer = new EasyTimer(LogLevel.Info, "Starting server...",
                   $"Listening on {config.Address + ":" + config.Port} ({EasyTimer.Time})")) {
            EnumUtils.Load();
            RequestHandler.Load();
            XmlLibrary.Load(config.XmlsDir);

            _ = IpcServer.StartAsync<AccServerRpcHandler>();
            DbClient.Load();

            ReleaseLocks(); // Release all account locks at startup

            listener.Prefixes.Add($"http://{config.Address}:{config.Port}/");
            listener.Start();
        }

        var semaphore = new SemaphoreSlim(config.MaxConcurrentRequests);
        Shutdown.Install(() => { try { listener.Stop(); } catch { } });   // makes GetContextAsync below throw, which ends the loop

        while (true) {
            HttpListenerContext context;
            try {
                context = await listener.GetContextAsync(); // non-blocking — frees the loop immediately
            }
            catch (HttpListenerException) {
                break; // listener was stopped (e.g. on shutdown)
            }
            catch (ObjectDisposedException) {
                break;
            }

            // Dispatch first, wait for a processing slot inside the task. Waiting on the
            // semaphore here (before looping back to GetContextAsync) would stall accepting
            // new connections entirely once the limit is hit, instead of just queuing them.
            _ = Task.Run(async () => {
                await semaphore.WaitAsync();
                try {
                    await HandleRequestAsync(context);
                }
                finally {
                    semaphore.Release();
                }
            });
        }

        await Shutdown.FinishAsync();
    }

    // Ctrl+C, SIGTERM (systemd stop on the VPS) and the console window closing all end here: the HTTP listener stops, then the
    // batched database writes (DbWriter<T>) are flushed before the process ends. 2026-09-21 audit (H9): nothing used to be flushed.
    private static class Shutdown {
        private static readonly ManualResetEventSlim _done = new(false);
        private static int _requested;
        private static Action _stopListener;

        public static void Install(Action stopListener) {
            _stopListener = stopListener;
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; Request(); };
            try {
                PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; Request(); });
            }
            catch (Exception) { /* not supported on this platform: ProcessExit below still runs */ }
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { Request(); _done.Wait(4000); };
        }

        private static void Request() {
            if (Interlocked.Exchange(ref _requested, 1) == 1)
                return;
            Log.Info("Shutdown requested: stopping the HTTP listener and flushing database writes...");
            _stopListener?.Invoke();
        }

        public static async Task FinishAsync() {
            try {
                await DbClient.Dispose();
                Log.Info("Database writes flushed. Bye.");
            }
            catch (Exception e) {
                Log.Error($"Error while flushing on shutdown: {e}");
            }
            finally {
                Logger.Flush(2000);
                _done.Set();
            }
        }
    }

    private static void ReleaseLocks() {
        _ = ReleaseStaleLocksAsync();
    }

    // Accounts stay locked to whichever GameServer instance owns them, to stop the
    // same account being played from two places at once. If we cleared every lock
    // unconditionally on every AccountServer restart, an account whose GameServer is
    // still up and running (just temporarily disconnected from AccountServer) could
    // get claimed by a second login while the original session is still active. Give
    // GameServer instances a short window to reconnect and reassert their locks
    // (see GameServer.Program.MaintainAccountServerConnectionAsync) before treating
    // any remaining lock as abandoned.
    private static async Task ReleaseStaleLocksAsync() {
        await Task.Delay(TimeSpan.FromSeconds(10));

        var liveServerIds = IpcServer.Clients.Keys.ToHashSet();
        var released = await AccountLockManager.ReleaseStaleLocksAsync(liveServerIds);

        if (released > 0)
            Log.Info($"Released stale account locks from {released} GameServer instance(s) that did not reconnect.");
    }

    private static async Task HandleRequestAsync(HttpListenerContext context) {
        var request = context.Request.Url.LocalPath;
        var ip = GetContextIP(context);

        if (!RequestHandler.Exists(request)) {
            Log.Warn($"Unknown request '{request}' from '{ip}'");
            try {
                context.Response.Close();
            }
            catch { }

            return;
        }

        Log.Debug($"Received '{request}' from '{ip}'");

        if (context.Request.ContentLength64 > MaxRequestBodyBytes) {
            Log.Warn($"Rejected '{request}' from '{ip}': declared body size {context.Request.ContentLength64} exceeds the {MaxRequestBodyBytes}-byte limit");
            context.Response.StatusCode = 413;
            try { context.Response.Close(); } catch { }
            return;
        }

        NameValueCollection query;
        try {
            using var inputStream = context.Request.InputStream;
            using var reader = new StreamReader(inputStream, Encoding.UTF8);

            var buffer = new char[MaxRequestBodyBytes];
            var totalRead = 0;
            int charsRead;
            while ((charsRead = await reader.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead))) > 0) {
                totalRead += charsRead;
                if (totalRead >= buffer.Length) {
                    Log.Warn($"Rejected '{request}' from '{ip}': body exceeded the {MaxRequestBodyBytes}-byte limit while reading");
                    context.Response.StatusCode = 413;
                    try { context.Response.Close(); } catch { }
                    return;
                }
            }

            query = HttpUtility.ParseQueryString(new string(buffer, 0, totalRead));
            // The Portal's public API is called with plain GETs from a browser: merge the URL's query string in (the game client
            // always POSTs a form, so this changes nothing for it).
            if (!string.IsNullOrEmpty(context.Request.Url.Query)) {
                var urlQuery = HttpUtility.ParseQueryString(context.Request.Url.Query);
                foreach (var key in urlQuery.AllKeys)
                    if (key != null && query[key] == null)
                        query[key] = urlQuery[key];
            }
        }
        catch (HttpListenerException) {
            return;
        }

        string response;
        try {
            response = await RequestHandler.Handle(request, ip, query);
        }
        catch (Exception e) {
            Log.Error($"Unhandled exception while handling '{request}': {e}");
            response = "<Error>Internal error.</Error>";
        }
        response ??= "<Error>Internal error.</Error>";

        try {
            var data = Encoding.UTF8.GetBytes(response);
            // JSON answers (the Portal's /public/* handlers) get their real type; everything else stays as it was
            var trimmed = response.TrimStart();
            context.Response.ContentType = trimmed.StartsWith('{') || trimmed.StartsWith('[') ? "application/json; charset=utf-8" : "text/*";
            await context.Response.OutputStream.WriteAsync(data, 0, data.Length);
            context.Response.Close();
        }
        catch (HttpListenerException) { }
    }

    private static void UnhandledException(object sender, UnhandledExceptionEventArgs args) {
        Log.Fatal(args.ExceptionObject);
    }

    // The caller's address for rate limits and logs. Requests that nginx forwards (the browser client's /api, The Portal's /api/public)
    // all arrive from this machine, so for those the header nginx sets (X-Forwarded-For) names the real visitor; the header is
    // trusted only when the connection really comes from this machine, never from the internet.
    private static string GetContextIP(HttpListenerContext context) {
        var remote = context.Request.RemoteEndPoint;
        var address = remote?.Address.ToString() ?? "";
        var forwarded = context.Request.Headers["X-Forwarded-For"];
        if (!string.IsNullOrWhiteSpace(forwarded) && remote != null && IsThisMachine(remote.Address))
            return forwarded.Split(',')[0].Trim();
        return address;
    }

    private static bool IsThisMachine(IPAddress address) {
        if (IPAddress.IsLoopback(address)) return true;
        try { return Dns.GetHostAddresses(Dns.GetHostName()).Contains(address) || address.ToString() == AppEngineConfig.Config?.Address; }
        catch (Exception) { return false; }
    }
}