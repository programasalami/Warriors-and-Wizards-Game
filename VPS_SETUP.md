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
- The ~300 `Missing descriptor` warnings in the game log are normal (monsters were removed from the game).
- Update: `systemctl stop alloy-game alloy-account`, upload again (step 5), `systemctl start alloy-account alloy-game`.
- Live logs: `journalctl -u alloy-game -f` (Ctrl+C to leave).

## 9. From your PC
```
Test-NetConnection 104.152.50.196 -Port 8080
Test-NetConnection 104.152.50.196 -Port 2050
```
Both `TcpTestSucceeded : True`, then run `<your repo folder>\AlloyClient\AlloyClient\bin\Debug\net10.0\AlloyClient.exe`.

## If something fails
- Account server can't bind `104.152.50.196` (the IP is not on the VPS's own network card, i.e. the provider NATs it): edit
  `/opt/alloy-server/Resources/Config/Data/appEngineConfig.xml`, change `<Address>` to `+`, restart alloy-account. (Do the same in
  the repo so it survives the next upload.)
- Game server says it can't find `rpc-server.cer`: the account server has not created it yet - `systemctl restart alloy-game`.
- Password authentication failed for user postgres: redo step 3.
- File-not-found errors that only happen on Linux are usually upper/lower-case in a file name; the log shows the exact path.
