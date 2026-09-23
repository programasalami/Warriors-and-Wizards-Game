namespace Common.Structs;

// A player's item slots, the same on client and server: 0-3 the gear the class wears, 4-11 the inventory, 12-19 the backpack, and since
// 2026-09-23 slot 20, the first Skill slot (the HUD's row on the left edge above the gear). Skill slots come after everything else so no
// existing slot number moved; a character saved with 20 slots simply has an empty Skill slot.
public static class InventoryLayout {
    public const int GearSlots = 4;
    public const int SkillSlot = 20;
    public const int SkillSlots = 1;
    public const int PlayerSlots = SkillSlot + SkillSlots;

    // The SlotType of a Skill item (<SlotType>30</SlotType> in Equip.xml). 1-25 are the original game's kinds (sword ... shuriken).
    public const int SkillSlotType = 30;

    public static bool IsSkillSlot(int slot) => slot >= SkillSlot && slot < SkillSlot + SkillSlots;
}
