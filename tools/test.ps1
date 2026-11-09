param([string]$DotNetPath, [ValidateSet('Debug','Release')][string]$Configuration = 'Release', [switch]$Windows, [switch]$External, [switch]$Ui, [switch]$Target)
. (Join-Path $PSScriptRoot 'common.ps1')
$dotnet = Resolve-VoiceMiteDotNet $DotNetPath
# A dependency-free executable test runner; a nonzero exit code means a failed check.
Invoke-VoiceMiteDotNet $dotnet @('run', '--project', (Join-Path $script:RepoRoot 'tests/VoiceMite.Tests/VoiceMite.Tests.csproj'), '-c', $Configuration)
if ($Windows) {
    # Explicit opt-in: actual desktop tests create windows, use clipboard and briefly record.
    Invoke-VoiceMiteDotNet $dotnet @('run', '--project', (Join-Path $script:RepoRoot 'tests/VoiceMite.WorkerTests/VoiceMite.WorkerTests.csproj'), '-c', $Configuration, '--', '--report', (Join-Path $script:RepoRoot 'docs/evidence/rebrand/worker-tests.json'))
    Invoke-VoiceMiteDotNet $dotnet @('run', '--project', (Join-Path $script:RepoRoot 'tests/VoiceMite.WindowsTests/VoiceMite.WindowsTests.csproj'), '-c', $Configuration, '--', '--report', (Join-Path $script:RepoRoot 'docs/evidence/rebrand/windows-tests.json'))
}
if ($External) {
    # Explicit opt-in: opens an isolated Edge profile/local page and a new Notepad test file.
    Invoke-VoiceMiteDotNet $dotnet @('run', '--project', (Join-Path $script:RepoRoot 'tests/VoiceMite.WindowsTests/VoiceMite.WindowsTests.csproj'), '-c', $Configuration, '--', '--external', '--report', (Join-Path $script:RepoRoot 'docs/evidence/rebrand/external-tests.json'))
}
if ($Ui) {
    try {
        $active = [Threading.Mutex]::OpenExisting('Local\VoiceInk.Desktop.v1')
        $active.Dispose()
        throw 'Exit VoiceMite before running its UI smoke tests.'
    }
    catch [Threading.WaitHandleCannotBeOpenedException] { }
    $uiOutput = Join-Path $script:RepoRoot ('artifacts/ui-smoke-rebrand-' + [Guid]::NewGuid().ToString('N'))
    $started = [DateTime]::UtcNow
    Invoke-VoiceMiteDotNet $dotnet @('run', '--project', (Join-Path $script:RepoRoot 'src/VoiceMite.App/VoiceMite.App.csproj'), '-c', $Configuration, '--', '--ui-smoke', $uiOutput)
    $uiReport = Join-Path $uiOutput 'ui-tests.json'
    if (-not (Test-Path -LiteralPath $uiReport) -or (Get-Item -LiteralPath $uiReport).LastWriteTimeUtc -lt $started) { throw 'UI smoke did not produce a fresh report.' }
    $uiResults = @(Get-Content -LiteralPath $uiReport -Raw | ConvertFrom-Json)
    if ($uiResults.Count -eq 0 -or @($uiResults | Where-Object { $_.status -ne 'passed' }).Count -gt 0) { throw 'UI smoke checks did not all pass.' }
    Write-Output "UI smoke checks: $($uiResults.Count) passed; own-window renders only, no API requests."
}
if ($Target) {
    try {
        $active = [Threading.Mutex]::OpenExisting('Local\VoiceInk.Desktop.v1')
        $active.Dispose()
        throw 'Exit VoiceMite before running its target-integration tests.'
    }
    catch [Threading.WaitHandleCannotBeOpenedException] { }
    $fixtureProject = Join-Path $script:RepoRoot 'tests/VoiceMite.WindowsTests/VoiceMite.WindowsTests.csproj'
    Invoke-VoiceMiteDotNet $dotnet @('build', $fixtureProject, '-c', $Configuration, '--nologo')
    $fixture = Join-Path $script:RepoRoot "tests/VoiceMite.WindowsTests/bin/$Configuration/net10.0-windows/VoiceMite.WindowsTests.dll"
    $targetOutput = Join-Path $script:RepoRoot ('artifacts/target-smoke-rebrand-' + [Guid]::NewGuid().ToString('N'))
    $started = [DateTime]::UtcNow
    Invoke-VoiceMiteDotNet $dotnet @('run', '--project', (Join-Path $script:RepoRoot 'src/VoiceMite.App/VoiceMite.App.csproj'), '-c', $Configuration, '--', '--target-smoke', $fixture, $dotnet, $targetOutput)
    $report = Join-Path $targetOutput 'target-tests.json'
    if (-not (Test-Path -LiteralPath $report) -or (Get-Item -LiteralPath $report).LastWriteTimeUtc -lt $started) { throw 'Target smoke did not produce a fresh report.' }
    $results = @(Get-Content -LiteralPath $report -Raw | ConvertFrom-Json)
    if ($results.Count -eq 0 -or @($results | Where-Object { $_.status -ne 'passed' }).Count -gt 0) { throw 'Target integration checks did not all pass.' }
    Write-Output "Target integration checks: $($results.Count) passed; real desktop input into isolated test controls."
}
