<#
.SYNOPSIS
Restores a folder written by export-db.ps1 into this machine's PQT Docker volumes.

.DESCRIPTION
Dry run first, into throwaway volumes (touches nothing real and ignores HOLDER.txt):

    powershell -ExecutionPolicy Bypass -File tools\db\restore-db.ps1 -Role laptop `
        -From D:\PQT\db\20261118-2119-desktop -Repo D:\PQT\PQT -PgVolume pqt-try-pg -DatasetVolume pqt-try-ds

Then the real restore:

    powershell -ExecutionPolicy Bypass -File tools\db\restore-db.ps1 -Role laptop `
        -From D:\PQT\db\20261118-2119-desktop -Repo D:\PQT\PQT -Replace

What it does:
- It checks SHA256SUMS.txt first.
- A real restore obeys HOLDER.txt in the store (the folder that holds -From). It goes ahead only
  when the database is in transit and the token's dump SHA-256 equals this folder's pqt.dump, or
  when this role already holds the database and rolls back: to the folder HOLDER.txt names (its
  last restore or hand-over), or to one of its own snapshots no older than that folder. Anything
  else is refused unless -Force.
- It refuses to run while any container uses the target volumes. Stop the stack first:
  docker compose down. Do not use -v.
- If a target volume already exists, it needs -Replace. The old volume is then saved as
  <store>\pre-restore\<stamp>\<volume>.tgz before anything is removed.
- Credentials come from <Repo>\.env (POSTGRES_USER, POSTGRES_DATABASE, POSTGRES_PASSWORD).
  Without a .env, pass -User and -Database; the password is then asked for at the console, so this
  works only when run by hand. The password reaches Docker through the environment, never the
  command line, and is never printed.
- It warns when this machine's Postgres image digest differs from the exporting machine's.
- It restores pqt.dump and datasets.tgz, then writes inventory-restored.txt and compares it with
  inventory.txt: the latest migration, every row count and every table's content hash.
- After a real restore that matches, HOLDER.txt names -Role.
#>
param(
    [Parameter(Mandatory)] [string] $From,
    [Parameter(Mandatory)] [ValidateSet('desktop', 'laptop')] [string] $Role,
    [string] $Repo,
    [string] $User,
    [string] $Database,
    [string] $Project = 'personal-quant-terminal',
    [string] $PgVolume,
    [string] $DatasetVolume,
    [string] $Image = 'postgres:17-alpine',
    [switch] $Replace,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'pqt-db-common.ps1')

$defaultPg = "${Project}_postgres-data"
$defaultDs = "${Project}_dataset-data"
if (-not $PgVolume) { $PgVolume = $defaultPg }
if (-not $DatasetVolume) { $DatasetVolume = $defaultDs }
# A dry run writes only to volumes that are not the project's own. It ignores the token.
$dryRun = ($PgVolume -ne $defaultPg) -and ($DatasetVolume -ne $defaultDs)
if (-not $dryRun -and (($PgVolume -ne $defaultPg) -or ($DatasetVolume -ne $defaultDs))) {
    throw 'Give both -PgVolume and -DatasetVolume for a dry run, or neither for a real restore.'
}

$From = (Resolve-Path $From).Path
$Store = Split-Path $From -Parent
Assert-StoreOutsideRepo $Store
Test-Checksums $From
$dump = Join-Path $From 'pqt.dump'
if (-not (Test-Path $dump)) { throw "No pqt.dump in $From" }
$datasets = Join-Path $From 'datasets.tgz'
$dumpSha = Get-FileSha256 $dump
$folder = Split-Path $From -Leaf

$meta = @{}
$metaFile = Join-Path $From 'meta.txt'
if (Test-Path $metaFile) {
    foreach ($line in Get-Content $metaFile) {
        if ($line -match '=') { $k, $v = $line -split '=', 2; $meta[$k] = $v }
    }
}

