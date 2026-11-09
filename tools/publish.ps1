param([string]$DotNetPath, [string]$InnoCompilerPath, [switch]$SkipInstaller, [switch]$PackageOnly)
. (Join-Path $PSScriptRoot 'common.ps1')
$dotnet = Resolve-VoiceMiteDotNet $DotNetPath
$version = Get-VoiceMiteVersion
$artifacts = Join-Path $script:RepoRoot 'artifacts'
$portable = Join-Path $artifacts 'VoiceMite-win-x64'
New-Item -ItemType Directory -Force $portable | Out-Null
if ($PackageOnly) {
    if (-not (Test-Path -LiteralPath (Join-Path $portable 'VoiceMite.exe')) -or -not (Test-Path -LiteralPath (Join-Path $portable 'BUILD-INFO.json'))) { throw 'PackageOnly requires a previously published and verified VoiceMite output.' }
    $existingBuild = Get-Content -LiteralPath (Join-Path $portable 'BUILD-INFO.json') -Raw | ConvertFrom-Json
    if ($existingBuild.application -ne 'VoiceMite' -or $existingBuild.version -ne $version) { throw 'The published output version does not match Directory.Build.props.' }
}
else {
    Invoke-VoiceMiteDotNet $dotnet @('publish', (Join-Path $script:RepoRoot 'src/VoiceMite.App/VoiceMite.App.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:DebugType=None', '-o', $portable, '--nologo')
}
foreach ($file in @('README.md','LICENSE','CHANGELOG.md','SECURITY.md')) {
    $sourcePath = Join-Path $script:RepoRoot $file
    if (Test-Path -LiteralPath $sourcePath) { Copy-Item -LiteralPath $sourcePath -Destination $portable -Force }
}
Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'docs') -Destination $portable -Recurse -Force
$licenses = Join-Path $portable 'licenses'
New-Item -ItemType Directory -Force $licenses | Out-Null
$sdkDirectory = Split-Path -Parent $dotnet
foreach ($notice in @('LICENSE.txt','ThirdPartyNotices.txt')) {
    $noticePath = Join-Path $sdkDirectory $notice
    if (Test-Path -LiteralPath $noticePath) { Copy-Item -LiteralPath $noticePath -Destination (Join-Path $licenses ("Microsoft-.NET-" + $notice)) -Force }
}
$runtimeConfig = Get-Content -LiteralPath (Join-Path $portable 'VoiceMite.runtimeconfig.json') -Raw | ConvertFrom-Json
$frameworks = @($runtimeConfig.runtimeOptions.includedFrameworks | ForEach-Object { "$($_.name) $($_.version)" })
$metadata = [ordered]@{ application = 'VoiceMite'; version = $version; architecture = 'win-x64'; selfContained = $true; sdk = (& $dotnet --version); frameworks = $frameworks; builtUtc = [DateTime]::UtcNow.ToString('o'); signed = $false }
if (-not $PackageOnly) { $metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $portable 'BUILD-INFO.json') -Encoding utf8 }
$zipPath = Join-Path $artifacts "VoiceMite-$version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force
if (-not $SkipInstaller) {
    $candidates = @($InnoCompilerPath, (Join-Path $script:RepoRoot '.tools/inno/ISCC.exe'), (Join-Path ${env:ProgramFiles} 'Inno Setup 7/ISCC.exe'), (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'))
    $compiler = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    if (-not $compiler) { throw 'Install Inno Setup 7 from https://jrsoftware.org/isdl.php, pass -InnoCompilerPath, or use -SkipInstaller.' }
    & $compiler '/Qp' ("/DSourceDir=$portable") ("/DOutputDir=$artifacts") ("/DAppVersion=$version") (Join-Path $PSScriptRoot 'VoiceMite.iss')
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed with exit code $LASTEXITCODE." }
}
$sourceZip = Join-Path $artifacts "VoiceMite-$version-source.zip"
Add-Type -AssemblyName System.IO.Compression
if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip -Force }
$sourceArchive = [IO.Compression.ZipFile]::Open($sourceZip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($sourceRoot in @('src','tests','tools','docs','.github')) {
        $sourceRootPath = Join-Path $script:RepoRoot $sourceRoot
        if (-not (Test-Path -LiteralPath $sourceRootPath)) { continue }
        Get-ChildItem -LiteralPath $sourceRootPath -Recurse -File -Force | Where-Object {
            ($_.FullName -notmatch '[\\/](bin|obj|\.git|\.tools|artifacts|secrets)[\\/]') -and ($_.Name -notmatch '(?i)(\.dpapi$|^\.env(?:\.|$)|\.user$|\.log$)')
        } | Sort-Object FullName | ForEach-Object {
            $entryName = [IO.Path]::GetRelativePath($script:RepoRoot, $_.FullName).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sourceArchive, $_.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    foreach ($sourceFile in @('README.md','LICENSE','Directory.Build.props','.gitignore','.gitattributes','.editorconfig','global.json','VoiceMite.sln','AGENTS.md','CONTRIBUTING.md','SECURITY.md','CHANGELOG.md')) {
        $sourcePath = Join-Path $script:RepoRoot $sourceFile
        if (Test-Path -LiteralPath $sourcePath) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sourceArchive, $sourcePath, $sourceFile, [IO.Compression.CompressionLevel]::Optimal) | Out-Null }
    }
}
finally { $sourceArchive.Dispose() }
$releaseNames = @("VoiceMite-$version-source.zip", "VoiceMite-$version-win-x64-portable.zip")
if (-not $SkipInstaller) { $releaseNames += "VoiceMite-Setup-$version-x64.exe" }
$hashes = $releaseNames | Sort-Object | ForEach-Object { $hash = Get-FileHash -LiteralPath (Join-Path $artifacts $_) -Algorithm SHA256; "$($hash.Hash.ToLowerInvariant())  $_" }
$hashes | Set-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Published portable files: $portable"
Write-Output "Published archive: $zipPath"
