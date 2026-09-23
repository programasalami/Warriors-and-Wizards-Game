#region

using System.Xml.Linq;
using Common.Utilities;

#endregion

namespace Common.Resources.Config;

// GameServer's side of the RPC channel - where to find AccountServer, which
// certificate to trust (pinned by thumbprint, no CA involved), and the shared
// secret that must match RpcServerConfig.SharedSecret on the AccountServer.
public class RpcClientConfig {
    private const string ConfigFile = "Resources/Config/Data/rpcClientConfig.xml";

    public RpcClientConfig(XElement e) {
        ServerHost = e.GetValue<string>("ServerHost");
        ServerPort = e.GetValue<int>("ServerPort");
        TrustedCertPath = e.GetValue<string>("TrustedCertPath");
        SharedSecret = e.GetValue<string>("SharedSecret");
    }

    public static RpcClientConfig Config
        => ConfigLoader<RpcClientConfig>.Load(ConfigFile, e => new RpcClientConfig(e));

    public string ServerHost { get; private set; }
    public int ServerPort { get; private set; }
    public string TrustedCertPath { get; private set; }
    public string SharedSecret { get; private set; }
}
