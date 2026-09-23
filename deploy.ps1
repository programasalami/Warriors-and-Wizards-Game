<#
  This folder -> VPS in one command. Run it from PowerShell on your PC:

      .\deploy.ps1                 # server + client
      .\deploy.ps1 -Server         # only rebuild + upload the servers, then restart them on the VPS
      .\deploy.ps1 -Client         # rebuild + zip the public client AND upload it to https://play.<domain>/download/WarriorsAndWizards-Client.zip
      .\deploy.ps1 -Linux          # same for Linux (x64): builds, packs WarriorsAndWizards-Client-linux.tar.gz and uploads it next to the Windows zip
      .\deploy.ps1 -Launcher       # the launcher players download (Windows + Linux): builds, packs, uploads, and records it in download/manifest.json
      .\deploy.ps1 -Manifest       # repair download/manifest.json from the archives already on the VPS (uploads nothing)
      .\deploy.ps1 -Server -NoUpload   # build + package but don't touch the VPS (a dry run)
      .\deploy.ps1 -Web           # build the BROWSER client (WebAssembly) and upload it to the VPS's nginx (play.warriorsandwizards.com)
      .\deploy.ps1 -Web -NoUpload             # only BUILD the browser client (the slow part, ~30 min); players notice nothing
      .\deploy.ps1 -Web -SkipWebBuild         # only UPLOAD the browser client built last time (seconds) - for a version bump, see below
      .\deploy.ps1 -SetupWeb      # one time: install nginx + HTTPS certificate + websocket bridge on the VPS (needs the DNS record first)
      .\deploy.ps1 -SetupForums -ForumAdminEmail you@example.com   # one time (re-run to upgrade): NodeBB forums at https://forums.<domain>
                                    #   (needs the DNS record first; asks for the forum admin password)
      .\deploy.ps1 -SetupPortal    # one time: nginx + HTTPS for The Portal at https://portal.<domain> (needs the DNS record first)
      .\deploy.ps1 -Portal         # build The Portal's data + icons from the game XML and upload the site (run -Server first when the API changed)
      .\deploy.ps1 -SetVersion 0.3.4   # release a new game version: sets it in the client AND the server config (then deploy -Server -Client -Web)
          A version bump locks out every client that does not match the server, so keep the gap short: SetVersion, then
          -Web -NoUpload (the 30-minute build, nothing changes yet), then back to back: -Server, -Web -SkipWebBuild, -Client, -Linux.

  Looking after the VPS (none of these upload or change any files):
      .\deploy.ps1 -Status             # are the servers running? players connected, memory, disk, last log lines
      .\deploy.ps1 -Logs              # the last 80 lines of the game + account server logs   (-LogLines 200 for more)
      .\deploy.ps1 -Logs -Follow      # the same, live, until you press Ctrl+C
      .\deploy.ps1 -Logs -Since '1 hour ago'                    # everything from a time window instead of the last N lines ('30 min ago', '2026-09-22 13:00')
      .\deploy.ps1 -Logs -Since '1 hour ago' -Load              # only the lines that measure load: [STATS] (every 10 s: tps, tick work, interval, late ticks,
                                        # users, sockets open / accepted / refused), [PLAUSIBILITY] (packet floods, speed), [FLOOD] (raw connections
                                        # refused on the game port), LAGGED, Error. -Bridge adds the ww-bridge service + nginx's error log.
      .\deploy.ps1 -Logs -Since '1 hour ago' -Filter STATS,late=   # your own pattern: commas mean "or" (write no '|' - deploy.cmd goes through cmd.exe, which
                                        # would read a '|' as a pipe)
      .\deploy.ps1 -Restart           # restart the game + account servers (players are dropped for ~15 seconds)
      .\deploy.ps1 -Backup            # download a copy of the database to Documents\WW_Backups
      .\deploy.ps1 -Shortcuts         # VPS desktop: a WaW folder with shortcuts to the live servers, settings, maps, website and backups, plus
                                    #   "Game Servers - Live Console / Status / Restart" launchers (the servers are services and have no window of their own)
      add -DryRun to any of them to only PRINT what would run

  Versions: the client (Core\Settings.cs BuildVersion) and the server (gameServerConfig.xml Version) must match - the game server turns away any client
  whose version differs, and clients ask the account server for the current version at start-up to show an "update required" prompt instead. Bump it
  with -SetVersion whenever the client and server change together, then redeploy all three (-Server -Client -Web): a web page or exe still on the old
  version is then stopped at the title screen with a prompt to download the new desktop client.

  Server: builds WarriorsAndWizards.Server.sln, packs bin\debug\net10.0, uploads it, stops both services on the VPS, unpacks over
  /opt/alloy-server, starts them again and shows the result. The VPS's database password (postgresConfig.xml), Redis settings
  and RPC certificate files are left out of the package on purpose, so the VPS keeps its own.
  Client: builds a self-contained Windows client (testers do NOT need .NET installed) and zips it into dist\.

  Tip: to stop typing the VPS password 2-3 times per run, set up an SSH key once:
      ssh-keygen -t ed25519
      type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh root@104.152.50.196 "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"
