# Warriors & Wizards - Engineering Audit and Plan

Written 2026-09-21 from the Testing folder (`Desktop\Repos\Warriors-and-Wizards-Testing`). Nothing has been changed yet: this is
Phase 1 (discovery) and Phase 2 (the prioritized plan). Phase 3 (implementation) starts only after the user approves the plan.
File references are `path:line` inside the Testing folder. Line numbers are as of this date.

Sections: 1 Architecture - 2 The two known problems, root-caused - 3 Findings by area - 4 Prioritized plan - 5 Proposed
implementation order - 6 Decisions needed from the user - 7 Measurement plan - 8 Manual test list - 9 Change log - 10 Baseline.

---

## 1. Architecture (how the system actually works today)

### 1.1 The products and the dependency map

```
Shared/Common.Protocol  (2 files: LevelRules, WorldPosData)          <- referenced by BOTH client and server
        ^                                   ^
AlloyClient/ (desktop exe)              alloy-server/
  Alloy.Common   (Color, math helpers, StbImage)        Common/      (DB client, RPC, XML/world resources, music, moderation)
  Alloy.Engine   (GameWindow loop, GL buffers/shaders)  AccountServer/ (HttpListener :8080, RPC hub :8081, Postgres, Redis)
  Alloy.ContentReader / Alloy.ContentBuilder (atlases)  GameServer/    (TCP :2050, ECS-style managers, game loop)
  Alloy.UiLib    (Sprite tree, SpriteRender, fonts)     Tests/ (Common.Tests, GameServer.Tests)
  Alloy.Audio    (OpenAL thread, ogg/mp3/wav)
  Alloy.ShaderSourceGen (Roslyn analyzer: shaders -> C#)
  AlloyClient    (the game: screens, HUD, Map, Networking, Rendering)
  Tests/AlloyClient.Tests
WebClient/  (browser build made FROM the desktop sources: web.csproj links them, patches 8 files, swaps GL/window/audio/socket shims)
Tools/      (python: editor, sheet, map and asset generators - developer only)
deploy.ps1 / promote.ps1 (release scripts)
```

Sizes: client 379 C# files / 37k lines, server 358 files / 39k lines, shared 4 files, web shims 18 files / 6.7k lines.
Tests: 36 test files, 349 `[Fact]`/`[Theory]` methods (xunit) across four test projects.

### 1.2 Desktop client

- **Startup** (`AlloyClient/AlloyClient/Main.cs`): `Main : GameWindow(GL 4.3)`. `Initialize` restores window state, sets GL state,
  starts audio, UI renderer, display manager. `LoadContent` loads the UI atlas and fades to a `LoadingScreen` driven by a `LoadPlan`
  (`Loading/LoadPlan.cs`): GL work one step per frame on the main thread, parsing and account calls on the thread pool.
- **Main loop** (`Alloy.Engine/GameWindow.cs:89-115`): process events, `Update(gameTime)`, `Draw`, swap, then sleep to
  `TargetFrameTime` (menu 60 fps cap; in game VSync on = no cap, `Main.SetGraphicOptions`). On Windows the thread is pinned to
  CPU core 0 and `timeBeginPeriod(1)` is set (`GameWindow.cs:171-179`). `GameTime` carries **milliseconds** (`TotalMs`, `ElapsedMs`).
- **Screens**: `DisplayManager` -> `ScreenManager` (title, character book, game) + `OverlayManager` (windows) + `DialogManager`.
- **Game screen** (`Game/GameScreen.cs`): per frame `Client.Tick()` (drain packets), camera, input (shooting), chat, notifications
  (damage text), HUD, then a fixed-update loop into `Map.FixedUpdate` (projectile hit tests + trails), then `Map.Update` (entities,
  particles, projectile movement) and `Map.Draw` (tiles, shadows, particles, models, entities).
- **World state** (`Game/Map.cs`): static class. `Entities`/`Players`/`InteractiveObjects` dictionaries, `EntityStorage` (render lists
  per model type), `Projectiles` list, `ParticleGenerators` list (unbounded), `Particles[30000]`, chunked `TileMap` (16x16 chunks,
  dirty-flag rebuild of per-chunk vertex data).
- **Rendering** (`Rendering/Render*.cs`, `Alloy.Engine/Graphics`): non-instanced expanded vertex buffers
  (`InstanceAttributeBuffer`, orphan + `BufferSubData` every draw), tiles = 35 visible chunks expanded to 6 vertices per tile every
  frame, entities sorted by depth and drawn in two passes (opaque, then glow/outline), shadows through a UBO, particles through an
  SSBO in 2000-particle chunks, UI through `SpriteRender` (ring of 8 buffer sets, no orphaning - required by the Intel HD 4400 driver).
  Shaders: `Ground/Model/Object` `#version 330 core`, `Particle/Shadow/Ui.vert` `#version 430 core` (SSBO/UBO). Real minimum is GL 4.3.
- **Networking** (`Networking/Client.cs`): raw TCP with `SocketAsyncEventArgs`, a receive loop on the thread pool that parses packets
  into a `ConcurrentQueue`, drained on the main thread in `Client.Tick`; outgoing packets are written into one 64 KB buffer under a
  lock and flushed once per frame (double buffer swap; only one send in flight). Packet classes are pooled. Packet ids are a hand
  written enum (`Networking/Packets/PacketIds.cs`, plus a dead `PacketIdOld`).
- **Account server access** (`AppEngine/`): one static `HttpClient`, form-encoded POSTs, username + password sent on **every** call.
- **Input**: OpenTK events -> `UserInput`; movement is integrated locally and sent to the server once per server tick (`NewTick`).
- **Audio**: own thread (`Alloy.Audio/AudioEngine.cs`), OpenAL Soft, streamed decoding.
- **Persistence**: `settings.xml` and `account.xml` in `%LOCALAPPDATA%\AlloyClient` (the password is stored Base64, not encrypted).

### 1.3 Browser client (WebClient/)

Type: **a generated build of the same client**, not a second implementation. `web/web.csproj` compiles Alloy.Common, ContentReader,
Engine, UiLib, AlloyClient and Common.Protocol in place; `tools/patch_sources.py` text-patches copies of exactly these desktop files
(the build fails loudly if a rule no longer matches):
`Alloy.Engine/Graphics/Buffers/IndexBuffer.cs`, `Alloy.Engine/Graphics/Buffers/UniformBuffer.cs`, `Alloy.Engine/Graphics/Shader.cs`,
`AlloyClient/AppEngine/AppEngineClient.cs`, `AlloyClient/Core/Settings.cs`, `AlloyClient/Logging/Logging.cs`,
`AlloyClient/Networking/Client.cs`, `AlloyClient/Utils/ClientPlatform.cs`, `Shared/Common.Protocol/WorldPosData.cs`.
Shims replace OpenTK GL (WebGL2 via JS), window/input (requestAnimationFrame), audio (WebAudio), sockets (WebSocket -> `ww-bridge`
on the VPS -> game server port 2050 unchanged), and the SSBO/UBO (data textures). Shaders are ported to GLSL ES 3.00 by
`tools/port_shaders.py`. Content is the desktop `Content` folder downloaded at start. Same game logic, same `GameScreen` fixed-update
loop (so every gameplay bug below exists in the browser too; the browser clamps a frame delta to 250 ms,
`WebClient/web/shim/GameWindow.cs:47`). WASM is single-threaded; `Task.Run` runs cooperatively.

Feature matrix (native vs web): rendering GL 4.3 vs WebGL2 (shim, feature-equivalent), input/window (shim), audio (shim), network TCP
vs WebSocket bridge, settings/login persistence file vs localStorage, editor launch (`ClientPlatform.OpenLocalPage`) native only,
clipboard native only. Game logic, UI, protocol, content: shared 1:1. No feature is implemented twice.

### 1.4 Game server

- **Startup** (`alloy-server/GameServer/Program.cs`): load enums, XML, merchants, worlds, behaviours (from the compiled `BehaviorLib`;
  Roslyn is only used by the admin hot-reload command), connect RPC to the AccountServer (TLS TCP, `IpcClient`), `RealmManager.Init`
  (worlds), `SocketServer.Start` (pre-allocates `MaxPlayers`=1000 `User` objects), then `GameLogic.Run(mspt)` on the main thread.
- **Game loop** (`Game/GameLogic.cs`): `while(true) { Update(); if (elapsed < mspt) continue; ...; TickWorlds(); }`. `Update()` drains
  the action queue, runs `world.Update()` for every world and **all users' network I/O** on every spin. There is **no sleep**: the loop
  busy-spins a core at 100% between ticks. `TickWorlds` uses `Parallel.ForEach` over worlds (2-3 worlds). TPS 20 (50 ms ticks).
- **Networking** (`Game/Network/SocketServer.cs`, `NetworkHandler.cs`): accept loop, per-user `SocketAsyncEventArgs`, receive on the
  thread pool into a per-user queue, handled on the game thread in `HandleIncomingPackets` with
  `pkt.Handle(User).GetAwaiter().GetResult()` (async handlers are **blocked on** the game thread). Outgoing packets are written into a
  growable per-user buffer and sent once per spin. Incoming packet factory = per-user dictionary of Expression-compiled lambdas.
- **Simulation**: ECS-style `ManagerBase<T>` sparse sets per world (`Entities`, `Projectiles`, `EntityStats`, `EntityCombat`,
  `EntityInventories`, `PlayerSights`, `EntityBehaviors`, ...), each ticked in `World.Tick`. Sight (`Systems/Sight`) computes visible
  tiles/entities per player and sends `Update` (new tiles/objects) and `NewTick` (status changes) packets. Hit reports from clients are
  validated by `HitValidation.IsPlausible` and de-duplicated per projectile (`Projectile.Hit` set).
- **Sessions**: `Hello` (version check + `VerifyAccount` over RPC -> Postgres + Redis lock), `Load`/`Create` character, `Reconnect`
  for world switches, `Escape`. Account locks live in Redis keyed by account, owned by the server GUID; released only for stale
  servers, never per disconnect (a second login on the same server passes the lock check).
- **Persistence from the game server**: only `FlushAccount` when a ban has lapsed (`Session/Hello.cs:77`) and `SaveVaultChests`.
  There is **no character save** anywhere in the game server (`Common/Messaging/Proxies.cs` has no such RPC). Characters are a list
  inside the account's `data` JSONB column.
- **Shutdown**: none. No Ctrl+C/SIGTERM handling; `DbClient.Dispose` (flush of pending writes) is never called in either server.

### 1.5 Account server

`HttpListener` on `Address:8080` (plain HTTP), 25 form-POST endpoints (`/account/*`, `/char/*`, `/inbox/*`, `/daily/*`, `/board/*`,
`/music/*`, `/guild/*`, `/app/version`), request body capped at 64 KB, a semaphore of `MaxConcurrentRequests` (64). Every protected
endpoint verifies username + password on each call (`DbClient.VerifyAccount`, SHA1 of password+salt, `Common/Database/DbClient.cs:241`).
No login rate limiting, no session tokens. Registration: name 1-10 letters, password > 8 chars, `MaxAccountsPerIp` = 3000.
The RPC hub (`IpcServer`, StreamJsonRpc over TLS with a self-signed certificate and a shared secret) listens on
`rpcServerConfig.xml` `ListenAddress` (example: `0.0.0.0:8081`, secret `changeme`).

### 1.6 Database

