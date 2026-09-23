using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Common.Utilities;

namespace Common.Resources.Xml.Descriptors;

public class PlayerDesc : ObjectDesc {
    public readonly int[] Equipment;
    public readonly int[] SlotTypes;
    public Dictionary<StatType, StatDesc> Stats;
    // How much each stat grows per level: <LevelIncrease min="20" max="30">MaxHitPoints</LevelIncrease> (2026-09-22; rolled by the game server).
    public readonly LevelIncrease[] LevelIncreases;

    public PlayerDesc(XElement e, string id, ushort type)
        : base(e, id, type) {
        SlotTypes = e.GetValue<string>("SlotTypes")?.CommaToArray<int>();
        Equipment = e.GetValue<string>("Equipment")?.CommaToArray<int>();

        Stats = new Dictionary<StatType, StatDesc> {
            { StatType.MaxHP, new StatDesc(e.Element("MaxHitPoints")) },
            { StatType.MaxMP, new StatDesc(e.Element("MaxMagicPoints")) },
            { StatType.Attack, new StatDesc(e.Element("Attack")) },
            { StatType.Defense, new StatDesc(e.Element("Defense")) },
            { StatType.Speed, new StatDesc(e.Element("Speed")) },
            { StatType.Dexterity, new StatDesc(e.Element("Dexterity")) },
            { StatType.Vitality, new StatDesc(e.Element("HpRegen")) },
            { StatType.Wisdom, new StatDesc(e.Element("MpRegen")) }
        };

        LevelIncreases = e.Elements("LevelIncrease")
            .Select(i => (Ok: TryStatFromXmlName(i.Value?.Trim(), out var stat), Stat: stat, Min: i.GetAttribute<int>("min"), Max: i.GetAttribute<int>("max")))
            .Where(i => i.Ok)
            .Select(i => new LevelIncrease(i.Stat, i.Min, i.Max))
            .ToArray();
    }

    public readonly record struct LevelIncrease(StatType Stat, int Min, int Max);

    // The stat a Players.xml element name stands for (the XML calls Vitality / Wisdom HpRegen / MpRegen).
    public static bool TryStatFromXmlName(string xmlName, out StatType stat) {
        switch (xmlName) {
            case "MaxHitPoints": stat = StatType.MaxHP; return true;
            case "MaxMagicPoints": stat = StatType.MaxMP; return true;
            case "Attack": stat = StatType.Attack; return true;
            case "Defense": stat = StatType.Defense; return true;
            case "Speed": stat = StatType.Speed; return true;
            case "Dexterity": stat = StatType.Dexterity; return true;
            case "HpRegen": stat = StatType.Vitality; return true;
            case "MpRegen": stat = StatType.Wisdom; return true;
            default: stat = StatType.MaxHP; return false;
        }
    }
}

public class StatDesc {
    public readonly int MaxValue;
    public readonly int StartValue;

    public StatDesc(XElement e) {
        StartValue = int.Parse(e.Value);
        MaxValue = e.GetAttribute<int>("max");
    }
}