#>
param(
    [switch]$Server,
    [switch]$Client,
    [switch]$Linux,
    [switch]$Launcher,
    [switch]$Manifest,
    [switch]$NoUpload,
    [switch]$SkipWebBuild,
    [switch]$Web,
    [switch]$SetupWeb,
    [switch]$SetupForums,
    [switch]$SetupPortal,
    [switch]$Portal,
    [string]$PortalDomain = 'portal.warriorsandwizards.com',
    [string]$ForumDomain = 'forums.warriorsandwizards.com',
    [string]$ForumAdminUser = 'admin',
    [string]$ForumAdminEmail,
    [string]$SetVersion,
    [switch]$Status,
    [switch]$Logs,
    [switch]$Follow,
    [int]$LogLines = 80,
    [string]$Since = '',                 # -Logs: a journalctl time window instead of the last N lines, e.g. '1 hour ago'
    [string]$Filter = '',                # -Logs: keep only lines matching this pattern; commas separate alternatives (a '|' would be eaten by cmd.exe)
    [switch]$Load,                       # -Logs: the standard load filter (STATS, PLAUSIBILITY, LAGGED, late=, Error)
    [switch]$Bridge,                     # -Logs: also the websocket bridge service and nginx's error log (the browser players' path)
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
$deploying = $Server -or $Client -or $Linux -or $Launcher -or $Manifest -or $Web -or $SetupWeb -or $SetupForums -or $SetupPortal -or $Portal -or $SetVersion
$ops = $Status -or $Logs -or $Restart -or $Backup -or $Shortcuts
if (-not ($deploying -or $ops)) { $Server = $true; $Client = $true }
$opsOnly = $ops -and -not $deploying     # VPS care commands do not need the game versions to line up

$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$remote = "$VpsUser@$VpsHost"

# ---- game version: one number, kept in two files (client + server) --------------------------------------------------------------------------
$settingsCs = Join-Path $root 'WaW-Client\WaWClient\Core\Settings.cs'
$serverCfg = Join-Path $root 'WaW-Server\Common\Resources\Config\Data\gameServerConfig.xml'
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
    if (-not ($Server -or $Client -or $Linux -or $Launcher -or $Manifest -or $Web -or $SetupWeb -or $SetupForums -or $SetupPortal -or $Portal)) {
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

# ---- addresses: the everyday source points at THIS PC (127.0.0.1); what gets published points at the VPS ---------------------------------------
# The two server config files carry the address the servers listen on / tell clients about. The source keeps 127.0.0.1, and the copy that is
# packed for the VPS gets $VpsHost written into it (the source files are never touched). The client is the same idea at compile time:
# -p:DeployTarget=vps defines TARGET_VPS, which switches the addresses in Core\Settings.cs. Each step is checked, so a build that still
# points at 127.0.0.1 can never be published by accident.
function Set-ConfigAddress([string]$path, [string]$address) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = [IO.File]::ReadAllText($path)
    if ($text -notmatch '<Address>[^<]*</Address>') { throw "No <Address> found in $path" }
    $new = [regex]::Replace($text, '(<Address>)[^<]*(</Address>)', ('${1}' + $address + '${2}'))
    [IO.File]::WriteAllText($path, $new, (New-Object Text.UTF8Encoding($hasBom)))
}
function Find-Bytes([byte[]]$hay, [byte[]]$needle) {
    $first = $needle[0]
    $last = $hay.Length - $needle.Length
    $i = [Array]::IndexOf($hay, $first, 0)
    while ($i -ge 0 -and $i -le $last) {
        $ok = $true
        for ($j = 1; $j -lt $needle.Length; $j++) { if ($hay[$i + $j] -ne $needle[$j]) { $ok = $false; break } }
        if ($ok) { return $i }
        $i = if ($i + 1 -lt $hay.Length) { [Array]::IndexOf($hay, $first, $i + 1) } else { -1 }
    }
    return -1
}
function Assert-BinaryHasAddress([string]$path, [string]$address) {
    # C# string constants are stored as UTF-16 in the compiled dll
    $bytes = [IO.File]::ReadAllBytes($path)
    if ((Find-Bytes $bytes ([Text.Encoding]::Unicode.GetBytes($address))) -lt 0) { throw "$path does not contain the VPS address $address - the build did not switch to the VPS (is -VpsHost different from the address in Core\Settings.cs?)" }
    if ((Find-Bytes $bytes ([Text.Encoding]::Unicode.GetBytes('127.0.0.1'))) -ge 0) { throw "$path still contains 127.0.0.1 - it would point players at their own PC" }
}

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
    Push-Location (Join-Path $root 'WaW-Server')
    try {
        dotnet build WarriorsAndWizards.Server.sln --nologo -v:q
        Check 'Server build'
    } finally { Pop-Location }

    Step 'Packing the servers'
    $bin = Join-Path $root 'WaW-Server\bin\debug\net10.0'
    $stage = Join-Path $dist 'server-stage'
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    robocopy $bin $stage /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying the server build failed (robocopy exit code $LASTEXITCODE)" }
    $global:LASTEXITCODE = 0
    $cfgDir = Join-Path $stage 'Resources\Config\Data'
    foreach ($name in 'appEngineConfig.xml', 'gameServerConfig.xml') {
        $f = Join-Path $cfgDir $name
        Set-ConfigAddress $f $VpsHost
        if ([IO.File]::ReadAllText($f) -notmatch [regex]::Escape("<Address>$VpsHost</Address>")) { throw "$name did not get the VPS address" }
    }
    Write-Host "Server configs in the package now say $VpsHost (your source files still say 127.0.0.1)." -ForegroundColor DarkGray
    # The VPS keeps its OWN database password, Redis settings and RPC certificate: these files are this PC's copies (local passwords, a local
    # certificate) and must never land on the VPS. Until 2026-09-21 they were shipped and silently overwrote the VPS's after every -Server -
    # unnoticed while both machines used the same database password, fatal the day the VPS got its own.
    $machineOwned = 'postgresConfig.xml', 'redisConfig.xml', 'rpcClientConfig.xml', 'rpcServerConfig.xml', 'rpc-server.cer', 'rpc-server.pfx'
    foreach ($name in $machineOwned) { Remove-Item (Join-Path $cfgDir $name) -Force -ErrorAction SilentlyContinue }
    Write-Host "Left out of the package (the VPS keeps its own): $($machineOwned -join ', ')" -ForegroundColor DarkGray
    $pack = Join-Path $dist 'WaW-Server.tgz'
    if (Test-Path $pack) { Remove-Item $pack -Force }
    tar -czf $pack -C $stage .
    Check 'Packing'
    Remove-Item $stage -Recurse -Force
    '{0:N1} MB -> {1}' -f ((Get-Item $pack).Length / 1MB), $pack

    if (-not $NoUpload) {
        Step "Uploading to $remote"
        scp $pack "${remote}:/tmp/WaW-Server.tgz"
        Check 'Upload'

        Step 'Restarting the servers on the VPS'
        $cmd = 'systemctl stop alloy-game alloy-account && mkdir -p /opt/alloy-server && tar -xzf /tmp/WaW-Server.tgz -C /opt/alloy-server && rm -f /tmp/WaW-Server.tgz && systemctl start alloy-account alloy-game && sleep 12 && systemctl is-active alloy-account alloy-game && journalctl -u alloy-account -n 3 --no-pager && journalctl -u alloy-game -n 3 --no-pager'
        ssh $remote $cmd
        Check 'Remote restart'
        Write-Host "`nServers updated." -ForegroundColor Green
    } else {
        Write-Host "`n(-NoUpload: VPS untouched)" -ForegroundColor Yellow
    }
}