Postgres via Npgsql `NpgsqlDataSource` (pooled) + Dapper. Tables: `accounts` (JSONB `data` holding the whole account incl. characters and
vault), `logins`, `guilds`, `bans`, `mutes`, `bug_posts`, `inbox_messages`, `daily_rewards`; `Schema.sql` is idempotent and runs at
every AccountServer start. Writes go through `DbWriter<T>` (unbounded channel, batches of 500 in one transaction, background task).
Redis (Memurai locally) only holds account locks. Backups: `deploy -Backup`.

### 1.7 Build, release, folders

.NET 10 SDK pinned by `global.json` in each solution (`10.0.100 latestMinor` client, `10.0.0 latestMajor allowPrerelease` server).
Client `TieredCompilation=false`. `deploy.ps1` builds, packs, uploads and restarts the VPS services (`-Server`, `-Client`
self-contained zip, `-Web` AOT WASM site), `-SetVersion` keeps `Settings.BuildVersion` and `gameServerConfig.xml` `<Version>` equal.
`promote.ps1` mirrors Testing -> Game after building and testing. `dist/` (300 MB) and `WebClient/dist/` (765 MB) are build output
inside the Testing folder (git-ignored). `Content/bin` (46 MB) is the content builder's output inside the content source tree.

---

## 2. The two reported problems, root-caused from the source

### 2.1 FPS collapse while shooting and walking

**Root cause: the fixed-update loop mixes seconds and milliseconds.**
`AlloyClient/AlloyClient/Game/GameScreen.cs:22` defines `FixedUpdateStep = 1d / 60` (= 0.0167, a value in *seconds*), but
`GameScreen.cs:148-152` accumulates `gameTime.ElapsedMs` (*milliseconds*) and loops `while (_fixedUpdateElapsed > FixedUpdateStep)`.
A normal 16.7 ms frame therefore runs **~1000 fixed steps**, each calling `Map.FixedUpdate` -> `Projectile.FixedUpdate`
(`Game/Objects/Projectile.cs:160-171`) for every live projectile. There is no cap on the number of catch-up steps, so a slower
frame produces more steps, which makes the next frame slower: a classic spiral of death. Walking adds just enough per-frame cost
(movement, animation, chunk rebuilds, camera) to tip it over.

What each of those ~1000 steps does per projectile:
- `HitTest` (`Projectile.cs:212-259`): `EntityUtils.GetClosestEnemy` walks **every** entity in `Map.Entities` (players, walls,
  bushes, trees, chests... the whole loaded world; entity culling is disabled, see 3.3).
- If the weapon's projectile has a trail: **three** `new SparkEffect(...)` per step (`Projectile.cs:166-170`) -> ~3000 particle
  generators per projectile per frame, kept alive for the trail lifetime, each adding a particle per frame. `Map.AddParticleEffect`
  (`Map.cs:397`) has no cap.
- Nothing in the step depends on the step's `ElapsedMs` except the trail spawn, so the movement itself is right; the loop simply runs
  60x too often.

Contributing costs on the same frame: `Client.QueuePacket` logs an interpolated Debug string for every packet (`Networking/Client.cs`,
`QueuePacket`) and the client logger's minimum level is `Trace` (`Logging/Logging.cs:19`) with a console sink, so every packet and
every `Update` tile sample is written to the console from the render thread; the client thread is pinned to CPU core 0
(`Alloy.Engine/GameWindow.cs:172`) on a PC where the game server busy-spins a core (2.3 below).

### 2.2 Stack of red damage text and the freeze on the DPS dummy

**Root cause: a hit is processed on every remaining fixed step of the same frame.**
`Projectile.HitTest` runs inside the ~1000-step loop above. When a normal (non multi-hit) projectile hits, it sets
`_elapsed = float.MaxValue` (`Projectile.cs:162`) so that `Update` will drop it - but `Map.Update` runs only **after** the whole fixed
loop, so the projectile stays in `Map.Projectiles` and the next ~999 steps hit the same target again: each step adds a
`HitEffect`, a `NotificationLayer.AddStatusText(enemy, "-dmg")` (`Projectile.cs:244-245`) and queues an `EnemyHit` packet.
The Dummy (`Content/Xmls/StaticObjects.xml:146`, 700 HP, `<Enemy/>`) is simply the only enemy that exists, so it is where this shows.

Why it freezes:
- `NotificationLayer.Update` (`Ui/Character/NotificationLayer.cs:21-26`) turns every queued text into a new `CharacterStatusText`
  sprite with its own `SimpleText` mesh (font size 20 x camera zoom x screen scale - that is the "oversized" look) and there is no
  limit: ~1000 UI sprites per hit, each rebuilt and drawn for one second.
- ~1000 `EnemyHit` packets per hit go through the 64 KB client send buffer (`Networking/SocketSendState.cs`, `WritePacket`), which
  has **no overflow check**: only one send is in flight per frame, so a few hits in a row overflow the `Span` and throw from
  `Client.QueuePacket` on the main thread - the game loop dies (that is the "client freezes completely").
- ~1000 Debug console lines per hit from `QueuePacket`.
- Server side each packet becomes a queued action + `IsHitPlausible` (8 samples); damage itself is applied once thanks to the
  `Projectile.Hit` set, so the dummy's HP is not the problem.

Both problems are the same bug in two costumes. The client's shown damage is also a client-side random roll
(`Game/Objects/Player.cs:439`, "Migrate to match server rng") - the server never sends `Damage` (client handler is empty), so the
number on screen is not the damage dealt.

### 2.3 A third cause that affects everything on this PC: the server never sleeps

`alloy-server/GameServer/Game/GameLogic.cs` `Run`: `while (true) { Update(); if (sw.ElapsedMilliseconds < mspt) continue; ... }`.
Between 50 ms ticks the thread spins at 100% on one core, and `Update()` (all worlds' pending removals + every user's packet
handling and socket send) runs millions of times a second. On the i5-4300U (2 cores / 4 threads) that is half the machine gone while
the client is pinned to core 0 by `GameWindow.SetWindowsJank`. This is why "the client also hosts the servers" hurts so much more than
it should.

---

## 3. Findings by area

### 3.1 Client gameplay loop and memory
- F1 Fixed-update unit bug + no step cap (2.1). `GameScreen.cs:22,148-152`.
- F2 Repeated hit per frame; dead projectile stays listed until `Map.Update` (2.2). `Projectile.cs:160-171,212-259`.
- F3 Unbounded `ParticleGenerators` (`Map.cs:397`), `SparkEffect`/`HitEffect` allocated per event, `Particles` buffer 30000 but drawn in
  2000-particle SSBO uploads (`Rendering/Render.Particle.cs`).
- F4 Unbounded status texts, each a sprite with a text mesh (`NotificationLayer.cs`, `CharacterStatusText.cs`).
- F5 Client-side send buffer fixed at 64 KB with no bounds check (`SocketSendState.cs` `WritePacket`); the server's twin resizes.
- F6 `GetClosestEnemy`/`GetClosestPlayer` scan all entities (`Game/Objects/Util/EntityUtils.cs:71-109`); no enemy sub-list.
- F7 World switch/reset leaks state: `Reconnect.Handle` clears only `Entities` and `EntityStorage`; `Map.Reset` does not clear
  `ParticleGenerators`/`ParticleGenCount`; `Projectiles`, `Players`, `InteractiveObjects` survive a `Reconnect`
  (`Networking/Packets/Incoming/Reconnect.cs`, `Map.cs` `Reset`).
- F8 `Update` packet handler builds a `StringBuilder` + interpolated Debug string every server tick (`Packets/Incoming/Update.cs`).
- F9 Client logger minimum level `Trace` with a console formatter (`Logging/Logging.cs:16-19`); hot-path `LogLevel.Debug` strings are
  interpolated eagerly (`Client.cs` `QueuePacket`/`HandleReceive`).
- F10 `TileMap.Get` bounds test uses `> _width`/`> _height` (off by one) -> a phantom chunk row/column can be created (`Map.cs` `TileMap.Get`).
- F11 Damage shown is a client roll; server never sends `Damage` (`Player.cs:439`, `Packets/Incoming/Damage.cs` `Handle` empty).

### 3.2 Client main loop, threads, platform
- F12 Main thread pinned to core 0 (`GameWindow.cs:172`); `ClearWindowsJank` calls `TimeBeginPeriod` instead of `TimeEndPeriod`
  (`GameWindow.cs:177-179`) so the 1 ms timer resolution is never released.
- F13 `Client.Connect` is `async void` (an exception there kills the process); connection-refused retries forever with no UI feedback
  (`Client.cs` `Connect`).
- F14 A malformed length in the receive loop throws `InvalidDataException` inside `Task.Run` -> the receive task faults silently and
  the client sits "connected" forever (`Client.cs` `HandleReceive`, `SocketReceiveState.PacketReady`).
- F15 `Texture` has no delete path (`Alloy.Engine/Graphics/Texture.cs`); title-only textures (logo sheet 2816x3520 RGBA ~40 MB of
  VRAM) stay resident during play on a shared-memory iGPU.
