using Alloy.UiLib.Data;
using AlloyClient.Utils;

namespace AlloyClient.Ui;

public static class ItemConstants {

    public const int NoItem = -1;
    public const int AllType = 0;
    public const int SwordType = 1;
    public const int DaggerType = 2;
    public const int BowType = 3;
    public const int TomeType = 4;
    public const int ShieldType = 5;
    public const int LeatherType = 6;
    public const int PlateType = 7;
    public const int WandType = 8;
    public const int RingType = 9;
    public const int PotionType = 10;
    public const int SpellType = 11;
    public const int SealType = 12;
    public const int CloakType = 13;
    public const int RobeType = 14;
    public const int QuiverType = 15;
    public const int HelmType = 16;
    public const int StaffType = 17;
    public const int PoisonType = 18;
    public const int SkullType = 19;
    public const int TrapType = 20;
    public const int OrbType = 21;
    public const int PrismType = 22;
    public const int ScepterType = 23;
    public const int KatanaType = 24;
    public const int ShurikenType = 25;

    // The faint picture drawn in an EMPTY equipment slot, by slot type. Only the kinds the two classes have (sword, helm, armor, staff, spell, robe, ring) exist;
    // everything else shows nothing.
    public static TextureInfo GetSlot(int slotType) {
        switch (slotType) {
            case SwordType:
                return TextureHelper.FromGameAtlas("icons", 1);
            case HelmType:
                return TextureHelper.FromGameAtlas("icons", 2);
            case PlateType:
                return TextureHelper.FromGameAtlas("icons", 3);
            case RingType:
                return TextureHelper.FromGameAtlas("icons", 4);
            case StaffType:
                return TextureHelper.FromGameAtlas("icons", 5);
            case SpellType:
                return TextureHelper.FromGameAtlas("icons", 6);
            case RobeType:
                return TextureHelper.FromGameAtlas("icons", 7);
        }

        return TextureHelper.FromGameAtlas(0x0096);
    }
}
