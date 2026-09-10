#region

using System.Xml.Linq;
using Common.Utilities;

#endregion

namespace Common.Resources.Config;

public class PostgresConfig {
    private const string ConfigFile = "Resources/Config/Data/postgresConfig.xml";

    public PostgresConfig(XElement e) {
        Host = e.GetValue<string>("Host");
        Port = e.GetValue<int>("Port");
        Database = e.GetValue<string>("Database");
        Username = e.GetValue<string>("Username");
        Password = e.GetValue<string>("Password");
    }

    public static PostgresConfig Config
        => ConfigLoader<PostgresConfig>.Load(ConfigFile, e => new PostgresConfig(e));

    public string Host { get; private set; }
    public int Port { get; private set; }
    public string Database { get; private set; }
    public string Username { get; private set; }
    public string Password { get; private set; }

    public string ConnectionString =>
        $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password}";
}
