param([string]$DotNetPath, [string]$LegacyWindowsDll, [string]$PublishedDirectory)
. (Join-Path $PSScriptRoot 'common.ps1')
$dotnet = Resolve-VoiceMiteDotNet $DotNetPath
if (-not $LegacyWindowsDll) { $LegacyWindowsDll = Join-Path $script:RepoRoot 'artifacts/VoiceInk-win-x64/VoiceInk.Windows.dll' }
if (-not $PublishedDirectory) { $PublishedDirectory = Join-Path $script:RepoRoot 'artifacts/VoiceMite-win-x64' }
$LegacyWindowsDll = [IO.Path]::GetFullPath($LegacyWindowsDll)
$PublishedDirectory = [IO.Path]::GetFullPath($PublishedDirectory)
foreach ($file in @($LegacyWindowsDll, (Join-Path $PublishedDirectory 'VoiceMite.Windows.dll'), (Join-Path $PublishedDirectory 'VoiceMite.Core.dll'))) { if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw 'Publish both the verified legacy and renamed Windows modules before running this check.' } }
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot '.tools'))
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $temporaryRoot ('profile-compat-' + [Guid]::NewGuid().ToString('N'))))
if (-not $fixtureRoot.StartsWith($temporaryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected compatibility fixture directory.' }
New-Item -ItemType Directory -Force $fixtureRoot | Out-Null
$newWindows = [Security.SecurityElement]::Escape((Join-Path $PublishedDirectory 'VoiceMite.Windows.dll'))
$newCore = [Security.SecurityElement]::Escape((Join-Path $PublishedDirectory 'VoiceMite.Core.dll'))
$project = @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><Reference Include="VoiceMite.Windows"><HintPath>$newWindows</HintPath></Reference><Reference Include="VoiceMite.Core"><HintPath>$newCore</HintPath></Reference></ItemGroup></Project>
"@
Set-Content -LiteralPath (Join-Path $fixtureRoot 'ProfileCompatibility.csproj') -Value $project -Encoding utf8
$code = @'
using System.Reflection;
using System.IO;
using System.Text.Json;
using VoiceMite.Core;
using VoiceMite.Windows;
var results = new List<object>();
void Check(string name, bool passed) { results.Add(new {name,status=passed?"passed":"failed"}); if(!passed)throw new InvalidOperationException("Compatibility assertion failed."); }
try
{
    // This artificial value is not a credential and never leaves the temporary test profile.
    const string first="fixture-secret-not-an-api-key";
    const string replacement="replacement-fixture-value";
    var legacyRoot=Path.Combine(args[1],"VoiceInk");
    Directory.CreateDirectory(legacyRoot);
    var legacyAssembly=Assembly.LoadFrom(args[0]);
    var legacyType=legacyAssembly.GetType("VoiceInk.Windows.SecretStore",true)!;
    var legacy=Activator.CreateInstance(legacyType,new object[]{legacyRoot})!;
    legacyType.GetMethod("Set")!.Invoke(legacy,["stt",first]);
    var resolved=ProfilePaths.Resolve(args[1]);
    Check("Existing legacy profile is reused without copying or creating a new profile",resolved==legacyRoot&&!Directory.Exists(Path.Combine(args[1],"VoiceMite")));
    var current=new SecretStore(resolved);
    Check("VoiceMite decrypts synthetic DPAPI data produced by actual VoiceInk binary",current.Get("stt")==first);
    current.Set("stt",replacement);
    Check("Renamed secret replacement remains readable by the legacy binary",(string?)legacyType.GetMethod("Get")!.Invoke(legacy,["stt"])==replacement);
    current.Delete("stt");
    Check("Deleting the reused secret removes it for both application names",legacyType.GetMethod("Get")!.Invoke(legacy,["stt"])==null);
}
catch(Exception error){results.Add(new {name="Compatibility exception",status="failed",detail=error.GetType().Name});}
var report=JsonSerializer.Serialize(new {classification="synthetic-secret-from-actual-legacy-binary",realCredentialsAccessed=false,networkRequestsMade=false,results},new JsonSerializerOptions{WriteIndented=true});
File.WriteAllText(args[2],report);
return report.Contains("\"failed\"",StringComparison.Ordinal)?1:0;
'@
Set-Content -LiteralPath (Join-Path $fixtureRoot 'Program.cs') -Value $code -Encoding utf8
$evidence = Join-Path $script:RepoRoot 'docs/evidence/rebrand'
New-Item -ItemType Directory -Force $evidence | Out-Null
try {
    Invoke-VoiceMiteDotNet $dotnet @('run','--project',(Join-Path $fixtureRoot 'ProfileCompatibility.csproj'),'-c','Release','--',$LegacyWindowsDll,(Join-Path $fixtureRoot 'profiles'),(Join-Path $evidence 'legacy-profile-tests.json'))
}
finally {
    # Generated fixture only: the checked absolute target is below this workspace's .tools root.
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}
Write-Output 'Legacy profile checks: 4 passed. Only synthetic DPAPI test data was accessed.'
