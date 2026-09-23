// The packet id table lives in Shared/Common.Protocol/PacketId.cs (one enum for both clients and the game server, 2026-09-21
// audit). This alias keeps every `PacketId.X` reference in the server compiling; the members are the shared PascalCase names.
global using PacketId = Common.Structs.PacketId;