- F16 `TieredCompilation=false` in `AlloyClient.csproj`: everything is fully JIT-compiled at first call on a slow CPU (longer start,
  no steady-state benefit; the runtime's default tiering with OSR is normally better). Needs a measurement, not an assumption.

### 3.3 Client rendering (CPU side; GPU path is left alone per the driver rules)
- F17 Every draw re-allocates the whole buffer at full capacity: `InstanceAttributeBuffer.SetData` calls `BufferData(Length)` then
  `BufferSubData(used)`. Tiles: 35 chunks x 256 tiles x 4 layers x 6 verts x 80 B = **~17 MB** orphaned per frame; entities
  10000 x 6 x ~96 B = ~5.8 MB; models similar (`Alloy.Engine/Graphics/Buffers/InstanceAttributeBuffer.cs`, `Rendering/Render.cs:15-17,81-109`).
  Orphaning itself must stay (driver constraint), but the size can be the used size, or a ring of buffer sets like `SpriteRender`
  (already proven on this GPU).
- F18 Tiles are re-expanded on the CPU every frame (`Render.Draw.cs` `DrawTiles`) although chunk data only changes on `Update` packets
  (`TileChunk` already has a dirty flag).
- F19 Entity culling is commented out: `Entity.UpdateVisibility` sets every entity visible (`Game/Objects/Entity.cs:228-240`); all
  entities are sorted (`FlushBufferEntity` `span.Sort()`) and drawn twice (opaque + glow pass) every frame.
- F20 `SpriteRender.Flush` uploads the full-capacity arrays (1000 instances, 6000 indices, 4000 vertices) for every UI batch instead of
  the used range (`Alloy.UiLib/Rendering/SpriteRender.cs` `Flush`).
- F21 `Ui.frag:108-143` still uses `dFdx`/`dFdy` into `textureGrad`, and `Particle.frag:12-13` uses `dFdx`/`dFdy`, contrary to the
  documented driver constraint. They work today, so this is a documented risk, not a change.
- F22 GLSL versions are mixed (330 / 430). The real minimum is **OpenGL 4.3** (SSBO in `Ui.vert`/`Particle.vert`, UBO for shadows,
  `ProgramUniform*`). Context creation failure shows a message box (good). No capability detection beyond that.
- F23 Shadow UBO is 4096 entries (`Render.cs:17`); the web build replaces it with a data texture (already handled).

### 3.4 Networking and protocol
- F24 Packet ids are duplicated by hand: client `PacketId` (+ dead `PacketIdOld`, `Networking/Packets/PacketIds.cs:3,75`) and server
  `PacketId` (`GameServer/Game/Network/Messaging/PacketId.cs`); currently equal (spot-checked 10 ids). `Shared/Common.Protocol` holds
  only `LevelRules` and `WorldPosData`. Packet structs are written twice (client `Write`, server `Read`) with no shared test.
- F25 Server trusts the client for: position (`Network/Messaging/Move.cs` -> `EntityExtensions.cs:18`, no speed, wall or teleport check),
  rate of fire (`Systems/Projectiles/PlayerShoot.cs`, no cooldown check), chat length (`PlayerChat.ValidateSpeak` has a cooldown but no
  length limit; `ReadUTF` allows 64 KB, broadcast to everyone), hit reports (validated - good).
- F26 No rate limit on incoming packets per user (`NetworkHandler._pendingReceive` unbounded; one client can queue thousands of packets
  per frame, as 2.2 does by accident).
- F27 `SocketServer._ips` is a plain `Dictionary` touched from the accept callback and from `DisconnectUser` on the game thread, and
  `Remove(ip)` drops the whole per-IP count instead of decrementing (`SocketServer.cs`). `MaxClientsPerIP` is 2000 anyway.
- F28 Session packets (`Hello`, `Load`, `Create`) `await` RPC + database calls, but the game thread blocks on them
  (`NetworkHandler.cs:169` `GetAwaiter().GetResult()`): every login stalls every world for a round trip; if the AccountServer is down,
  each such packet stalls the whole server up to the RPC timeout.
- F29 Version compatibility is enforced by exact string match in `Hello` (good) and by `/app/version` at start (good).
- F30 Plain-text credentials on the wire: HTTP :8080 form posts and the TCP `Hello` packet both carry the password
  (`Networking/Packets/Outgoing/Hello.cs`, `GameServer/Game/Session/Hello.cs`). The browser client is behind nginx TLS, the desktop client is not.

### 3.5 Server loop, concurrency, lifecycle
- F31 Busy-spin game loop with unthrottled `Update()` (2.3). `GameLogic.cs` `Run`/`Update`.
- F32 `Parallel.ForEach` over 2-3 worlds every tick on a 2-core box (`GameLogic.TickWorlds`); the same file notes that parallelising
  `Update()` made things worse. Needs a measurement.
- F33 1000 pre-allocated `User`s each construct a `NetworkHandler` that Expression-compiles every incoming packet factory
  (`NetworkHandler.cs` `_packetFactory`): ~40 compiled delegates x 1000 at startup (slow start, memory) for a per-instance table that
  could be static.
- F34 No graceful shutdown in either server: `deploy -Restart` sends SIGTERM; pending `DbWriter` batches and the DbClient are never
  flushed/disposed; Redis locks stay until the next start's stale-lock sweep (10 s).
- F35 Server `Logger` writes synchronously: `Console.WriteLine` under a lock plus `File.AppendAllLines` (open + close per line) on the
  calling thread, including the game thread and the "LAGGED" warning during lag (`Common/Utilities/Logger.cs:88-108`).
- F36 Behaviour hot reload compiles C# with Roslyn from `BehaviorsDir` = `../../../GameServer/Game/Entities/Behaviors/Library`, a path
  into the source tree that does not exist on the VPS; Roslyn (~20 MB) ships with the server for this dev-only feature.

### 3.6 Persistence and data safety
- F37 **The game server never saves characters.** No `SaveCharacter` RPC exists (`Common/Messaging/Proxies.cs`); `FlushAccount` is
  called only when a ban has lapsed (`Session/Hello.cs:77`). Equipment swaps, potions drunk, items picked up, HP, and any future XP are
  lost on disconnect or restart. Vault chests are the one thing saved (`Worlds/Logic/Vault.cs:127`).
- F38 Player death only disconnects (`EntityCombat.Death`, "TODO: register death"); nothing is recorded (known, graveyard is a placeholder).
- F39 `DbWriter<T>` channel is unbounded; a crash loses at most the batch in flight (fine) but nothing flushes on exit (F34).
- F40 Account locks are per server GUID and never released per disconnect; two sessions of one account on the same server are not
  prevented (`DbClient.VerifyAccount`, `AccountLockManager`).
- F41 Characters live inside `accounts.data` JSONB: every change re-writes the whole account document; no per-character rows or indexes.
  Acceptable at this scale; a migration later would be a real project.

### 3.7 Security
- F42 Password hashing is `SHA1(password + salt)` (`DbClient.cs:202,241`) - fast, unsalted-per-round, no work factor. Should be
  PBKDF2/Argon2 with lazy re-hash on the next successful login (no user-visible change).
- F43 No login/registration rate limiting anywhere (`/account/verify`, `/account/register`, TCP `Hello`); `MaxAccountsPerIp` 3000.
- F44 Credentials sent on every HTTP call (no session tokens) and stored Base64 in `account.xml` on the player's disk
  (`Core/Settings.cs:317-345`).
- F45 Server-side trust gaps: movement speed/teleport, rate of fire, chat length (F25), packet flood (F26).
- F46 RPC example config listens on `0.0.0.0:8081` with `SharedSecret` `changeme` (`rpcServerConfig.example.xml`); VPS_SETUP relies on
  `ufw` to keep 8081 closed. Bind `127.0.0.1` by default.
- F47 Secrets are git-ignored correctly (`postgresConfig.xml`, `rpc*Config.xml`, `.pfx`). Good.
- F48 Admin commands check rank server-side (`Ranks.Of`), hit reports are validated - good work already done there.

### 3.8 Build, dependencies, project structure
- F49 Unused/dead dependencies: `GameServer.csproj` references `Google.Protobuf`, `Grpc.Net.Client`, `Grpc.Tools` (no usage found) and
  `Microsoft.CodeAnalysis.*` (only F36); `Common.csproj` references `System.Drawing.Common` only because `PlayerShoot.cs` has a dead
  `using System.Drawing.Imaging;` (System.Drawing is Windows-only on .NET 7+, a trap for the Linux VPS if it were ever used);
  `AlloyClient.csproj` references `BouncyCastle.Cryptography` (`Networking/RSA.cs`, no callers found); `System.Linq.Async`, `Ionic.Zlib.Core`
  in use. `Nullable` is disabled everywhere except tests.
- F50 `AlloyClient.csproj` cannot be built alone (`$(SolutionDir)` in the post-build) - documented in CLAUDE.md; a `Directory.Build.props`
  would fix it properly.
- F51 `Content/bin` (46 MB of content-builder output) lives inside the content source folder; `dist/` and `WebClient/dist/` (1 GB) inside
  the Testing folder. Ignored by git and skipped by `promote`, but they confuse searches and backups.
- F52 `AGENTS.md` in `alloy-server/` describes projects that no longer exist (`WebServer/`, `GameServerOld/`, LiteDB); the root
  `README.md` is two lines. CLAUDE.md is the only real documentation and is a session log, not an architecture document.
- F53 61 `TODO/FIXME` markers, commented-out packet handlers, `PacketIdOld`, "jank" comments in `EntityTexture.Create`
  (`Projectile.cs:28-71`), `Projectile` "TODO: make struct".

### 3.9 Assets
- F54 `Content/Title/LogoSheet.png` 14.8 MB on disk, 2816x3520 RGBA (~40 MB in VRAM), decoded on the main thread at start and kept for
  the whole session. `Main_Music.wav` 8 MB uncompressed (the other tracks are ogg/mp3). Content total ~44 MB + 46 MB `bin` copy.
- F55 Atlases are built by `Alloy.ContentBuilder` at build time (hash-cached); `Game.atlas` 0.7 MB, `Ui.atlas` 1.8 MB. Sheets are
  organised (see `Tools/Sheets/README.md`). The `playerskins` atlas entries fail to pack (known, cosmetic skins unused).

### 3.10 Tests
Present (349 facts): rules/logic (rewards, moderation, ranks, level curve, music), hit validation, vault, packet buffer isolation,
span reader/writer, version check, commands, world loading. Missing: the fixed-update loop, projectile hit flow, status-text and
particle budgets, send-buffer bounds, packet-id parity between client and server, server movement/shoot validation, chat limits,
graceful shutdown flush, account lock release.

---

## 4. Prioritized plan

Each item: what is wrong - why it matters - where - what should change - risk of the change - expected benefit.
"Wire" = changes the client/server protocol (needs a version bump and a coordinated deploy). All CRITICAL items are wire-neutral
unless marked.

### CRITICAL

**C1. Fixed-update loop runs ~60x too often and has no cap** (F1)
- Why: the direct cause of the 1-5 FPS collapse and the amplifier of everything else; also on the web client (15,000 steps for a
  250 ms browser frame).
- Where: `AlloyClient/AlloyClient/Game/GameScreen.cs:22,148-152`; `WebClient/web/shim/GameWindow.cs:47` inherits it.
- Change: `FixedUpdateStep = 1000d / 60` (ms), cap catch-up to a few steps per frame (e.g. 5) and drop the remainder with a counter
  exposed on the DEV readout; unit test the accumulator with a small pure helper (`FixedStepper`).
- Risk: low. Projectile movement is time-based (`Path.PositionAt(_elapsed)`), so fewer steps do not change trajectories; hit tests
  happen 60 times a second instead of 60,000.
- Benefit: removes the spiral; expected to fix problem 1 outright.

**C2. A hit is re-processed on every remaining fixed step of the frame** (F2, F4, F5)
- Why: direct cause of the damage-text stack and the freeze; also floods the server with hit packets.
- Where: `Game/Objects/Projectile.cs:160-171,212-259`, `Ui/Character/NotificationLayer.cs`, `Networking/SocketSendState.cs`.
- Change: a `_dead` flag checked at the top of `FixedUpdate`/`Update` (and remove dead projectiles at the end of the fixed loop);
  `NotificationLayer` gets a hard cap on live texts (e.g. 48) with pooling and per-entity coalescing ("-12" then "-12 x3" style is
  optional; simplest is drop-oldest); client `SocketSendState.WritePacket` grows the buffer like the server's twin (bounded, e.g.
  1 MB, then drop + log once); tests for the stepper + hit-once + cap.
- Risk: low.
- Benefit: fixes problem 2; one hit = one text, one effect, one packet.

**C3. Game server busy-spins a CPU core forever** (F31)
- Why: on the dev PC this is the biggest single competitor for the client's CPU; on the VPS it is wasted money and heat, and it
  makes tick timing worse, not better.
- Where: `alloy-server/GameServer/Game/GameLogic.cs` `Run`/`Update`.
- Change: after `Update()`, wait for the remainder of the tick (`Thread.Sleep(1)` loop with a final spin of <= 1 ms, or a
  `PeriodicTimer`); keep draining network I/O every 1-5 ms, not millions of times per second. Log actual TPS and tick time.
- Risk: low-medium (the tick becomes slightly less precise; 50 ms ticks tolerate 1 ms).
- Benefit: frees ~one core on the dev PC; expected large FPS gain for the client when self-hosting.

**C4. Character progress is never persisted by the game server** (F37)
- Why: data loss for players: equipment/inventory/HP changes vanish on disconnect or restart. Any XP/level-up work would inherit it.
- Where: `Common/Messaging/Proxies.cs` (no save RPC), `AccServerRpcHandler.cs`, `GameServer` (no caller), `DbClient.FlushAsync`.
- Change: add `SaveCharacter(accountId, Character)` RPC (AccountServer writes the account document through `DbWriter<Account>`),
  call it on disconnect/escape/world switch and on a periodic timer (every 60 s for players with dirty inventory/stats), with a
  server-side dirty flag. Server-only change (both servers deploy together); no client change.
- Risk: medium (touches the persistence path; must not double-write vault chests; test with the DB harness pattern already used).
- Benefit: no more lost progress; prerequisite for XP/levels/loot.

