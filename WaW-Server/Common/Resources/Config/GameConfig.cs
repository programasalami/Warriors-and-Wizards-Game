#region

using System.Xml.Linq;
using Common.Utilities;

#endregion

namespace Common.Resources.Config;

public class GameConfig {
    private const string ConfigFile = "Resources/Config/Data/gameConfig.xml";

    public int[] StarGoals { get; set; }

    public GameConfig(XElement e) {
        StarGoals = e.GetValue<string>("StarGoals")?.CommaToArray<int>();
    }

    public static GameConfig Config
        => ConfigLoader<GameConfig>.Load(ConfigFile, e => new GameConfig(e));
}
