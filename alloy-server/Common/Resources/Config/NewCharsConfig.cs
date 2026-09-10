#region

using System.Xml.Linq;
using Common.Utilities;

#endregion

namespace Common.Resources.Config;

public class NewCharsConfig {
    private const string ConfigFile = "Resources/Config/Data/newCharsConfig.xml";

    public NewCharsConfig(XElement e) {
        Experience = e.GetValue<int>("Experience");
        Level = e.GetValue<int>("Level");
        Fame = e.GetValue<int>("Fame");
        Tex1 = e.GetValue<int>("Tex1");
        Tex2 = e.GetValue<int>("Tex2");
        HealthPotions = e.GetValue<int>("HealthPotions");
        MagicPotions = e.GetValue<int>("MagicPotions");
        HasBackpack = e.HasElement("HasBackpack");
    }

    public static NewCharsConfig Config
        => ConfigLoader<NewCharsConfig>.Load(ConfigFile, e => new NewCharsConfig(e));

    public int Experience { get; private set; }
    public int Level { get; private set; }
    public int Fame { get; private set; }
    public int Tex1 { get; private set; }
    public int Tex2 { get; private set; }
    public int HealthPotions { get; private set; }
    public int MagicPotions { get; private set; }
    public bool HasBackpack { get; private set; }
}
