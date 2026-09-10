using System;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Common.Database;
using Common.Database.Models;
using Common.Game;
using Common.Messaging;
using Common.Structs;
using Common.Utilities;
using StreamJsonRpc;

namespace AccountServer.Messaging;

public class AccServerRpcHandler : IAccountServerHandler {
    private static readonly Logger _log = new Logger(typeof(AccServerRpcHandler));

    public Guid ServerId { get; set; }

    private IGameServerRpc _proxy;

    public void Attach(IGameServerRpc proxy) {
        _proxy = proxy;
    }

    public void Close() {
        IpcServer.Clients.TryRemove(ServerId, out _);
        DbClient.Accounts.UpdateMany( // Release locks acquired by this GameServer instance
            acc => new Account { LockOwner = Guid.Empty },
            acc => acc.LockOwner == ServerId);
    }
    
    public Task GameServerConnected(Guid gameServerId) {
        if (!IpcServer.Clients.TryAdd(gameServerId, _proxy))
            throw new InvalidOperationException($"Failed to add GameServer to dictionary: {gameServerId}");
        ServerId = gameServerId;
        
        _log.Info($"[RPC] GameServer ({gameServerId}) connected");
        return Task.CompletedTask;
    }

    public async Task<GameInfoDto?> GetUserInfo(string name, int accountId) {
        return await _proxy.GetUserInfo(name, accountId);
    }

    public Task<VerifyResultDto> VerifyAccount(string username, string password, Guid gameServerGuid) {
        var (acc, status) = DbClient.VerifyAccount(username, password, gameServerGuid);
        return Task.FromResult(new VerifyResultDto(acc, status));
    }

    public Task<BanRecord[]> GetActiveBans(int accountId) {
        return Task.FromResult(DbClient.Bans.Find(b => b.TargetAccId == accountId).ToArray());
    }

    public async Task FlushAccount(Account account) {
        await DbClient.FlushAsync(account);
    }

    public Task<Character> GetCharacter(int accountId, int charId) {
        return Task.FromResult(DbClient.GetCharacter(accountId, charId));
    }

    public async Task<CreateCharacterResultDto> CreateCharacter(Account account, ushort objectType, ushort skinType) {
        var result = await DbClient.CreateCharacterAsync(account, objectType, skinType);
        return new CreateCharacterResultDto(account, result.Char, result.Status);
    }
}