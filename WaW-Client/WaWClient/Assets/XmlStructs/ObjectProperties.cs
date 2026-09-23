using System;
using System.Collections.Generic;
using System.Xml.Linq;
using WaW.Common;

namespace WaWClient.Assets.XmlStructs;


public class ObjectProperties {
    public readonly ushort ObjectType;
    public readonly string ObjectId;

    public readonly string DisplayId;
    public readonly string DisplayName;

    public readonly Dictionary<byte, ProjectileProperties> Projectiles;

    public readonly PlayerProperties PlayerProperties;

    public readonly string Class;
    public readonly string Model;
    public readonly string Effect;
    public readonly float Rotation;
    public readonly float AngleCorrection;
    public readonly int NumProjectiles;
    public readonly float ArcGap;

    public readonly bool IsPlayer;
    public readonly bool IsEnemy;
    public readonly bool IsAlly;

    public readonly bool EnemyOccupySquare;
    public readonly bool OccupySquare;
    public readonly bool FullOccupy;

    public readonly bool Static;
    public readonly bool NoMiniMap;
    public readonly bool DrawOnGround;

    public readonly int RealSize;
    public readonly int MinSize;
    public readonly int MaxSize;
    public readonly int SizeStep = 5;

    // Fraction of the sprite's cell height the art's visual base sits above the cell bottom
    // (see RenderBase.TextureBottomInset). XML <BottomInset>; 0 for stock art that fills its cell.
    public readonly float BottomInset;

    // XML <FlatOnGround/>: draw the sprite lying in the ground plane (rotates with the world, no
    // shadow, always behind standing objects) instead of as a camera-facing billboard.
    public readonly bool FlatOnGround;

    // XML <CrossedCards>N</CrossedCards> (or <CrossedCards/> = 2): draw the sprite as N upright copies of its picture spread evenly
    // round the vertical axis and fixed in the world (2 = a cross, 4 = a star) instead of a billboard that turns to face the camera
    // (TypeCrossedCards, 2026-09-22). 0 = an ordinary billboard.
    public readonly int CrossedCards;

    // XML <Thickness>t</Thickness> on a <FlatOnGround/> object (the logs): draw the flat picture as a stack of layers t tiles high so it
    // has a visible side (TypeFlatStack, 2026-09-22). 0 = the plain flat sprite.
    public readonly float Thickness;

    public readonly string Description;

    public readonly ushort PlayerClassType;
    public readonly bool Skin;

    public readonly bool Container;
    public readonly bool LockedPortal;

    public readonly List<int> SlotTypes;
    public readonly List<ushort?> Equipment;

    public ObjectProperties(XElement e) {
        ObjectType = e.GetAttribute<ushort>("type");
        ObjectId = e.GetAttribute<string>("id");

        DisplayId = e.GetValue<string>("DisplayId");
        DisplayName = string.IsNullOrWhiteSpace(DisplayId) ? ObjectId : DisplayId;

        Projectiles = [];
        foreach (var proj in e.Elements("Projectile")) {
            var props = new ProjectileProperties(proj);
            Projectiles.TryAdd((byte)props.BulletType, props);
        }
        NumProjectiles = e.GetValue("NumProjectiles", 1);
        ArcGap = e.GetValue("ArcGap", 11.25f);

        Class = e.GetValue<string>("Class");
        Model = e.GetValue<string>("Model");
        Effect = e.GetValue<string>("Effect");
        Rotation = e.GetValue<float>("Rotation");
        AngleCorrection = e.GetValue<float>("AngleCorrection");

        IsPlayer = e.GetValue<bool>("Player");
        IsEnemy = e.GetValue<bool>("Enemy");
        IsAlly = e.GetValue<bool>("Ally");

        if (IsPlayer) {
            PlayerProperties = new PlayerProperties(e);
        }

        EnemyOccupySquare = e.GetValue<bool>("EnemyOccupySquare");
        OccupySquare = e.GetValue<bool>("OccupySquare");
        FullOccupy = e.GetValue<bool>("FullOccupy");

        Static = e.GetValue<bool>("Static");
        NoMiniMap = e.GetValue<bool>("NoMiniMap");
        DrawOnGround = e.GetValue<bool>("DrawOnGround");

        RealSize = e.GetValue<int>("RealSize", -1);
        MinSize = e.GetValue<int>("MinSize");
        MaxSize = e.GetValue<int>("MaxSize");
        SizeStep = e.GetValue("SizeStep", 5);
        BottomInset = e.GetValue("BottomInset", 0f);
        FlatOnGround = e.HasElement("FlatOnGround");
        Thickness = e.GetValue("Thickness", 0f);
        CrossedCards = e.HasElement("CrossedCards") ? Math.Clamp(int.TryParse(e.Element("CrossedCards")?.Value, out var cards) ? cards : 2, 1, 8) : 0;

        Description = e.GetValue<string>("Description");

        PlayerClassType = e.GetValue<ushort>("PlayerClassType");
        Skin = e.GetValue<bool>("Skin");

        SlotTypes = [];
        var slotTypes = e.GetValue<string>("SlotTypes");
        if (slotTypes != null) {
            foreach (var slot in slotTypes.Split(',')) {
                SlotTypes.Add(int.Parse(slot));
            }
        }

        Container = e.HasElement("Container");
        LockedPortal = e.HasElement("LockedPortal");

        Equipment = [];
        var equipment = e.GetValue<string>("Equipment");
        if (equipment != null) {
            foreach (var equip in equipment.Split(',')) {
                var clean = equip.Replace(" ", "");
                if (clean == "-1") {
                    Equipment.Add(null);
                    continue;
                }

                var objectType = Convert.ToUInt16(clean, WaW.Common.Utils.GetBase(clean));
                Equipment.Add(objectType);
            }
        }
    }
}