**C5. Session packets block the game thread on RPC + database calls** (F28)
- Why: every login/character load stalls all worlds; an AccountServer hiccup freezes the game server for seconds.
- Where: `GameServer/Game/Network/NetworkHandler.cs:169`; handlers `Session/Hello.cs`, `Load.cs`, `Create.cs`.
- Change: run packet handlers whose `Handle` truly awaits (session packets) as fire-and-continue tasks whose results are re-entered
  through `GameLogic.Enqueue`; keep gameplay packets synchronous. Mark handlers explicitly (`IAsyncPacket`) so nothing else changes.
- Risk: medium (ordering: a Load must not be processed before Hello finished; per-user sequencing queue solves it).
- Benefit: login cost off the tick; server survives account-server outages.

**C6. Client network robustness** (F5, F13, F14)
- Why: silent receive-loop death, process crash from `async void`, endless connect retries.
- Where: `Networking/Client.cs`, `SocketReceiveState.cs`, `SocketSendState.cs`.
- Change: catch `InvalidDataException` -> `Disconnect("Invalid packet")`; `Connect` returns a `Task` and reports failure to the UI
  after N retries; send-buffer growth (C2). Note `Client.cs` is web-patched (`WebClient/tools/patch_rules_more.py`): keep the patch
  rules valid or update them and rebuild the web client.
- Risk: low.
- Benefit: no more "connected but dead" clients.

**C7. Authentication and trust (parts that are wire-neutral)** (F42, F43, F45, F46)
- Why: brute-forceable logins, weak hashes, speed/fire-rate cheats, chat floods, RPC exposure.
- Where: `DbClient.cs:202,241`, `AccountServer/Systems/Account/*`, `Session/Hello.cs`, `Network/Messaging/Move.cs`,
  `Systems/Projectiles/PlayerShoot.cs`, `Systems/Chat/PlayerChat.cs`, `rpcServerConfig.example.xml`.
- Change: PBKDF2-SHA256 (or Argon2id) with lazy re-hash on next successful login (existing accounts keep working); per-IP and
  per-account login attempt limiter on `/account/verify`, `/account/register` and `Hello`; server-side movement plausibility
  (max distance per tick from speed + slack, wall check) with soft correction (`Goto`), fire-rate check with slack, chat length cap
  (e.g. 256) and per-user incoming packet budget per tick (drop + disconnect on abuse); RPC default bind 127.0.0.1.
  The wire-breaking parts (TLS for HTTP/TCP, session tokens instead of passwords per call) are listed under section 6 for a decision.
- Risk: medium (validation slack must not reject honest laggy players; use generous limits and log rejections first, enforce later).
- Benefit: closes the obvious cheats and the brute-force hole without a protocol change.

### HIGH

**H1. Client thread affinity and timer period** (F12): remove `SetThreadAffinityMask(...,1)` (keep `timeBeginPeriod`), fix
`ClearWindowsJank` to call `TimeEndPeriod`. `Alloy.Engine/GameWindow.cs:171-179`. Risk low. Benefit: the render thread can move off
the core the OS/drivers/servers are using.

**H2. Per-frame buffer re-allocation at full capacity, tiles re-expanded every frame** (F17, F18): orphan at the used size
(`BufferData(usedBytes, null)` then `BufferSubData`) or a ring of buffer sets like `SpriteRender`; cache the expanded tile vertices per
chunk behind the existing dirty flag and only concatenate. `InstanceAttributeBuffer.cs`, `Render.Draw.cs`, `Map.cs` `TileChunk`.
Risk: medium (GPU path on this driver - measure flicker with the documented dxcam method before/after; do not remove orphaning).
Benefit: tens of MB less driver traffic per frame; less CPU per frame.

**H3. Entity culling disabled; two entity passes** (F19): re-enable distance culling against the camera's visible radius (data is
already there: `Camera.VisibleTileRadius`), which also shrinks the depth sort. Keep the two passes (they implement the glow/outline
correctly) but skip pass 2 when no drawn entity has an outline. `Entity.cs:228-240`, `Render.Draw.cs` `FlushBufferEntity`.
Risk: low-medium (pop-in at the edge: use radius + 2 tiles). Benefit: large maps (Realm 140x140) stop drawing everything.

**H4. Particle budget and pooling** (F3): cap live generators (e.g. 300), pool `SparkEffect`, spawn trails per real time (e.g. every
30 ms) not per fixed step. `Map.cs:397`, `Projectile.cs:166-170`, `ParticleEffects/*`. Risk low. Benefit: bounded worst case.

**H5. Logging cost and levels** (F9, F8, F35): client minimum level `Information` in Release (`Debug` in Debug builds), guard
hot-path logs with `IsEnabled`, remove the per-packet `[DIAG]` lines and the `Update` tile sample; server `Logger` writes files through
a background channel (one open writer per level file), console under the lock only. `Logging/Logging.cs` (web-patched),
`Client.cs`, `Update.cs`, `Common/Utilities/Logger.cs`. Risk low. Benefit: no console/file I/O on the render or game thread.

**H6. Per-user compiled packet factories** (F33): make `_packetFactory` static (one table). `NetworkHandler.cs`. Risk low.
Benefit: faster server start, less memory.

**H7. Stale state across world switches** (F7): `Reconnect` and `Map.Reset` clear projectiles, particle generators, players,
interactive objects, pending status texts; `Entity.MultiHitUsed` cleared on reset. Risk low. Benefit: no ghost bullets/players.

**H8. One source of truth for packet ids** (F24): move the packet id enum into `Shared/Common.Protocol` and reference it from both
sides (remove `PacketIdOld`); add a parity test now (cheap) even before the move. Wire-neutral as long as values stay identical.
Risk low. Benefit: a class of "client and server disagree" bugs becomes impossible.

**H9. Graceful shutdown** (F34, F39): handle `Console.CancelKeyPress`/`AppDomain.ProcessExit`/SIGTERM in both servers: stop
accepting, save characters (C4), flush `DbWriter`s, dispose the data source, release this server's Redis locks. Risk low-medium.
Benefit: `deploy -Restart` and crashes stop losing the last writes.

**H10. Dead and risky dependencies** (F49): remove Grpc/Protobuf, System.Drawing (delete the dead `using`), BouncyCastle if `RSA.cs`
is truly unreferenced; make Roslyn/behaviour hot-reload a conditional (Debug-only or `--dev`) feature or ship the behaviour sources.
Risk low (build proves it). Benefit: smaller server package, no Windows-only surprises on Linux.

**H11. Documentation drift** (F52): rewrite `alloy-server/AGENTS.md` to the real layout, give the root `README.md` build/run/deploy
instructions (this document becomes `Docs/Architecture` material). Risk none. Benefit: future sessions stop being misled.

### MEDIUM

**M1. Damage numbers are client-side guesses** (F11): either the server sends `Damage` (wire addition: new outgoing packet, old
clients ignore unknown ids after a logged error) or the client shows the roll and reconciles on the next HP update. Decision in section 6.

**M2. Enemy lookup scans everything** (F6): maintain `Map.Enemies` (entities with `IsEnemy`) alongside `Entities`; hit tests iterate
that. Risk low.

**M3. `Parallel.ForEach` over worlds** (F32): measure tick time sequential vs parallel on the dev PC with the new tick metrics; keep
whichever is faster (likely sequential with 2-3 worlds).

**M4. UI batch uploads at full capacity** (F20): upload used ranges only (`_instanceCount`, `_indexCount`, `_vertexCount`) - keeps the
ring semantics, no orphaning. Measure flicker after (documented method).

**M5. Title textures resident all session** (F15, F54): add `Texture.Delete`, unload the title logo/map on entering the game and reload
on return (or keep if VRAM turns out fine - measure with the DEV readout extended with a GPU memory estimate).

**M6. Shader derivative use documented as risky** (F21): leave as is; add a comment + a `Settings` feature flag that switches
`textureGrad` to `textureLod` in `Ui.frag` if a user reports UI corruption. No default change.

**M7. Movement/tick config**: expose fixed-step count cap, particle budget, status text cap and log level in `Settings`/server config
(not every constant - just the budgets and levels).

**M8. Account lock lifecycle** (F40): release the lock on disconnect (RPC `ReleaseAccount`) so a stuck session cannot block a player
for the life of the server; prevent a second login of the same account on the same server (disconnect the old session, RotMG style).

**M9. Startup JIT** (F16): measure startup and steady-state with `TieredCompilation` default vs false; keep the better one.

**M10. Tests** (3.10): add the ones listed there as each item lands.

**M11. `TileMap.Get` off-by-one** (F10): `>=`. Trivial.

**M12. Death not recorded** (F38): out of scope of this audit's fixes (feature), noted for the graveyard work.

### LOW

- L1 Remove `PacketIdOld`, commented-out handlers, the "jank" scale math comments; name `SetWindowsJank` for what it does.
- L2 `Content/bin`, `dist/`, `WebClient/dist/` locations: move build output out of the source tree (content builder output to
  `obj`/`bin`), or at least document.
- L3 `Directory.Build.props` with `SolutionDir` fallback so `dotnet build AlloyClient.csproj` works (F50).
- L4 Magic numbers: hit radius 0.5, projectile z 0.25, sight radius, in one `CombatConstants`/`Shared` place used by both sides
  (`HitValidation.HitRadius` already exists server-side).
- L5 `AccountServer` `ThreadPool.SetMinThreads(1000,1000)` is excessive for 64 concurrent requests; leave default.
- L6 `MaxClientsPerIP` 2000 / `MaxAccountsPerIp` 3000 are "no limit"; set sane values (e.g. 5 / 10) once rate limiting exists.

---

## 5. Proposed implementation order (Phase 3), one verifiable step at a time

Each step: build both solutions, run all tests, build the web client when a patched file is touched, update the change log and the
manual test list, and stop for the user's manual check when the step changes runtime behaviour.

0. **Instrumentation first (no behaviour change)**: extend the DEV/F5 readout with fixed steps per frame (and steps dropped),
   projectiles, particle generators, live status texts, packets queued/sent per frame, send-buffer bytes, entity scans per frame,
   and a split of frame time into Update / FixedUpdate / Draw / Swap. Server: log actual TPS, tick ms (avg/max), queue lengths every
   10 s at Info. This is how every later step is verified without me running the game.
1. **C1 + C2 + H4 + H7** (client gameplay loop): stepper with cap, hit-once, status text cap/pool, particle budget, send-buffer growth,
   world-switch cleanup. Tests: `FixedStepperTests`, `ProjectileHitOnceTests`, `NotificationCapTests`, `ParticleBudgetTests`,
   `SendBufferTests`. Manual test: shoot + walk in the Nexus, hit the dummy 10 times.
2. **H5 + H1** (logging levels/guards, affinity/timer). Web client rebuilt (Logging.cs and Client.cs are patched files).
3. **C3 + H6 + M3** (server loop sleep, static packet factory, world tick parallelism measured).
4. **C5 + H9 + C4** (async session packets, graceful shutdown, character save RPC). Server only.
5. **C6 + C7 wire-neutral parts** (client network robustness; hashing upgrade with lazy re-hash; login limiter; movement/fire-rate
   plausibility in log-only mode first; chat length; per-user packet budget; RPC bind).
6. **H2 + H3 + M2 + M4** (rendering CPU side: used-size orphaning or buffer ring, cached tile expansion, culling, enemy list, UI
   used-range uploads). Flicker measured with the documented dxcam method by the user or on request.
7. **H8 + H10 + H11 + L-items** (shared packet ids + parity test, dependency cleanup, docs, small fixes).
8. **Phase 4 validation**: full rebuild of exe + web, all tests, a reference-check pass over everything touched, updated final report.

