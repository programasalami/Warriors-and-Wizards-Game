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
using Common.Database.Models;
using Common.Messaging;
using Common.Resources.Config;
using Common.Resources.Xml;
using Common.Utilities;

#endregion

namespace AccountServer;

internal class Program {
    private static readonly Logger Log = new(typeof(Program));

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
            DbClient.Load(DatabaseConfig.Config.DbFile);

            ReleaseLocks(); // Release all account locks at startup

            listener.Prefixes.Add($"http://{config.Address}:{config.Port}/");
            listener.Start();
        }

        var semaphore = new SemaphoreSlim(200);

        while (true) {
            HttpListenerContext context;
            try {
                context = await listener.GetContextAsync(); // non-blocking — frees the loop immediately
            }
            catch (HttpListenerException) {
                break; // listener was stopped (e.g. on shutdown)
            }

            await semaphore.WaitAsync();

            _ = Task.Run(async () => {
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

        var staleAccounts = DbClient.Accounts
            .Find(acc => acc.LockOwner != Guid.Empty)
            .Where(acc => !liveServerIds.Contains(acc.LockOwner))
            .ToList();

        foreach (var acc in staleAccounts) {
            acc.LockOwner = Guid.Empty;
            DbClient.Accounts.Update(acc);
        }

        if (staleAccounts.Count > 0)
            Log.Info($"Released {staleAccounts.Count} stale account lock(s) from GameServer instances that did not reconnect.");
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

        NameValueCollection query;
        try {
            using var inputStream = context.Request.InputStream;
            using var reader = new StreamReader(inputStream, Encoding.UTF8);
            query = HttpUtility.ParseQueryString(await reader.ReadToEndAsync());
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