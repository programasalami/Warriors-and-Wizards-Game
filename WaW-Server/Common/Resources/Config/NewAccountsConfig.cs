#region

using System.Collections.Generic;
using System.Xml.Linq;
using Common.Database.Models;
using Common.Resources.Xml;
using Common.Utilities;

#endregion

namespace Common.Resources.Config;

public class NewAccountsConfig {
    private const string ConfigFile = "Resources/Config/Data/newAccountsConfig.xml";

    public NewAccountsConfig(XElement e) {
        Fame = e.GetValue<int>("Fame");
        Credits = e.GetValue<int>("Credits");
        MaxChars = e.GetValue<int>("MaxChars");
        VaultCount = e.GetValue<int>("VaultCount");
        CharSlotCost = e.GetValue<int>("CharSlotCost");
        VaultSlotCost = e.GetValue<int>("VaultSlotCost");
    }

    public static NewAccountsConfig Config
        => ConfigLoader<NewAccountsConfig>.Load(ConfigFile, e => new NewAccountsConfig(e));

    public int Fame { get; private set; }
    public int Credits { get; private set; }
    public int MaxChars { get; private set; }
    public int VaultCount { get; private set; }
    public int CharSlotCost { get; private set; }
    public int VaultSlotCost { get; private set; }

    public static List<ClassStats> CreateClassStats() {
        var classStats = new List<ClassStats>();
        foreach (var player in XmlLibrary.PlayerDescs.Values)
            classStats.Add(new ClassStats {
                ObjectType = player.ObjectType,
                BestFame = 0,
                BestLevel = 0
            });

        return classStats;
    }
}
