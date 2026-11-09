param([string]$InstallerPath, [switch]$Ui)
. (Join-Path $PSScriptRoot 'common.ps1')
if (-not $InstallerPath) { $InstallerPath = Join-Path $script:RepoRoot ("artifacts/VoiceMite-Setup-" + (Get-VoiceMiteVersion) + '-x64.exe') }
$InstallerPath = [IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $InstallerPath)) { throw 'Build the installer first.' }
$registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B09FDC83-04D7-4515-94D6-54776D0E9196}_is1'
if (Test-Path -LiteralPath $registration) { throw 'An existing VoiceMite installation is registered. Smoke testing will not replace it.' }
try {
    $active = [Threading.Mutex]::OpenExisting('Local\VoiceInk.Desktop.v1')
    $active.Dispose()
    throw 'Exit VoiceMite before testing its installer.'
}
catch [Threading.WaitHandleCannotBeOpenedException] { }

$results = [Collections.Generic.List[object]]::new()
$testId = [Guid]::NewGuid().ToString('N')
$installDir = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot ("artifacts/install-smoke-" + $testId)))
$expectedPrefix = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $installDir.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected test directory.' }
$dataDir = Get-VoiceMiteProfileDirectory
$dataDirExisted = Test-Path -LiteralPath $dataDir -PathType Container
$marker = Join-Path $dataDir ("installer-test-" + $testId + '.marker')
New-Item -ItemType Directory -Force $dataDir | Out-Null
Set-Content -LiteralPath $marker -Value 'Installer retention smoke test; no personal data.' -Encoding ascii
function Check([string]$Name, [bool]$Condition, [string]$Detail = '') {
    $results.Add([ordered]@{ name = $Name; status = $(if ($Condition) {'passed'} else {'failed'}); detail = $Detail })
    if (-not $Condition) { throw "Failed: $Name" }
}
try {
    $arguments = @('/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/LANG=english',('/DIR="' + $installDir + '"'))
    $process = Start-Process -FilePath $InstallerPath -ArgumentList ($arguments + ('/LOG="' + (Join-Path $script:RepoRoot 'artifacts/install-smoke.log') + '"')) -WindowStyle Hidden -Wait -PassThru
    $process.WaitForExit(); $process.Refresh()
    Check 'Per-user isolated install exits successfully' ($process.ExitCode -eq 0) ("exit=" + $process.ExitCode)
    Check 'Installed executable and uninstaller exist' ((Test-Path -LiteralPath (Join-Path $installDir 'VoiceMite.exe')) -and (Test-Path -LiteralPath (Join-Path $installDir 'unins000.exe')))
    $publishedDll = Join-Path $script:RepoRoot 'artifacts/VoiceMite-win-x64/VoiceMite.dll'
    Check 'Installed application bytes match published application' ((Get-FileHash -LiteralPath (Join-Path $installDir 'VoiceMite.dll')).Hash -eq (Get-FileHash -LiteralPath $publishedDll).Hash)
    Check 'Installation registered for current user' (Test-Path -LiteralPath $registration)
    $process = Start-Process -FilePath $InstallerPath -ArgumentList ($arguments + ('/LOG="' + (Join-Path $script:RepoRoot 'artifacts/reinstall-smoke.log') + '"')) -WindowStyle Hidden -Wait -PassThru
    $process.WaitForExit(); $process.Refresh()
    Check 'Same-version reinstall exits successfully' ($process.ExitCode -eq 0) ("exit=" + $process.ExitCode)
    Check 'Reinstall retains existing personal-data marker' (Test-Path -LiteralPath $marker)
    if ($Ui) {
        $uiOutput = Join-Path $script:RepoRoot ("artifacts/ui-smoke-installed-" + $testId)
        $uiStart = [DateTime]::UtcNow
        $uiProcess = Start-Process -FilePath (Join-Path $installDir 'VoiceMite.exe') -ArgumentList @('--ui-smoke', ('"' + $uiOutput + '"')) -WindowStyle Hidden -Wait -PassThru
        $uiProcess.WaitForExit(); $uiProcess.Refresh()
        $uiReport = Join-Path $uiOutput 'ui-tests.json'
        $uiPassed = @()
        if (Test-Path -LiteralPath $uiReport) { $uiPassed = @(Get-Content -LiteralPath $uiReport -Raw | ConvertFrom-Json) }
        $uiSuccess = $uiProcess.ExitCode -eq 0 -and $uiPassed.Count -gt 0 -and @($uiPassed | Where-Object status -ne 'passed').Count -eq 0 -and (Get-Item -LiteralPath $uiReport).LastWriteTimeUtc -ge $uiStart
        $startup = [ordered]@{ status = $(if($uiSuccess){'passed'}else{'failed'}); exitCode = $uiProcess.ExitCode; uiChecks = $uiPassed.Count; classification = 'installed-self-contained-own-WPF-render'; liveVendorOrMicrophoneTest = $false; repeatedCasesNotAdditionalChecks = $true }
        $rebrandEvidence = Join-Path $script:RepoRoot 'docs/evidence/rebrand'
        New-Item -ItemType Directory -Force $rebrandEvidence | Out-Null
        $startup | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $rebrandEvidence 'installed-startup.json') -Encoding utf8
        if (-not $uiSuccess) { throw 'Installed application startup/UI smoke failed.' }
        $installedEvidence = Join-Path $rebrandEvidence 'ui-installed'
        New-Item -ItemType Directory -Force $installedEvidence | Out-Null
        Copy-Item -LiteralPath $uiReport -Destination (Join-Path $installedEvidence 'ui-tests.json') -Force
    }
    $uninstaller = Join-Path $installDir 'unins000.exe'
    $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -Wait -PassThru
    $process.WaitForExit(); $process.Refresh()
    Check 'Silent uninstall exits successfully' ($process.ExitCode -eq 0) ("exit=" + $process.ExitCode)
    Check 'Uninstall removes application executable' (-not (Test-Path -LiteralPath (Join-Path $installDir 'VoiceMite.exe')))
    Check 'Silent uninstall retains existing personal-data marker' (Test-Path -LiteralPath $marker)
    Check 'Uninstall removes current-user registration' (-not (Test-Path -LiteralPath $registration))
}
finally {
    if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker -Force }
    if (-not $dataDirExisted -and (Test-Path -LiteralPath $dataDir -PathType Container) -and @(Get-ChildItem -LiteralPath $dataDir -Force).Count -eq 0) { Remove-Item -LiteralPath $dataDir }
    $evidenceDir = Join-Path $script:RepoRoot 'docs/evidence/rebrand'
    New-Item -ItemType Directory -Force $evidenceDir | Out-Null
    $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidenceDir 'install-tests.json') -Encoding utf8
}
Write-Output "Installer smoke checks: $($results.Count) passed. No microphone or API request was used."
