namespace Common.Structs;

public readonly record struct GameInfoDto(int AccountId, int WorldId, string WorldName, WorldPosData Position);