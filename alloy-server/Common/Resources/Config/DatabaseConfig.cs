#region

using System.Xml.Linq;
using Common.Utilities;

#endregion

namespace Common.Resources.Config;

public class DatabaseConfig {
    private const string ConfigFile = "Resources/Config/Data/databaseConfig.xml";

    public DatabaseConfig(XElement e) {
        DbFile = e.GetValue<string>("DbFile");
    }

    public static DatabaseConfig Config
        => ConfigLoader<DatabaseConfig>.Load(ConfigFile, e => new DatabaseConfig(e));

    public string DbFile { get; private set; }
}
