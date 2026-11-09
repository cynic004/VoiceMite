param([string]$LegacyInstallerPath, [string]$InstallerPath, [switch]$Ui, [Guid]$AppId = 'B09FDC83-04D7-4515-94D6-54776D0E9196', [ValidatePattern('^[A-Za-z0-9.-]+$')][string]$LegacyShortcutName = 'VoiceInk', [ValidatePattern('^[A-Za-z0-9.-]+$')][string]$CurrentShortcutName = 'VoiceMite', [string]$ProfileDirectory, [string]$TestProfileRoot)
. (Join-Path $PSScriptRoot 'common.ps1')
if (-not $LegacyInstallerPath) { $LegacyInstallerPath = Join-Path $script:RepoRoot 'artifacts/VoiceInk-Setup-0.1.0-x64.exe' }
if (-not $InstallerPath) { $InstallerPath = Join-Path $script:RepoRoot ("artifacts/VoiceMite-Setup-" + (Get-VoiceMiteVersion) + '-x64.exe') }
foreach ($candidate in @($LegacyInstallerPath, $InstallerPath)) { if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw 'Both the verified legacy and current installers are required.' } }
$registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{' + $AppId.ToString().ToUpperInvariant() + '}_is1'
if (Test-Path -LiteralPath $registration) { throw 'An existing VoiceInk/VoiceMite installation is registered. The upgrade smoke will not replace it.' }
try { $active = [Threading.Mutex]::OpenExisting('Local\VoiceInk.Desktop.v1'); $active.Dispose(); throw 'Exit VoiceInk and VoiceMite before this opt-in desktop test.' }
catch [Threading.WaitHandleCannotBeOpenedException] { }
$programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$legacyLinks = @((Join-Path $programs ($LegacyShortcutName + '.lnk')), (Join-Path $desktop ($LegacyShortcutName + '.lnk')))
$currentLinks = @((Join-Path $programs ($CurrentShortcutName + '.lnk')), (Join-Path $desktop ($CurrentShortcutName + '.lnk')))
foreach ($shortcut in ($legacyLinks + $currentLinks)) { if (Test-Path -LiteralPath $shortcut) { throw 'An existing product shortcut is present. The smoke will not overwrite it.' } }
$id = [Guid]::NewGuid().ToString('N')
$installDir = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot ('artifacts/upgrade-smoke-' + $id)))
$allowedPrefix = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $installDir.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected upgrade test directory.' }
$profile = if ($ProfileDirectory) { [IO.Path]::GetFullPath($ProfileDirectory) } else { Get-VoiceMiteProfileDirectory }
$profileExisted = Test-Path -LiteralPath $profile -PathType Container
New-Item -ItemType Directory -Force $profile | Out-Null
$marker = Join-Path $profile ('upgrade-test-' + $id + '.marker')
Set-Content -LiteralPath $marker -Value 'Synthetic upgrade-retention marker. No personal data or credentials.' -Encoding ascii
$markerHash = (Get-FileHash -LiteralPath $marker).Hash
$evidence = Join-Path $script:RepoRoot 'docs/evidence/rebrand'
New-Item -ItemType Directory -Force $evidence | Out-Null
$results = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [bool]$Passed, [string]$Detail = '') {
    $results.Add([ordered]@{ name = $Name; status = $(if ($Passed) {'passed'} else {'failed'}); detail = $Detail })
    if (-not $Passed) { throw "Failed: $Name" }
}
function Run-Setup([string]$Path, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    $process.WaitForExit(); $process.Refresh(); return $process.ExitCode
}
try {
    $common = @('/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/LANG=english','/TASKS=desktopicon')
    $legacyExit = Run-Setup $LegacyInstallerPath ($common + @('/DIR="' + $installDir + '"','/LOG="' + (Join-Path $script:RepoRoot ('artifacts/upgrade-legacy-' + $id + '.log')) + '"'))
    Check 'Legacy VoiceInk installs into isolated test directory' ($legacyExit -eq 0 -and (Test-Path -LiteralPath (Join-Path $installDir 'VoiceInk.exe'))) ("exit=$legacyExit")
    Check 'Legacy Start Menu and desktop shortcuts exist' (@($legacyLinks | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 2)
    # Omitting /DIR tests Inno's same-AppId previous-directory behavior.
    $upgradeExit = Run-Setup $InstallerPath ($common + @('/LOG="' + (Join-Path $script:RepoRoot ('artifacts/upgrade-current-' + $id + '.log')) + '"'))
    Check 'Renamed installer upgrades without requesting another directory' ($upgradeExit -eq 0 -and (Test-Path -LiteralPath (Join-Path $installDir 'VoiceMite.exe'))) ("exit=$upgradeExit")
    Check 'Same AppId preserves the previous install directory' (([IO.Path]::GetFullPath([string](Get-ItemProperty -LiteralPath $registration).InstallLocation)).TrimEnd('\') -eq $installDir.TrimEnd('\'))
    $legacyFiles = @('VoiceInk.exe','VoiceInk.dll','VoiceInk.deps.json','VoiceInk.runtimeconfig.json','VoiceInk.Core.dll','VoiceInk.Windows.dll')
    Check 'All six known legacy runtime files are removed' (@($legacyFiles | Where-Object { Test-Path -LiteralPath (Join-Path $installDir $_) }).Count -eq 0)
    Check 'Both known legacy shortcuts are removed' (@($legacyLinks | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 0)
    Check 'Renamed Start Menu and desktop shortcuts exist' (@($currentLinks | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 2)
    Check 'Upgrade leaves profile retention marker unchanged' ((Test-Path -LiteralPath $marker) -and (Get-FileHash -LiteralPath $marker).Hash -eq $markerHash)
    Check 'Installed renamed DLL matches verified published DLL' ((Get-FileHash -LiteralPath (Join-Path $installDir 'VoiceMite.dll')).Hash -eq (Get-FileHash -LiteralPath (Join-Path $script:RepoRoot 'artifacts/VoiceMite-win-x64/VoiceMite.dll')).Hash)
    $reinstallExit = Run-Setup $InstallerPath ($common + @('/LOG="' + (Join-Path $script:RepoRoot ('artifacts/upgrade-reinstall-' + $id + '.log')) + '"'))
    Check 'Renamed same-version reinstall succeeds and preserves data' ($reinstallExit -eq 0 -and (Test-Path -LiteralPath $marker) -and (Get-FileHash -LiteralPath $marker).Hash -eq $markerHash) ("exit=$reinstallExit")
    if ($Ui) {
        $uiOutput = Join-Path $script:RepoRoot ('artifacts/ui-smoke-upgrade-' + $id)
        $started = [DateTime]::UtcNow
        $uiArguments = @('--ui-smoke', ('"' + $uiOutput + '"'))
        if ($TestProfileRoot) { $uiArguments += @('--test-profile-root', ('"' + [IO.Path]::GetFullPath($TestProfileRoot) + '"')) }
        $process = Start-Process -FilePath (Join-Path $installDir 'VoiceMite.exe') -ArgumentList $uiArguments -WindowStyle Hidden -Wait -PassThru
        $process.WaitForExit(); $process.Refresh()
        $report = Join-Path $uiOutput 'ui-tests.json'
        $checks = @()
        if (Test-Path -LiteralPath $report) { $checks = @(Get-Content -LiteralPath $report -Raw | ConvertFrom-Json) }
        $passed = $process.ExitCode -eq 0 -and $checks.Count -gt 0 -and @($checks | Where-Object status -ne passed).Count -eq 0 -and (Get-Item -LiteralPath $report).LastWriteTimeUtc -ge $started
        Check 'Upgraded executable starts with all own-window UI checks passing' $passed ("exit=$($process.ExitCode); checks=$($checks.Count); repeatedCasesNotAdditional=true")
        Copy-Item -LiteralPath $report -Destination (Join-Path $evidence 'upgrade-ui-tests.json') -Force
    }
    $uninstallExit = Run-Setup (Join-Path $installDir 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
    Check 'Renamed silent uninstaller exits successfully' ($uninstallExit -eq 0) ("exit=$uninstallExit")
    Check 'Renamed silent uninstall preserves the profile marker' ((Test-Path -LiteralPath $marker) -and (Get-FileHash -LiteralPath $marker).Hash -eq $markerHash)
    Check 'Renamed executable and registration are removed' (-not (Test-Path -LiteralPath (Join-Path $installDir 'VoiceMite.exe')) -and -not (Test-Path -LiteralPath $registration))
    Check 'Renamed shortcuts are removed by uninstall' (@($currentLinks | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 0)
}
finally {
    # Cleanup is restricted to the uninstaller inside this exact generated directory.
    $uninstaller = Join-Path $installDir 'unins000.exe'
    if ((Test-Path -LiteralPath $registration) -and (Test-Path -LiteralPath $uninstaller)) {
        $registered = [IO.Path]::GetFullPath([string](Get-ItemProperty -LiteralPath $registration).InstallLocation).TrimEnd('\')
        if ($registered -eq $installDir.TrimEnd('\')) { [void](Run-Setup $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')) }
    }
    if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker -Force }
    $results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'upgrade-tests.json') -Encoding utf8
    if (-not $profileExisted -and (Test-Path -LiteralPath $profile -PathType Container) -and @(Get-ChildItem -LiteralPath $profile -Force).Count -eq 0) { Remove-Item -LiteralPath $profile -Force }
}
Write-Output "Upgrade checks: $($results.Count) passed. Only isolated installers and synthetic retention data were used."
