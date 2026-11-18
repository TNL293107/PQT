# Shared helpers for export-db.ps1, restore-db.ps1 and preflight.ps1. Dot-source only.
# ASCII only: Windows PowerShell 5.1 reads a BOM-less script as the ANSI code page.

$script:Roles = @('desktop', 'laptop')

function Invoke-Docker {
    & docker @args
    if ($LASTEXITCODE -ne 0) { throw "docker $($args[0]) failed with exit code $LASTEXITCODE" }
}

# Runs a native command with its stderr dropped. Windows PowerShell 5.1 turns redirected native
# stderr into a terminating error under $ErrorActionPreference = 'Stop', so it is relaxed here.
function Invoke-Quiet {
    # Always an array: splatting a single string would pass it wrongly (dotnet --list-sdks).
    $command = $args[0]
    $rest = @($args | Select-Object -Skip 1)
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $command @rest 2>$null
        return [pscustomobject]@{ Output = @($output); Code = $LASTEXITCODE }
    } finally {
        $ErrorActionPreference = $saved
    }
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

# The dumps hold IDS figures, role password hashes and possibly vendor lines. They must never sit
# inside a git work tree, where one `git add` would commit them.
function Assert-StoreOutsideRepo([string] $Store) {
    $message = "Keep dumps outside any repository (the stick's db folder)."
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $probe = Invoke-Quiet git -C $Store rev-parse --show-toplevel
        if ($probe.Code -eq 0) { throw "-Store $Store is inside the git work tree $($probe.Output[0]). $message" }
    }
    # git refuses a work tree it calls "dubious ownership" (a stick's NTFS or exFAT owner), and then
    # rev-parse fails as if there were none. So look for .git (a folder, or a worktree's file) too.
    $dir = (Resolve-Path $Store).Path
    while ($dir) {
        if (Test-Path -LiteralPath (Join-Path $dir '.git')) { throw "-Store $Store is inside the git work tree $dir. $message" }
        $parent = Split-Path $dir -Parent
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
}

# The yyyyMMdd-HHmm stamp a folder name starts with, or $null. Stamps compare as strings.
function Get-FolderStamp([string] $Folder) {
    $name = Split-Path $Folder -Leaf
    if ($name -match '^(\d{8}-\d{4})-') { return $Matches[1] }
    return $null
}

function Get-FileSha256([string] $Path) {
    return (Get-FileHash $Path -Algorithm SHA256).Hash.ToLower()
}

# The image's content address, so two machines can tell they ran the same Postgres build. A tag
# such as postgres:17-alpine moves; the digest does not.
function Get-ImageDigest([string] $Image) {
    # No double quotes in the template: Windows PowerShell 5.1 strips them from native arguments.
    $probe = Invoke-Quiet docker image inspect $Image --format '{{range .RepoDigests}}{{.}} {{end}}|{{.Id}}'
    if ($probe.Code -ne 0 -or -not $probe.Output) { return $null }
    $repoDigests, $id = ([string] $probe.Output[0]) -split '\|', 2
    $first = @($repoDigests.Trim() -split ' ' | Where-Object { $_ })
    if ($first.Count -gt 0) { return $first[0] }
    return $id.Trim()
}

# --- HOLDER.txt: which machine holds the one live database --------------------------------------
#
# holder       desktop | laptop | in-transit
# since        UTC time of the last change
# folder       the export folder the holder was restored from, or the one in transit
# dump_sha256  that folder's pqt.dump
# image        the Postgres image digest of the machine that wrote the folder
# commit       the repository commit the writing machine ran

function Get-HolderPath([string] $Store) { return (Join-Path $Store 'HOLDER.txt') }

function Read-Holder([string] $Store) {
    $path = Get-HolderPath $Store
    if (-not (Test-Path $path)) { return $null }
    $holder = @{}
    foreach ($line in Get-Content $path) {
        if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
        $key, $value = $line -split '=', 2
        $holder[$key.Trim()] = $value.Trim()
    }
    return $holder
}

function Write-Holder([string] $Store, [hashtable] $Holder) {
    $Holder['since'] = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    $lines = @('# Which machine holds the live PQT database. Written by tools/db; do not edit by hand.')
    foreach ($key in @('holder', 'since', 'folder', 'dump_sha256', 'image', 'commit')) {
        $lines += '{0}={1}' -f $key, $Holder[$key]
    }
    Set-Content -Path (Get-HolderPath $Store) -Value $lines -Encoding ascii
}

function Get-FolderRole([string] $Folder) {
    $name = Split-Path $Folder -Leaf
    if ($name -match '^\d{8}-\d{4}-(desktop|laptop)(-|$)') { return $Matches[1] }
    return $null
}

function Get-RepoCommit([string] $Repo) {
    if (-not $Repo -or -not (Test-Path $Repo)) { return '' }
    $probe = Invoke-Quiet git -C $Repo rev-parse HEAD
    if ($probe.Code -ne 0) { return '' }
    return [string] $probe.Output[0]
}

# --- Postgres --------------------------------------------------------------------------------------

