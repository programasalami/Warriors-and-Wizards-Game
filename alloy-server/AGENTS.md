# AGENTS.md - Alloy Server (Warriors & Wizards)

Rules and facts for anyone (human or AI) changing the servers. Rewritten 2026-09-21 during the engineering audit; the old
version described projects that no longer exist (`WebServer/`, `GameServerOld/`, LiteDB). The full audit, the change log and the
manual test list are in `../Docs/EngineeringAudit.md` - read that first for anything touching the game loop, networking or persistence.

## 1. Stack and layout

- C# on .NET 10 (`global.json`: 10.0.0, roll forward, prerelease allowed). `Nullable` disabled in the servers, enabled in the tests.
- `WarriorsAndWizards.Server.sln`:
  - `Common/` - shared by both servers: database access (`Database/`: Npgsql + Dapper, `Schema.sql`, `DbWriter<T>` batching,
    `CharacterDb`/`VaultDb` targeted saves, `PasswordHasher`, `AttemptLimiter`, `AccountLockManager` on Redis), the RPC contract
    (`Messaging/Proxies.cs`, StreamJsonRpc over TLS), XML/world resources (`Resources/`), music (`Music/`), utilities (`Utilities/`:
    the `Logger`, sparse-set collections, `EntityId`).
  - `AccountServer/` - `HttpListener` on :8080 (`Systems/*` request handlers, one class per endpoint) and the RPC hub (`IpcServer`,
    :8081, loopback by default). The only process that talks to Postgres.
  - `GameServer/` - TCP :2050 (`Game/Network`), the game loop (`Game/GameLogic.cs`), worlds (`Game/Worlds`), ECS-style managers
    under `Game/Systems/*` (sparse sets of structs per world), sessions (`Game/Session`), behaviours (`Game/Systems/Behaviors`).
    Reaches account/character data ONLY through the RPC.
  - `Tests/Common.Tests`, `Tests/GameServer.Tests` - xunit. `TestWorldFactory` builds a real `World` without a database.
- `../Shared/Common.Protocol` - `PacketId` (THE packet id table, shared with both clients), `LevelRules`, `WorldPosData`.
- Config lives in `Common/Resources/Config/Data/*.xml`. `postgresConfig.xml`, `rpc*Config.xml` and the `.pfx` are git-ignored;
  the `.example.xml` files show the shape. Never print or commit their contents.

## 2. The game loop (GameServer/Game/GameLogic.cs)

- One thread. `Run(mspt)`: sleep 1 ms at a time until the next tick (never spin), drain queued actions and every user's network
  I/O each iteration, `TickWorlds` every `MsPT` (50 ms at TPS 20). `[STATS]` line every 10 s (tps, tick work, late ticks).
- Everything that touches a world runs on this thread. Other threads hand work over with `GameLogic.Enqueue(Action)`.
- `GameThreadSynchronizationContext` is installed on it: a packet handler may `await` an RPC / database call and the code after
  the `await` runs back on the game thread. `NetworkHandler` never blocks on a handler; while one user's handler is in flight no
  further packet of that user is processed (order is kept), other users are unaffected.
- After an `await` in a session handler, check `user.State == ConnectionState.Disconnected` and return: the pooled `User` may
  already belong to someone else.
- Shutdown: Ctrl+C / SIGTERM / console close -> `RequestStop` -> the loop ends -> `ShutdownAsync` saves every character,
  disconnects everyone, drains the save queue. Never `Environment.Exit` from elsewhere.

## 3. Persistence rules

- Characters are saved by the game server through `CharacterSaver` (snapshot on the game thread, RPC `SaveCharacter` from a
  worker): on disconnect, world switch, every `AutosaveIntervalMs`, and at shutdown. `CharacterDb.SaveAsync` replaces only that
  character's entry in the account JSON (`jsonb_set`), never the whole account.
- Do NOT `FlushAccount` from the game server with its in-memory `Account`: that copy is stale and would overwrite gold / inbox /
  rank changes the AccountServer made meanwhile. (Known leftover: `Create.Handle` still does - F56 in the audit.)
- Schema changes go in `Schema.sql`, additive and idempotent (`IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`). Never a destructive
  migration; the local database holds real development accounts.
- Passwords: `PasswordHasher` (PBKDF2). Legacy SHA1 rows verify and are upgraded on the next successful login. Never write a hash
  any other way.
- Account locks (one session per account) live in Redis, owned by the game server's GUID; released when its RPC session ends
  (`IpcServer`, in a `finally`) and taken over by a login if the owning server is no longer connected.

## 4. Networking rules

- Packet ids: `Common.Structs.PacketId` only (both servers alias it with `global using`). Never renumber; append.
- Incoming packets are `record`s with `[Packet(PacketId.X)]`, `Read(ref SpanReader)` and `Task Handle(User)`. Gameplay handlers
  return a completed task; only session handlers await.
- The server does not trust the client. Hit reports go through `HitValidation`; movement and fire rate through `PlausibilityRules`
  (log-only for now: `[PLAUSIBILITY]` warnings, tune from real logs before enforcing); chat is capped at 256 characters and
  rate-limited; a user sending 2000+ packets in one tick is disconnected.
- Outgoing packets are `readonly record struct : IOutgoingPacket` written into the user's send buffer; `SendSocketData` flushes
  once per loop iteration.
- The wire protocol must stay compatible with the deployed client version unless the version is bumped with
  `deploy -SetVersion` and server + exe + web are deployed together.

## 5. Logging

- `Common.Utilities.Logger`: formatting on the caller, console + file writes on a background thread (a full queue drops the oldest
  lines and says so). Debug lines are compiled in but skipped unless `ALLOY_DEBUG_LOG` is set. Never log per tick or per packet at
  Info or above. `Fatal` flushes.

## 6. Conventions

- Culture-invariant everywhere (`CultureInfo.InvariantCulture` is set at startup).
- Structs in the managers are accessed by `ref` (`ref var stats = ref world.EntityStats.Get(id)`); check `Id == EntityId.Null`.
- Behaviour hot reload (`/reloadbehaviors`, Roslyn) is off by default; build with `-p:BehaviorHotReload=true` to get it.
- After changing XML or world files, rebuild the servers (the packaged copies are what they load). New world files must be listed
  in `Common.csproj`.
- Definition of done: `dotnet build` + `dotnet test` of `WarriorsAndWizards.Server.sln` green, the change log in `../Docs/EngineeringAudit.md`
  updated, and a manual test written down for the user (nobody launches the game unasked).