# --- The token -------------------------------------------------------------------------------------
if ($dryRun) {
    Write-Host "Dry run into $PgVolume and $DatasetVolume; HOLDER.txt is neither checked nor changed."
} else {
    if ($folder -like '*-stale-backup*' -and -not $Force) {
        throw "$folder is a stale backup. Restore it only into throwaway volumes, or pass -Force."
    }
    $holder = Read-Holder $Store
    $refusal = $null
    if (-not $holder) {
        $refusal = "No HOLDER.txt in $Store, so nothing says which database is current."
    } elseif ($holder['holder'] -eq 'in-transit') {
        if ($holder['dump_sha256'] -ne $dumpSha) {
            $refusal = "HOLDER.txt is in transit with $($holder['folder']), whose dump SHA-256 is not this folder's ($folder)."
        }
    } elseif ($holder['holder'] -eq $Role) {
        # A rollback: the folder this role last took the database from, or one of its own snapshots
        # taken since then. An older snapshot predates the last hand-over and would lose the other
        # machine's work.
        $baseStamp = Get-FolderStamp $holder['folder']
        $fromStamp = Get-FolderStamp $From
        if ($folder -eq $holder['folder']) {
            if ($holder['dump_sha256'] -and $holder['dump_sha256'] -ne $dumpSha) {
                $refusal = "$folder is the folder HOLDER.txt names, but its dump SHA-256 has changed."
            }
        } elseif ((Get-FolderRole $From) -ne $Role) {
            $refusal = "$Role already holds the database, and $folder is not one of its own snapshots."
        } elseif (-not $baseStamp -or -not $fromStamp) {
            $refusal = "Cannot tell whether $folder is newer than $($holder['folder']), the folder HOLDER.txt names."
        } elseif ($fromStamp -lt $baseStamp) {
            $refusal = "$folder is older than $($holder['folder']), the last restore or hand-over HOLDER.txt names; it would lose the work done since."
        }
    } else {
        $refusal = "HOLDER.txt names $($holder['holder']). Hand the database over from there first (export-db.ps1 -Handover)."
    }
    if ($refusal) {
        if (-not $Force) { throw "$refusal Refused; pass -Force only if you know this folder is the current database." }
        Write-Warning "$refusal Going ahead because of -Force."
    }
}

$localDigest = Get-ImageDigest $Image
if (-not $localDigest) {
    Write-Warning "$Image is not present locally; docker will try to pull it."
} elseif ($meta['image_digest'] -and $meta['image_digest'] -ne $localDigest) {
    Write-Warning "This machine's $Image ($localDigest) differs from the exporting machine's ($($meta['image_digest'])). A dump restores across minor versions; a different major version would not."
}

# --- Credentials -----------------------------------------------------------------------------------
$envFile = if ($Repo) { Join-Path $Repo '.env' } else { $null }
$password = $null
if ($envFile -and (Test-Path $envFile)) {
    if (-not $User) { $User = Read-EnvValue $envFile 'POSTGRES_USER' }
    if (-not $Database) { $Database = Read-EnvValue $envFile 'POSTGRES_DATABASE' }
    $password = Read-EnvValue $envFile 'POSTGRES_PASSWORD'
} else {
    if (-not $User -or -not $Database) {
        throw 'No .env found. Pass -Repo with a .env, or -User and -Database (the password is then asked for).'
    }
    if (-not [Environment]::UserInteractive -or [Console]::IsInputRedirected) {
        throw 'No .env found and no console to ask for the password on. Run it by hand, or give -Repo with a .env.'
    }
    $secure = Read-Host -AsSecureString "Postgres password for $User (it becomes this machine's password)"
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
    if (-not $password) { throw 'An empty password is refused.' }
}

# --- Volumes ---------------------------------------------------------------------------------------
$backupDir = Join-Path (Join-Path $Store 'pre-restore') (Get-Date -Format 'yyyyMMdd-HHmmss')

