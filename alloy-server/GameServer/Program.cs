using System.Globalization;
using System.Reflection;
using Common.Messaging;
using Common.Resources.Config;
using Common.Resources.World;
using Common.Resources.Xml;
using Common.Utilities;
using GameServer.Game;
using GameServer.Game.Systems.Chat.Commands;
using GameServer.Game.Systems.Behaviors;
using GameServer.Game.Network;
using GameServer.Messaging;
using StreamJsonRpc;

namespace GameServer;

public class Program {
    private static readonly Logger _log = new(typeof(Program));

    public static readonly Guid Guid = Guid.NewGuid();
    
    public static IAccountServerRpc AccountServerRpc { get; private set; }
    
    public static async Task Main(string[] args) {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        Console.Title = $"Alloy Server v{version} - GameServer";
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        
        AppDomain.CurrentDomain.UnhandledException += UnhandledException;

        var config = GameServerConfig.Config;
        using (var timer = new EasyTimer(LogLevel.Info, "Starting server...", $"Listening on port {config.Port} ([TIME])")) {
            EnumUtils.Load();
            XmlLibrary.Load(config.XmlsDir);
            MerchantsLibrary.Load(config.MerchantsDir);
            WorldLibrary.Load(config.WorldsDir);
            BehaviorLibrary.Load();
            CommandManager.Load();

            var (session, rpc) = await IpcClient.ConnectAsync(new GameServerRpcHandler(), TaskUtils.Timeout(5));
            AccountServerRpc = rpc;
            await AccountServerRpc.GameServerConnected(Guid);
            _log.Info($"[RPC] Connected to AccountServer. GUID: {Guid}");

            _ = MaintainAccountServerConnectionAsync(session);

            RealmManager.Init();

            // Start the socket server to accept and manage TCP connections
            SocketServer.Start(config.Port, config.MaxPlayers);
        }

        InstallShutdownHandlers();
        GameLogic.Run(config.MsPT);
        await GameLogic.ShutdownAsync();
        Logger.Flush(2000);
    }

    // Ctrl+C, SIGTERM (systemd stop on the VPS) and the console window closing end the game loop cleanly: characters are saved,
    // players are told, then the process exits. 2026-09-21 audit (H9). ProcessExit waits a few seconds for that to finish.
    private static void InstallShutdownHandlers() {
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; GameLogic.RequestStop(); };
        try {
            System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM,
                ctx => { ctx.Cancel = true; GameLogic.RequestStop(); });
        }
        catch (Exception) { /* not supported here: ProcessExit below still runs */ }
        AppDomain.CurrentDomain.ProcessExit += (_, _) => {
            GameLogic.RequestStop();
            GameLogic.ShutdownCompleted.Wait(6000);
        };
    }
    
    // If the RPC pipe to AccountServer ever drops (e.g. AccountServer restarts),
    // keep retrying so this instance can reassert its account locks instead of
    // leaving them stuck until this GameServer is also restarted.
    private static async Task MaintainAccountServerConnectionAsync(JsonRpc session) {
        while (true) {
            try {
                await session.Completion;
            }
            catch {
                // Connection dropped; fall through to reconnect below.
            }

            _log.Warn("[RPC] Lost connection to AccountServer. Attempting to reconnect...");

            while (true) {
                try {
                    IAccountServerRpc rpc;
                    (session, rpc) = await IpcClient.ConnectAsync(new GameServerRpcHandler(), TaskUtils.Timeout(5));
                    AccountServerRpc = rpc;
                    await AccountServerRpc.GameServerConnected(Guid);
                    _log.Info("[RPC] Reconnected to AccountServer.");
                    break;
                }
                catch (Exception ex) {
                    _log.Error($"[RPC] Reconnect attempt failed: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
            }
        }
    }

    private static void UnhandledException(object sender, UnhandledExceptionEventArgs args) {
        _log.Fatal(args.ExceptionObject);
    }
}