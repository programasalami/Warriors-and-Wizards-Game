using WaWClient.Game;
using WaWClient.Networking.Structs.DataObjects;

namespace WaWClient.Networking.Packets.Incoming;

// A floating text the server puts over an entity: "+70 XP", "Level 5!", "+1 Fame", "+30 HP", damage from areas / dashes. The handler was EMPTY
// until 2026-09-22 (the texts arrived and were thrown away) and the reader stopped before the size and damage flag the server writes.
// Shown through NotificationLayer.AddStatusText, which is capped (live + queued) and drops the oldest first, so a burst cannot pile up.
// Texts that arrive for the same entity within a moment of each other are staggered so they rise one above the other instead of overlapping.
public class Notification : IncomingPacket<Notification> {
    public int ObjectId;
    public string Message;
    public ARGB Color;
    public int Size;
    public bool IsDamage;

    private const int Lifetime = 1500;
    private const int StaggerMs = 250;
    private const int MaxStagger = 4;
    private static int _lastObjectId;
    private static double _lastAt = double.MinValue;
    private static int _stack;

    public override PacketId PacketId => PacketId.Notification;

    public override void Reset() {
        ObjectId = 0;
        Message = null;
        Color.Reset();
        Size = 0;
        IsDamage = false;
    }

    public override void Read(ref SpanReader reader) {
        ObjectId = reader.ReadInt32();
        Message = reader.ReadUTF();
        Color.Read(ref reader);
        Size = reader.ReadInt32();
        IsDamage = reader.ReadBoolean();
    }

    public override void Handle() {
        if (string.IsNullOrEmpty(Message))
            return;
        var entity = Map.Entities.TryGetValue(ObjectId, out var e) ? e : ObjectId == Map.LocalPlayerId ? Map.LocalPlayer : null;
        if (entity == null)
            return;

        var now = Map.CurrentTime;
        _stack = ObjectId == _lastObjectId && now - _lastAt < StaggerMs ? System.Math.Min(_stack + 1, MaxStagger) : 0;
        _lastObjectId = ObjectId;
        _lastAt = now;

        var rgb = (uint)(Color.R << 16 | Color.G << 8 | Color.B);
        Ui.Character.NotificationLayer.AddStatusText(entity, Message, rgb, Lifetime, _stack * StaggerMs);
    }

    public override string ToString() {
        return $"ObjectId: {ObjectId}, Message: {Message}, Color: {Color}";
    }
}