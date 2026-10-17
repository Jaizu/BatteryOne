# Builds everything and puts the files for a GitHub release in dist\release:
#   BatteryOne.exe               the app (with the Stream Deck plugin embedded)
#   BatteryOne.streamDeckPlugin  the plugin on its own
#   versions.json                {"app": "...", "plugin": "..."}, read by the updater
# Versions come from Directory.Build.props. Tag the release v<AppVersion>.

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$out = Join-Path $root 'dist\release'

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$appVersion = $props.Project.PropertyGroup.AppVersion
$pluginVersion = $props.Project.PropertyGroup.PluginVersion

dotnet build (Join-Path $root 'BatteryOne.csproj') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null
Copy-Item (Join-Path $root 'bin\Release\net48\BatteryOne.exe') $out
Copy-Item (Join-Path $root 'obj\Release\net48\BatteryOne.streamDeckPlugin') $out
[IO.File]::WriteAllText((Join-Path $out 'versions.json'),
    "{`"app`": `"$appVersion`", `"plugin`": `"$pluginVersion`"}`n", (New-Object Text.UTF8Encoding($false)))

"BatteryOne $appVersion, Stream Deck plugin $pluginVersion"
Get-ChildItem $out | ForEach-Object { "  $($_.Name)  ($([math]::Round($_.Length / 1KB)) KB)" }
"Release tag: v$appVersion"
