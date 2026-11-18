<#
.SYNOPSIS
    Fills the pqt-data volume from data/fixtures and data/schemas, for
    docker-compose.portable.yml.

.DESCRIPTION
    Docker Desktop cannot bind-mount every drive (a USB stick, for one), and a
    failed bind mount leaves the backend with an empty /app/data. The portable
    overlay mounts a named volume there instead; this script fills it.

    - Copies data/fixtures and data/schemas only. data/tmp holds review notes
      and anything else under data/ is not the backend's input, so neither is
      copied.
    - Empties the volume first, so a file deleted from the checkout does not
      linger in it.
    - Writes SHA256SUMS at the volume root (sha256sum -c format, paths relative
      to the root) and checks it inside the volume before it finishes.
    - Never starts the stack, and prints no file content.

.PARAMETER Repo
    The checkout. Defaults to the repository this script is in.

.PARAMETER Volume
    The volume to fill. Defaults to the name docker-compose.portable.yml gives it.

.PARAMETER Image
    The helper image. It needs sh and sha256sum.

.EXAMPLE
    pwsh tools/stack/seed-data.ps1
    docker compose -f docker-compose.yml -f docker-compose.portable.yml up -d postgres redis backend
#>
[CmdletBinding()]
param(
    [string] $Repo,
    [string] $Volume = 'personal-quant-terminal_pqt-data',
    # The stack's own Postgres image: already present wherever the stack runs, so an offline
    # machine needs no pull. It is alpine-based, so busybox tar and sha256sum are there.
    [string] $Image = 'postgres:17-alpine'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Resolved here, not in the param block: Windows PowerShell 5.1 leaves
# $PSScriptRoot empty while it binds parameter defaults.
if (-not $Repo) { $Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
$Repo = (Resolve-Path $Repo).Path

# What the backend reads under /app/data. Nothing else is copied.
$Folders = @('fixtures', 'schemas')

function Invoke-Docker {
    & docker @args
    if ($LASTEXITCODE -ne 0) { throw "docker $($args -join ' ') failed with exit code $LASTEXITCODE." }
}

$data = Join-Path $Repo 'data'
foreach ($folder in $Folders) {
    if (-not (Test-Path (Join-Path $data $folder) -PathType Container)) {
        throw "No data\$folder under $Repo. Pass -Repo with the checkout to seed from."
    }
}

# The backend mounts the volume read-only, so copying under it is safe, but it
# keeps reading the old files until it restarts. Say so rather than refuse.
$users = & docker ps --filter "volume=$Volume" --format '{{.Names}}'
if ($users) {
    Write-Warning "Running containers use $Volume ($($users -join ', ')). Restart the backend after seeding."
}

# Hashes are taken on the host, from the files that are about to be copied.
$lines = foreach ($folder in $Folders) {
    Get-ChildItem -Path (Join-Path $data $folder) -Recurse -File |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($data.Length + 1).Replace('\', '/')
            '{0}  {1}' -f (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant(), $relative
        }
}

$sums = Join-Path ([System.IO.Path]::GetTempPath()) "pqt-seed-$([guid]::NewGuid().ToString('N')).sha256"
# LF and no BOM: sha256sum inside the volume reads this file.
[System.IO.File]::WriteAllText($sums, (($lines -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))

$helper = "pqt-seed-$([guid]::NewGuid().ToString('N').Substring(0, 8))"

try {
    Invoke-Docker volume create $Volume | Out-Null
    Invoke-Docker run -d --name $helper -v "${Volume}:/target" $Image sleep 600 | Out-Null

    Invoke-Docker exec $helper sh -c 'find /target -mindepth 1 -delete'

    foreach ($folder in $Folders) {
        Invoke-Docker cp (Join-Path $data $folder) "${helper}:/target/$folder"
    }

    Invoke-Docker cp $sums "${helper}:/target/SHA256SUMS"
    Invoke-Docker exec $helper sh -c 'cd /target && sha256sum -c -s SHA256SUMS'

    Write-Output "Seeded $Volume from $data with $($lines.Count) files ($($Folders -join ', ')). SHA256SUMS checks out."
}
finally {
    & docker rm -f $helper 2>$null | Out-Null
    Remove-Item -LiteralPath $sums -ErrorAction SilentlyContinue
}
