<#
  This folder -> VPS in one command. Run it from PowerShell on your PC:

      .\deploy.ps1                 # server + client
      .\deploy.ps1 -Server         # only rebuild + upload the servers, then restart them on the VPS
      .\deploy.ps1 -Client         # only rebuild + zip the public client for testers (dist\WarriorsAndWizards-Client.zip)
      .\deploy.ps1 -Server -NoUpload   # build + package but don't touch the VPS (a dry run)
      .\deploy.ps1 -Web           # build the BROWSER client (WebAssembly) and upload it to the VPS's nginx (play.warriorsandwizards.com)
      .\deploy.ps1 -SetupWeb      # one time: install nginx + HTTPS certificate + websocket bridge on the VPS (needs the DNS record first)
      .\deploy.ps1 -SetVersion 0.3.4   # release a new game version: sets it in the client AND the server config (then deploy -Server -Client -Web)

  Looking after the VPS (none of these upload or change any files):
      .\deploy.ps1 -Status             # are the servers running? players connected, memory, disk, last log lines
      .\deploy.ps1 -Logs              # the last 80 lines of the game + account server logs   (-LogLines 200 for more)
      .\deploy.ps1 -Logs -Follow      # the same, live, until you press Ctrl+C
      .\deploy.ps1 -Restart           # restart the game + account servers (players are dropped for ~15 seconds)
      .\deploy.ps1 -Backup            # download a copy of the database to Documents\WW_Backups
      .\deploy.ps1 -Shortcuts         # make a WaW folder on the VPS desktop with shortcuts to the live servers, settings, maps, website and backups (moves nothing)
      add -DryRun to any of them to only PRINT what would run

  Versions: the client (Core\Settings.cs BuildVersion) and the server (gameServerConfig.xml Version) must match - the game server turns away any client
  whose version differs, and clients ask the account server for the current version at start-up to show an "update required" prompt instead. Bump it
  with -SetVersion whenever the client and server change together, then redeploy all three (-Server -Client -Web): a web page or exe still on the old
  version is then stopped at the title screen with a prompt to download the new desktop client.

  Server: builds RealmServer.sln, packs bin\debug\net10.0, uploads it, stops both services on the VPS, unpacks over
  /opt/alloy-server, starts them again and shows the result. The VPS's database, Redis and its generated RPC certificate are
  not touched (the package contains none of them).
  Client: builds a self-contained Windows client (testers do NOT need .NET installed) and zips it into dist\.

  Tip: to stop typing the VPS password 2-3 times per run, set up an SSH key once:
      ssh-keygen -t ed25519
      type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh root@104.152.50.196 "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"
#>
param(
    [switch]$Server,
    [switch]$Client,
    [switch]$NoUpload,
    [switch]$Web,
    [switch]$SetupWeb,
    [string]$SetVersion,
    [switch]$Status,
    [switch]$Logs,
    [switch]$Follow,
    [int]$LogLines = 80,
    [switch]$Restart,
    [switch]$Backup,
    [switch]$Shortcuts,
    [string]$BackupDir = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'WW_Backups'),
    [switch]$DryRun,
    [string]$WebDomain = 'play.warriorsandwizards.com',
    [string]$VpsHost = '104.152.50.196',
    [string]$VpsUser = 'root'
)

$ErrorActionPreference = 'Stop'
$deploying = $Server -or $Client -or $Web -or $SetupWeb -or $SetVersion
$ops = $Status -or $Logs -or $Restart -or $Backup -or $Shortcuts
if (-not ($deploying -or $ops)) { $Server = $true; $Client = $true }
$opsOnly = $ops -and -not $deploying     # VPS care commands do not need the game versions to line up

$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$remote = "$VpsUser@$VpsHost"

# ---- game version: one number, kept in two files (client + server) --------------------------------------------------------------------------
$settingsCs = Join-Path $root 'AlloyClient\AlloyClient\Core\Settings.cs'
$serverCfg = Join-Path $root 'alloy-server\Common\Resources\Config\Data\gameServerConfig.xml'
function Get-ClientVersion { [regex]::Match([IO.File]::ReadAllText($settingsCs), 'BuildVersion\s*=\s*"([^"]+)"').Groups[1].Value }
function Get-ServerVersion { [regex]::Match([IO.File]::ReadAllText($serverCfg), '<Version>([^<]+)</Version>').Groups[1].Value }
function Set-FileText($path, $pattern, $replacement) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = [IO.File]::ReadAllText($path)
    $new = [regex]::Replace($text, $pattern, $replacement)
    if ($new -eq $text) { throw "Could not find the version in $path" }
    [IO.File]::WriteAllText($path, $new, (New-Object Text.UTF8Encoding($hasBom)))
}

