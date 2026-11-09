param([string]$InnoCompilerPath, [switch]$Ui)
. (Join-Path $PSScriptRoot 'common.ps1')
if (-not $InnoCompilerPath) { $InnoCompilerPath = Join-Path $script:RepoRoot '.tools/inno/ISCC.exe' }
if (-not (Test-Path -LiteralPath $InnoCompilerPath -PathType Leaf)) { throw 'Specify the verified Inno Setup compiler.' }
$legacySource = Join-Path $script:RepoRoot 'artifacts/VoiceInk-0.1.0-source.zip'
$legacyPublish = Join-Path $script:RepoRoot 'artifacts/VoiceInk-win-x64'
$currentPublish = Join-Path $script:RepoRoot 'artifacts/VoiceMite-win-x64'
foreach ($required in @($legacySource, (Join-Path $legacyPublish 'VoiceInk.exe'), (Join-Path $currentPublish 'VoiceMite.exe'))) { if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw 'Verified legacy source/runtime and renamed runtime are required.' } }
$id = [Guid]::NewGuid()
$suffix = $id.ToString('N').Substring(0,10)
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot '.tools'))
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $temporaryRoot ('installer-compat-' + $id.ToString('N'))))
if (-not $fixtureRoot.StartsWith($temporaryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected installer fixture directory.' }
New-Item -ItemType Directory -Force $fixtureRoot | Out-Null
$productionRegistration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B09FDC83-04D7-4515-94D6-54776D0E9196}_is1'
function Get-ProtectedRegistrySnapshot {
    if (-not (Test-Path -LiteralPath $productionRegistration)) { return 'absent' }
    $properties = (Get-ItemProperty -LiteralPath $productionRegistration).PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' } | Sort-Object Name
    return (($properties | ForEach-Object { [ordered]@{name=$_.Name;value=$_.Value} }) | ConvertTo-Json -Depth 6 -Compress)
}
function Get-ProtectedShortcutSnapshot {
    $items = foreach ($folder in @([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs), [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory))) {
        foreach ($name in @('VoiceInk','VoiceMite')) {
            $path = Join-Path $folder ($name + '.lnk')
            if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } else { 'absent' }
        }
    }
    return ($items -join '|')
}
$registryBefore = Get-ProtectedRegistrySnapshot
$shortcutsBefore = Get-ProtectedShortcutSnapshot
$legacyShortcut = 'VoiceInk-Compat-' + $suffix
$currentShortcut = 'VoiceMite-Compat-' + $suffix
$output = Join-Path $fixtureRoot 'setup'
New-Item -ItemType Directory -Force $output | Out-Null
$evidence = Join-Path $script:RepoRoot 'docs/evidence/rebrand'
New-Item -ItemType Directory -Force $evidence | Out-Null
try {
    Add-Type -AssemblyName System.IO.Compression
    $archive = [IO.Compression.ZipFile]::OpenRead($legacySource)
    try {
        $entry = $archive.GetEntry('tools/VoiceInk.iss')
        if (-not $entry) { throw 'Legacy source archive does not contain its installer definition.' }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $legacy = $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
    $current = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VoiceMite.iss') -Raw
    # Only installer identity/shortcut names differ; published application binaries remain exact.
    $appIdLine = 'AppId={{' + $id.ToString().ToUpperInvariant() + '}'
    $legacy = [regex]::Replace($legacy, '(?m)^AppId=.*$', $appIdLine)
    $current = [regex]::Replace($current, '(?m)^AppId=.*$', $appIdLine)
    foreach ($folder in @('{autoprograms}', '{autodesktop}')) {
        $legacy = $legacy.Replace(($folder + '\VoiceInk"'), ($folder + '\' + $legacyShortcut + '"'))
        $current = $current.Replace(($folder + '\VoiceMite"'), ($folder + '\' + $currentShortcut + '"'))
        $current = $current.Replace(($folder + '\VoiceInk.lnk"'), ($folder + '\' + $legacyShortcut + '.lnk"'))
    }
    foreach ($definition in @($legacy, $current)) {
        if (-not [regex]::IsMatch($definition, ('(?m)^' + [regex]::Escape($appIdLine) + '\r?$'))) { throw 'The temporary installer AppId was not replaced exactly.' }
        foreach ($folder in @('{autoprograms}', '{autodesktop}')) {
            foreach ($shortcut in @('VoiceInk','VoiceInk.lnk','VoiceMite','VoiceMite.lnk')) {
                if ($definition.Contains(('"' + $folder + '\' + $shortcut + '"'))) { throw 'A temporary installer still references a real product shortcut.' }
            }
        }
    }
    $legacyScript = Join-Path $fixtureRoot 'Legacy.iss'
    $currentScript = Join-Path $fixtureRoot 'Current.iss'
    Set-Content -LiteralPath $legacyScript -Value $legacy -Encoding utf8
    Set-Content -LiteralPath $currentScript -Value $current -Encoding utf8
    & $InnoCompilerPath '/Qp' ("/DSourceDir=$legacyPublish") ("/DOutputDir=$output") '/DAppVersion=0.1.0' $legacyScript
    if ($LASTEXITCODE -ne 0) { throw 'Legacy identity-fixture compilation failed.' }
    & $InnoCompilerPath '/Qp' ("/DSourceDir=$currentPublish") ("/DOutputDir=$output") ("/DAppVersion=$(Get-VoiceMiteVersion)") $currentScript
    if ($LASTEXITCODE -ne 0) { throw 'Renamed identity-fixture compilation failed.' }
    $legacySetup = Join-Path $output 'VoiceInk-Setup-0.1.0-x64.exe'
    $currentSetup = Join-Path $output ("VoiceMite-Setup-$(Get-VoiceMiteVersion)-x64.exe")
    $profile = Join-Path $fixtureRoot 'synthetic-profile'
    $testProfiles = Join-Path $fixtureRoot 'ui-test-profiles'
    & (Join-Path $PSScriptRoot 'smoke-install.ps1') -InstallerPath $currentSetup -AppId $id -ProfileDirectory $profile -TestProfileRoot $testProfiles -Ui:$Ui
    & (Join-Path $PSScriptRoot 'smoke-upgrade.ps1') -LegacyInstallerPath $legacySetup -InstallerPath $currentSetup -AppId $id -LegacyShortcutName $legacyShortcut -CurrentShortcutName $currentShortcut -ProfileDirectory $profile -TestProfileRoot $testProfiles -Ui:$Ui
    $registryUnchanged = (Get-ProtectedRegistrySnapshot) -eq $registryBefore
    $shortcutsUnchanged = (Get-ProtectedShortcutSnapshot) -eq $shortcutsBefore
    [ordered]@{ classification='isolated-installer-identity'; productionAppIdTested=$false; actualPublishedBinariesUsed=$true; identityChanges=@('temporary AppId','temporary shortcut names','temporary installer output directory'); realUserRegistrationUnchanged=$registryUnchanged; realUserShortcutsUnchanged=$shortcutsUnchanged; realUserProfilesWritten=$false; realCredentialsAccessed=$false; status=$(if($registryUnchanged -and $shortcutsUnchanged){'passed'}else{'failed'}) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'isolated-installer-identity.json') -Encoding utf8
    if (-not $registryUnchanged -or -not $shortcutsUnchanged) { throw 'Protected production installation changed unexpectedly.' }
}
finally {
    # This generated directory is scoped under .tools and contains no real user profile.
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}
Write-Output 'Isolated installer identity checks passed. The existing production installation was not replaced.'
