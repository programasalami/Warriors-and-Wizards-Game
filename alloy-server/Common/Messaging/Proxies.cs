using System;
using System.Threading.Tasks;
using Common.Database.Models;
using Common.Structs;
using PolyType;
using StreamJsonRpc;

namespace Common.Messaging;

[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
public partial interface IGameServerRpc {
    Task<bool> GlobalAnnouncement(string from, string message);
    Task<ServerInfo> GetGameServer();
    Task<GameInfoDto?> GetUserInfo(string name, int accountId);
}

[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
public partial interface IAccountServerRpc {
    Task GameServerConnected(Guid gameServerId);
    Task<GameInfoDto?> GetUserInfo(string name, int accountId);

    // AccountServer is the sole process allowed to open alloy.db directly (Direct
    // connection mode - fast, in-process locking only). GameServer never touches
    // the file; it reaches account/character data through these RPC calls instead.
    Task<VerifyResultDto> VerifyAccount(string username, string password, Guid gameServerGuid);
    Task<BanRecord[]> GetActiveBans(int accountId);
    Task FlushAccount(Account account);
    Task<Character> GetCharacter(int accountId, int charId);
    Task<CreateCharacterResultDto> CreateCharacter(Account account, ushort objectType, ushort skinType);
}

public readonly record struct VerifyResultDto(Account Acc, VerifyStatus Status);

public readonly record struct CreateCharacterResultDto(Account Account, Character Char, CreateCharacterStatus Status);

public interface IAccountServerHandler : IAccountServerRpc {
    Guid ServerId { get; set; }
    void Attach(IGameServerRpc proxy);
    Task Close();
}