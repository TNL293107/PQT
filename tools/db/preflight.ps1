<#
.SYNOPSIS
Checks a machine before a PQT restore, a stack start, or work on a started stack.

.DESCRIPTION
    powershell -ExecutionPolicy Bypass -File tools\db\preflight.ps1 -Role laptop -Store D:\PQT\db -Stage before-restore -From D:\PQT\db\<folder>
    powershell -ExecutionPolicy Bypass -File tools\db\preflight.ps1 -Role desktop -Store H:\PQT\db -Stage before-up
    powershell -ExecutionPolicy Bypass -File tools\db\preflight.ps1 -Role desktop -Store H:\PQT\db -Stage after-up

Each check prints one line: PASS, WARN, FAIL or SKIP, its id and a reason. It never prints a value
from the database, a dataset or .env: only key names, counts and verdicts. Any FAIL exits 1.

Stages:
  before-restore  the stack is down and a folder is about to be restored (give -From)
  before-up       the stack is about to start
  after-up        the stack runs; checks the live database, the API and the backend's /app/data

Checks:
  A  machine     Docker, the Postgres image and its digest, the .NET SDK global.json asks for,
                 quant/.venv, frontend/node_modules
  B  repository  clean tree, the database's commit is an ancestor, not behind origin/develop,
                 73588e3 is not an ancestor, the portable override on a removable drive, .env keys,
                 MARKET_DATA_IMPORT_ON_STARTUP=false, nothing tracked under tmp/
  C  Docker      the project's volumes, no container on them before a restore, free host ports
  D  token, DB   HOLDER.txt names this role; after up: inventory, /health, /app/data, schema status,
                 the seed manifest of the portable data volume
  E  store       the folder's checksums, free space on the stick

The seed manifest is the one tools/stack/seed-data.ps1 writes: SHA256SUMS at the root of the volume
<project>_pqt-data, in sha256sum -c format, one line per file under data/fixtures and data/schemas,
as "<sha256>  <path relative to data/, with forward slashes>". D5 checks that the volume's files
match it (sha256sum -c inside the volume) and that it matches the checkout, in any order.
#>
param(
    [Parameter(Mandatory)] [ValidateSet('desktop', 'laptop')] [string] $Role,
    [Parameter(Mandatory)] [string] $Store,
    [Parameter(Mandatory)] [ValidateSet('before-restore', 'before-up', 'after-up')] [string] $Stage,
    [string] $From,
    [string] $Repo = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [string] $Project = 'personal-quant-terminal',
    [string] $Image = 'postgres:17-alpine'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'pqt-db-common.ps1')

$script:failures = 0
function Report([string] $Status, [string] $Id, [string] $Reason) {
    if ($Status -eq 'FAIL') { $script:failures++ }
    Write-Host ('{0,-4} {1,-3} {2}' -f $Status, $Id, $Reason)
}

$Repo = (Resolve-Path $Repo).Path
$envFile = Join-Path $Repo '.env'
$envMap = @{}
if (Test-Path $envFile) {
    foreach ($line in Get-Content $envFile) {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=(.*)$') { $envMap[$Matches[1]] = $Matches[2].Trim().Trim('"').Trim("'") }
    }
}
$holder = if (Test-Path $Store) { Read-Holder $Store } else { $null }
$pgVolume = "${Project}_postgres-data"
$dsVolume = "${Project}_dataset-data"
$dataVolume = "${Project}_pqt-data"
$composeFiles = if ($envMap['COMPOSE_FILE']) { $envMap['COMPOSE_FILE'] } else { '' }
$portableInUse = $composeFiles -match 'docker-compose\.portable\.yml'

Write-Host "preflight: role=$Role stage=$Stage repo=$Repo"

# --- A. Machine ------------------------------------------------------------------------------------
$dockerUp = (Invoke-Quiet docker info --format '{{.ServerVersion}}').Code -eq 0
if ($dockerUp) { Report PASS A2 'Docker answers' } else { Report FAIL A2 'Docker does not answer; start Docker Desktop' }

$digest = if ($dockerUp) { Get-ImageDigest $Image } else { $null }
if (-not $digest) {
    Report FAIL A3 "$Image is not present locally (a restore offline cannot pull it)"
} elseif ($holder -and $holder['image'] -and $holder['image'] -ne $digest) {
    Report WARN A3 "$Image digest differs from the one HOLDER.txt recorded; a dump restores across minor versions, not majors"
} else {
    Report PASS A3 "$Image present$(if ($holder -and $holder['image']) { ', same digest as HOLDER.txt' })"
}

