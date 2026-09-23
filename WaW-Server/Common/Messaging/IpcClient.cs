using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Common.Resources.Config;
using StreamJsonRpc;

namespace Common.Messaging;

public static class IpcClient {

    public static async Task<(JsonRpc Session, IAccountServerRpc ServerProxy)> ConnectAsync(IGameServerRpc localHandler, CancellationToken cancellationToken = default)
    {
        var config = RpcClientConfig.Config;
        var trustedCert = RpcCertificateHelper.LoadTrustedCertificate(config.TrustedCertPath);

        var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(config.ServerHost, config.ServerPort, cancellationToken);

        var sslStream = new SslStream(tcpClient.GetStream(), false,
            (sender, certificate, chain, errors) => RpcCertificateHelper.ValidatePinned(trustedCert, sender, certificate, chain, errors));

        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            TargetHost = config.ServerHost
        }, cancellationToken);

        await RpcHandshake.SendSecretAsync(sslStream, config.SharedSecret, cancellationToken);

        // Listener for incoming calls from IpcServer
        var jsonRpc = JsonRpc.Attach(sslStream, localHandler);

        // Proxy for outgoing calls to IpcServer
        var proxy = jsonRpc.Attach<IAccountServerRpc>();

        return (jsonRpc, proxy);
    }
}
