#region

using System.IO;
using System.Xml.Linq;
using Common.Utilities;

#endregion

namespace Common.Resources.Config;

public class AppEngineConfig
{
    private const string ConfigFile = "Resources/Config/Data/appEngineConfig.xml";

    private static AppEngineConfig _config;

    public AppEngineConfig(XElement e)
    {
        XmlsDir = e.GetValue<string>("XmlsDir");
        WorldsDir = e.GetValue<string>("WorldsDir");
        Port = e.GetValue<int>("Port");
        Address = e.GetValue<string>("Address");
        MaxConcurrentRequests = e.GetValue("MaxConcurrentRequests", 200);
    }

    public static AppEngineConfig Config
        => _config ??= Load();

    public string XmlsDir { get; private set; }
    public string WorldsDir { get; private set; }
    public int Port { get; private set; }
    public string Address { get; private set; }
    public int MaxConcurrentRequests { get; private set; }

    private static AppEngineConfig Load()
    {
        var configFile = Path.Combine(Directory.GetCurrentDirectory(), "bin", "debug", "net10.0", ConfigFile);

        return new AppEngineConfig(
            XElement.Parse(File.ReadAllText(configFile))
        );
    }
}