Estimated wire impact: none for steps 0-7 except what is explicitly decided in section 6.

---

## 6. Decisions needed from the user before or during Phase 3

1. Approve the plan and the order above (or reorder).
2. **Damage packet** (M1): server-sent damage numbers (wire addition, needs a version bump and deploy of server + exe + web together)
   or client-side numbers reconciled later. Recommendation: server-sent, bundled into the next release.
3. **TLS + session tokens** (F30, F44): the desktop exe talks plain HTTP/TCP to the VPS; a real fix is HTTPS for :8080 (nginx is
   already there for the web client) and a login token instead of the password on every call. Both are protocol changes. Recommendation:
   plan it as its own release after this audit's work; not started without a yes.
4. **Password hash upgrade** (C7): PBKDF2 with lazy migration - no user-visible effect, but it touches the login path. Recommendation: yes.
5. **Movement/fire-rate enforcement** (C7): start in log-only mode and switch to enforcing after a week of clean logs. Recommendation: yes.
6. **Character auto-save interval** (C4): 60 s + on disconnect. Recommendation: yes.
7. **Title assets** (M5): unloading on entering the game is a code change only; splitting/re-encoding `LogoSheet.png` would be an art
   pipeline change (not done without asking).

---

## 7. Measurement plan (numbers I need from the user after step 0 and after step 1)

Repro A (problem 1): Nexus, stand still, hold fire for 10 s; then walk in circles while holding fire for 10 s. Press F5 and read:
FPS, Avg/P99 frame ms, "Fixed steps/frame" (expected 1 after the fix, ~1000 before), "Steps dropped", projectiles, particle gens,
allocated delta KB.
Repro B (problem 2): hit the DPS dummy 10 times. Read: live status texts (expected <= 10, not hundreds), packets sent/frame
(expected ~1 per hit), send-buffer bytes, and whether the client stays responsive.
Repro C (server): with the client idle in the Nexus, read Task Manager CPU for `GameServer.exe` (expected: from ~25% of the machine
(one core) to a few percent after step 3) and the server's periodic "TPS/tick ms" log line.

---

## 8. Manual test list (grows with each step; nothing is tested by me at runtime)

### After steps 0 + 1 (client gameplay loop) - please run these and send me the numbers

Start the two local servers, launch the client, log in, enter the Nexus. Press **F5** (or the DEV tab) to show the readout. New
rows at the bottom: `Fixed steps`, `Update/Fixed/Draw`, `Projectiles`, `Particle gens`, `Status texts`, `Packets/frame`,
`Entity scans/frame`. The server console now prints a `[STATS] tps=...` line every 10 seconds.

1. **Idle baseline**: stand still 10 s. Expect: FPS ~60 (VSync), `Fixed steps: 1 (dropped 0)` most of the time (0 or 2 on some
   frames is normal), `Projectiles: 0`, `Packets/frame: 0` (1 on tick frames).
2. **Shoot standing still**: hold fire at nothing for 10 s. Expect: FPS stays ~60; `Projectiles` = a handful (the bullets in flight);
   `Particle gens` well under 300; `Entity scans/frame` = projectiles x entities (hundreds to a few thousand, not millions);
   `Packets/frame` 1-3.
3. **Shoot while walking** (the problem-1 repro): walk circles holding fire for 15 s, also rotate the camera with Q/E.
   Expect: FPS stays ~60, no drop to single digits; `Fixed steps` stays 1 (dropped stays 0 or grows only slowly).
   Send me: FPS, Avg/P99/Max ms, the Fixed steps line, Projectiles, Particle gens, Entity scans.
4. **Hit the DPS dummy** (the problem-2 repro): shoot the Nexus dummy 10 times in a row, then keep firing for 10 s.
   Expect: ONE "-dmg" number per hit, normal size, floating up and fading; `Status texts` <= 10 and `(dropped 0)`;
   `Packets/frame` about 1 per hit; `send buf` a few hundred B; the client never freezes.
   Send me: the Status texts line, Packets/frame line, whether the dummy loses HP and eventually disappears (700 HP).
5. **Wall shot**: shoot into a wall/tree. Expect: the bullet vanishes at the wall as before (no change intended).
6. **World switch cleanup**: fire a few shots, then step into the Realm portal / press R (Escape) to the Nexus while bullets are in
   the air. Expect: no leftover bullets or sparks in the new world; the Nearby players panel does not list players from the old world.
7. **Server line**: with the client idle in the Nexus, copy one `[STATS]` line from the GameServer console
   (expected before step 3: `tps=20.0`, tick work avg under 1 ms, interval avg ~50 ms).
8. **Browser** (optional): same as 3 and 4 in the web client at `http://127.0.0.1:8126/?game=ws://127.0.0.1:8125` if you have the
   bridge running; the readout rows are there too.

If anything is wrong, tell me what the readout showed at that moment.

---

## 9. Change log

### 2026-09-21 - Step 0 (instrumentation) and Step 1 (client gameplay loop: C1, C2, H4, H7)

Client (`AlloyClient/AlloyClient`):
- NEW `Game/FixedStepper.cs`: millisecond accumulator that returns 0..MaxStepsPerFrame (5) fixed steps per frame and drops (and
  counts) any backlog beyond that. `GameScreen.FixedUpdateStep` is now `1000/60` ms (was `1/60`, seconds against a ms accumulator).
- `Game/GameScreen.cs`: uses the stepper; measures Update / FixedUpdate / Draw ms into `PerfCounters`.
- NEW `Game/PerfCounters.cs`: plain per-frame counters read by the F5 readout.
- `Game/Objects/Projectile.cs`: `_dead` flag - a projectile that hit, expired or struck a wall does nothing more until Map.Update
  removes it that same frame (it used to keep hitting on every remaining fixed step).
- `Game/Map.cs`: `MaxParticleGenerators = 300` budget (extra effects dropped + counted); new `ClearWorldObjects()` returns
  projectiles to the pool and clears particle effects, entities, players, interactive objects, render targets and queued texts;
  `Reset()` uses it; projectile/particle counts recorded for the readout.
- `Networking/Packets/Incoming/Reconnect.cs`: world switch calls `Map.ClearWorldObjects()` (used to clear only entities).
- `Ui/Character/NotificationLayer.cs`: `MaxLive = 48` live texts, `MaxQueued = 96`; oldest dropped + counted; `ClearQueued()`;
  null owner ignored.
- `Networking/SocketSendState.cs`: write buffer grows 64 KB -> up to 1 MB, then drops packets (counted) instead of throwing;
  `PendingBytes`, `Capacity`, `DroppedPackets`.
- `Networking/Client.cs`: removed the per-packet `[DIAG] SENDING` Debug string; warns once (then every 1000) when a packet is
  dropped; records packets queued per frame and the send-buffer size. (`Client.cs` is replaced wholesale in the web build by
  `WebClient/web/shim/WebClient.cs`, which got the same packet counter.)
- `Game/Objects/Util/EntityUtils.cs`: counts entities scanned by the hit tests.
- `Ui/Components/Elements/DebugStats.cs`: eight new rows (see section 8).

Server (`alloy-server/GameServer`):
- `Game/GameLogic.cs`: `Stats` - one `[STATS]` Info line every 10 s with real TPS, tick work avg/max, tick interval avg/max, users,
  worlds, queued actions. The loop itself is unchanged (that is step 3).

Tests (`AlloyClient/Tests/AlloyClient.Tests`): NEW `Game/FixedStepperTests.cs` (8), `Game/BudgetTests.cs` (7), `Networking/SendBufferTests.cs` (4).
Results: client 223 passed, shared 35, server Common 225, GameServer 102 - all green. Web client (`python WebClient/tools/build_web.py`,
interpreter build) compiles and assembles `WebClient/dist/site` (build id 20260921093330); the only warnings are the pre-existing
CS0108 member-hiding ones in the HUD files.

Behaviour changes to be aware of: bullets now hit-test 60 times a second instead of ~60,000 (trajectories unchanged, they are
time-based); a hit produces exactly one damage number / effect / packet; at most 300 particle effects and 48 damage texts exist at
once; a world switch discards in-flight bullets and effects from the old world.

Not changed on purpose (later steps): the damage number is still the client's own roll (M1), logging levels (step 2), the server
busy-spin (step 3).

**User's manual test result (2026-09-21):** "FPS looks great it literally isn't going under 60 at all no matter how much I move
and shoot"; dummy hits show one number per hit and the client no longer freezes (tests 1, 2, 4, 5 pass; 3 not testable in practice -
bullets do not live long enough to survive a world switch). The pasted server log showed the `[DIAG] sight tick` spam and a
`max=7738ms` tick that coincided with selecting console text to copy it - confirming F35 (synchronous console logging on the game
thread), fixed in step 2 below.

### 2026-09-21 - Step 2 (logging, thread affinity: H5, H1) and Step 3 (server loop: C3, H6)

Client:
- `Logging/Logging.cs`: minimum level Information in every build; `ALLOY_LOG=debug` / `=trace` environment variable turns the chatty
  levels back on. (Web-patched file: the patch rule still matches, web build verified.)
- Removed the one-shot and per-tick `[DIAG]` Debug lines from `GameScreen.cs`, `Map.cs`, `Networking/Client.cs` (RECEIVING),
  `Packets/Incoming/Update.cs` (per-tick tile sample string + per-object line), `Packets/Incoming/MapInfo.cs`.
- `Alloy.Engine/GameWindow.cs`: the render thread is no longer pinned to CPU core 0; `ClearWindowsJank` now calls `timeEndPeriod`
  (it called `timeBeginPeriod` a second time). The `SetThreadAffinityMask`/`GetCurrentThread` imports are gone.

Server:
- `Common/Utilities/Logger.cs`: rewritten around a bounded channel (20,000 lines, drop-oldest) and one background writer thread;
  console + file writes happen there, file handles stay open (`AutoFlush`), `Fatal` and process exit flush with a 2 s timeout, a
  dropped-lines warning is emitted when the queue overflowed. Debug lines are skipped unless `ALLOY_DEBUG_LOG` is set (they used to
  print in Debug builds). Public API unchanged (`ILogger` + the static helpers).
- `GameServer/Game/Systems/Sight/PlayerSightManager.cs`: removed the per-tick `[DIAG] sight tick` line and its logger.
- `GameServer/Game/GameLogic.cs`: the loop sleeps 1 ms at a time until 2 ms before the tick, then spin-waits the last moment
  (`timeBeginPeriod(1)` on Windows so 1 ms sleeps are real). `Update()` (queued actions, world removals, network I/O) still runs
  every iteration, i.e. about once a millisecond instead of millions of times a second.
- `GameServer/Game/Network/NetworkHandler.cs`: the incoming packet factory table is static (one copy instead of one per pooled User).

Verification: exe + server solutions build, 223 + 35 + 225 + 102 tests pass; measured with the new build running idle with one
player in the Nexus: GameServer CPU = 5.3% of one core over 10 s (it was a full core before). Web build: `build_web.py` succeeded after the Logging.cs change (patch rule matched, site assembled).

Manual test additions (section 8, items 9-11): the GameServer console should be quiet apart from `[STATS]` every 10 s (expected
`tps=20.0`, tick work avg well under 1 ms, interval avg 50 ms, max around 51-52 ms); selecting text in the console must not stall
the game any more (walk while a console selection is active, the client keeps moving smoothly); the client console should show
only Information/Warning lines.