# ---- download/manifest.json: what the launcher reads (game version + archive per platform + hashes + launcher version) --------------------------
# Each of -Client / -Linux / -Launcher updates only its own entries: the current manifest is fetched from the VPS, changed, and put back.
function Update-Manifest([scriptblock]$change) {
    $local = Join-Path $dist 'manifest.json'
    $manifest = $null
    # A failed ssh (mistyped password, no connection) must NOT look like "no manifest yet": on 2026-09-21 it did, and the fresh manifest it wrote
    # wiped the Windows and launcher entries. The VPS answers __NONE__ when the file really is missing; anything else that is not JSON is an error.
    $existing = ssh $remote 'if [ -f /var/www/warriors/download/manifest.json ]; then cat /var/www/warriors/download/manifest.json; else echo __NONE__; fi'
    Check 'Reading the manifest on the VPS'
    $text = (($existing -join "`n").TrimStart([char]0xFEFF)).Trim()
    if (-not $text) { throw "The VPS sent nothing back for manifest.json - not writing one blind. Run the command again." }
    if ($text -ne '__NONE__') {
        try { $manifest = $text | ConvertFrom-Json } catch { $manifest = $null }
        if (-not $manifest) { throw "The manifest on the VPS could not be parsed - not overwriting it. Look at /var/www/warriors/download/manifest.json" }
    }
    if (-not $manifest) { $manifest = [pscustomobject]@{ version = ''; windows = $null; linux = $null; launcher = $null } }
    foreach ($name in 'version','windows','linux','launcher') { if (-not ($manifest.PSObject.Properties.Name -contains $name)) { $manifest | Add-Member -NotePropertyName $name -NotePropertyValue $null } }
    & $change $manifest
    # UTF-8 WITHOUT a byte-order mark: Set-Content -Encoding utf8 writes one in Windows PowerShell, and JSON parsers refuse it
    [IO.File]::WriteAllText($local, ($manifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    scp $local "${remote}:/var/www/warriors/download/manifest.json"
    Check 'Manifest upload'
    Write-Host "manifest.json updated (game $($manifest.version), launcher $($manifest.launcher.version))" -ForegroundColor DarkGray
}
# The archive as it sits on the VPS (size + sha256), computed there so a server-side fix-up (Linux exec bits) is included.
function Get-RemoteArchiveInfo([string]$file) {
    $out = ssh $remote "cd /var/www/warriors/download && stat -c %s '$file' && sha256sum '$file' | cut -d' ' -f1"
    Check "Remote archive info for $file"
    return [pscustomobject]@{ file = $file; size = [long]$out[0]; sha256 = $out[1] }
}

if ($Client) {
    Step 'Building the public client (self-contained, Windows x64)'
    $clientDir = Join-Path $root 'WaW-Client'
    $out = Join-Path $dist 'client'
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    Push-Location $clientDir
    try {
        # ONE file since 2026-09-22: WarriorsAndWizards.exe carries the runtime and the native libraries (the csproj names it for DeployTarget=vps)
        dotnet publish 'WaWClient\WaWClient.csproj' -c Release -r win-x64 --self-contained true "-p:SolutionDir=$($clientDir -replace '\\','/')/" "-p:DeployTarget=vps" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out --nologo -v:q
        Check 'Client publish'
    } finally { Pop-Location }

    # checked on the game's own assembly as it went into the single file (the bundle also holds the framework, with its own 127.0.0.1 strings)
    Assert-BinaryHasAddress (Join-Path $clientDir 'WaWClient\bin\Release\net10.0\win-x64\WarriorsAndWizards.dll') $VpsHost
    if (-not (Test-Path (Join-Path $out 'WarriorsAndWizards.exe'))) { throw 'The publish did not produce WarriorsAndWizards.exe' }
    Write-Host "Client build points at $VpsHost." -ForegroundColor DarkGray

    # The game's art / sound / fonts are built by the project's post-build step next to the built exe, and 'publish' does not
    # carry that folder along - copy it in or the client starts with no content.
    $built = Join-Path $clientDir 'WaWClient\bin\Release\net10.0\win-x64\Content'
    if (-not (Test-Path $built)) { throw "Built content folder not found: $built" }
    Copy-Item $built (Join-Path $out 'Content') -Recurse -Force

    # The audio engine loads OpenAL BY PATH from runtimes\win-x64\native; the single file bundles it where that path cannot see it,
    # so the copy from the build output goes where the client looks.
    $native = Join-Path $out 'runtimes\win-x64\native'
    New-Item -ItemType Directory -Force -Path $native | Out-Null
    Copy-Item (Join-Path $clientDir 'WaWClient\bin\Release\net10.0\win-x64\soft_oal.dll') $native -Force

    Step 'Zipping the client'
    $zip = Join-Path $dist 'WarriorsAndWizards-Client.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
    '{0:N1} MB -> {1}' -f ((Get-Item $zip).Length / 1MB), $zip

    # The zip is served by the web client's nginx at https://<WebDomain>/download/<zip> - the address the account server hands out
    # (appEngineConfig.xml <DownloadUrl>) and the website's Download page link to. -Web leaves that folder alone.
    if (-not $NoUpload) {
        Step "Uploading the client zip to $remote"
        ssh $remote 'mkdir -p /var/www/warriors/download'
        scp $zip "${remote}:/var/www/warriors/download/WarriorsAndWizards-Client.zip"
        Check 'Zip upload'
        Update-Manifest { param($m) $m.version = Get-ClientVersion; $m.windows = Get-RemoteArchiveInfo 'WarriorsAndWizards-Client.zip' }
        Write-Host "`nClient zip online: https://$WebDomain/download/WarriorsAndWizards-Client.zip" -ForegroundColor Green
    } else {
        Write-Host "`nClient zip ready (-NoUpload: not uploaded)." -ForegroundColor Yellow
    }
}

if ($Linux) {
    Step 'Building the Linux client (x64, self-contained)'
    $clientDir = Join-Path $root 'WaW-Client'
    $out = Join-Path $dist 'client-linux'
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    Push-Location $clientDir
    try {
        dotnet publish 'WaWClient\WaWClient.csproj' -c Release -r linux-x64 --self-contained true "-p:SolutionDir=$($clientDir -replace '\\','/')/" "-p:DeployTarget=vps" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out --nologo -v:q
        Check 'Linux client publish'
    } finally { Pop-Location }

    Assert-BinaryHasAddress (Join-Path $clientDir 'WaWClient\bin\Release\net10.0\linux-x64\WarriorsAndWizards.dll') $VpsHost
    if (-not (Test-Path (Join-Path $out 'WarriorsAndWizards'))) { throw 'The publish did not produce WarriorsAndWizards' }
    Write-Host "Linux client build points at $VpsHost." -ForegroundColor DarkGray

    # Content is built next to the RID-specific build output by the post-build step; publish does not carry it along.
    $built = Join-Path $clientDir 'WaWClient\bin\Release\net10.0\linux-x64\Content'
    if (-not (Test-Path $built)) { throw "Built content folder not found: $built" }
    Copy-Item $built (Join-Path $out 'Content') -Recurse -Force

    # The audio engine looks for runtimes/linux-x64/native/libopenal.so (see WaW.Audio InternalUtils.GetAudioBinaryPath).
    $native = Join-Path $out 'runtimes\linux-x64\native'
    New-Item -ItemType Directory -Force -Path $native | Out-Null
    $so = Join-Path $clientDir 'WaWClient\bin\Release\net10.0\linux-x64\libopenal.so'
    if (-not (Test-Path $so)) { throw "libopenal.so was not produced by the build - check the Silk.NET.OpenAL.Soft.Native package" }
    Copy-Item $so $native -Force

    # A tar made on Windows carries no executable bits, so a tiny start script sets them on first run. Players: bash run.sh
    $run = @"
#!/bin/bash
# Warriors & Wizards - Linux client. First run:  bash run.sh   (after that ./run.sh or ./WarriorsAndWizards work too)
cd "`$(dirname "`$0")"
chmod +x ./WarriorsAndWizards ./run.sh 2>/dev/null
exec ./WarriorsAndWizards "`$@"
"@
    [IO.File]::WriteAllText((Join-Path $out 'run.sh'), ($run -replace "`r`n", "`n"))

    Step 'Packing the Linux client'
    $tgz = Join-Path $dist 'WarriorsAndWizards-Client-linux.tar.gz'
    if (Test-Path $tgz) { Remove-Item $tgz -Force }
    tar -czf $tgz -C $out .
    Check 'Packing the Linux client'
    '{0:N1} MB -> {1}' -f ((Get-Item $tgz).Length / 1MB), $tgz

    if (-not $NoUpload) {
        Step "Uploading the Linux client to $remote"
        ssh $remote 'mkdir -p /var/www/warriors/download'
        scp $tgz "${remote}:/var/www/warriors/download/WarriorsAndWizards-Client-linux.tar.gz"
        Check 'Linux upload'
        # the executable bit is set inside the archive on the VPS, so players who extract with a GUI tool can double-click too
        ssh $remote 'cd /tmp && rm -rf ww-lx && mkdir ww-lx && tar -xzf /var/www/warriors/download/WarriorsAndWizards-Client-linux.tar.gz -C ww-lx && chmod +x ww-lx/WarriorsAndWizards ww-lx/run.sh && tar -czf /var/www/warriors/download/WarriorsAndWizards-Client-linux.tar.gz -C ww-lx . && rm -rf ww-lx'
        Check 'Linux archive fix-up'
        Update-Manifest { param($m) $m.version = Get-ClientVersion; $m.linux = Get-RemoteArchiveInfo 'WarriorsAndWizards-Client-linux.tar.gz' }
        Write-Host "`nLinux client online: https://$WebDomain/download/WarriorsAndWizards-Client-linux.tar.gz" -ForegroundColor Green
    } else {
        Write-Host "`nLinux client ready (-NoUpload: not uploaded)." -ForegroundColor Yellow
    }
}


if ($Launcher) {
    # Launcher 2.0.0+ (2026-09-22) is WaWLauncher, a window (Avalonia). Four files go online:
    #   WarriorsAndWizards-Launcher-windows.zip / -linux.tar.gz  the SELF-UPDATE archives the manifest names. They hold the launcher under
    #       the old name WarriorsAndWizards(.exe) on purpose: launcher 1.0.0 only accepts a file with that name, and the new launcher,
    #       started under it, moves the folder over to the new layout by itself (Launcher/Install.cs Legacy).
    #   WaWLauncher.exe / WaWLauncher-linux.tar.gz              what the website's download buttons hand out.
    $launcherDir = Join-Path $root 'Launcher'
    $launcherVersion = [regex]::Match([IO.File]::ReadAllText((Join-Path $launcherDir 'Launcher.csproj')), '<Version>([^<]+)</Version>').Groups[1].Value
    Step "Building the launcher $launcherVersion (Windows + Linux, self-contained single file)"
    $packs = @{}
    foreach ($rid in 'win-x64', 'linux-x64') {
        $out = Join-Path $dist "launcher-$rid"
        if (Test-Path $out) { Remove-Item $out -Recurse -Force }
        dotnet publish (Join-Path $launcherDir 'Launcher.csproj') -c Release -r $rid -o $out --nologo -v:q
        Check "Launcher publish ($rid)"
        Get-ChildItem $out -Filter '*.pdb' | Remove-Item -Force
        $updateDir = Join-Path $out 'bridge'
        New-Item -ItemType Directory -Force -Path $updateDir | Out-Null
        if ($rid -eq 'win-x64') {
            $exe = Join-Path $out 'WaWLauncher.exe'
            if (-not (Test-Path $exe)) { throw "The launcher publish did not produce WaWLauncher.exe" }
            Copy-Item $exe (Join-Path $updateDir 'WarriorsAndWizards.exe')
            $pack = Join-Path $dist 'WarriorsAndWizards-Launcher-windows.zip'
            if (Test-Path $pack) { Remove-Item $pack -Force }
            Compress-Archive -Path (Join-Path $updateDir '*') -DestinationPath $pack -CompressionLevel Optimal
            $site = Join-Path $dist 'WaWLauncher.exe'
            Copy-Item $exe $site -Force
        } else {
            $exe = Join-Path $out 'WaWLauncher'
            if (-not (Test-Path $exe)) { throw "The launcher publish did not produce WaWLauncher" }
            Copy-Item $exe (Join-Path $updateDir 'WarriorsAndWizards')
            $pack = Join-Path $dist 'WarriorsAndWizards-Launcher-linux.tar.gz'
            if (Test-Path $pack) { Remove-Item $pack -Force }
            tar -czf $pack -C $updateDir .
            $siteDir = Join-Path $out 'site'
            New-Item -ItemType Directory -Force -Path $siteDir | Out-Null
            Copy-Item $exe (Join-Path $siteDir 'WaWLauncher')
            $site = Join-Path $dist 'WaWLauncher-linux.tar.gz'
            if (Test-Path $site) { Remove-Item $site -Force }
            tar -czf $site -C $siteDir .
        }
        Check "Packing the launcher ($rid)"
        '{0:N1} MB -> {1}' -f ((Get-Item $pack).Length / 1MB), $pack
        '{0:N1} MB -> {1}' -f ((Get-Item $site).Length / 1MB), $site
        $packs[$rid] = $pack
    }

    if (-not $NoUpload) {
        Step "Uploading the launcher to $remote"
        ssh $remote 'mkdir -p /var/www/warriors/download'
        scp $packs['win-x64'] "${remote}:/var/www/warriors/download/WarriorsAndWizards-Launcher-windows.zip"
        Check 'Launcher upload (windows, self-update)'
        scp $packs['linux-x64'] "${remote}:/var/www/warriors/download/WarriorsAndWizards-Launcher-linux.tar.gz"
        Check 'Launcher upload (linux, self-update)'
        scp (Join-Path $dist 'WaWLauncher.exe') "${remote}:/var/www/warriors/download/WaWLauncher.exe"
        Check 'Launcher upload (windows, website)'
        scp (Join-Path $dist 'WaWLauncher-linux.tar.gz') "${remote}:/var/www/warriors/download/WaWLauncher-linux.tar.gz"
        Check 'Launcher upload (linux, website)'
        # executable bits inside both Linux archives, set on the VPS (a tar made on Windows has none)
        ssh $remote 'cd /tmp && rm -rf ww-ll && mkdir ww-ll && tar -xzf /var/www/warriors/download/WarriorsAndWizards-Launcher-linux.tar.gz -C ww-ll && chmod +x ww-ll/WarriorsAndWizards && tar -czf /var/www/warriors/download/WarriorsAndWizards-Launcher-linux.tar.gz -C ww-ll . && rm -rf ww-ll && mkdir ww-ll && tar -xzf /var/www/warriors/download/WaWLauncher-linux.tar.gz -C ww-ll && chmod +x ww-ll/WaWLauncher && tar -czf /var/www/warriors/download/WaWLauncher-linux.tar.gz -C ww-ll . && rm -rf ww-ll'
        Check 'Launcher archive fix-up'
        Update-Manifest {
            param($m)
            $m.launcher = [pscustomobject]@{
                version = $launcherVersion
                windows = Get-RemoteArchiveInfo 'WarriorsAndWizards-Launcher-windows.zip'
                linux   = Get-RemoteArchiveInfo 'WarriorsAndWizards-Launcher-linux.tar.gz'
            }
        }
        Write-Host "`nLauncher online: https://$WebDomain/download/WaWLauncher.exe (and WaWLauncher-linux.tar.gz; self-update archives refreshed)" -ForegroundColor Green
    } else {
        Write-Host "`nLauncher packs ready (-NoUpload: not uploaded)." -ForegroundColor Yellow
    }
}

if ($SetupForums) {
    if (-not $ForumAdminEmail) { throw "-SetupForums needs -ForumAdminEmail (the forum admin account's e-mail)." }
    $secure = Read-Host "Password for the forum admin user '$ForumAdminUser' (you can change it later in the forum)" -AsSecureString
    $forumPass = [Runtime.InteropServices.Marshal]::PtrToStringUni([Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($secure))
    if ($forumPass.Length -lt 8) { throw "The forum admin password must be at least 8 characters." }
    if ($forumPass -match '[\s''"]') { throw "Please use a password without spaces or quotes for the setup (change it in the forum afterwards)." }
    Step "Setting up the forums on the VPS ($ForumDomain)"
    # scp -r into an EXISTING folder nests the upload inside it and the stale first copy would run: clear it first.
    ssh $remote 'rm -rf /root/ww-forums'
    scp -r (Join-Path $root 'Forums\vps') "${remote}:/root/ww-forums"
    Check 'Upload of the setup files'
    ssh $remote "bash /root/ww-forums/setup_forums.sh $ForumDomain $ForumAdminUser $ForumAdminEmail '$forumPass'"
    Check 'Forum setup'
    Write-Host "`nForums are up: https://$ForumDomain" -ForegroundColor Green
}

if ($Manifest) {
    # Repair: rebuild download/manifest.json from whatever archives are on the VPS right now (nothing is uploaded). Each entry is filled only
    # when its file is there; the game version comes from the source, the launcher version from Launcher.csproj.
    Step 'Rebuilding download/manifest.json from the archives on the VPS'
    $present = ssh $remote 'cd /var/www/warriors/download && ls -1'
    Check 'Listing the download folder'
    $present = @($present | ForEach-Object { $_.Trim() })
    $launcherVersion = [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'Launcher\Launcher.csproj')), '<Version>([^<]+)</Version>').Groups[1].Value
    Update-Manifest {
        param($m)
        $m.version = Get-ClientVersion
        if ($present -contains 'WarriorsAndWizards-Client.zip') { $m.windows = Get-RemoteArchiveInfo 'WarriorsAndWizards-Client.zip' }
        if ($present -contains 'WarriorsAndWizards-Client-linux.tar.gz') { $m.linux = Get-RemoteArchiveInfo 'WarriorsAndWizards-Client-linux.tar.gz' }
        if ($present -contains 'WarriorsAndWizards-Launcher-windows.zip' -and $present -contains 'WarriorsAndWizards-Launcher-linux.tar.gz') {
            $m.launcher = [pscustomobject]@{
                version = $launcherVersion
                windows = Get-RemoteArchiveInfo 'WarriorsAndWizards-Launcher-windows.zip'
                linux   = Get-RemoteArchiveInfo 'WarriorsAndWizards-Launcher-linux.tar.gz'
            }
        }
        foreach ($name in 'windows', 'linux', 'launcher') { if (-not $m.$name) { Write-Host "  no $name archive on the VPS: that entry stays empty" -ForegroundColor Yellow } }
    }
    Write-Host "`nManifest rebuilt: https://$WebDomain/download/manifest.json" -ForegroundColor Green
}

if ($SetupPortal) {
    Step "Setting up The Portal on the VPS ($PortalDomain)"
    ssh $remote 'rm -rf /root/ww-portal'     # scp -r into an existing folder would nest the upload and run the old copy
    scp -r (Join-Path $root 'Portal\vps') "${remote}:/root/ww-portal"
    Check 'Upload of the setup files'
    ssh $remote "bash /root/ww-portal/setup_portal.sh $PortalDomain $VpsHost"
    Check 'Portal setup'
    Write-Host "`nPortal setup finished. Now run: .\deploy.ps1 -Portal   (and -Server if the servers on the VPS predate the public API)" -ForegroundColor Green
}

if ($Portal) {
    Step 'Building The Portal (items, classes, icons from the game XML)'
    python (Join-Path $root 'Tools\Portal\build_portal_data.py')
    Check 'Portal data'
    $site = Join-Path $root 'Portal\site'
    $pack = Join-Path $dist 'ww-portal.tgz'
    if (Test-Path $pack) { Remove-Item $pack -Force }
    tar -czf $pack -C $site .
    Check 'Packing the Portal'
    '{0:N1} MB -> {1}' -f ((Get-Item $pack).Length / 1MB), $pack

    if (-not $NoUpload) {
        Step "Uploading to $remote"
        scp $pack "${remote}:/tmp/ww-portal.tgz"
        Check 'Upload'
        ssh $remote 'mkdir -p /var/www/portal && find /var/www/portal -mindepth 1 -maxdepth 1 -exec rm -rf {} + && tar -xzf /tmp/ww-portal.tgz -C /var/www/portal && rm -f /tmp/ww-portal.tgz && ls /var/www/portal | head'
        Check 'Remote unpack'
        Write-Host "`nThe Portal is updated: https://$PortalDomain" -ForegroundColor Green
    } else {
        Write-Host "`n(-NoUpload: VPS untouched)" -ForegroundColor Yellow
    }
}

if ($SetupWeb) {
    Step "Setting up the web client on the VPS ($WebDomain)"
    ssh $remote 'rm -rf /root/ww-vps'     # scp -r into an existing folder would nest the upload and run the old copy
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
    $site = if ($flags.Count) { Join-Path $root 'WebClient\dist\site-aot' } else { Join-Path $root 'WebClient\dist\site' }
    if ($SkipWebBuild) {
        # Upload what the last build left (made with -Web -NoUpload): seconds instead of ~30 minutes, for a version bump's back-to-back uploads.
        if (-not (Test-Path (Join-Path $site 'index.html'))) { throw "No built browser client at $site - run .\deploy.ps1 -Web -NoUpload first." }
        Write-Host ('Using the browser client built {0:yyyy-MM-dd HH:mm} (no rebuild)' -f (Get-Item (Join-Path $site 'index.html')).LastWriteTime) -ForegroundColor Yellow
    } else {
        python (Join-Path $root 'WebClient\tools\build_web.py') @flags
        Check 'Web build'
    }
    $pack = Join-Path $dist 'ww-site.tgz'
    if (Test-Path $pack) { Remove-Item $pack -Force }
    tar -czf $pack -C $site .
    Check 'Packing the site'
    '{0:N1} MB -> {1}' -f ((Get-Item $pack).Length / 1MB), $pack

    if (-not $NoUpload) {
        Step "Uploading to $remote"
        scp $pack "${remote}:/tmp/ww-site.tgz"
        Check 'Upload'
        # everything but the download folder (the client zip lives there, uploaded by -Client)
        ssh $remote 'find /var/www/warriors -mindepth 1 -maxdepth 1 ! -name download -exec rm -rf {} + && tar -xzf /tmp/ww-site.tgz -C /var/www/warriors && rm -f /tmp/ww-site.tgz && ls /var/www/warriors | head'
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
        if ($Load) { $Filter = 'STATS,PLAUSIBILITY,FLOOD,LAGGED,late=,Error' }
        $Filter = ($Filter -replace ',', '|')
        $units = '-u alloy-game -u alloy-account' + $(if ($Bridge) { ' -u ww-bridge' } else { '' })
        $window = if ($Since) { "--since '$($Since -replace "'", '')'" } else { "-n $LogLines" }
        $grep = if ($Filter) { " | grep -E '$($Filter -replace "'", '')'" } else { '' }
        $what = 'game + account server' + $(if ($Bridge) { ' + bridge' } else { '' })
        $header = $(if ($Since) { "Log lines since $Since ($what)" } else { "Last $LogLines log lines ($what)" }) + $(if ($Filter) { ", filtered by /$Filter/" } else { '' })
        Step $header
        Invoke-Remote "journalctl $units $window --no-pager$grep"
        if ($Bridge) {
            # The network side, which the game loop never sees (no quotes inside these: ssh through PowerShell eats them).
            $sinceArg = if ($Since) { "--since '$($Since -replace "'", '')'" } else { '--since -1h' }
            Step 'Kernel: SYN floods / connection-table drops in the window (empty = none)'
            Invoke-Remote "journalctl -k $sinceArg --no-pager | grep -iE 'flood|conntrack|drop|possible syn' || echo none"
            Step 'Connections right now (ss -s) and the game port'
            Invoke-Remote 'ss -s | head -n 6; echo ---; ss -tan state established | grep -c :2050 | sed s/^/game-port-connections:/'
            Step 'nginx: top client addresses in the last 5000 requests, then the last 20 error-log lines'
            Invoke-Remote 'test -f /var/log/nginx/access.log && tail -n 5000 /var/log/nginx/access.log | awk {print\$1} | sort | uniq -c | sort -rn | head -n 10 || echo no-access-log; echo ---; test -f /var/log/nginx/error.log && tail -n 20 /var/log/nginx/error.log || echo no-error-log'
        }
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

The servers run as SERVICES (started by systemd at boot, restarted if they crash), so they have no window of their own and
do not show up as open applications on the desktop. The launchers next to this folder on the desktop are their window:
  Game Servers - Live Console   the game + account server output as it happens (like a console you started by hand)
  Game Servers - Status         which services are running, players connected, memory, disk
  Game Servers - Restart        restart the game + account servers (players are dropped for ~15 seconds)

The game is built on the PC and published here with "deploy -Server" / "deploy -Web": those replace what is in server/ and website/.
The source code lives on the PC, not here.
TXT

# Desktop launchers: the servers are systemd services (no window of their own), so these open a terminal that shows them.
# The logic lives in small scripts (a .desktop Exec line mangles % and quotes).
mkdir -p $WAW/launchers
cat > "$WAW/launchers/ww-console.sh" <<'SH'
#!/bin/bash
echo "Warriors and Wizards servers - live output. Closing this window does NOT stop the servers."
echo
exec journalctl -u alloy-game -u alloy-account -u ww-bridge -n 60 -f
SH
cat > "$WAW/launchers/ww-status.sh" <<'SH'
#!/bin/bash
echo "--- services"
for s in alloy-account alloy-game ww-bridge nginx postgresql redis-server ww-forums; do
  printf '%-16s %s\n' "$s" "$(systemctl is-active $s 2>&1)"
done
echo
echo "game port 2050 : $(ss -Htn state established '( sport = :2050 )' | wc -l) connected"
echo
uptime
free -h | head -2
df -h / | tail -1
echo
systemctl status alloy-account alloy-game --no-pager -n 8 2>&1 | tail -40
echo
read -r -p "Press Enter to close"
SH
cat > "$WAW/launchers/ww-restart.sh" <<'SH'
#!/bin/bash
read -r -p "Restart the game + account servers now? Players are dropped for about 15 seconds. [y/N] " a
if [ "$a" = y ] || [ "$a" = Y ]; then
  systemctl restart alloy-account alloy-game
  sleep 3
  systemctl status alloy-account alloy-game --no-pager -n 5
else
  echo "Nothing done."
fi
echo
read -r -p "Press Enter to close"
SH
mk() {  # name, title, comment, icon
cat > "$WAW/launchers/$1.desktop" <<DSK
[Desktop Entry]
Type=Application
Name=$2
Comment=$3
Exec=$WAW/launchers/$1.sh
Terminal=true
Icon=$4
Categories=Game;
DSK
}
mk ww-console "Game Servers - Live Console" "The game and account server output as it happens (closing the window does not stop them)" utilities-terminal
mk ww-status  "Game Servers - Status"       "Which services are running, players connected, memory and disk" utilities-system-monitor
mk ww-restart "Game Servers - Restart"      "Restart the game and account servers (players are dropped for about 15 seconds)" view-refresh
chmod +x $WAW/launchers/*.sh $WAW/launchers/*.desktop

for h in /root /home/*; do
  [ -d "$h/Desktop" ] || continue
  ln -sfn $WAW "$h/Desktop/WaW"
  owner="$(stat -c %U "$h")"
  chown -h "$owner" "$h/Desktop/WaW" 2>/dev/null || true
  for f in $WAW/launchers/*.desktop; do
    cp "$f" "$h/Desktop/"
    chmod +x "$h/Desktop/$(basename "$f")"
    chown "$owner" "$h/Desktop/$(basename "$f")" 2>/dev/null || true
    # GNOME asks to "trust" a launcher before it runs; this marks it trusted for the desktop's owner where gio is available
    su - "$owner" -s /bin/bash -c "DBUS_SESSION_BUS_ADDRESS= gio set '$h/Desktop/$(basename "$f")' metadata::trusted true" >/dev/null 2>&1 || true
  done
  echo "desktop shortcuts: $h/Desktop/WaW + the three Game Servers launchers"
done
echo
echo "/opt/WaW holds:"
ls -1 $WAW
'@
    if (-not $DryRun) { Write-Host "`nDone. On the VPS desktop: the WaW folder plus three launchers (Game Servers - Live Console / Status / Restart). If a launcher asks whether to trust it, say yes once." -ForegroundColor Green }
}