if ($SetVersion) {
    if ($SetVersion -notmatch '^\d+\.\d+\.\d+$') { throw "-SetVersion must look like 1.2.3 (got '$SetVersion')" }
    Set-FileText $settingsCs '(BuildVersion\s*=\s*")[^"]+(")' ('${1}' + $SetVersion + '${2}')
    Set-FileText $serverCfg '(<Version>)[^<]+(</Version>)' ('${1}' + $SetVersion + '${2}')
    Write-Host "Game version is now $SetVersion (client + server config)." -ForegroundColor Green
    if (-not ($Server -or $Client -or $Web -or $SetupWeb)) {
        Write-Host 'Next: deploy -Server -Client -Web   (all three, so nobody is left on the old version)' -ForegroundColor Yellow
        exit 0
    }
}
if (-not $opsOnly) {
$clientVersion = Get-ClientVersion
$serverVersion = Get-ServerVersion
if ($clientVersion -ne $serverVersion) {
    throw "Version mismatch: the client says '$clientVersion' but the server config says '$serverVersion' - the game server would refuse every client. Fix it with:  deploy -SetVersion $clientVersion"
}
Write-Host "Game version $clientVersion" -ForegroundColor DarkGray
}

function Step($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Check($what) { if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" } }

# Runs one command line on the VPS (or just prints it with -DryRun).
function Invoke-Remote([string]$command) {
    if ($DryRun) { Write-Host "[dry run] ssh $remote $command" -ForegroundColor DarkYellow; return }
    ssh $remote $command
}

# Runs a small bash script on the VPS. It is sent as base64 so no quoting or line-ending problems can creep in on the way.
function Invoke-RemoteScript([string]$script) {
    $script = $script -replace "`r`n", "`n"
    if ($DryRun) { Write-Host "[dry run] ssh $remote  <- runs this script there:" -ForegroundColor DarkYellow; Write-Host $script; return }
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($script))
    ssh $remote "echo $b64 | base64 -d | bash"
}

# A backup must be a whole, readable gzip of a Postgres dump - not an empty or cut-off file. pg_dump always ends its output with a
# "database dump complete" line, so a partial file (even one Windows PowerShell reads without complaint) is caught by that line missing.
function Assert-GoodBackup([string]$path) {
    $size = (Get-Item $path).Length
    if ($size -lt 512) { throw "The backup is only $size bytes - something went wrong: $path" }
    $fs = [IO.File]::OpenRead($path)
    try {
        $gz = New-Object IO.Compression.GZipStream($fs, [IO.Compression.CompressionMode]::Decompress)
        $buf = New-Object byte[] 65536
        $tail = ''
        $first = $true
        while (($n = $gz.Read($buf, 0, $buf.Length)) -gt 0) {
            $text = [Text.Encoding]::ASCII.GetString($buf, 0, $n)
            if ($first) {
                if ($text -notmatch 'PostgreSQL database dump') { throw 'The file is not a Postgres dump.' }
                $first = $false
            }
            $tail += $text
            if ($tail.Length -gt 4096) { $tail = $tail.Substring($tail.Length - 4096) }
        }
        if ($first) { throw 'The backup is empty or damaged.' }
        if ($tail -notmatch 'PostgreSQL database dump complete') { throw 'The backup is cut off (it does not end the way a finished dump does).' }
    } finally { $fs.Dispose() }
}

if ($Server) {
    Step 'Building the servers'
    Push-Location (Join-Path $root 'alloy-server')
    try {
        dotnet build RealmServer.sln --nologo -v:q
        Check 'Server build'
    } finally { Pop-Location }

    Step 'Packing the servers'
    $bin = Join-Path $root 'alloy-server\bin\debug\net10.0'
    $pack = Join-Path $dist 'alloy-server.tgz'
    if (Test-Path $pack) { Remove-Item $pack -Force }
    tar -czf $pack -C $bin .
    Check 'Packing'
    '{0:N1} MB -> {1}' -f ((Get-Item $pack).Length / 1MB), $pack

    if (-not $NoUpload) {
        Step "Uploading to $remote"
        scp $pack "${remote}:/tmp/alloy-server.tgz"
        Check 'Upload'

        Step 'Restarting the servers on the VPS'
        $cmd = 'systemctl stop alloy-game alloy-account && mkdir -p /opt/alloy-server && tar -xzf /tmp/alloy-server.tgz -C /opt/alloy-server && rm -f /tmp/alloy-server.tgz && systemctl start alloy-account alloy-game && sleep 12 && systemctl is-active alloy-account alloy-game && journalctl -u alloy-account -n 3 --no-pager && journalctl -u alloy-game -n 3 --no-pager'
        ssh $remote $cmd
        Check 'Remote restart'
        Write-Host "`nServers updated." -ForegroundColor Green
    } else {
        Write-Host "`n(-NoUpload: VPS untouched)" -ForegroundColor Yellow
    }
}

if ($Client) {
    Step 'Building the public client (self-contained, Windows x64)'
    $clientDir = Join-Path $root 'AlloyClient'
    $out = Join-Path $dist 'client'
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    Push-Location $clientDir
    try {
        dotnet publish 'AlloyClient\AlloyClient.csproj' -c Release -r win-x64 --self-contained true "-p:SolutionDir=$($clientDir -replace '\\','/')/" -o $out --nologo -v:q
        Check 'Client publish'
    } finally { Pop-Location }

    # The game's art / sound / fonts are built by the project's post-build step next to the built exe, and 'publish' does not
    # carry that folder along - copy it in or the client starts with no content.
    $built = Join-Path $clientDir 'AlloyClient\bin\Release\net10.0\win-x64\Content'
    if (-not (Test-Path $built)) { throw "Built content folder not found: $built" }
    Copy-Item $built (Join-Path $out 'Content') -Recurse -Force

    # The audio engine loads OpenAL from runtimes\win-x64\native (where a normal build puts it), but a self-contained publish
    # drops it next to the exe - put a copy where the client looks.
    $native = Join-Path $out 'runtimes\win-x64\native'
    New-Item -ItemType Directory -Force -Path $native | Out-Null
    Copy-Item (Join-Path $out 'soft_oal.dll') $native -Force

    Step 'Zipping the client'
    $zip = Join-Path $dist 'WarriorsAndWizards-Client.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
    '{0:N1} MB -> {1}' -f ((Get-Item $zip).Length / 1MB), $zip
    Write-Host "`nClient zip ready (this is what testers download)." -ForegroundColor Green
}

if ($SetupWeb) {
    Step "Setting up the web client on the VPS ($WebDomain)"
    scp -r (Join-Path $root 'WebClient\vps') "${remote}:/root/ww-vps"
    Check 'Upload of the setup files'
    ssh $remote "bash /root/ww-vps/setup_web.sh $WebDomain"
    Check 'Web setup'
    Write-Host "`nWeb setup finished. Now run: .\deploy.ps1 -Web" -ForegroundColor Green
}

if ($Web) {
    Step 'Building the browser client'
    $aotSdk = Join-Path $env:USERPROFILE '.dotnet-wasm\dotnet.exe'
    $flags = @()
    if (Test-Path $aotSdk) { $flags += '--aot' } else { Write-Host 'wasm-tools SDK not found: building the (slower) interpreted version' -ForegroundColor Yellow }
    python (Join-Path $root 'WebClient\tools\build_web.py') @flags
    Check 'Web build'
    $site = if ($flags.Count) { Join-Path $root 'WebClient\dist\site-aot' } else { Join-Path $root 'WebClient\dist\site' }
    $pack = Join-Path $dist 'ww-site.tgz'
    if (Test-Path $pack) { Remove-Item $pack -Force }
    tar -czf $pack -C $site .
    Check 'Packing the site'
    '{0:N1} MB -> {1}' -f ((Get-Item $pack).Length / 1MB), $pack

    if (-not $NoUpload) {
        Step "Uploading to $remote"
        scp $pack "${remote}:/tmp/ww-site.tgz"
        Check 'Upload'
        ssh $remote 'rm -rf /var/www/warriors/* && tar -xzf /tmp/ww-site.tgz -C /var/www/warriors && rm -f /tmp/ww-site.tgz && ls /var/www/warriors | head'
        Check 'Remote unpack'
        Write-Host "`nWeb client updated: https://$WebDomain" -ForegroundColor Green
    } else {
        Write-Host "`n(-NoUpload: VPS untouched)" -ForegroundColor Yellow
    }
}

# ---- looking after the VPS -------------------------------------------------------------------------------------------------------------------
if ($Status) {
    Step "Server status ($remote)"
    Invoke-RemoteScript @'
echo '--- services (active = running)'
for s in alloy-account alloy-game postgresql redis-server nginx ww-bridge; do
  printf '%-16s %s\n' "$s" "$(systemctl is-active $s 2>&1)"
done
echo
echo '--- machine'
uptime -p
uptime | sed 's/.*load average/load average/'
free -h | awk 'NR==1 || NR==2'
df -h / | awk 'NR==1 || NR==2'
echo
echo '--- connections'
echo "game port 2050 : $(ss -Htn state established '( sport = :2050 )' | wc -l) connected (browser players show up here too)"
echo "account 8080   : $(ss -Htn state established '( sport = :8080 )' | wc -l) open"
echo
echo '--- last game server log lines'
journalctl -u alloy-game -n 6 --no-pager
true
'@
}

if ($Logs) {
    if ($Follow) {
        Step 'Live server log (press Ctrl+C to stop)'
        Invoke-Remote 'journalctl -u alloy-game -u alloy-account -n 20 -f'
    } else {
        Step "Last $LogLines log lines (game + account server)"
        Invoke-Remote "journalctl -u alloy-game -u alloy-account -n $LogLines --no-pager"
    }
}

if ($Restart) {
    Step 'Restarting the account + game servers (players are dropped for about 15 seconds)'
    Invoke-Remote 'systemctl restart alloy-account alloy-game && sleep 12 && systemctl is-active alloy-account alloy-game'
    if (-not $DryRun) {
        Check 'Restart'
        Write-Host "`nServers restarted." -ForegroundColor Green
    }
}

if ($Backup) {
    Step 'Backing up the database'
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $local = Join-Path $BackupDir "alloy-$stamp.sql.gz"
    Invoke-RemoteScript @'
set -e -o pipefail
runuser -u postgres -- pg_dump alloy | gzip > /tmp/ww-backup.sql.gz
ls -l /tmp/ww-backup.sql.gz
'@
    if ($DryRun) {
        Write-Host "[dry run] scp ${remote}:/tmp/ww-backup.sql.gz $local" -ForegroundColor DarkYellow
        Write-Host "[dry run] ssh $remote rm -f /tmp/ww-backup.sql.gz" -ForegroundColor DarkYellow
    } else {
        Check 'Database dump'
        New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
        scp "${remote}:/tmp/ww-backup.sql.gz" $local
        Check 'Download'
        ssh $remote 'rm -f /tmp/ww-backup.sql.gz'
        Assert-GoodBackup $local
        Write-Host ("`nBackup saved: {0}  ({1:N1} MB)" -f $local, ((Get-Item $local).Length / 1MB)) -ForegroundColor Green
        Write-Host 'It contains every account (password hashes included) - keep it private.' -ForegroundColor Yellow
    }
}

# ---- one place on the VPS to see everything -------------------------------------------------------------------------------------------------
if ($Shortcuts) {
    Step 'Making the WaW folder on the VPS (shortcuts to the live files; nothing is moved or copied)'
    Invoke-RemoteScript @'
set -e
WAW=/opt/WaW
mkdir -p $WAW /var/backups/waw
chmod 700 /var/backups/waw

link() { ln -sfn "$1" "$WAW/$2"; }
link /opt/alloy-server server
link /opt/alloy-server/Resources/Config/Data server-settings
link /opt/alloy-server/Resources/World/Data maps
link /var/www/warriors website
link /etc/nginx/conf.d/warriors.conf website-config.conf
link /opt/ww-bridge browser-bridge
link /var/backups/waw backups
mkdir -p $WAW/services
for u in alloy-account alloy-game ww-bridge; do ln -sfn /etc/systemd/system/$u.service $WAW/services/$u.service; done

cat > $WAW/README.txt <<'TXT'
WaW - Warriors and Wizards on this server

Everything in here is a SHORTCUT to where the live files really are. Editing through a shortcut changes the running game.

server/             the running game: AccountServer.dll, GameServer.dll and everything they load
server-settings/    their config files (addresses, version, realm count, database)
maps/               the world maps and the world configs
website/            the browser version of the game (nginx serves it)
website-config.conf the nginx settings for the website
browser-bridge/     the small program that lets browser players reach the game server
services/           the systemd files that start everything on boot
backups/            database backups made on this machine (private)

The game is built on the PC and published here with "deploy -Server" / "deploy -Web": those replace what is in server/ and website/.
The source code lives on the PC, not here.
TXT

for h in /root /home/*; do
  [ -d "$h/Desktop" ] || continue
  ln -sfn $WAW "$h/Desktop/WaW"
  chown -h "$(stat -c %U "$h")" "$h/Desktop/WaW" 2>/dev/null || true
  echo "desktop shortcut: $h/Desktop/WaW"
done
echo
echo "/opt/WaW holds:"
ls -1 $WAW
'@
    if (-not $DryRun) { Write-Host "`nDone. On the VPS open the WaW folder on the desktop (or press Ctrl+L in the file manager and type /opt/WaW)." -ForegroundColor Green }
}
