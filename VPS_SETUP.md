# VPS setup (Linux) - 104.152.50.196

Everything here runs ON the VPS over SSH (`ssh root@104.152.50.196`) unless it says "on your PC".
Assumes Debian / Ubuntu (apt). Ports: 22 SSH, 8080 account server, 2050 game server. Never open 8081 (internal RPC),
5432 (Postgres) or 6379 (Redis).

## 1. Check the box
```
cat /etc/os-release
ip -4 addr | grep inet
free -h
```
`ip` must list `104.152.50.196` (the account server binds to exactly that address - see the note at the bottom if not).

## 2. Install the basics + Postgres + Redis
```
apt update && apt upgrade -y
apt install -y curl ca-certificates unzip libicu-dev ufw postgresql redis-server
systemctl enable --now postgresql redis-server
redis-cli ping
```
`redis-cli ping` must answer `PONG`.

## 3. Postgres: password + empty database (the server creates its own tables)

(Pick your own strong password for `<your-db-password>`, and put the same one in `alloy-server/Common/Resources/Config/Data/postgresConfig.xml` - that file is git-ignored on purpose. Never write the real password in this document: it is committed to git.)
```
runuser -u postgres -- psql -c "ALTER USER postgres PASSWORD '<your-db-password>';"
runuser -u postgres -- createdb alloy
PGPASSWORD=<your-db-password> psql -h localhost -U postgres -d alloy -c "select 1;"
```
The last command must print a table with `1`.

## 4. Install the .NET 10 runtime
```
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --runtime dotnet --install-dir /usr/share/dotnet
ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
dotnet --list-runtimes
```
Must list `Microsoft.NETCore.App 10.0.x`.

## 5. Upload the server (on your PC, PowerShell - not on the VPS)
On the VPS first: `mkdir -p /opt`
Then on your PC:
```
scp -r "<your repo folder>\alloy-server\bin\debug\net10.0" root@104.152.50.196:/opt/alloy-server
```
(re-run the same command after every update; stop the services first - step 8.)

## 6. Firewall (allow SSH FIRST so you don't lock yourself out)
```
ufw allow 22/tcp
ufw allow 8080/tcp
ufw allow 2050/tcp
ufw --force enable
ufw status
```
If your provider has its own firewall in their control panel, open 8080 and 2050 there too.

## 7. Run it as services (start on boot, restart on crash)
```
cat > /etc/systemd/system/alloy-account.service <<'EOF'
[Unit]
Description=Alloy AccountServer
After=network.target postgresql.service redis-server.service

[Service]
WorkingDirectory=/opt/alloy-server
ExecStart=/usr/bin/dotnet /opt/alloy-server/AccountServer.dll
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

cat > /etc/systemd/system/alloy-game.service <<'EOF'
[Unit]
Description=Alloy GameServer
After=alloy-account.service

[Service]
WorkingDirectory=/opt/alloy-server
# The account server creates the RPC certificate on its first start; give it a moment.
ExecStartPre=/bin/sleep 6
ExecStart=/usr/bin/dotnet /opt/alloy-server/GameServer.dll
Restart=on-failure
RestartSec=8

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable --now alloy-account
systemctl enable --now alloy-game
```

## 8. Check it, and how to update / restart
```
systemctl status alloy-account alloy-game --no-pager
journalctl -u alloy-account -n 40 --no-pager
journalctl -u alloy-game -n 40 --no-pager
ss -ltn | grep -E ':8080|:2050'
```
- Account log should say `Listening on 104.152.50.196:8080`; game log should say `[RPC] Connected to AccountServer`.
- The `Missing descriptor` lines (behaviours of monsters that were removed from the game) are normal; since 2026-09-21 they are Debug level and only show with the `ALLOY_DEBUG_LOG` environment variable set.
- Update: `systemctl stop alloy-game alloy-account`, upload again (step 5), `systemctl start alloy-account alloy-game`.
- Live logs: `journalctl -u alloy-game -f` (Ctrl+C to leave).

## 9. From your PC
```
Test-NetConnection 104.152.50.196 -Port 8080
Test-NetConnection 104.152.50.196 -Port 2050
```
Both `TcpTestSucceeded : True`, then run `<your repo folder>\AlloyClient\AlloyClient\bin\Debug\net10.0\AlloyClient.exe`.

## 10. The website, the download and the forums

- `warriorsandwizards.com` is the Website repo on Cloudflare Pages (push = deploy). Its Download page links to the client zip below.
- `play.<domain>` is the browser client on this VPS (`deploy -SetupWeb` once, `deploy -Web` per update). `deploy -Client` also uploads the
  Windows zip to `/var/www/warriors/download/WarriorsAndWizards-Client.zip`, reachable as `https://warriorsandwizards.com/downloads/WarriorsAndWizards-Client.zip` through a
  `_redirects` rule in the Website repo (Pages cannot host files over 25 MB). `appEngineConfig.xml` `<DownloadUrl>` sends outdated clients to the
  website's Download page. `deploy -Web` never touches that folder. `deploy -Linux` does the same for a 64-bit Linux build
  (`WarriorsAndWizards-Client-linux.tar.gz`, executable bit set on the VPS; players run `bash run.sh`).
