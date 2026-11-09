#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$script:RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
function Get-VoiceMiteVersion {
    [xml]$properties = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'Directory.Build.props') -Raw
    $version = [string]$properties.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Directory.Build.props must define a safe semantic Version.' }
    return $version
}
function Get-VoiceMiteProfileDirectory {
    # Match Core.ProfilePaths: preserve the existing profile without merging or copying secrets.
    $current = Join-Path $env:LOCALAPPDATA 'VoiceMite'
    $legacy = Join-Path $env:LOCALAPPDATA 'VoiceInk'
    if (Test-Path -LiteralPath $current -PathType Container) { return $current }
    if (Test-Path -LiteralPath $legacy -PathType Container) { return $legacy }
    return $current
}
function Resolve-VoiceMiteDotNet {
    param([string]$RequestedPath)
    $candidates = @($RequestedPath, (Join-Path $env:LOCALAPPDATA 'VoiceMiteDev/dotnet/dotnet.exe'), (Join-Path $env:LOCALAPPDATA 'VoiceInkDev/dotnet/dotnet.exe'))
    $systemDotNet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($systemDotNet) { $candidates += $systemDotNet.Source }
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            $sdks = & $candidate --list-sdks
            $matching = @($sdks | Where-Object { $_ -match '^10\.0\.4\d\d\s' -and [Version](($_ -split '\s')[0]) -ge [Version]'10.0.403' })
            if ($LASTEXITCODE -eq 0 -and $matching.Count -gt 0) { return $candidate }
        }
    }
    throw 'The .NET SDK 10.0.403 (or later 10.0.4xx patch) is required by global.json. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 or pass -DotNetPath.'
}
function Invoke-VoiceMiteDotNet {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE." }
}