function Reset-Volume([string] $Name, [string] $ComposeName) {
    if (@(docker ps -aq --filter "volume=$Name").Count -gt 0) {
        throw "A container uses $Name. Stop it first (docker compose down, without -v)."
    }
    if (Test-Volume $Name) {
        if (-not $Replace) { throw "$Name already exists. Pass -Replace to save it and replace it." }
        if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory -Path $backupDir | Out-Null }
        $backup = Join-Path $backupDir "$Name.tgz"
        Write-Host "Saving $Name as $backup"
        Export-VolumeTar $Name $backup $Image
        Invoke-Docker volume rm $Name | Out-Null
    }
    Invoke-Docker volume create --label "com.docker.compose.project=$Project" `
        --label "com.docker.compose.volume=$ComposeName" $Name | Out-Null
}

# Both volumes are checked before either is touched.
foreach ($volume in @($PgVolume, $DatasetVolume)) {
    if (@(docker ps -aq --filter "volume=$volume").Count -gt 0) {
        throw "A container uses $volume. Stop it first (docker compose down, without -v)."
    }
    if ((Test-Volume $volume) -and -not $Replace) { throw "$volume already exists. Pass -Replace to save it and replace it." }
}

Reset-Volume $PgVolume 'postgres-data'
$temp = "pqt-restore-$PID"
$env:POSTGRES_PASSWORD = $password
try {
    Invoke-Docker run -d --name $temp -e POSTGRES_PASSWORD -e "POSTGRES_USER=$User" -e "POSTGRES_DB=$Database" `
        -e PGDATA=/var/lib/postgresql/data/pgdata -v "${PgVolume}:/var/lib/postgresql/data" $Image | Out-Null
} finally {
    Remove-Item Env:POSTGRES_PASSWORD
    $password = $null
}

try {
    Wait-Postgres $temp $User $Database
    $roles = Join-Path $From 'roles.sql'
    if (Test-Path $roles) {
        # Skip the bootstrap user: it already exists, and this machine's password owns it.
        $skip = '^(CREATE|ALTER) ROLE "?{0}"?[ ;]' -f [regex]::Escape($User)
        $sqlRoles = Get-Content $roles | Where-Object { $_ -notmatch $skip }
        $sqlRoles | docker exec -i $temp psql -U $User -d $Database -X -q -v ON_ERROR_STOP=1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'restoring roles failed' }
    }
    Invoke-Docker cp $dump "${temp}:/tmp/pqt.dump"
    Invoke-Docker exec $temp pg_restore -U $User -d $Database --no-owner --exit-on-error /tmp/pqt.dump
    Invoke-Docker exec $temp rm -f /tmp/pqt.dump
    Write-Inventory $temp $User $Database (Join-Path $From 'inventory-restored.txt')
} finally {
    Invoke-Quiet docker rm -f $temp | Out-Null
}

if (Test-Path $datasets) {
    Reset-Volume $DatasetVolume 'dataset-data'
    Import-VolumeTar $DatasetVolume $datasets $Image
}

# --- Verdict ---------------------------------------------------------------------------------------
$comparison = Compare-Inventory (Get-Content (Join-Path $From 'inventory.txt')) (Get-Content (Join-Path $From 'inventory-restored.txt'))
if (-not $comparison.HashesCompared) {
    Write-Warning 'This export predates content hashes; only the migration and the row counts were compared.'
}
if ($comparison.Differences.Count -gt 0) {
    Write-Warning ('The restored inventory differs from the exported one: ' + ($comparison.Differences -join ', '))
    Write-Warning 'HOLDER.txt was not changed.'
    exit 1
}

if (-not $dryRun) {
    Write-Holder $Store @{
        holder      = $Role
        folder      = $folder
        dump_sha256 = $dumpSha
        image       = $localDigest
        commit      = $meta['commit']
    }
    Write-Host "HOLDER.txt now names $Role."
}
Write-Host 'Restored. The inventory matches the export.'