$globalJson = Join-Path $Repo 'global.json'
if (Test-Path $globalJson) {
    $wanted = [version] ((Get-Content $globalJson -Raw | ConvertFrom-Json).sdk.version)
    $sdks = @((Invoke-Quiet dotnet --list-sdks).Output | ForEach-Object { ($_ -split ' ')[0] } |
        Where-Object { $_ -match '^\d+\.\d+\.\d+$' } | ForEach-Object { [version] $_ })
    # rollForward latestPatch: same major, minor and feature band (hundreds of the patch), patch at least.
    $ok = @($sdks | Where-Object {
        $_.Major -eq $wanted.Major -and $_.Minor -eq $wanted.Minor -and
        [math]::Floor($_.Build / 100) -eq [math]::Floor($wanted.Build / 100) -and $_.Build -ge $wanted.Build
    })
    if ($ok.Count -gt 0) { Report PASS A4 ".NET SDK satisfies global.json ($wanted, latestPatch)" }
    else { Report FAIL A4 ".NET SDK $wanted or a later patch in its band is not installed" }
} else {
    Report SKIP A4 'no global.json'
}

$python = Join-Path $Repo 'quant\.venv\Scripts\python.exe'
if (-not (Test-Path $python)) {
    Report WARN A5 'quant\.venv is missing; build it on this machine (it does not travel)'
} else {
    $probe = Invoke-Quiet $python -c 'import sys, personal_quant; sys.exit(0 if sys.version_info >= (3, 12) else 3)'
    if ($probe.Code -eq 0) { Report PASS A5 'quant\.venv runs Python 3.12+ and imports personal_quant' }
    else { Report WARN A5 'quant\.venv does not import personal_quant on Python 3.12+; reinstall it' }
}

if (Test-Path (Join-Path $Repo 'frontend\node_modules')) { Report PASS A6 'frontend\node_modules present' }
else { Report WARN A6 'frontend\node_modules missing; run npm ci in frontend' }

# --- B. Repository ---------------------------------------------------------------------------------
$status = Invoke-Quiet git -C $Repo status --porcelain
if ($status.Code -ne 0) { Report FAIL B1 'not a git work tree' }
elseif ($status.Output.Count -eq 0) { Report PASS B1 'work tree clean' }
else { Report WARN B1 "work tree has $($status.Output.Count) changed path(s)" }

if ($holder -and $holder['commit'] -and $Stage -ne 'before-restore') {
    $anc = Invoke-Quiet git -C $Repo merge-base --is-ancestor $holder['commit'] HEAD
    if ($anc.Code -eq 0) { Report PASS B1 "HEAD contains the commit the database was written with" }
    else { Report FAIL B1 'HEAD does not contain the commit the database was written with; check it out or pull first' }
}

$behind = Invoke-Quiet git -C $Repo rev-list --count HEAD..origin/develop
if ($behind.Code -ne 0) { Report WARN B2 'origin/develop is not known here; fetch it' }
elseif ([int] $behind.Output[0] -eq 0) { Report PASS B2 'not behind origin/develop (as last fetched)' }
else { Report WARN B2 "$($behind.Output[0]) commit(s) behind origin/develop (as last fetched)" }

$tainted = Invoke-Quiet git -C $Repo merge-base --is-ancestor 73588e3 HEAD
if ($tainted.Code -eq 0) { Report FAIL B3 'HEAD contains 73588e3 (ADR-041): never push this branch' }
else { Report PASS B3 '73588e3 is not an ancestor of HEAD' }

$drive = (Split-Path $Repo -Qualifier)
$disk = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$drive'" -ErrorAction SilentlyContinue
$removable = $disk -and $disk.DriveType -eq 2
$portableFile = Test-Path (Join-Path $Repo 'docker-compose.portable.yml')
if ($removable) {
    if ($portableFile -and $portableInUse) { Report PASS B4 'repo on a removable drive, and .env COMPOSE_FILE uses docker-compose.portable.yml' }
    elseif (-not $portableFile) { Report FAIL B4 'repo on a removable drive, and docker-compose.portable.yml is missing: ./data cannot be bind-mounted' }
    else { Report FAIL B4 'repo on a removable drive, and .env COMPOSE_FILE does not include docker-compose.portable.yml' }
} else {
    Report PASS B4 "repo on a fixed drive$(if ($portableInUse) { '; the portable override is in use' })"
}

if (-not (Test-Path $envFile)) {
    Report FAIL B5 '.env is missing; create it by hand on this machine (it never travels)'
} else {
    $missing = @('POSTGRES_USER', 'POSTGRES_DATABASE', 'POSTGRES_PASSWORD', 'POSTGRES_HOST_PORT', 'RESEARCH_DB_PASSWORD') |
        Where-Object { -not $envMap[$_] }
    if ($missing.Count -eq 0) { Report PASS B5 '.env has the Postgres keys and RESEARCH_DB_PASSWORD' }
    else { Report FAIL B5 (".env lacks a value for: " + ($missing -join ', ')) }
    $startup = $envMap['MARKET_DATA_IMPORT_ON_STARTUP']
    if (-not $startup -or $startup -eq 'false') { Report PASS B5 'MARKET_DATA_IMPORT_ON_STARTUP is false' }
    else { Report FAIL B5 'MARKET_DATA_IMPORT_ON_STARTUP is not false: every compose up would write reference data' }
}

