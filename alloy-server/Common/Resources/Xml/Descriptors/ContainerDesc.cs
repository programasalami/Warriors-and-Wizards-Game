using System.Xml.Linq;
using Common.Utilities;

namespace Common.Resources.Xml.Descriptors;

public class ContainerDesc : ObjectDesc {
    public readonly int[] Equipment;
    public readonly int[] SlotTypes;

    // A container the server keeps when it is emptied and saves (a Vault Chest). Loot bags are not persistent: an empty one disappears.
    public readonly bool Persistent;

    public ContainerDesc(XElement e, string id, ushort type)
        : base(e, id, type) {
        SlotTypes = e.GetValue<string>("SlotTypes")?.CommaToArray<int>();
        Equipment = e.GetValue<string>("Equipment")?.CommaToArray<int>();
        Persistent = e.HasElement("Persistent");
    }
}