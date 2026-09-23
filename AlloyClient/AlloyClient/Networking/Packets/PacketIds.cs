// The packet id table lives in Shared/Common.Protocol/PacketId.cs (one enum for both clients and the game server, 2026-09-21
// audit). This alias keeps every existing `PacketId.X` reference in the client compiling unchanged. The dead `PacketIdOld` enum
// that used to sit here is gone.
global using PacketId = Common.Structs.PacketId;