**User's result (2026-09-21):** FPS still solid. The pasted server log settled at `tps=20.0`, tick work avg 0.6-0.8 ms, interval
max 51-52 ms once the machine was idle; the `LAGGED` lines in the first minute coincided with the browser-client build running in
the background and the client starting up. Startup noise to clean up later: `Missing descriptor for <stock enemy>` (behaviours for
enemies that were deleted from the XML) and `Behavior not found for 'Dummy Strong' / 'Target Strong' / DpsDummy*` (dummies have no
behaviour on purpose) - added to LOW.

### 2026-09-21 - Step 4 (server only: C5 async session packets, H9 graceful shutdown, C4 character save)

Common:
- NEW `Common/Database/CharacterDb.cs`: `Clean(Character)` (a validated copy: never stored at 0 HP - full HP instead, because death
  is not a real feature yet; HP/MP clamped to their max; negative counts and out-of-range levels clamped; null arrays become empty)
  and `SaveAsync(accountId, chr)`: `jsonb_set(data, '{Characters,<CharId>}', ...)` - only that character's entry is replaced, so
  gold / inbox / rank changes made by the AccountServer meanwhile are never overwritten. Writes nothing (and warns) if the slot does
  not exist.
- `Common/Messaging/Proxies.cs`: new RPC `SaveCharacter(int accountId, Character chr)`; `AccServerRpcHandler` implements it.

GameServer:
- NEW `Game/GameThreadSynchronizationContext.cs`: installed on the game thread; continuations after an `await` are queued through
  `GameLogic.Enqueue`, so handler code always runs on the game thread while the awaited RPC/database work runs elsewhere.
- `Game/Network/NetworkHandler.cs`: `HandleIncomingPackets` no longer blocks (`GetAwaiter().GetResult()` is gone). A handler that is
  still awaiting is remembered per user and no further packet of that user is processed until it completes (Hello -> Load order
  kept); a faulted handler disconnects that user with the error logged. `Reset` clears the pending handler and queue.
- `Game/Session/Hello.cs`, `Load.cs`, `Create.cs`: after each `await`, stop if the user disconnected meanwhile (the pooled User
  object may already be reused).
- NEW `Game/Systems/Persistence/CharacterSaver.cs`: `CharacterSnapshot.CaptureStats` (the reverse of `LoadCharacterStats`: level,
  fame, XP, potions, HP/MP/max, the six base stats) + `Clone`; `CharacterSaver` keeps the newest snapshot per (account, character)
  and a background worker sends them over the new RPC, retrying 5 s later on failure; `FlushAsync` for shutdown; counters
  `Saved`/`Failed`/`Pending`.
- `Game/Network/GameInfo.cs` `Unload` (disconnect, Escape, portal): snapshot + queue the save before the entity leaves the world
  (it used to update only the in-memory record, which was then thrown away).
- `Game/GameLogic.cs`: installs the sync context; `AutosaveIntervalMs = 60_000` - every playing character is queued once a minute
  (`Autosave: N character(s) queued.` at Info); `RequestStop()` ends the loop; `ShutdownAsync()` saves every playing character,
  sends "The server is restarting. Please reconnect in a moment.", flushes the sockets and waits (8 s) for the save queue;
  `DrainPendingActions()` extracted (public, used by tests and shutdown).
- `Program.cs` (GameServer): Ctrl+C, SIGTERM and the console window closing all call `RequestStop` and wait for the shutdown.
- `Program.cs` (AccountServer): the same signals stop the HTTP listener; the accept loop then ends and `DbClient.Dispose()` flushes
  the batched writes ("Database writes flushed. Bye.").

Tests: NEW `Common.Tests/CharacterDbTests.cs` (7), `GameServer.Tests/Persistence/CharacterSnapshotTests.cs` (2),
`GameServer.Tests/GameThreadSyncContextTests.cs` (4). Server solution builds; Common 232, GameServer 108, Protocol 35 - all green.
No client change in this step (nothing on the wire changed; the RPC is between the two servers, which deploy together).

Known limitation kept on purpose: account-level values (gold, fame totals) are still written only by the AccountServer; the
character save covers everything `LoadCharacterStats`/`InitPlayerInventory` read. Note for later (F56): `Create.Handle` still sends the
game server's whole in-memory `Account` to `CreateCharacter`, which flushes that stale copy - the same overwrite class the targeted
character save avoids; to fix when the account model is next touched.

**User's result (2026-09-21):** items 12-15 pass - potion slot kept after closing with the X, GameServer prints the Bye line and
exits, login afterwards works with the inventory intact (after the lock fix below).

Manual tests for step 4 (section 8, items 12-15):
12. **Save on disconnect**: in the Nexus, move an item to another inventory slot (or /give yourself something and equip it), drink a
    potion if you have one, then close the client (or press R twice / log out). Log back in: the inventory layout, equipment and
    potion count must be exactly as you left them. (Before this step everything reverted.)
13. **Autosave**: stay in the Nexus for 2 minutes; the GameServer console prints `Autosave: 1 character(s) queued.` about once a
    minute; the AccountServer console stays quiet (Debug is off). Nothing should stutter when it happens.
14. **Graceful shutdown**: with the client in the Nexus, press Ctrl+C in the GameServer window. Expect in that window
    `Shutdown requested: saving characters and disconnecting players...` then `Shutdown: 1 player(s) saved and disconnected. Bye.`,
    the client goes back to the character list (it may show "The server is restarting..."), and the window closes. Then Ctrl+C the
    AccountServer window: `Shutdown requested: stopping the HTTP listener...` then `Database writes flushed. Bye.`
    Start both again and log in: the character is intact.
15. **Login feel**: log in / switch worlds / escape to the Nexus a few times - it should feel the same as before (the change moved the
    login's database wait off the game thread; nothing user-visible is meant to differ).

**User's result (2026-09-21):** item 12 passed on the first try (a potion moved to another slot, client closed with the X, potion
still there after relogging). Item 14 found a bug: the GameServer printed `Shutdown requested` but never the `Bye` line and stayed
alive. Cause: the shutdown routine ran on the main thread with the game-thread synchronization context still installed, so its own
`await` was queued into the game loop's queue, which nothing pumps once the loop has stopped. Fixed: `Run` removes the context after
the loop, and the shutdown wait pumps the queue itself with `ConfigureAwait(false)`; regression test
`ShutdownCompletesEvenIfTheGameThreadContextIsStillInstalled`. The AccountServer's shutdown was correct all along - its window
closed too fast to read, but `logs/AccountServer/info/log.txt` has `Shutdown requested ...` and `Database writes flushed. Bye.`
Server tests: Common 232, GameServer 109, Protocol 35 - green.

