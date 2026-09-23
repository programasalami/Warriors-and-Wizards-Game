namespace Common.Structs;

// THE packet id table, shared by the desktop client, the browser client and the game server (2026-09-21 audit, H8). It used to be
// two hand-maintained enums (client PascalCase, server UPPERCASE) that happened to agree; now there is one, and a value can only
// change on both sides at once. Values are the wire protocol: never renumber an existing entry (see PacketIdTests), only append.
public enum PacketId : byte {
    Failure = 0,
    CreateSuccess = 1,
    Create = 2,
    PlayerShoot = 3,
    Move = 4,
    PlayerText = 5,
    Text = 6,
    ServerPlayerShoot = 7,
    Damage = 8,
    Update = 9,
    Notification = 10,
    NewTick = 11,
    InvSwap = 12,
    UseItem = 13,
    ShowEffect = 14,
    Hello = 15,
    Goto = 16,
    InvDrop = 17,
    InvResult = 18,
    Reconnect = 19,
    MapInfo = 20,
    Load = 21,
    Teleport = 22,
    UsePortal = 23,
    Death = 24,
    Buy = 25,
    BuyResult = 26,
    Aoe = 27,
    PlayerHit = 28,
    EnemyHit = 29,
    AoeAck = 30,
    ShootAck = 31,
    SquareHit = 32,
    EditAccountList = 33,
    AccountList = 34,
    DamageCounterUpdate = 35,       // not implemented on either side
    CreateGuild = 36,
    GuildResult = 37,
    GuildRemove = 38,
    GuildInvite = 39,
    EnemyShoot = 40,
    Escape = 41,
    InvitedToGuild = 42,
    JoinGuild = 43,
    ChangeGuildRank = 44,
    PlaySound = 45,
    Reskin = 46,
    GotoAck = 47,
    ServerProjectileProps = 48,
    ConstellationsSave = 49,        // not implemented on either side
    OptionsChanged = 50,
    StatsApply = 51,                // not implemented on either side
    StatsApplyResult = 52,          // not implemented on either side
    GemstoneApply = 53,             // not implemented on either side
    GemstoneRemove = 54,            // not implemented on either side
    TradeRequest = 55,
    TradeRequested = 56,
    TradeStart = 57,
    ChangeTrade = 58,
    TradeChanged = 59,
    CancelTrade = 60,
    TradeDone = 61,
    AcceptTrade = 62,
    TradeAccepted = 63,
    GemstoneSwap = 64,              // not implemented on either side
    PartyInvite = 65,               // not implemented on either side
    Unknown = 255                   // client-side marker for "no id" (never sent)
}
