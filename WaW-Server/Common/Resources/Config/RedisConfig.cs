#region

using System.Xml.Linq;
using Common.Utilities;

#endregion

namespace Common.Resources.Config;

public class RedisConfig {
    private const string ConfigFile = "Resources/Config/Data/redisConfig.xml";

    public RedisConfig(XElement e) {
        Host = e.GetValue<string>("Host");
        Port = e.GetValue<int>("Port");
    }

    public static RedisConfig Config
        => ConfigLoader<RedisConfig>.Load(ConfigFile, e => new RedisConfig(e));

    public string Host { get; private set; }
    public int Port { get; private set; }

    public string ConnectionString => $"{Host}:{Port}";
}