- The **launcher** (`Launcher/`, what players actually download): `deploy -Launcher` publishes it for Windows + Linux (single self-contained
  file each, ~7 MB packed) and records it in `download/manifest.json`. That manifest is what the launcher reads: game version, archive per
  platform with size + sha256, launcher version. `deploy -Client` and `deploy -Linux` each update their own entry, so the order for a
  release is: `-Client`, `-Linux`, `-Launcher` (the launcher only when it changed - bump `<Version>` in `Launcher/Launcher.csproj` AND
  `SelfUpdate.Version`; a test enforces they match). The launcher downloads the game into a `game` folder next to itself, verifies the
  hash, swaps folders, can replace itself, and starts the game. `WarriorsAndWizards.exe --check` only reports. If the manifest ever loses an
  entry (it happened once when an ssh password was mistyped mid-deploy), `deploy -Manifest` rebuilds it from the archives already on the VPS
  without uploading anything.
- `forums.<domain>` is NodeBB on this VPS: DNS A record `forums` -> the VPS (grey cloud), then on your PC
  `.\deploy.ps1 -SetupForums -ForumAdminEmail you@example.com` (asks for the admin password; re-run the same command later to upgrade
  NodeBB). It installs Node 20, a `nodebb` Postgres role + database on the existing Postgres, NodeBB in `/opt/nodebb` as the service
  `ww-forums`, and nginx + Let's Encrypt for the name. Needs about 1 GB of free RAM while it builds. Logs: `journalctl -u ww-forums -f`.
  Never open port 4567 in the firewall; nginx proxies to it on localhost.
- **Seeing the servers on the VPS desktop**: the game + account server, the browser bridge, nginx, Postgres, Redis and NodeBB all run as
  systemd services, so they never appear as open applications when you log into the desktop. `deploy -Shortcuts` puts three launchers
  on the desktop next to the WaW folder: "Game Servers - Live Console" (a terminal following their output, like a console you started by
  hand; closing it does not stop them), "Game Servers - Status" and "Game Servers - Restart". `deploy -Status` shows the same from the PC.
- **What `deploy -Server` does NOT overwrite**: `postgresConfig.xml`, `redisConfig.xml`, `rpcClientConfig.xml`, `rpcServerConfig.xml`,
  `rpc-server.cer/.pfx` in `/opt/alloy-server/Resources/Config/Data` are the VPS's own and are left out of the package (since 2026-09-21; before
  that every deploy replaced them with the PC's copies, which broke the moment the VPS database got a password of its own). A fresh VPS gets
  them once from the step 5 upload, then edits them in place.
- **nginx -> account server**: the account server's listener only accepts requests whose `Host` header is its own listen address
  (`104.152.50.196:8080`); a proxied request carrying `Host: play...` / `portal...` gets a 404 "Not Found (Not Found)" from the listener
  itself. Both setup scripts therefore set `proxy_set_header Host <ip>:8080` (found 2026-09-21 when every Portal call answered 404).
- **The Portal** (`portal.<domain>`, the game's RealmEye): a static site (`Portal/site`) plus the account server's read-only
  `/public/...` endpoints, proxied by nginx as `/api/public/...` on that name. DNS A record `portal` -> the VPS (grey cloud), then on your PC
  `.\deploy.ps1 -SetupPortal` once (nginx site with pretty URLs + Let's Encrypt), `.\deploy.ps1 -Server` if the servers on the VPS predate
  the public API, and `.\deploy.ps1 -Portal` to build the item / class data + icons from the game XML (`Tools/Portal/build_portal_data.py`)
  and upload the site to `/var/www/portal`. Re-run `-Portal` whenever items, classes or the site change; the profiles themselves come live
  from the database (answers cached 60 s on the account server, 240 requests per visitor per minute).

## If something fails
- Account server can't bind `104.152.50.196` (the IP is not on the VPS's own network card, i.e. the provider NATs it): edit
  `/opt/alloy-server/Resources/Config/Data/appEngineConfig.xml`, change `<Address>` to `+`, restart alloy-account. (Do the same in
  the repo so it survives the next upload.)
- Game server says it can't find `rpc-server.cer`: the account server has not created it yet - `systemctl restart alloy-game`.
- Password authentication failed for user postgres: redo step 3.
- File-not-found errors that only happen on Linux are usually upper/lower-case in a file name; the log shows the exact path.
