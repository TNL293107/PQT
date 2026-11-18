<#
.SYNOPSIS
Restores a folder written by export-db.ps1 into this machine's PQT Docker volumes.

.DESCRIPTION
    powershell -ExecutionPolicy Bypass -File H:\PQT\db\restore-db.ps1 `
        -From H:\PQT\db\20261118-1530-laptop -Repo E:\PQT -Replace

What it does:
- It checks SHA256SUMS.txt first.
- It refuses to run while any container uses the target volumes. Stop the stack first:
  docker compose down. Do not use -v.
- If a target volume already exists, it needs -Replace. The old volume is then saved as
  pre-restore-<volume>-<stamp>.tgz in the -From folder before anything is removed.
- It creates the database with POSTGRES_USER, POSTGRES_PASSWORD and POSTGRES_DATABASE from .env.
  The password goes to Docker through the environment, never on the command line, and is never
  printed.
- It restores pqt.dump and datasets.tgz, then writes inventory-restored.txt. Compare that with
  inventory.txt.

Use -PgVolume and -DatasetVolume to restore into throwaway volumes for a dry run.
#>
param(
    [Parameter(Mandatory)] [string] $From,
    [Parameter(Mandatory)] [string] $Repo,
    [string] $Project = 'personal-quant-terminal',
    [string] $PgVolume,
    [string] $DatasetVolume,
    [string] $Image = 'postgres:17-alpine',
    [switch] $Replace
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'pqt-db-common.ps1')

if (-not $PgVolume) { $PgVolume = "${Project}_postgres-data" }
if (-not $DatasetVolume) { $DatasetVolume = "${Project}_dataset-data" }
$From = (Resolve-Path $From).Path
Test-Checksums $From
$dump = Join-Path $From 'pqt.dump'
if (-not (Test-Path $dump)) { throw "No pqt.dump in $From" }
$datasets = Join-Path $From 'datasets.tgz'

$envFile = Join-Path $Repo '.env'
if (-not (Test-Path $envFile)) { throw "No .env at $envFile" }
$user = Read-EnvValue $envFile 'POSTGRES_USER'
$db = Read-EnvValue $envFile 'POSTGRES_DATABASE'

function Reset-Volume([string] $Name, [string] $ComposeName) {
    if (@(docker ps -aq --filter "volume=$Name").Count -gt 0) {
        throw "A container uses $Name. Stop it first (docker compose down, without -v)."
    }
    if (Test-Volume $Name) {
        if (-not $Replace) { throw "$Name already exists. Pass -Replace to save it and replace it." }
        $backup = 'pre-restore-{0}-{1}.tgz' -f $Name, (Get-Date -Format 'yyyyMMdd-HHmmss')
        Write-Host "Saving $Name as $backup"
        Export-VolumeTar $Name (Join-Path $From $backup) $Image
        Invoke-Docker volume rm $Name | Out-Null
    }
    Invoke-Docker volume create --label "com.docker.compose.project=$Project" `
        --label "com.docker.compose.volume=$ComposeName" $Name | Out-Null
}

Reset-Volume $PgVolume 'postgres-data'
$temp = "pqt-restore-$PID"
$env:POSTGRES_PASSWORD = Read-EnvValue $envFile 'POSTGRES_PASSWORD'
try {
    Invoke-Docker run -d --name $temp -e POSTGRES_PASSWORD -e "POSTGRES_USER=$user" -e "POSTGRES_DB=$db" `
        -e PGDATA=/var/lib/postgresql/data/pgdata -v "${PgVolume}:/var/lib/postgresql/data" $Image | Out-Null
} finally {
    Remove-Item Env:POSTGRES_PASSWORD
}

try {
    Wait-Postgres $temp $user $db
    $roles = Join-Path $From 'roles.sql'
    if (Test-Path $roles) {
        # Skip the bootstrap user: it already exists, and this machine's .env owns its password.
        $skip = '^(CREATE|ALTER) ROLE "?{0}"?[ ;]' -f [regex]::Escape($user)
        $sqlRoles = Get-Content $roles | Where-Object { $_ -notmatch $skip }
        $sqlRoles | docker exec -i $temp psql -U $user -d $db -X -q -v ON_ERROR_STOP=1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'restoring roles failed' }
    }
    Invoke-Docker cp $dump "${temp}:/tmp/pqt.dump"
    Invoke-Docker exec $temp pg_restore -U $user -d $db --no-owner --exit-on-error /tmp/pqt.dump
    Invoke-Docker exec $temp rm -f /tmp/pqt.dump
    Write-Inventory $temp $user $db (Join-Path $From 'inventory-restored.txt')
} finally {
    docker rm -f $temp | Out-Null
}

if (Test-Path $datasets) {
    Reset-Volume $DatasetVolume 'dataset-data'
    Import-VolumeTar $DatasetVolume $datasets $Image
}

$a = Get-Content (Join-Path $From 'inventory.txt')
$b = Get-Content (Join-Path $From 'inventory-restored.txt')
$diff = Compare-Object $a $b
if ($diff) {
    Write-Warning 'The restored inventory differs from the exported one:'
    $diff | Format-Table -AutoSize
} else {
    Write-Host 'Restored. The inventory matches the export.'
}