**Retry result:** the GameServer now prints the `Bye` line and exits. But logging back in afterwards bounced the user to the
character book every time. Redis showed `account_lock:10` still owned by the GameServer that had just exited: `IpcServer` awaited
`jsonRpc.Completion`, which THROWS when the connection drops, so `handler.Close()` (which releases that server's locks) was never
reached. This was pre-existing (F40): every GameServer exit leaked its locks, hidden by the AccountServer's startup sweep as long as
both were restarted together. Fixed twice over: `IpcServer` releases in a `finally`, and `DbClient.VerifyAccount` takes an optional
`isLockOwnerLive` check (the AccountServer passes `IpcServer.Clients.ContainsKey`) - a lock owned by a server that is no longer
connected is released and taken over instead of answering "Account in use". Rebuilt, 376 server tests green, both servers
restarted; the stale key was gone at restart.

### 2026-09-21 - Step 5 (C6 client network robustness; C7 wire-neutral security and validation)

Common / AccountServer:
- NEW `Common/Database/PasswordHasher.cs`: PBKDF2-SHA256, 100,000 rounds, stored as `pbkdf2$<rounds>$<base64>`; still verifies
  the old `Base64(SHA1(password + salt))` rows and reports them as needing an upgrade.
- `Common/Database/DbClient.cs`: registration stores PBKDF2; `VerifyAccount` verifies through the hasher and, on success, silently
  re-hashes a legacy or weaker hash in place (`UPDATE logins SET password_hash`) - every existing account migrates on its next
  login, no password is reset. Failed logins per account name are limited by `DbClient.LoginAttempts` (10 in 10 minutes ->
  `VerifyStatus.TooManyAttempts`, a new enum value with the message "Too many failed logins. Please wait a few minutes and try
  again."); a success clears the counter. Covers the HTTP endpoints AND the game server's Hello, since all of them end here.
- NEW `Common/Database/AttemptLimiter.cs`: sliding-window counter per key, clock-injected, self-pruning.
- NEW `AccountServer/Systems/Account/LoginGuards.cs` + `Verify.cs` / `Register.cs`: per-IP limits (20 failed password checks per
  10 minutes; 5 registrations per hour).
- `rpcServerConfig.example.xml`: `ListenAddress` 127.0.0.1 with a comment (the real, git-ignored config on the VPS is untouched).

GameServer (all LOG-ONLY - nothing is corrected or refused yet; the logs decide whether to enforce later):
- NEW `Game/Systems/Combat/PlausibilityRules.cs`: `MovementRules` (max distance per elapsed ms from the client's own speed formula,
  x1.5 Speedy, x1.5 slack, +1 tile flat, allowance capped at 2 s) and `FireRateBucket` (a leaky bucket refilled at the weapon's real
  rate from dexterity + RateOfFire, one packet per projectile, 30% slack, two attacks of burst).
- `Network/Messaging/Move.cs`: distance since the last Move packet checked; `[PLAUSIBILITY]` warning on the 1st and every 50th
  violation per session. Known false positives to expect in the log: the first Move after a server-side teleport (Escape, portals,
  /goto-style commands) - that is why it is log-only.
- `Systems/Projectiles/PlayerShoot.cs`: fire rate checked the same way (also removed the dead `using System.Drawing.Imaging`).
- `Network/GameInfo.cs`: per-session `LastMoveAtMs`, `FireRate`, violation counters (reset on load / reset).
- `Systems/Chat/PlayerChat.cs`: chat messages over 256 characters are refused with an error to the sender (they used to be
  broadcast at up to 64 KB); blank messages ignored. This one IS enforced.
- `Network/NetworkHandler.cs`: packet budget per drain - 200+ packets in one tick logs a warning, 2000+ disconnects the user
  (`IllegalAction`). An honest client sends a handful per tick.

Client:
- `Networking/Client.cs`: `Connect` can no longer throw out of its `async void` (everything is caught and ends in a clean
  `Disconnect`); a refused connection is retried 10 times, one second apart, then gives up with a logged reason instead of forever;
  a corrupt length prefix from the server now disconnects cleanly ("Invalid packet from server") instead of silently killing the
  receive task. (This file is replaced wholesale in the web build, which has its own WebSocket client - unaffected.)

Tests: NEW `Common.Tests/PasswordHasherTests.cs` (5), `Common.Tests/AttemptLimiterTests.cs` (5),
`GameServer.Tests/Combat/PlausibilityRulesTests.cs` (11 cases). Results: Common 242, GameServer 120, Protocol 35, Client 223 - green.
Web client rebuilt: `build_web.py` succeeded (patch rules matched, site assembled).

Compatibility: nothing on the wire changed. Existing accounts keep working (legacy hashes verify, then upgrade). A player who fails
their password 10 times waits 10 minutes; a machine that fails 20 times waits 10 minutes; 5 new accounts per hour per address.

Manual tests for step 5 (section 8, items 16-20):
16. **Normal login still works** (this also upgrades your stored password hash silently - nothing visible).
17. **Wrong password**: type a wrong password on purpose, twice. Expect the usual "Invalid account credentials." message, then log in
    correctly - it must work (the counter is cleared by the success).
18. **Lockout**: fail the password 10 times in a row; the 11th attempt (even with the RIGHT password) must answer
    "Too many failed logins. Please wait a few minutes and try again." Wait 10 minutes (or restart the AccountServer, which forgets
    the counters) and log in normally.
19. **Play for a few minutes** (walk, shoot, use a portal, press R to escape to the Nexus). Then look in the GameServer window for
    `[PLAUSIBILITY]` lines. Expected: none while walking and shooting normally; possibly ONE movement line right after a
    portal / Escape (a server-side teleport looks like a jump to this check). Paste any you see - they tune the limits before
    anything is enforced.
20. **Chat**: send a normal message (works); paste a message longer than 256 characters - it is refused with
    "Message too long (max 256 characters)." and nothing is broadcast.

**Results (2026-09-21):** user: login works (16), chat works (20), several minutes of play with portals and R produced no
`[PLAUSIBILITY]` line at all (19). Verified from here: the `logins` table shows only the account that logged in (id 10) moved to a
`pbkdf2$` hash, every other row still legacy - the lazy migration works as designed; 12 POSTs to `/account/verify` with a made-up
name answered "Invalid account credentials." ten times and "Too many failed logins..." from the 11th (18). Not explicitly exercised
live: 17 (two wrong passwords then the right one) - covered by `AttemptLimiterTests.ASuccessClearsTheKey` and the login path
calling `Clear` on success. Note: those 10 test failures also count toward the per-IP limit for 127.0.0.1 for 10 minutes.

Server log observation: `LAGGED` lines (tick started 75-150 ms after the previous one) appear in bursts while the machine is busy
(the browser build ran 11:25-11:33; the client started at 11:43) and never when idle. With the loop now sleeping, a late tick is the
OS scheduler's doing, not the server's. Tweak (builds with step 6): the game thread asks for `AboveNormal` priority, late ticks are
counted into the `[STATS]` line (`late=N`) and only a tick 5x late gets its own warning line.

### 2026-09-21 - Step 6 (client rendering, CPU side: H2, H3, M2, M4) + the late-tick tweak

Client rendering:
- NEW `Rendering/TileChunkMesh.cs`: each 16x16 tile chunk owns its own static GPU buffer + VAO, rebuilt only when that chunk's
  tiles change (an Update packet marks it dirty), drawn as-is every frame. `Render.ExpandTiles` (pure, tested) expands tiles into
  the 6-vertex form; `Render.UploadTileChunk` / `BeginTiles` / `DrawTileChunk` replace the old `DrawTiles`. The single
  35,840-tile buffer (about 20 MB re-orphaned and re-uploaded every frame after re-expanding every visible tile on the CPU) is gone;
  the ground now costs one draw call per visible chunk (up to 35) and no per-frame upload at all. Rebuilds still orphan before
  writing (the driver rule), but a handful of times per world instead of 60 times a second. Meshes of a world that is left are
  deleted on the next Draw on the main thread (`TileMap.Clear` may run on the network thread, where GL is not allowed).
- `Game/CullRules.cs` (new, tested) + `Entity.UpdateVisibility(matrix, cameraPos, cullRadius)`: entities farther than the screen's
  half-diagonal x1.25 + 3 tiles from the camera are not drawn (no sort, no two-pass draw, no shadow); the local player is always
  drawn. Culling had been commented out entirely.
- `Map.Enemies` (only `IsEnemy` entities, maintained with `Entities`) is what `GetClosestEnemy` scans now.
- `SpriteRender.Flush` uploads only the used range of each batch (the ring of 8 buffer sets is unchanged; nothing is re-allocated).
- Engine fixes: `VertexArrayObject.Dispose` called `GL.DeleteBuffer` (wrong object; VAOs leaked) -> `DeleteVertexArray`;
  `VertexBuffer.LengthBytes` was never set (Length was overwritten with the byte size).
- Web shim: `deleteVertexArray` added to `web/shim/GL.cs` + `web/wwwroot/gl.js` (the web build failed without it; green now).

Server: game thread `AboveNormal` priority; `LAGGED` lines folded into the `[STATS]` line as `late=N` (only a tick 5x late still
gets its own line).

Tests: NEW `Game/CullRulesTests.cs` (4). Results: client 227, protocol 35, Common 242, GameServer 120 - green. Web build green.

Manual tests for step 6 (section 8, items 21-24) - this is the one that needs YOUR EYES on the HD 4400:
21. **Ground looks right**: walk the whole Nexus and the Vault, rotate the camera (Q/E), zoom (shift + wheel). Every tile must be
    there, edges/blends unchanged, no missing chunk squares, no flicker on the ground. Walk to a map edge.
22. **World switch**: Realm portal, then R back. The new world's ground appears complete; nothing of the old one lingers.
23. **Culling**: things at the screen edge must not pop in late while rotating or walking fast. If a tall tree / the Bug Board /
    the Jukebox pops in at the edge, say so - the margin gets raised.
24. **F5 readout**: `Tiles` now counts drawn tiles (same as before), `Entities` should be visibly LOWER than before (only what is
    near), `Entity scans/frame` should be near zero while shooting (only the dummy and any enemies are scanned), and the FPS /
    Avg ms should be equal or better than this morning. Also watch the loading screen and the first frames of a world for a bright
    flash or garbled GUI (the documented flicker class) - none expected, but that is the check for the UI upload change.

**User's result (2026-09-21):** ground, world switch and edge behaviour "look fine"; Entities readout 260 -> 240 in the Nexus
(expected: the Nexus is small enough that nearly everything is inside the visible radius - the Realm is where culling matters).
Request on the side: camera rotation back to free-form. `Settings.SnapRotation` now defaults to false (hold Q/E = smooth
continuous spin at `RotateSpeed`; the 45-degree snap stays available in Options) and the user's `settings.xml` was flipped to
False (backup `settings.xml.bak-before-freerotation`). Open question from the user: "master rotation" for sprites of very different
sizes (8x8 to 32x32 and larger) - no concrete symptom given yet; billboards are anchored at the feet via `TextureBottomInset`, so
the thing to look for is whether large sprites appear to swing or sink when the camera turns.

### 2026-09-21 - Step 7 (H8 shared packet ids, H10 dead dependencies, H11 docs, LOW items)

- NEW `Shared/Common.Protocol/PacketId.cs`: THE packet id table (PascalCase, byte, `Unknown = 255`), used by both clients and the
  game server. The client's `PacketIds.cs` and the server's `PacketId.cs` are now one line each: `global using PacketId =
  Common.Structs.PacketId;`. The server's 25 files with UPPERCASE members were renamed mechanically (`SERVER_PROJECTILE_PROPS` ->
  `ServerProjectileProps`, `UNUSED*` -> the client's names). The dead client `PacketIdOld` enum is gone. Both tables were verified
  identical by value before the merge (66 vs 65 entries, no value mismatches). NEW `PacketIdTests` (3, 27 cases) pin the wire values.
- Dependencies removed: `Google.Protobuf`, `Grpc.Net.Client`, `Grpc.Tools` (GameServer, never used), `System.Drawing.Common` and
  `System.Linq.Async` (Common; the one `await foreach` is BCL), `BouncyCastle.Cryptography` (client + web; `Networking/RSA.cs`
  deleted, it had no callers). Roslyn (`Microsoft.CodeAnalysis.*`) is now referenced only when the server is built with
  `-p:BehaviorHotReload=true` (`BEHAVIOR_HOTRELOAD` define); otherwise `/reloadbehaviors` answers that it is not available in this
  build. The VPS never had the behaviour source files this feature needs, so nothing is lost there.
- LOW items: `TileMap.Get` off-by-one (`>` -> `>=`); `SetWindowsJank` -> `SetWindowsTimerResolution` / `RestoreWindowsTimerResolution`;
  AccountServer no longer sets `ThreadPool.SetMinThreads(1000, 1000)`; `MaxAccountsPerIp` 3000 -> 10; the start-up
  `Missing descriptor for <stock enemy>` and `Behavior not found for '<dummy>'` lines are Debug now (both are expected).
- Docs: root `README.md` rewritten (layout, build/test, run locally, release, requirements); `alloy-server/AGENTS.md` rewritten to
  the real layout and the rules established by this audit (the old one described `WebServer/`, `GameServerOld/` and LiteDB);
  `CLAUDE.md` points at this document and marks the two open problems fixed.
- Results: client 227, protocol 62, Common 242, GameServer 120 - green. Web build: see below.

Manual test for step 7 (section 8, item 25): a normal session (login, Nexus, portal, chat, shoot the dummy, R) - nothing is meant to
look or behave differently; the packet ids are the same numbers under new names. The server console should start much quieter
(no descriptor / behaviour lines).

---

**2026-09-22 (later) - FPS readout fact-check + GPU counters (H2 / M9 still open).** `Alloy.Engine/FrameTiming.cs` (work / swap /
sleep / GPU ms + monitor Hz, written by the desktop `GameWindow` loop; a 4-deep `GL_TIME_ELAPSED` query ring that only reads finished
results, disabled if queries fail), `Alloy.Engine/Graphics/GpuStats.cs` (draw calls + uploaded bytes per frame, counted at every
`GL.Draw*` / upload site, copied into `PerfCounters` at `BeginFrame`), `AlloyClient/Game/FrameStats.cs` (pure: FPS = frames / the real
window length, nearest-rank P90 / P99 over exactly the last second's frames, no per-frame sort; `FrameStatsTests` 6). `DebugStats`
shows the limiter next to the FPS (VSync at N Hz / cap N / no limit) and "Unlimited: ~N FPS" = 1000 / max(CPU work, GPU time), and
logs `[FPS] ...` every 10 s. No rendering path was changed for speed: measure with the new readout first (the streaming-buffer
history in section 3.3 and the CLAUDE.md flicker notes say every buffer change on this driver needs a dxcam measurement).
Web build: `Sampler.ClampToEdge` uses the GL constant 0x812F because the WebGL shim has no `TextureWrapMode` enum.

**2026-09-22 (evening) - measured frame costs (Game/DevPerfTest.cs + PerfSections.cs).** Remote player ~0.15 ms each on the HD 4400
laptop (not the reported halving); walking +0.3-0.5 ms (more props in view); model pass ~1 ms fixed. H2 re-tested: no-orphan writes and a
no-orphan ring both measured slower (driver sync) - the orphan stays; sprite buffer 10000 -> 4000. Options now save on change.

**2026-09-22 (night) - static props baked per area (H2 closed for props).** `StaticProps` + `StaticPropMesh` + `Render.Baked` +
Model.vert `DepthColumn`: model pass 2.5-3.9 -> ~0.1 ms, CPU work 5.75 -> 3.24 ms on the HD 4400 laptop; players / enemies measured cheap
(10 real pirates +0.2-0.8 ms). The per-frame sprite pass (players, projectiles, names, bars) is unchanged.

## 10. Baseline (before any change, 2026-09-21)

| Step | Result |
| --- | --- |
| `dotnet build AlloyClient/WarriorsAndWizards.Client.sln` | succeeded, 0 warnings, 0 errors (8.7 s) |
| `dotnet build alloy-server/WarriorsAndWizards.Server.sln` | succeeded, 0 errors, 2 MinVer warnings ("not a valid Git working directory", version 0.0.0-0: the Testing folder is not a git repo, so the server binaries built here carry version 0.0.0 in their console title - cosmetic, noted as L7) |
| `dotnet test` AlloyClient.Tests | 204 passed, 0 failed |
| `dotnet test` Common.Protocol.Tests | 35 passed, 0 failed |
| `dotnet test` Common.Tests (server) | 225 passed, 0 failed |
| `dotnet test` GameServer.Tests | 102 passed, 0 failed |

566 tests green before any change. The web client was not rebuilt for the baseline (no patched file has changed).


---

## 11. Final report (Phase 4 validation, 2026-09-21)

Everything below was done on 2026-09-21 in the Testing folder. Nothing was deployed or promoted by the audit; the user does that.

### Architecture (as it stands now)
One game, two clients, two servers. The desktop client (OpenTK, GL 4.3) and the browser client (WebAssembly + WebGL2, generated
from the same sources at build time) share every line of game logic, UI and protocol; only window/GL/audio/socket shims differ.
`Shared/Common.Protocol` now owns the packet id table as well as level rules and positions. The AccountServer (HTTP :8080) is the
only process on the database (Postgres, characters inside the account's JSON document) and the RPC hub; the GameServer (TCP :2050)
simulates worlds on one game thread with sparse-set managers and reaches persistence only over RPC. Section 1 has the full picture.

### Problems discovered
Sections 2-3 list 56 findings (F1-F56). The two reported problems had one root cause (a seconds/milliseconds mix-up that ran the
fixed-update loop ~1000 times per frame, plus a hit re-processed on every one of those steps). Beyond them the serious ones were: a
game loop that never slept; no character persistence at all from the game server; logins blocking the game thread on RPC; no
graceful shutdown; account locks leaking on every server exit; SHA1 passwords, no login limits, blind trust of client position and
fire rate, 64 KB chat messages; synchronous console logging that froze the server when its console was selected; the ground
re-uploaded (~20 MB) every frame and entity culling switched off; two hand-kept packet id tables; dead dependencies.

### Changes made (steps 0-7, all in section 9)
Instrumentation (F5 readout rows, `[STATS]`); FixedStepper + hit-once + particle/status-text/send-buffer budgets + world-switch
cleanup; logging levels and a background server logger; no core-0 pinning; server loop sleeps; static packet factory; async
session packets via a game-thread synchronization context; graceful shutdown in both servers; character autosave/save-on-leave
through a targeted RPC; RPC lock release fixed + dead-server lock takeover; PBKDF2 with silent upgrade; login/registration
limiters; log-only movement/fire-rate plausibility; chat length cap; packet flood budget; client connect/receive robustness;
per-chunk static ground meshes; entity culling; enemy-only hit scans; UI used-range uploads; VAO delete fix; shared `PacketId`;
dependency removal; docs. Free-form camera rotation restored at the user's request.

### Performance
Measured by the user on the target machine (i5-4300U, HD 4400, servers on the same box): shooting + walking went from 1-5 FPS to a
steady 60 (VSync); the dummy no longer freezes the client; the game server went from a full core to ~5% of one while idle, ticking
at exactly 20 TPS with sub-millisecond tick work; late ticks now only appear while the machine is busy with something else.
Ground rendering no longer uploads anything per frame; far entities are no longer sorted or drawn. Not measured yet: frame time on
the large Realm map (the culling win) and the browser client's frame time after these changes.

### Compatibility
Nothing on the wire changed; the deployed 0.3.4 client keeps working against these servers, and vice versa (the servers only added
an RPC between themselves, which deploy together). Existing accounts keep their passwords (legacy hashes verify, then upgrade).
Windows desktop needs GL 4.3; the browser needs WebGL2 + WebAssembly; the servers run on Linux (System.Drawing is gone, so no
Windows-only trap remains). Known compatibility risks kept: `Ui.frag`/`Particle.frag` still use manual derivatives (F21, works);
GLSL 330/430 mix (F22).

### Security
Done: PBKDF2, login limits per account and per IP, registration limit per IP, chat cap, packet flood cap, plausibility logging,
RPC bound to loopback by default, dead-server lock takeover. Still open (user decisions, section 6): TLS for the desktop client's
HTTP and TCP, session tokens instead of the password on every request, the Base64 password in the local `account.xml`,
enforcement of the plausibility checks once the logs are clean, `MaxClientsPerIP` in the config (2000).

### Database
Characters are now written (targeted `jsonb_set`, never the whole account from the game server); pending writes are flushed at
shutdown; the schema is unchanged. Remaining risks: F41 (characters embedded in the account document - fine at this scale),
F56 (`Create` still sends the game server's stale account copy to `CreateCharacter`), `DbWriter` batches lost on a hard crash
(bounded, by design), no death record (feature not built).

### Networking
Client: bounded connect retries, clean disconnect on corrupt input, growable send buffer, one hit = one packet. Server: no
blocking on handlers, per-user ordering kept, packet budget, chat cap, plausibility logging. Remaining: server-authoritative
movement/fire rate (log-only today), the client-rolled damage number (M1, needs the user's decision on a `Damage` packet), token
auth and TLS (section 6).

### Rendering
Requirement stays OpenGL 4.3 (SSBO/UBO in the UI, particle and shadow shaders). Kept on purpose: orphan-before-write on the
per-draw entity/model buffers, the UI's ring of buffer sets, no hardware instancing, no `textureGrad` changes. Changed: ground as
static per-chunk meshes, culling, used-range UI uploads, VAO deletion. Not done (candidates, section 4 M5/M6): unloading title
textures during play, a feature flag for the derivative-based UI shading, the tile-buffer capacity vs. `MaxTileData` mismatch.

### Technical debt
Removed: `PacketIdOld`, `RSA.cs`, five unused packages, the `[DIAG]` logging, the core-0 pin, the busy-spin, the second packet
id table. Replaced: the server logger, the fixed-update loop, `SetWindowsJank` naming. Documented and kept: the driver
workarounds, the two-pass entity draw, the mixed GLSL versions, F56, the `LogoSheet.png` size, `TieredCompilation=false` (M9,
unmeasured). Still listed: 61 TODO/FIXME markers in the source (untouched, none blocking).

### Testing
Before: 566 tests. After: 651 (client 227, protocol 62, Common 242, GameServer 120) plus the editor's 54, all green, plus the
browser build as a compile check after every client change. New coverage: the fixed stepper, budgets, send buffer, character
cleaning and snapshot, the game-thread context and shutdown, password hashing and upgrade, attempt limiting, movement/fire-rate
plausibility, culling and tile expansion, packet id values. Still untested by machine: anything that needs a window or the
network stack (covered by the manual test list, all of which the user passed).

### Validation pass details
Clean (`--no-incremental`) rebuild of both solutions: 0 errors. Every warning is pre-existing and stylistic (CS0108 member
hiding in four HUD classes, nullable-annotation warnings in the test projects and two DTOs, analyzer release-tracking notes in the
shader source generator, MinVer's "not a git directory" in Testing); none comes from the audit's changes. Stale-reference sweep
over `.cs`, `.py` and `.md`: nothing refers to the removed `PacketIdOld`, `RSA.cs`, `DrawTiles`, `VisibleTiles` (client) or
`SetWindowsJank`. Web build: patch rules all matched. Editor tests: 54 passed. `VPS_SETUP.md` updated for the Debug-only
descriptor lines.

**User's result (2026-09-21):** the final build passed the normal-session check ("everything looks good"). Every manual test item
1-25 has been passed by the user. The build in Testing is ready for `deploy` and `promote`, which the user runs.

### Follow-up after the final check (2026-09-21, later)
The user then noticed the HP/MP bars blinking rapidly while walking / shooting. The only UI-path change of the day was M4
(used-range uploads in `SpriteRender.Flush`), and the audit note said to revert it alone if any flicker appeared: reverted, with a
comment saying why (full-size uploads keep every buffer of a set the same shape from batch to batch; this driver evidently needs
that). If the blink persists after the revert, the next suspect is `HudBar.Update` (per-row `Visible = w > 0` while HP regenerates
each tick), to be measured with the documented dxcam method. Also added on request: `Settings.ShowStatusBars` + hotkey
`Settings.ToggleStatusBars` (default H) on the hotkeys page, a "Show HP/MP Bars" on/off option, and `PlayerPlate` hides both bars
when off. Client 227 + protocol 62 tests green; web rebuilt.

**Resolved (2026-09-21, later):** the blinking bars were the small HP/MP bars UNDER the character (entity renderer), not the plate.
Cause: `TypeBar`/`TypeHpBar` gave the fill quad, its background quad and the character sprite the same `SortId` (= depth in
`Object.vert`), and `FlushBufferEntity` sorts with an unstable sort each frame, so the order of the two bar quads flipped between
frames and the background sometimes covered the fill (depth test `Less` rejects equal depth) - a pre-existing upstream bug (the
author's commented-out `+ 0.001` was an attempt at it). Fix: fill at `SortId - 0.0004`, background at `SortId - 0.0002` (nearer =
smaller), deterministic. The 32-set UI ring and the batch counter stay (harmless, useful). The M4 revert stays too. The HP/MP
toggle (`Settings.ShowStatusBars`, hotkey H) now hides the player's OWN under-character bars; the plate bars are untouched.

**The Portal (2026-09-21, later):** a public, read-only API was added to the AccountServer (`Systems/Public/PublicHandlers.cs`,
`Common/Database/PublicDb.cs`) for the RealmEye-style site in `Portal/`. It shares nothing with the login paths: no session, no
writes, answers cached 60 s, 240 requests per address per minute, and the profile shape is built by one function with tests that
assert the private fields (credits, inventory, potions, e-mail, account id, deleted characters) are never serialised. Presence
("online in Nexus") and the online count are asked from the game servers over the existing RPC. `Program.GetContextIP` now honours
`X-Forwarded-For` for connections from the VPS itself (nginx), which also makes the browser client's /api rate limits per visitor
instead of per proxy. Server 428 tests green. The site itself is untested in a browser until the user deploys it.

**In-client Portal (2026-09-21, later):** the same pages inside the client (`PortalScreen` / `PortalView`), reached from a PORTAL row on the
title screen. It reuses the public API through `PortalRequests` (no login, never sets the offline flag, answers queued to the frame loop,
one request per key in flight, cached per visit) and the client's own XML for the wiki. `PortalDataTests` cover the JSON. Client 232 +
protocol 62 tests green. Not run in a window by me: the user tests the screen.

**Fast travel (2026-09-21, later):** the client's Hello now asks for a KIND of world (`WorldIds`: Nexus / Vault / GuildHall, negative ids)
chosen on the Character Book's new FAST TRAVEL tab; the server resolves the account's Vault or its guild's hall (new `GuildHall` world, one per
guild) before `SetGameInfo`. The SERVERS title row is gone (the list lives on that tab). Deploy the server first, or bump the version: an old
server refuses the new ids. Server 431 + client 232 + protocol 62 tests green; not run in a window.

### Remaining work (in order of value)
1. Decide and build the `Damage` packet (M1) so damage numbers are real - a version bump.
2. Turn plausibility checks from log-only to enforcing once a few days of VPS logs show no false positives.
3. TLS + session tokens for the desktop client (section 6, a release of its own).
4. F56: `Create` should send only what it needs, not the stale account.
5. Measure on the Realm and in the browser; then M5 (title textures), M9 (tiered compilation), M3 (world tick parallelism).
6. Death / graveyard, XP awarding and enemies - features, not audit items.

### Recommendations (do NOT do yet - they need bigger decisions)
- Moving characters out of the account JSON into their own table (real per-character rows/indexes) - only when a second server or
  real player counts make the document model a bottleneck.
- A proper session/token model across HTTP and TCP together with TLS - design it once, not piecemeal.
- Replacing the hand-written packet `Read`/`Write` pairs with a generated or shared serializer - large, wire-neutral, low urgency.
- Server-side simulation of player movement (full authority) - a gameplay-feel change that needs the log-only data first.