function Wait-Postgres([string] $Container, [string] $User, [string] $Db, [int] $Seconds = 120) {
    # Ready over TCP: during first-run init the image's temporary server listens on the socket only,
    # so TCP readiness means the real server is up.
    for ($i = 0; $i -lt $Seconds; $i++) {
        $probe = Invoke-Quiet docker exec $Container pg_isready -q -h 127.0.0.1 -U $User -d $Db
        if ($probe.Code -eq 0) { return }
        Start-Sleep -Seconds 1
    }
    throw "Postgres in $Container was not ready after $Seconds s. See: docker logs $Container"
}

# The inventory: the latest migration, each table's row count and each table's content hash.
# A content hash is the md5 of every row's text, in one fixed order and one fixed rendering, so two
# databases with the same counts but different rows no longer look equal. A hash shows no value.
$script:InventorySql = @'
SELECT 'latest migration: ' || COALESCE(
  (SELECT max("MigrationId") FROM quant."__EFMigrationsHistory"), '(none)');
SELECT 'migrations: ' || count(*) FROM quant."__EFMigrationsHistory";
SELECT 'rows ' || schemaname || '.' || relname || ': ' ||
  (xpath('/row/c/text()', query_to_xml(
    format('SELECT count(*) AS c FROM %I.%I', schemaname, relname), false, true, '')))[1]::text
  FROM pg_stat_user_tables ORDER BY schemaname, relname;
SELECT 'hash ' || schemaname || '.' || relname || ': ' ||
  (xpath('/row/h/text()', query_to_xml(
    format('SELECT md5(COALESCE(string_agg(t::text, E''\n'' ORDER BY t::text COLLATE "C"), '''')) AS h FROM %I.%I t',
           schemaname, relname), false, true, '')))[1]::text
  FROM pg_stat_user_tables ORDER BY schemaname, relname;
'@

# One rendering for every value, whatever the server's defaults are.
$script:InventoryOptions = '-c TimeZone=UTC -c DateStyle=ISO,YMD -c IntervalStyle=postgres -c extra_float_digits=3 -c bytea_output=hex'

function Get-Inventory([string] $Container, [string] $User, [string] $Db) {
    $lines = $script:InventorySql |
        docker exec -i -e "PGOPTIONS=$script:InventoryOptions" $Container psql -U $User -d $Db -X -q -At -v ON_ERROR_STOP=1
    if ($LASTEXITCODE -ne 0) { throw 'inventory query failed' }
    return @($lines)
}

function Write-Inventory([string] $Container, [string] $User, [string] $Db, [string] $Path) {
    Set-Content -Path $Path -Value (Get-Inventory $Container $User $Db) -Encoding utf8
}

function ConvertTo-InventoryMap([string[]] $Lines) {
    $map = [ordered]@{}
    foreach ($line in $Lines) {
        $text = $line.TrimStart([char] 0xFEFF)
        $at = $text.LastIndexOf(': ')
        if ($at -gt 0) { $map[$text.Substring(0, $at)] = $text.Substring($at + 2) }
    }
    return $map
}

# Compares two inventories. Returns the differing keys. An inventory written before content hashes
# existed has no 'hash ' lines; then only the counts can be compared, and HashesCompared is false.
function Compare-Inventory([string[]] $Expected, [string[]] $Actual) {
    $a = ConvertTo-InventoryMap $Expected
    $b = ConvertTo-InventoryMap $Actual
    $hashes = @($a.Keys | Where-Object { $_ -like 'hash *' }).Count -gt 0
    $differences = @()
    foreach ($key in $a.Keys) {
        if ($a[$key] -ne $b[$key]) { $differences += $key }
    }
    foreach ($key in $b.Keys) {
        if (-not $a.Contains($key) -and ($hashes -or $key -notlike 'hash *')) { $differences += $key }
    }
    return [pscustomobject]@{ Differences = $differences; HashesCompared = $hashes }
}

# --- Files and volumes -----------------------------------------------------------------------------

function Write-Checksums([string] $Folder) {
    Get-ChildItem $Folder -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
        '{0}  {1}' -f (Get-FileSha256 $_.FullName), $_.Name
    } | Set-Content -Path (Join-Path $Folder 'SHA256SUMS.txt') -Encoding ascii
}

function Test-Checksums([string] $Folder) {
    $sums = Join-Path $Folder 'SHA256SUMS.txt'
    if (-not (Test-Path $sums)) { throw "No SHA256SUMS.txt in $Folder" }
    foreach ($line in Get-Content $sums) {
        $hash, $name = $line -split '  ', 2
        $actual = Get-FileSha256 (Join-Path $Folder $name)
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
    } finally { Invoke-Quiet docker rm -f $c | Out-Null }
    if (-not (Test-Path $File)) { throw "Archive of $Volume was not written to $File" }
}

function Import-VolumeTar([string] $Volume, [string] $File, [string] $Image) {
    $c = "pqt-untar-$PID"
    Invoke-Docker run -d --name $c -v "${Volume}:/d" $Image sleep 3600 | Out-Null
    try {
        Invoke-Docker cp $File "${c}:/tmp/volume.tgz"
        Invoke-Docker exec $c tar xzf /tmp/volume.tgz -C /d
    } finally { Invoke-Quiet docker rm -f $c | Out-Null }
}
