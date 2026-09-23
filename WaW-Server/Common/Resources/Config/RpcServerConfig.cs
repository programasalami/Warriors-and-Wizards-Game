#region

using System.Xml.Linq;
using Common.Utilities;

#endregion

namespace Common.Resources.Config;

// AccountServer's side of the RPC channel - who it listens for, and how it
// proves its identity / gates who's allowed to connect.
public class RpcServerConfig {
    private const string ConfigFile = "Resources/Config/Data/rpcServerConfig.xml";

    public RpcServerConfig(XElement e) {
        ListenAddress = e.GetValue<string>("ListenAddress");
        ListenPort = e.GetValue<int>("ListenPort");
        CertificatePfxPath = e.GetValue<string>("CertificatePfxPath");
        CertificatePassword = e.GetValue<string>("CertificatePassword");
        SharedSecret = e.GetValue<string>("SharedSecret");
    }

    public static RpcServerConfig Config
        => ConfigLoader<RpcServerConfig>.Load(ConfigFile, e => new RpcServerConfig(e));

    public string ListenAddress { get; private set; }
    public int ListenPort { get; private set; }
    public string CertificatePfxPath { get; private set; }
    public string CertificatePassword { get; private set; }
    public string SharedSecret { get; private set; }
}
