param([string]$DotNetPath, [ValidateSet('Debug','Release')][string]$Configuration = 'Release')
. (Join-Path $PSScriptRoot 'common.ps1')
$dotnet = Resolve-VoiceMiteDotNet $DotNetPath
Invoke-VoiceMiteDotNet $dotnet @('build', (Join-Path $script:RepoRoot 'src/VoiceMite.App/VoiceMite.App.csproj'), '-c', $Configuration, '--nologo')
