# Raven Portable: only manages a launcher-owned junction at %LOCALAPPDATA%\Raven.
# Never reassign or erase an existing ordinary directory or someone else's junction.
param(
    [Parameter(Mandatory=$true)][ValidateSet('mount','unmount')][string]$Mode,
    [Parameter(Mandatory=$true)][string]$HostPath,
    [Parameter(Mandatory=$true)][string]$TargetPath,
    [Parameter(Mandatory=$true)][string]$MarkerPath
)
$ErrorActionPreference='Stop'
function Normal([string]$v) { return [IO.Path]::GetFullPath($v).TrimEnd('\') }
$hostPath2 = Normal $HostPath
$targetPath2 = Normal $TargetPath
$markerPath2 = Normal $MarkerPath
function ItemPresent([string]$path) {
    try { $null = Get-Item -LiteralPath $path -Force -ErrorAction Stop; return $true }
    catch { return $false }
}
function JunctionTarget([string]$path) {
    $entry = Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if ($entry.LinkType -ne 'Junction') { throw 'Host Raven folder is not a junction. Existing data must be preserved.' }
    $value = @($entry.Target)[0]
    if (-not $value) { throw 'Windows could not verify the junction target; refusing cleanup.' }
    return (Normal ([string]$value))
}
function HasValidOwner() {
    if (-not (Test-Path -LiteralPath $markerPath2 -PathType Leaf)) { return $false }
    try {
        $owner = Get-Content -LiteralPath $markerPath2 -Raw | ConvertFrom-Json
        if (-not $owner.Host -or -not $owner.Target) { return $false }
        if ((Normal ([string]$owner.Host)) -ine $hostPath2) { return $false }
        if ((Normal ([string]$owner.Target)) -ine (JunctionTarget $hostPath2)) { return $false }
        return $true
    } catch { return $false }
}
if ($Mode -eq 'mount') {
    if (ItemPresent $hostPath2) {
        if (-not (HasValidOwner)) { throw 'A pre-existing Raven AppData folder/junction was detected. Refusing to overwrite it.' }
        # An interrupted previous run of this exact portable profile. Caller checks no Raven process is running.
        [IO.Directory]::Delete($hostPath2)
    }
    $parent = Split-Path -Parent $markerPath2
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    New-Item -ItemType Directory -Force -Path $targetPath2 | Out-Null
    @{ Host=$hostPath2; Target=$targetPath2; CreatedUtc=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json |
        Set-Content -LiteralPath $markerPath2 -Encoding UTF8
    try {
        New-Item -ItemType Junction -Path $hostPath2 -Target $targetPath2 -ErrorAction Stop | Out-Null
        if ((JunctionTarget $hostPath2) -ine $targetPath2) { throw 'The new Raven junction target did not match.' }
    } catch {
        if ((ItemPresent $hostPath2) -and (HasValidOwner)) { [IO.Directory]::Delete($hostPath2) }
        throw
    }
    Write-Output 'MOUNTED: Raven LocalAppData points to portable Data.'
} else {
    if (-not (ItemPresent $hostPath2)) {
        if (Test-Path -LiteralPath $markerPath2) { Remove-Item -LiteralPath $markerPath2 -Force }
        Write-Output 'NO HOST JUNCTION: nothing to remove.'
    } elseif (-not (HasValidOwner)) {
        throw 'Refusing to remove a directory not verified as this portable session junction.'
    } else {
        [IO.Directory]::Delete($hostPath2) # NON-RECURSIVE: deletes junction only, not its portable target
        if (Test-Path -LiteralPath $markerPath2) { Remove-Item -LiteralPath $markerPath2 -Force }
        Write-Output 'UNMOUNTED: verified portable junction removed safely.'
    }
}
