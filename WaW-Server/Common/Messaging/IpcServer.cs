using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Common.Resources.Config;
using Common.Utilities;
using StreamJsonRpc;

namespace Common.Messaging;

public class IpcServer {
    private static readonly Logger _log = new Logger(typeof(IpcServer));

    public static readonly ConcurrentDictionary<Guid, IGameServerRpc> Clients = new();

    public static async Task StartAsync<THandler>(CancellationToken ct = default) where THandler : IAccountServerHandler, new() {
        var config = RpcServerConfig.Config;
        var certificate = RpcCertificateHelper.LoadOrCreateServerCertificate(config.CertificatePfxPath, config.CertificatePassword);

        var listener = new TcpListener(IPAddress.Parse(config.ListenAddress), config.ListenPort);
        listener.Start();

        _log.Info($"[RPC] Starting IpcServer at {config.ListenAddress}:{config.ListenPort} (TLS)...");

        while (!ct.IsCancellationRequested) {
            var tcpClient = await listener.AcceptTcpClientAsync(ct);
            _ = HandleClientConnectionAsync<THandler>(tcpClient, certificate, config.SharedSecret, ct);
        }
    }

    private static async Task HandleClientConnectionAsync<THandler>(
        TcpClient tcpClient, X509Certificate2 certificate, string sharedSecret, CancellationToken cancellationToken)
        where THandler : IAccountServerHandler, new() {
        var remoteEndpoint = tcpClient.Client.RemoteEndPoint;

        using (tcpClient)
        await using (var sslStream = new SslStream(tcpClient.GetStream(), false)) {
            try {
                await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
                    ServerCertificate = certificate,
                    ClientCertificateRequired = false
                }, cancellationToken);
            }
            catch (Exception ex) {
                _log.Warn($"[RPC] TLS handshake failed from {remoteEndpoint}: {ex.Message}");
                return;
            }

            if (!await RpcHandshake.ValidateSecretAsync(sslStream, sharedSecret, cancellationToken)) {
                _log.Warn($"[RPC] Rejected connection from {remoteEndpoint} - shared secret mismatch.");
                return;
            }

            // Bind for incoming calls from GameServer
            var handler = new THandler();
            var jsonRpc = new JsonRpc(sslStream);
            jsonRpc.AddLocalRpcTarget<IAccountServerRpc>(handler, null);

            var gameServerProxy = jsonRpc.Attach<IGameServerRpc>();
            handler.Attach(gameServerProxy);

            jsonRpc.StartListening();

            // Completion waits until the client disconnects or the connection breaks. It THROWS when the connection breaks
            // (a GameServer exiting, cleanly or not), which used to skip Close() below, so that server's account locks were never
            // released and every player of it got "Account in use" until this process was restarted (2026-09-21 audit, F40).
            try {
                await jsonRpc.Completion;
            }
            catch (Exception ex) {
                _log.Info($"[RPC] GameServer {handler.ServerId} connection ended: {ex.GetType().Name}");
            }
            finally {
                try {
                    await handler.Close();
                }
                catch (Exception ex) {
                    _log.Error($"[RPC] Releasing locks of GameServer {handler.ServerId} failed: {ex}");
                }
            }
            _log.Info($"[RPC] GameServer {handler.ServerId} has disconnected.");
        }
    }
}
