<#
.SYNOPSIS
Exports the PQT Postgres database and the dataset volume into a new folder next to this script.

.DESCRIPTION
Run it on the machine that holds the database. For PQT that is the laptop, because the sprint 9/10
database lives there:

    powershell -ExecutionPolicy Bypass -File H:\PQT\db\export-db.ps1 -Repo C:\path\to\PQT

It writes <yyyyMMdd-HHmm>-<computer>\ with these files:
    pqt.dump          pg_dump custom format (restore with restore-db.ps1)
    roles.sql         pg_dumpall --roles-only; it holds password hashes, so keep it private
    datasets.tgz      the dataset-data volume (Parquet builds and manifests)
    inventory.txt     the latest migration and per-table row counts; no figures
    SHA256SUMS.txt

If a Postgres container already uses the volume, the script dumps through it. Otherwise it starts a
temporary one, and it never runs two servers on the same data. It reads only POSTGRES_USER and
POSTGRES_DATABASE from .env and never reads or prints the password.

The files hold IDS figures and possibly vendor lines. Never commit them or put them on GitHub
(ADR-038, ADR-041).
#>
param(
    [string] $Repo = (Split-Path $PSScriptRoot -Parent | Join-Path -ChildPath 'PQT'),
    [string] $User,
    [string] $Database,
    [string] $Project = 'personal-quant-terminal',
    [string] $Image = 'postgres:17-alpine',
    [switch] $SkipDatasets
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'pqt-db-common.ps1')

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
$user = $User
$db = $Database
$pgVolume = "${Project}_postgres-data"
$dsVolume = "${Project}_dataset-data"
Assert-Volume $pgVolume

$out = Join-Path $PSScriptRoot ('{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmm'), $env:COMPUTERNAME.ToLower())
New-Item -ItemType Directory -Path $out | Out-Null
Write-Host "Exporting $pgVolume (database $db) to $out"

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
    Wait-Postgres $container $user $db
    Invoke-Docker exec $container pg_dump -U $user -d $db -Fc -f /tmp/pqt.dump
    Invoke-Docker cp "${container}:/tmp/pqt.dump" (Join-Path $out 'pqt.dump')
    Invoke-Docker exec $container rm -f /tmp/pqt.dump
    # Roles live outside the database (quant_research comes from a migration, ADR-016).
    Invoke-Docker exec $container pg_dumpall -U $user --roles-only -f /tmp/roles.sql
    Invoke-Docker cp "${container}:/tmp/roles.sql" (Join-Path $out 'roles.sql')
    Invoke-Docker exec $container rm -f /tmp/roles.sql
    Write-Inventory $container $user $db (Join-Path $out 'inventory.txt')
} finally {
    if ($temp) { docker rm -f $temp | Out-Null }
}

if (-not $SkipDatasets) {
    $hasDatasets = Test-Volume $dsVolume
    if ($hasDatasets) {
        Export-VolumeTar $dsVolume (Join-Path $out 'datasets.tgz') $Image
    } else {
        Write-Warning "No volume $dsVolume; datasets were not exported."
    }
}

Write-Checksums $out
Write-Host 'Done:'
Get-ChildItem $out | Format-Table Name, Length -AutoSize
