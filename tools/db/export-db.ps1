<#
.SYNOPSIS
Exports the PQT Postgres database and the dataset volume into a new folder in the stick's db store.

.DESCRIPTION
Run it on the machine that holds the database (HOLDER.txt in the store names it):

    powershell -ExecutionPolicy Bypass -File tools\db\export-db.ps1 -Role desktop -Store H:\PQT\db
    powershell -ExecutionPolicy Bypass -File tools\db\export-db.ps1 -Role desktop -Store H:\PQT\db -Handover

It writes <yyyyMMdd-HHmm>-<role>[-<note>]\ in -Store, with these files:
    pqt.dump          pg_dump custom format (restore with restore-db.ps1)
    roles.sql         pg_dumpall --roles-only; it holds password hashes, so keep it private
    datasets.tgz      the dataset-data volume (Parquet builds and manifests)
    inventory.txt     the latest migration, per-table row counts and per-table content hashes; no figures
    meta.txt          role, repository commit and Postgres image digest of the exporting machine
    SHA256SUMS.txt

The folder is named by -Role (desktop or laptop), never by the computer name, so a laptop called
DESKTOP-... cannot be read as the desktop.

HOLDER.txt in -Store says which machine holds the one live database:
- Without -Handover the export is a snapshot (a rollback point). HOLDER.txt must name this role.
- With -Handover, HOLDER.txt becomes in-transit, carrying this folder and its dump's SHA-256.
  After that, stop the stack (docker compose down, without -v) and eject the stick. Only a restore
  of exactly this folder takes the database on another machine.
- -StaleBackup exports from a machine that does not hold the database. The folder is marked
  -stale-backup and is never restored except into throwaway volumes.
- -InitHolder writes the first HOLDER.txt, naming this role, when the store has none.

-Store must not be inside a git work tree: the files hold IDS figures, role password hashes and
possibly vendor lines (ADR-038, ADR-041). The script reads only POSTGRES_USER and POSTGRES_DATABASE
from .env and never reads or prints the password.
#>
param(
    [Parameter(Mandatory)] [ValidateSet('desktop', 'laptop')] [string] $Role,
    [Parameter(Mandatory)] [string] $Store,
    [string] $Repo = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,39}$')] [string] $Note,
    [switch] $Handover,
    [switch] $StaleBackup,
    [switch] $InitHolder,
    [string] $User,
    [string] $Database,
    [string] $Project = 'personal-quant-terminal',
    [string] $Image = 'postgres:17-alpine',
    [switch] $SkipDatasets
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'pqt-db-common.ps1')

if ($Handover -and $StaleBackup) { throw '-Handover and -StaleBackup exclude each other: a stale copy is never handed over.' }
if (-not (Test-Path $Store)) { throw "-Store $Store does not exist." }
$Store = (Resolve-Path $Store).Path
Assert-StoreOutsideRepo $Store

# The token. Only the holder exports a snapshot or hands the database over.
$holder = Read-Holder $Store
if (-not $holder) {
    if (-not $InitHolder -and -not $StaleBackup) {
        throw "No HOLDER.txt in $Store. If this machine holds the live database, pass -InitHolder."
    }
} elseif ($InitHolder) {
    throw "HOLDER.txt already exists in $Store (holder=$($holder['holder'])). -InitHolder is for an empty store only."
} elseif (-not $StaleBackup) {
    if ($holder['holder'] -eq 'in-transit') {
        throw "The database is in transit ($($holder['folder'])). Restore that folder first; this machine's data may be stale."
    }
    if ($holder['holder'] -ne $Role) {
        throw "HOLDER.txt names $($holder['holder']), not $Role. This machine's database is stale. Use -StaleBackup for a backup copy."
    }
}

# .env is gitignored and may be missing (a copied repo has none). Then the .env.example defaults
# apply; pass -User and -Database if the stack was set up with other names.
$envFile = Join-Path $Repo '.env'
if (Test-Path $envFile) {
    if (-not $User) { $User = Read-EnvValue $envFile 'POSTGRES_USER' }
    if (-not $Database) { $Database = Read-EnvValue $envFile 'POSTGRES_DATABASE' }
} else {
    Write-Warning "No .env at $envFile; using -User/-Database or the .env.example defaults."
}
if (-not $User) { $User = 'quant_user' }
if (-not $Database) { $Database = 'personal_quant' }
$pgVolume = "${Project}_postgres-data"
$dsVolume = "${Project}_dataset-data"
Assert-Volume $pgVolume

$parts = @((Get-Date -Format 'yyyyMMdd-HHmm'), $Role)
if ($Note) { $parts += $Note }
if ($StaleBackup) { $parts += 'stale-backup' }
$name = $parts -join '-'
$out = Join-Path $Store $name
if (Test-Path $out) { throw "$out already exists. Wait a minute or pass -Note." }
New-Item -ItemType Directory -Path $out | Out-Null
Write-Host "Exporting $pgVolume (database $Database) to $out"

$running = @(docker ps -q --filter "volume=$pgVolume")
$temp = $null
if ($running.Count -gt 0) {
    $container = $running[0]
    Write-Host "Using the running container $container"
} else {
    $temp = "pqt-export-$PID"
    Invoke-Docker run -d --name $temp -e PGDATA=/var/lib/postgresql/data/pgdata -v "${pgVolume}:/var/lib/postgresql/data" $Image | Out-Null
    $container = $temp
}

try {
    Wait-Postgres $container $User $Database
    Invoke-Docker exec $container pg_dump -U $User -d $Database -Fc -f /tmp/pqt.dump
    Invoke-Docker cp "${container}:/tmp/pqt.dump" (Join-Path $out 'pqt.dump')
    Invoke-Docker exec $container rm -f /tmp/pqt.dump
    # Roles live outside the database (quant_research comes from a migration, ADR-016).
    Invoke-Docker exec $container pg_dumpall -U $User --roles-only -f /tmp/roles.sql
    Invoke-Docker cp "${container}:/tmp/roles.sql" (Join-Path $out 'roles.sql')
    Invoke-Docker exec $container rm -f /tmp/roles.sql
    Write-Inventory $container $User $Database (Join-Path $out 'inventory.txt')
} finally {
    if ($temp) { Invoke-Quiet docker rm -f $temp | Out-Null }
}

if (-not $SkipDatasets) {
    if (Test-Volume $dsVolume) {
        Export-VolumeTar $dsVolume (Join-Path $out 'datasets.tgz') $Image
    } else {
        Write-Warning "No volume $dsVolume; datasets were not exported."
    }
}

$digest = Get-ImageDigest $Image
$commit = Get-RepoCommit $Repo
Set-Content -Path (Join-Path $out 'meta.txt') -Encoding ascii -Value @(
    "role=$Role",
    "commit=$commit",
    "image=$Image",
    "image_digest=$digest",
    "stale_backup=$([bool] $StaleBackup)"
)
Write-Checksums $out

if ($Handover -or $InitHolder) {
    Write-Holder $Store @{
        holder      = $(if ($Handover) { 'in-transit' } else { $Role })
        folder      = $name
        dump_sha256 = Get-FileSha256 (Join-Path $out 'pqt.dump')
        image       = $digest
        commit      = $commit
    }
}

Write-Host 'Done:'
Get-ChildItem $out | Format-Table Name, Length -AutoSize
if ($Handover) {
    Write-Host "HOLDER.txt is now in-transit with $name."
    Write-Host 'Next: docker compose down (never -v), then eject the stick. This machine no longer holds the database.'
} elseif ($InitHolder) {
    Write-Host "HOLDER.txt now names $Role."
}