$tracked = Invoke-Quiet git -C $Repo ls-files -- tmp data/tmp
if ($tracked.Output.Count -eq 0) { Report PASS B6 'nothing under tmp/ or data/tmp/ is tracked' }
else { Report FAIL B6 "$($tracked.Output.Count) file(s) under tmp/ or data/tmp/ are tracked" }

# --- C. Docker state -------------------------------------------------------------------------------
if ($dockerUp) {
    foreach ($volume in @($pgVolume, $dsVolume)) {
        if (Test-Volume $volume) { Report PASS C1 "volume $volume exists" }
        elseif ($Stage -eq 'before-restore') { Report WARN C1 "volume $volume does not exist yet (a first restore creates it)" }
        else { Report FAIL C1 "volume $volume does not exist" }
    }
    if ($Stage -eq 'before-restore') {
        foreach ($volume in @($pgVolume, $dsVolume)) {
            $users = @(docker ps -aq --filter "volume=$volume")
            if ($users.Count -eq 0) { Report PASS C2 "no container uses $volume" }
            else { Report FAIL C2 "$($users.Count) container(s) use $volume; docker compose down (never -v)" }
        }
    }
    if ($Stage -eq 'before-up') {
        foreach ($key in @('POSTGRES_HOST_PORT', 'API_PORT')) {
            $port = if ($envMap[$key]) { [int] $envMap[$key] } elseif ($key -eq 'API_PORT') { 8080 } else { 5432 }
            $busy = @(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)
            if ($busy.Count -eq 0) { Report PASS C3 "$key port is free" }
            else { Report FAIL C3 "$key port is already in use" }
        }
    }
}

# --- D. Token and database -------------------------------------------------------------------------
if (-not $holder) {
    Report FAIL D1 "no HOLDER.txt in $Store"
} elseif ($holder['holder'] -eq $Role) {
    Report PASS D1 "HOLDER.txt names $Role"
} elseif ($holder['holder'] -eq 'in-transit') {
    if ($Stage -eq 'before-restore' -and $From -and (Split-Path $From -Leaf) -eq $holder['folder'] -and
        (Test-Path (Join-Path $From 'pqt.dump')) -and (Get-FileSha256 (Join-Path $From 'pqt.dump')) -eq $holder['dump_sha256']) {
        Report PASS D1 'the database is in transit, and -From is the folder HOLDER.txt names'
    } else {
        Report FAIL D1 'the database is in transit: restore the folder HOLDER.txt names before starting or writing'
    }
} else {
    Report FAIL D1 "HOLDER.txt names $($holder['holder']): this machine's database is stale; do not start or write"
}

if ($Stage -eq 'after-up' -and $dockerUp) {
    $pg = @(docker ps -q --filter "volume=$pgVolume")
    $folderInventory = if ($holder -and $holder['folder']) { Join-Path (Join-Path $Store $holder['folder']) 'inventory.txt' } else { $null }
    if ($pg.Count -eq 0) {
        Report FAIL D2 "no running container uses $pgVolume"
    } elseif (-not $folderInventory -or -not (Test-Path $folderInventory)) {
        Report SKIP D2 'HOLDER.txt names no folder with an inventory to compare with'
    } else {
        $live = Get-Inventory $pg[0] $envMap['POSTGRES_USER'] $envMap['POSTGRES_DATABASE']
        $expected = Get-Content $folderInventory
        $cmp = Compare-Inventory $expected $live
        $a = ConvertTo-InventoryMap $expected
        $b = ConvertTo-InventoryMap $live
        $dropped = @($a.Keys | Where-Object { $_ -like 'rows *' -and $b.Contains($_) -and [long] $b[$_] -lt [long] $a[$_] })
        if ($cmp.Differences.Count -eq 0) {
            Report PASS D2 "live inventory equals $($holder['folder'])$(if (-not $cmp.HashesCompared) { ' (counts only: old export)' })"
        } elseif ($a['latest migration'] -ne $b['latest migration'] -and $a['latest migration'] -gt $b['latest migration']) {
            Report FAIL D2 'the live database is at an older migration than the folder it came from'
        } elseif ($dropped.Count -gt 0) {
            Report FAIL D2 "$($dropped.Count) table(s) hold fewer rows than in $($holder['folder']): wrong database?"
        } else {
            Report WARN D2 "$($cmp.Differences.Count) inventory line(s) changed since $($holder['folder']); expected only if this machine wrote since"
        }
    }

    $apiPort = if ($envMap['API_PORT']) { $envMap['API_PORT'] } else { '8080' }
    try {
        $health = Invoke-WebRequest -UseBasicParsing -TimeoutSec 10 "http://localhost:$apiPort/health"
        if ($health.StatusCode -eq 200) { Report PASS D3 '/health answers 200' } else { Report FAIL D3 "/health answers $($health.StatusCode)" }
    } catch { Report FAIL D3 '/health does not answer' }

    $fixtures = Invoke-Quiet docker compose --project-directory $Repo exec -T backend sh -c 'ls -A /app/data/fixtures | wc -l'
    if ($fixtures.Code -ne 0) { Report FAIL D3 'cannot list /app/data/fixtures in the backend container' }
    elseif ([int] $fixtures.Output[-1] -gt 0) { Report PASS D3 '/app/data/fixtures in the backend is not empty' }
    else { Report FAIL D3 '/app/data/fixtures in the backend is empty: the mount or the seed failed silently' }

    $schema = Invoke-Quiet docker compose --project-directory $Repo exec -T backend dotnet cli/PersonalQuant.Cli.dll schema status
    if ($schema.Code -eq 0) { Report PASS D4 'schema status: no pending migration' }
    else { Report FAIL D4 'schema status: the database is behind this build, or the CLI failed' }
}

