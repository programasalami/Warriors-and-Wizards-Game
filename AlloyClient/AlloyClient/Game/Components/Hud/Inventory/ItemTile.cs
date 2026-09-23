using System;
using AlloyClient.Game.Components.Options;
using AlloyClient.Assets.XmlStructs;
using AlloyClient.Display;
using AlloyClient.Game.Objects;
using AlloyClient.Networking;
using AlloyClient.Networking.Packets.Outgoing;
using AlloyClient.Networking.Structs.DataObjects;
using AlloyClient.Ui.Components.Tooltips;
using Alloy.UiLib.BuiltIn;
using Alloy.UiLib.Core;
using Alloy.UiLib.Extra;
using AlloyClient.Utils;
using Alloy.Common;
using AlloyClient.Ui;
using OpenTK.Mathematics;

namespace AlloyClient.Game.Components.Hud.Inventory;

public sealed class ItemTile : Sprite {

    public int Size = 50;

    public readonly byte SlotId;

    public readonly byte SlotType;

    public readonly bool Interactive;

    public readonly bool OneWay;

    public readonly Entity Owner;

    public ItemDesc ItemDesc;

    private readonly ObjectRect _sprite;
    private readonly SimpleText _tierText;

    private EquipmentToolTip _tooltip;

    private Vector2i _dragStart;
    private bool _checkForDrag;
    private bool _dragging;
    private uint _bgColor;

    private readonly Timer _doubleTimer = new Timer(250, 1);
    private bool _pendingDouble;

    private readonly NineSliceRect _background;
    // an item your class can't use: the slot gets a red wash
    private readonly ColorRect _unusable;
    private readonly ObjectRect _slotDetail;
    private readonly SimpleText _slotId;

    public ItemTile(Entity owner, byte slotId, bool interactive, CutEdges cut, bool oneWay, byte slotType = 0, int tileSize = 50, uint bgcolor = 0x545454) {
        Size = tileSize;
        Owner = owner;
        SlotId = slotId;
        SlotType = slotType;
        Interactive = interactive;
        OneWay = oneWay;
        _bgColor = bgcolor;

        _doubleTimer.AddEventListener(TimerEvent.TimerComplete, OnSingleClick);

        // The walnut slot the options menu uses. `cut` / `bgcolor` are kept for the callers but the look is the slot's own.
        _background = OptionsStyle.Slot(Size, Size);
        AddChild(_background);

        _unusable = new ColorRect(new ColorRectConfig { X = 4, Y = 4, Width = Size - 8, Height = Size - 8, Color = 0xB74132, Alpha = 0.4f });
        _unusable.Visible = false;
        AddChild(_unusable);

        // The "what goes here" silhouette: the Dark Dungeon item pictures fill their cell edge to edge (the old 8x8 icons had a wide margin), so the
        // shape is drawn well inside the slot - a little over half its size, centred - or it looks fat and runs over the frame.
        var detail = Size * 9 / 16;
        _slotDetail = new ObjectRect(new ObjectRectConfig {Texture = TextureHelper.FromGameAtlas(0x0096), X = (Size - detail) / 2, Y = (Size - detail) / 2, Width = detail, Height = detail, OutlineEnabled = false, GlowEnabled = false});
        _slotDetail.Visible = false;
        _slotDetail.ColorTransformation = new ColorTransform(0, 0, 0, 1, 54, 54, 54, 0);
        _slotDetail.SetColorSecondary(0, 0);
        AddChild(_slotDetail);

        if (SlotType != 0) {
            _slotDetail.ChangeTexture(ItemConstants.GetSlot(SlotType));
            _slotDetail.Visible = true;
        }

        _slotId = new SimpleText(new TextConfig {Text = "", X = Size / 2, Y = Size / 2, FontSize = 32, FontType = FontType.Bold, Color = 0x534664, OutlineColor = 0x120E23, Anchor = UiAnchor.Middle});
        _slotId.Visible = false;
        AddChild(_slotId);

        if (Owner is Player && SlotType == 0) {
            _slotId.Visible = true;
        }

        _sprite = new ObjectRect(new ObjectRectConfig {Texture = TextureHelper.FromGameAtlas(0x0096), Width = Size, Height = Size});
        AddChild(_sprite);

        _tierText = new SimpleText(new TextConfig {FontSize = 16, FontType = FontType.Bold, Text = "", OutlineThickness = 6});
        _tierText.Visible = false;
        _tierText.SetAnchor(UiAnchor.RightBottom);
        _tierText.X = Size - 2;
        _tierText.Y = Size;
        AddChild(_tierText);

        if (Owner != null) {
            SetItem(Owner.Equipment[SlotId]);
        }

        _sprite.MouseEnabled = true;

        if (Interactive) {
            _sprite.AddEventListener(MouseEvent.LeftDown, OnMouseDown);
            _sprite.AddEventListener(MouseEvent.LeftUp, OnMouseUp);
        }

        _sprite.AddEventListener(MouseEvent.MouseOver, OnMouseOver);
        _sprite.AddEventListener(MouseEvent.MouseOut, OnMouseOut);
        AddEventListener(Event.EnterFrame, OnFrameEnter);
    }

