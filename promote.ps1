<#
  Testing -> Game: makes the "Warriors-and-Wizards-Game" folder an exact copy of this one (the confirmed-working backup).

      .\promote.ps1                     # DRY RUN (the default): shows what would be added / changed / deleted. Touches nothing.
      .\promote.ps1 -Apply              # does it: checks the build first, zips the old Game folder as a safety net, then copies
      .\promote.ps1 -Apply -SkipChecks  # same, without the build + tests check (only if you already know they pass)

  Only run -Apply once you and Claude have agreed this build is good.

  What it does with -Apply:
    1. refuses unless the target is a git repo folder (it has .git) and is not this folder
    2. builds both solutions and runs every test here; any failure stops it (skip with -SkipChecks)
    3. zips the current Game folder to Documents\WW_Backups\Game-before-promote-DATE.zip (without .git / build output)
    4. mirrors this folder into it: new + changed files are copied and files that no longer exist here are DELETED there
  It NEVER touches .git, and leaves build output (bin, obj, dist), logs and databases alone on both sides.
  It runs no git commands: afterwards open the Game folder, look at `git status`, and commit it yourself.

  Note: the copy is exact, so the Game folder gets this folder's server addresses (the VPS), not 127.0.0.1.
#>
param(
    [switch]$Apply,
    [switch]$SkipChecks,
    [string]$Target = 'C:\Users\cbart\Desktop\Repos\Warriors-and-Wizards-Game',
    [string]$BackupDir = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'WW_Backups')
)

$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot

function Step($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Full($p) { [IO.Path]::GetFullPath($p).TrimEnd('\') }

# ---- safety checks -----------------------------------------------------------------------------------------------------------------------------
if (-not (Test-Path (Join-Path $source 'alloy-server')) -or -not (Test-Path (Join-Path $source 'AlloyClient'))) { throw "This does not look like the game folder: $source" }
if (-not (Test-Path $Target)) { throw "The target folder does not exist: $Target" }
$src = Full $source; $dst = Full $Target
if ($src -ieq $dst) { throw 'Source and target are the same folder.' }
if ($dst.StartsWith($src + '\', 'OrdinalIgnoreCase') -or $src.StartsWith($dst + '\', 'OrdinalIgnoreCase')) { throw 'One folder is inside the other.' }
if (-not (Test-Path (Join-Path $dst '.git'))) { throw "The target has no .git folder, so it is not the git repo: $dst" }

# what is never copied and never deleted (build output, the git data, runtime data)
$excludeDirs = @('.git', '.claude', '.vs', '.idea', 'bin', 'obj', 'dist', 'node_modules', 'logs', 'patched')
$excludeFiles = @('*.db', '*.db-shm', '*.db-wal', '*.log', 'Thumbs.db', 'patched.props')
$common = @($src, $dst, '/MIR', '/XD') + $excludeDirs + @('/XF') + $excludeFiles + @('/R:1', '/W:1', '/NP', '/NJH', '/NJS', '/FP')

function Get-Version {
    $cs = [IO.File]::ReadAllText((Join-Path $src 'AlloyClient\AlloyClient\Core\Settings.cs'))
    $xml = [IO.File]::ReadAllText((Join-Path $src 'alloy-server\Common\Resources\Config\Data\gameServerConfig.xml'))
    $c = [regex]::Match($cs, 'BuildVersion\s*=\s*"([^"]+)"').Groups[1].Value
    $s = [regex]::Match($xml, '<Version>([^<]+)</Version>').Groups[1].Value
    if ($c -ne $s) { throw "Client version '$c' and server version '$s' differ - fix that first (deploy -SetVersion $c)." }
    $c
}

# ---- what would change (always shown) ---------------------------------------------------------------------------------------------------------
Step "Comparing  $src  ->  $dst"
$lines = & robocopy @common /L 2>&1 | ForEach-Object { "$_" }
if ($LASTEXITCODE -ge 8) { throw "robocopy could not compare the folders (exit code $LASTEXITCODE)`n$($lines -join "`n")" }
$new = @($lines | Where-Object { $_ -match '^\s*New File\s' })
$chg = @($lines | Where-Object { $_ -match '^\s*(Newer|Older|Changed|Modified)\s' })
$del = @($lines | Where-Object { $_ -match '^\s*\*EXTRA File\s' })
$delDirs = @($lines | Where-Object { $_ -match '^\s*\*EXTRA Dir\s' })
function Clean($l) {
    $t = ($l -replace '^\s*(New File|Newer|Older|Changed|Modified|\*EXTRA File|\*EXTRA Dir)\s+(-?\d+(\.\d+)?\s*[kmgt]?\s+)?', '').Trim()
    if ($t.StartsWith($dst, 'OrdinalIgnoreCase')) { $t = $t.Substring($dst.Length).TrimStart([char]92) }
    if ($t.StartsWith($src, 'OrdinalIgnoreCase')) { $t = $t.Substring($src.Length).TrimStart([char]92) }
    $t
}
Write-Host ("  new files     : {0}" -f $new.Count)
Write-Host ("  changed files : {0}" -f $chg.Count)
Write-Host ("  DELETED files : {0}   (only in the Game folder, so they will be removed)" -f $del.Count) -ForegroundColor $(if ($del.Count) { 'Yellow' } else { 'Gray' })
if ($del.Count) { $del | Select-Object -First 20 | ForEach-Object { Write-Host ("      - " + (Clean $_)) -ForegroundColor Yellow }; if ($del.Count -gt 20) { Write-Host "      ... and $($del.Count - 20) more" -ForegroundColor Yellow } }
if ($delDirs.Count) { Write-Host ("  DELETED folders: {0}" -f $delDirs.Count) -ForegroundColor Yellow; $delDirs | Select-Object -First 10 | ForEach-Object { Write-Host ("      - " + (Clean $_)) -ForegroundColor Yellow } }

if ($new.Count + $chg.Count + $del.Count + $delDirs.Count -eq 0) { Write-Host "`nThe Game folder already matches. Nothing to do." -ForegroundColor Green; return }

if (-not $Apply) {
    Write-Host "`nDRY RUN: nothing was changed. Run  .\promote.ps1 -Apply  when you and Claude have agreed this build is good." -ForegroundColor Yellow
    return
}

# ---- checks ---------------------------------------------------------------------------------------------------------------------------------------
$version = Get-Version
Write-Host "`nPromoting game version $version" -ForegroundColor DarkGray
if (-not $SkipChecks) {
    Step 'Checking the build: both solutions must build and every test must pass'
    foreach ($job in @(@('AlloyClient', 'AlloyTk.sln'), @('alloy-server', 'RealmServer.sln'))) {
        Push-Location (Join-Path $src $job[0])
        try {
            dotnet build $job[1] --nologo -v:q
            if ($LASTEXITCODE -ne 0) { throw "$($job[1]) does not build - nothing was promoted." }
            dotnet test $job[1] --no-build --nologo -v:q
            if ($LASTEXITCODE -ne 0) { throw "Tests fail in $($job[1]) - nothing was promoted." }
        } finally { Pop-Location }
    }
}

# ---- safety net: a zip of the Game folder as it is right now -------------------------------------------------------------------------------
Step 'Saving the current Game folder as a zip first'
New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
$zip = Join-Path $BackupDir ("Game-before-promote-{0}.zip" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
$tarArgs = @('-a', '-c', '-f', $zip)
foreach ($d in $excludeDirs) { $tarArgs += "--exclude=$d" }
$tarArgs += @('-C', $dst, '.')
& (Join-Path (Join-Path $env:SystemRoot 'System32') 'tar.exe') @tarArgs    # Windows' own tar (a GNU tar earlier on the PATH cannot handle drive letters)
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $zip) -or (Get-Item $zip).Length -lt 1024) { throw 'Could not write the safety zip - nothing was promoted.' }
Write-Host ("  {0}  ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))

# ---- the copy --------------------------------------------------------------------------------------------------------------------------------
Step 'Copying'
& robocopy @common | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed (exit code $LASTEXITCODE). The safety zip is at $zip" }
if (-not (Test-Path (Join-Path $dst '.git'))) { throw '.git is missing after the copy - restore from the safety zip!' }

Write-Host "`nDone: the Game folder now matches Testing (game version $version)." -ForegroundColor Green
Write-Host "Nothing is committed. Next: open  $dst  , check `git status`, and commit it yourself." -ForegroundColor Green
Write-Host "Safety copy of how it was before: $zip" -ForegroundColor DarkGray