if ($Stage -ne 'before-restore' -and $dockerUp) {
    if (-not $portableInUse) {
        Report SKIP D5 'the portable override is not in use; ./data is bind-mounted'
    } elseif (-not (Test-Volume $dataVolume)) {
        Report FAIL D5 "volume $dataVolume does not exist; run the seed script"
    } else {
        $dataRoot = Join-Path $Repo 'data'
        $local = @(foreach ($sub in @('fixtures', 'schemas')) {
            $dir = Join-Path $dataRoot $sub
            if (Test-Path $dir) {
                Get-ChildItem $dir -Recurse -File | ForEach-Object {
                    $relative = $_.FullName.Substring($dataRoot.Length + 1).Replace('\', '/')
                    '{0}  {1}' -f (Get-FileSha256 $_.FullName), $relative
                }
            }
        })
        $seeded = Invoke-Quiet docker run --rm -v "${dataVolume}:/d:ro" $Image cat /d/SHA256SUMS
        if ($seeded.Code -ne 0) {
            Report FAIL D5 "$dataVolume has no SHA256SUMS; run tools/stack/seed-data.ps1"
        } else {
            $selfCheck = Invoke-Quiet docker run --rm -v "${dataVolume}:/d:ro" $Image sh -c 'cd /d && sha256sum -c -s SHA256SUMS'
            if ($selfCheck.Code -eq 0) { Report PASS D5 "the files in $dataVolume match its SHA256SUMS" }
            else { Report FAIL D5 "the files in $dataVolume do not match its SHA256SUMS; re-run tools/stack/seed-data.ps1" }
            $theirs = @($seeded.Output | Where-Object { $_ })
            $diff = @(Compare-Object $local $theirs -CaseSensitive)
            if ($diff.Count -eq 0) { Report PASS D5 "SHA256SUMS matches data/fixtures and data/schemas in the checkout ($($local.Count) files)" }
            else { Report FAIL D5 "SHA256SUMS differs from the checkout in $($diff.Count) line(s); re-run tools/stack/seed-data.ps1" }
        }
    }
}

# --- E. Store --------------------------------------------------------------------------------------
if ($Stage -eq 'before-restore') {
    if (-not $From) { Report FAIL E1 'give -From for a before-restore check' }
    else {
        try { Test-Checksums (Resolve-Path $From).Path; Report PASS E1 'SHA256SUMS.txt of -From checks out' }
        catch { Report FAIL E1 "checksums of -From: $($_.Exception.Message)" }
    }
}

if (Test-Path $Store) {
    $latest = Get-ChildItem $Store -Directory | Where-Object Name -match '^\d{8}-\d{4}-' | Sort-Object Name | Select-Object -Last 1
    $need = if ($latest) { 2 * ((Get-ChildItem $latest.FullName -File | Measure-Object Length -Sum).Sum) } else { 0 }
    $free = (Get-PSDrive -Name (Split-Path (Resolve-Path $Store).Path -Qualifier).TrimEnd(':')).Free
    if ($free -ge $need) { Report PASS E2 'the store has room for two more exports' }
    else { Report FAIL E2 'the store has less free space than two exports need' }
} else {
    Report FAIL E2 "-Store $Store does not exist"
}

if ($script:failures -gt 0) {
    Write-Host "preflight: $($script:failures) FAIL"
    exit 1
}
Write-Host 'preflight: no FAIL'