    public void SetItem(ItemDesc itemDesc) {
        ItemDesc = itemDesc;
        if (ItemDesc != null && ItemDesc.ObjectType > 0) {
            _sprite.ChangeTexture(TextureHelper.FromGameAtlas(ItemDesc.ObjectType));
            _unusable.Visible = !IsUsableByPlayer(ItemDesc);
            _slotDetail.Visible = false;
            _slotId.Visible = false;
        } else {
            _sprite.ChangeTexture(TextureHelper.FromGameAtlas(0x0096));
            _unusable.Visible = false;
            if (SlotType != 0) _slotDetail.Visible = true;
            if (Owner is Player && SlotType == 0) _slotId.Visible = true;
            if (_tooltip != null) TooltipManager.RemoveTooltip(_tooltip);
        }

        UpdateTierTag();
    }

    public void SetTileNumber(int slot) {
        _slotId.SetText($"{slot}");
    }

    public void SetDim(bool isDim) {
        _sprite.ColorTransformation = isDim ? Transforms.Dark : Transforms.Default;
    }

    private void UpdateTierTag() {
        if (ItemDesc == null || ItemDesc.Consumable || ItemDesc.SlotType == 10) {
            _tierText.Visible = false;
            return;
        }

        var color = 0xFFFFFFu;
        var tag = $"T{ItemDesc.Tier}";

        if (ItemDesc.Tier == -1) {
            color = 0x8A2BE2;
            tag = "UT";
        }

        //todo: item set
        /*if (Item.Set) {
            color = 0xFF9900;
            tag = "ST";
        }*/


        _tierText.SetText(tag);
        _tierText.SetColor(color);
        _tierText.Visible = true;
    }

    private void OnMouseOver() {
        if (ItemDesc == null || _dragging) return;
        _tooltip = new EquipmentToolTip(ItemDesc);
        TooltipManager.AddTooltip(_tooltip);
    }

    private void OnMouseOut() {
        if (ItemDesc == null || _tooltip == null || _dragging) return;
        TooltipManager.RemoveTooltip(_tooltip);
        _tooltip = null;
        _pendingDouble = false;
    }

    private void OnMouseDown(MouseEvent args) {
        if (ItemDesc == null) return;

        _dragStart = GetRelativeMousePosition();
        _checkForDrag = true;
        _sprite.AddEventListener(MouseEvent.LeftUp, CancelDragCheck);
    }

    private void OnMouseUp(MouseEvent args) {
        if (_dragging) return;

        if (args.ShiftKey) {
            _pendingDouble = false;

            // added basic consume logic, this will be looked at another time i assume
            if (ItemDesc.ObjectType == ItemConstants.PotionType || ItemDesc.Consumable) {
                int timeStuff = (int) Map.LastGameTime.TotalMs;

                useItem(
                    time: timeStuff,
                    objectId: Owner.ObjectId,
                    slotId: SlotId,
                    objectType: ItemDesc.ObjectType,
                    itemUsePosX: Owner.Position.X,
                    itemUsePosY: Owner.Position.Y,
                    useType: (byte) UseType.START_USE
                );
            }

            return;
        }

        if (args.CtrlKey) {
            _pendingDouble = false;
            // todo: swap to backpack
            return;
        }

        if (_pendingDouble) {
            _pendingDouble = false;
            _doubleTimer.Stop();
            DoubleClick();
            return;
        }

        _pendingDouble = true;
        _doubleTimer.Reset();
        _doubleTimer.Start();
    }

