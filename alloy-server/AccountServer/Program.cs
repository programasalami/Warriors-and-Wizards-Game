#region

using System;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using AccountServer.Handlers;
using AccountServer.Messaging;
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

        while (true) {
            HttpListenerContext context;
            try {
                context = await listener.GetContextAsync(); // non-blocking — frees the loop immediately
            }
            catch (HttpListenerException) {
                break; // listener was stopped (e.g. on shutdown)
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
            context.Response.ContentType = "text/*";
            await context.Response.OutputStream.WriteAsync(data, 0, data.Length);
            context.Response.Close();
        }
        catch (HttpListenerException) { }
    }

    private static void UnhandledException(object sender, UnhandledExceptionEventArgs args) {
        Log.Fatal(args.ExceptionObject);
    }

    private static string GetContextIP(HttpListenerContext context) {
        return context.Request.RemoteEndPoint.ToString().Split(':')[0];
    }
}