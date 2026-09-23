#region

using System.Xml.Linq;
using Common.Utilities;

#endregion

namespace Common.Resources.Config;

public class AppEngineConfig
{
    private const string ConfigFile = "Resources/Config/Data/appEngineConfig.xml";

    public AppEngineConfig(XElement e)
    {
        XmlsDir = e.GetValue<string>("XmlsDir");
        WorldsDir = e.GetValue<string>("WorldsDir");
        Port = e.GetValue<int>("Port");
        Address = e.GetValue<string>("Address");
        MaxConcurrentRequests = e.GetValue("MaxConcurrentRequests", 200);
        DownloadUrl = e.GetValue("DownloadUrl", string.Empty).Trim();
    }

    public static AppEngineConfig Config
        => ConfigLoader<AppEngineConfig>.Load(ConfigFile, e => new AppEngineConfig(e));

    public string XmlsDir { get; private set; }
    public string WorldsDir { get; private set; }
    public int Port { get; private set; }
    public string Address { get; private set; }
    public int MaxConcurrentRequests { get; private set; }

    // Where players get the newest desktop client (shown when their build is out of date). Empty = no download button.
    public string DownloadUrl { get; private set; }
}
