# Shared helpers for export-db.ps1 and restore-db.ps1. Dot-source only.

function Invoke-Docker {
    & docker @args
    if ($LASTEXITCODE -ne 0) { throw "docker $($args[0]) failed with exit code $LASTEXITCODE" }
}

function Read-EnvValue([string] $Path, [string] $Key) {
    $line = Get-Content $Path | Where-Object { $_ -match "^\s*$Key\s*=" } | Select-Object -Last 1
    if (-not $line) { throw "$Key is not set in $Path" }
    return ($line -split '=', 2)[1].Trim().Trim('"').Trim("'")
}

function Test-Volume([string] $Name) {
    return @(docker volume ls -q) -contains $Name
}

function Assert-Volume([string] $Name) {
    if (-not (Test-Volume $Name)) { throw "Docker volume $Name not found. Check -Project." }
}

function Wait-Postgres([string] $Container, [string] $User, [string] $Db, [int] $Seconds = 120) {
    # Ready over TCP: during first-run init the image's temporary server listens on the socket only,
    # so TCP readiness means the real server is up.
    for ($i = 0; $i -lt $Seconds; $i++) {
        try { docker exec $Container pg_isready -q -h 127.0.0.1 -U $User -d $Db 2>$null } catch { }
        if ($LASTEXITCODE -eq 0) { return }
        Start-Sleep -Seconds 1
    }
    throw "Postgres in $Container was not ready after $Seconds s. See: docker logs $Container"
}

function Write-Inventory([string] $Container, [string] $User, [string] $Db, [string] $Path) {
    # Row counts and the latest migration only. No figure leaves the database.
    $sql = @'
SELECT 'latest migration: ' || COALESCE(
  (SELECT max("MigrationId") FROM quant."__EFMigrationsHistory"), '(none)');
SELECT 'migrations: ' || count(*) FROM quant."__EFMigrationsHistory";
SELECT 'rows ' || schemaname || '.' || relname || ': ' ||
  (xpath('/row/c/text()', query_to_xml(
    format('SELECT count(*) AS c FROM %I.%I', schemaname, relname), false, true, '')))[1]::text
  FROM pg_stat_user_tables ORDER BY schemaname, relname;
'@
    $lines = $sql | docker exec -i $Container psql -U $User -d $Db -X -q -At -v ON_ERROR_STOP=1
    if ($LASTEXITCODE -ne 0) { throw 'inventory query failed' }
    Set-Content -Path $Path -Value $lines -Encoding utf8
}

function Write-Checksums([string] $Folder) {
    Get-ChildItem $Folder -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name
    } | Set-Content -Path (Join-Path $Folder 'SHA256SUMS.txt') -Encoding ascii
}

function Test-Checksums([string] $Folder) {
    $sums = Join-Path $Folder 'SHA256SUMS.txt'
    if (-not (Test-Path $sums)) { throw "No SHA256SUMS.txt in $Folder" }
    foreach ($line in Get-Content $sums) {
        $hash, $name = $line -split '  ', 2
        $actual = (Get-FileHash (Join-Path $Folder $name) -Algorithm SHA256).Hash.ToLower()
        if ($actual -ne $hash) { throw "Checksum mismatch: $name" }
    }
}

# Docker Desktop cannot bind-mount removable drives such as the USB stick, and a failed mount is silent.
# So volumes are archived inside a helper container and moved with docker cp, never with -v <host path>.
function Export-VolumeTar([string] $Volume, [string] $File, [string] $Image) {
    $c = "pqt-tar-$PID"
    Invoke-Docker run -d --name $c -v "${Volume}:/d:ro" $Image sleep 3600 | Out-Null
    try {
        Invoke-Docker exec $c tar czf /tmp/volume.tgz -C /d .
        Invoke-Docker cp "${c}:/tmp/volume.tgz" $File
    } finally { docker rm -f $c | Out-Null }
    if (-not (Test-Path $File)) { throw "Archive of $Volume was not written to $File" }
}

function Import-VolumeTar([string] $Volume, [string] $File, [string] $Image) {
    $c = "pqt-untar-$PID"
    Invoke-Docker run -d --name $c -v "${Volume}:/d" $Image sleep 3600 | Out-Null
    try {
        Invoke-Docker cp $File "${c}:/tmp/volume.tgz"
        Invoke-Docker exec $c tar xzf /tmp/volume.tgz -C /d
    } finally { docker rm -f $c | Out-Null }
}