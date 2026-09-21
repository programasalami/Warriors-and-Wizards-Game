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

    // Moderation and mail (see AdminDb): the game server's chat commands go through these, so who may do what to whom is decided in one place.
    Task<AccountBriefDto> FindAccount(string name);
    Task<ModerationResultDto> Moderate(ModerationRequestDto request);
    Task<MuteStateDto> GetMuteState(int accountId);
    Task<ModerationResultDto> SendMail(MailRequestDto request);

    // The Vault: writes ONLY the account's chests (a targeted save, so nothing else on the account is overwritten).
    Task SaveVaultChests(int accountId, VaultChest[] chests);
}

// Action is one of: ban, unban, mute, unmute, setrank. DurationMinutes 0 = permanent (ban / mute only). Rank is used by setrank only.
public readonly record struct ModerationRequestDto(string Action, int ModeratorId, string Target, string Reason, int DurationMinutes, int Rank);

// Error is null when it worked, otherwise a sentence for the person who typed the command.
public readonly record struct ModerationResultDto(string? Error);

public readonly record struct AccountBriefDto(bool Found, int Id, string Name, int Rank, bool IsBanned);

// MuteEndUnix: 0 = not muted, long.MaxValue = muted with no end, otherwise the UTC time (unix seconds) the mute ends.
public readonly record struct MuteStateDto(long MuteEndUnix);

public readonly record struct MailRequestDto(string Target, string From, string Subject, string Body, int Gold, int Fame);

public readonly record struct VerifyResultDto(Account Acc, VerifyStatus Status);

public readonly record struct CreateCharacterResultDto(Account Account, Character Char, CreateCharacterStatus Status);

public interface IAccountServerHandler : IAccountServerRpc {
    Guid ServerId { get; set; }
    void Attach(IGameServerRpc proxy);
    Task Close();
}