    private void OnSingleClick() {
        _doubleTimer.Stop();
        _pendingDouble = false;
    }

    private void CancelDragCheck() {
        _checkForDrag = false;
        _sprite.RemoveEventListener(MouseEvent.LeftUp, CancelDragCheck);
    }

    private void OnFrameEnter() {
        if (!_checkForDrag) return;
        var delta = GetRelativeMousePosition() - _dragStart;
        var dist = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y);

        if (dist > 3) {
            _pendingDouble = false;
            CancelDragCheck();
            OnBeginDrag();
        }
    }

    private void OnBeginDrag() {
        _dragging = true;

        if (SlotType != 0)
            _slotDetail.Visible = true;

        if (Owner is Player && SlotType == 0)
            _slotId.Visible = true;

        TooltipManager.RemoveTooltip(_tooltip);

        RemoveChild(_sprite);
        RemoveChild(_tierText);

        _sprite.Scale = Stage.ScreenScale;
        // Attach to the new parent before starting the drag: StartDrag() reads _sprite.Stage
        // (via Stage.Mouse.GetMousePosition()) to compute the drag offset, which is null while
        // _sprite sits between its old parent (just removed above) and this one.
        GameScreen.GameSprite.AddChild(_sprite);
        _sprite.StartDrag();
        _sprite.AddEventListener(MouseEvent.LeftUp, OnEndDrag);
    }

    private void OnEndDrag(MouseEvent args) {
        _dragging = false;
        _sprite.RemoveEventListener(MouseEvent.LeftUp, OnEndDrag);
        _sprite.EndDrag();
        _sprite.Scale = Vector2.One;
        GameScreen.GameSprite.RemoveChild(_sprite);
        AddChild(_sprite);
        AddChild(_tierText);

        _slotDetail.Visible = false;
        _slotId.Visible = false;

        HandleDropTarget();
    }

    private void HandleDropTarget() {
        var list = new[] {typeof(ItemTile), typeof(InventoryGrid), typeof(HudView), typeof(GameScreen)};

        var target = _sprite.DropTarget.GetTypeFromList(list);

        if (target == null) {
            SetItem(ItemDesc);
            return;
        }

        switch (target) {
            case ItemTile tile:
                Console.WriteLine($"{!tile.Interactive} {tile.OneWay} {!CanSwapItems(this, tile)}");
                if (!tile.Interactive) break;
                if (tile.OneWay) break;
                if (!CanSwapItems(this, tile)) break;

                var swap = InvSwap.CreatePacket();

                swap.SlotObj1 = new ObjectSlot {
                    ObjectId = Owner.ObjectId,
                    SlotId = SlotId
                };
                swap.SlotObj2 = new ObjectSlot {
                    ObjectId = tile.Owner.ObjectId,
                    SlotId = tile.SlotId
                };
                Client.QueuePacket(swap);

                (tile.ItemDesc, ItemDesc) = (ItemDesc, tile.ItemDesc);

                SetItem(ItemDesc);
                tile.SetItem(tile.ItemDesc);
                break; // swap
            case InventoryGrid grid:
                break; // add to first free slot
            case GameScreen:
                var drop = InvDrop.CreatePacket();
                drop.SlotObject = new ObjectSlot {
                    ObjectId = Owner.ObjectId,
                    SlotId = SlotId
                };

                Client.QueuePacket(drop);

                SetItem(null);
                break; // drop
            default:
                //reset tile
                SetItem(ItemDesc);
                break;

        }
    }

    private static bool CanSwapItems(ItemTile source, ItemTile target) {
        return source.CanHoldItem(target.ItemDesc) && target.CanHoldItem(source.ItemDesc);
    }

    private bool CanHoldItem(ItemDesc itemDesc) {
        return (itemDesc?.ObjectType ?? 0) == 0 || SlotType == 0 || SlotType == itemDesc.SlotType;
    }

    private static bool IsUsableByPlayer(ItemDesc itemDesc) {
        if (Map.LocalPlayer == null || itemDesc == null) return true;
        if (itemDesc.ObjectType == 0) return false;

        var slotType = itemDesc.SlotType;

        if (slotType == ItemConstants.PotionType)
            return true;

        var slots = Map.LocalPlayer.Properties.SlotTypes;
        for (var i = 0; i < slots.Count; i++) {
            if (slots[i] == slotType)
                return true;
        }

        return false;
    }

    // Double-click (2026-09-22): a consumable is used; a piece of gear your class can wear swaps with the matching gear slot (or, from a gear slot,
    // goes to the first free inventory slot). Anything else does nothing. The server checks every swap and use again.
    private void DoubleClick() {
        if (ItemDesc == null || Owner is not Player || Owner != Map.LocalPlayer)
            return;
        if (ItemDesc.SlotType == ItemConstants.PotionType || ItemDesc.Consumable) {
            UseFromSlot(Owner, SlotId, ItemDesc);
            return;
        }
        var slotTypes = Owner.Properties?.SlotTypes;
        if (slotTypes == null || ItemDesc.SlotType == 0 || !IsUsableByPlayer(ItemDesc))
            return;

        var gearSlots = Math.Min(4, slotTypes.Count);
        int target;
        if (SlotId < gearSlots) {
            target = -1;                                              // unequip: the first free inventory slot
            for (var i = gearSlots; i < 12; i++)
                if (Owner.Equipment[i] == null) { target = i; break; }
        } else {
            target = -1;                                              // equip: the gear slot of this item's kind
            for (var i = 0; i < gearSlots; i++)
                if (slotTypes[i] == ItemDesc.SlotType) { target = i; break; }
        }
        if (target < 0)
            return;

        var swap = InvSwap.CreatePacket();
        swap.SlotObj1 = new ObjectSlot { ObjectId = Owner.ObjectId, SlotId = SlotId };
        swap.SlotObj2 = new ObjectSlot { ObjectId = Owner.ObjectId, SlotId = (byte)target };
        Client.QueuePacket(swap);
    }

    public static void UseFromSlot(Entity owner, byte slotId, ItemDesc item) {
        if (owner == null || item == null)
            return;
        useItem((int)Map.LastGameTime.TotalMs, owner.ObjectId, slotId, item.ObjectType, owner.Position.X, owner.Position.Y, (byte)UseType.START_USE);
    }

    // The C / V keys: the first Health Potion / Magic Potion in the inventory slots. False when there is none.
    public static bool UseFirst(string itemId) {
        var player = Map.LocalPlayer;
        if (player == null)
            return false;
        for (var i = 4; i < 12 && i < player.Equipment.Length; i++) {
            var item = player.Equipment[i];
            if (item != null && item.ObjectId == itemId) {
                UseFromSlot(player, (byte)i, item);
                return true;
            }
        }
        return false;
    }

    // The 1-8 keys: use the consumable in that inventory slot (nothing happens for gear).
    public static void UseInventorySlot(int index) {
        var player = Map.LocalPlayer;
        var slot = 4 + index;
        if (player == null || index < 0 || index > 7 || slot >= player.Equipment.Length)
            return;
        var item = player.Equipment[slot];
        if (item != null && (item.Consumable || item.SlotType == ItemConstants.PotionType))
            UseFromSlot(player, (byte)slot, item);
    }

    private static void useItem(int time, int objectId, byte slotId, ushort objectType, float itemUsePosX, float itemUsePosY, byte useType) {
        var packet = UseItem.CreatePacket();

        packet.Time = time;
        packet.SlotObject.ObjectId = objectId;
        packet.SlotObject.SlotId = slotId;
        packet.ItemUsePos.X = itemUsePosX;
        packet.ItemUsePos.Y = itemUsePosY;
        packet.UseType = useType;

        Client.QueuePacket(packet);
    }
}