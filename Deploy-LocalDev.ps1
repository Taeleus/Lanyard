[CmdletBinding()]
param(
    [string]$PulsarRoot = (Join-Path $env:APPDATA 'Pulsar'),
    [string]$Bin64,
    # Legacy = Legacy.exe on .NET Framework 4.8 (net48, Pulsar runtime CLR).
    # Interim = Interim.exe on .NET 10 (net10.0, Pulsar runtime CoreCLR).
    [ValidateSet('Legacy', 'Interim')][string]$Edition = 'Legacy'
)
$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$framework = if ($Edition -eq 'Legacy') { 'net48' } else { 'net10.0' }
$runtime = if ($Edition -eq 'Legacy') { 'CLR' } else { 'CoreCLR' }
$arguments = @('build', (Join-Path $repo 'ClientPlugin\ClientPlugin.csproj'), '-c', 'Release', '-f', $framework, '-p:RunPostBuildEvent=Never', '-v', 'minimal')
if ($Bin64) { $arguments += "-p:Bin64=$Bin64" }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed; no deployed files were changed.' }
$output = Join-Path $repo "ClientPlugin\bin\Release\$framework"
$dll = Join-Path $output 'Lanyard.dll'
if ([Reflection.AssemblyName]::GetAssemblyName($dll).Name -ne 'Lanyard') { throw 'Unexpected assembly identity.' }
# Resolve the data folder the way Pulsar does: Pulsar/<Edition> if it exists, otherwise Pulsar/Legacy.
# Do not create Pulsar/Interim here; Interim would switch to that empty folder and lose the user's profile.
$editionRoot = Join-Path $PulsarRoot $Edition
if (!(Test-Path -LiteralPath $editionRoot -PathType Container)) { $editionRoot = Join-Path $PulsarRoot 'Legacy' }
if (!(Test-Path -LiteralPath $editionRoot -PathType Container)) { throw "Pulsar data folder not found under $PulsarRoot. Run Pulsar once and retry." }
$destination = Join-Path $editionRoot 'Local\Lanyard'
# A shared folder holds one build. Say so when this replaces the other edition's build (it is backed up below).
$existingXml = Join-Path $destination 'plugin.xml'
if (Test-Path -LiteralPath $existingXml -PathType Leaf) {
    $existing = [regex]::Match([IO.File]::ReadAllText($existingXml), '<Runtimes>([^<]*)</Runtimes>').Groups[1].Value
    if ($existing -and $existing -ne $runtime) { Write-Warning "Replacing the $existing build in $destination with the $runtime build." }
}
$backup = Join-Path $repo ('artifacts\local-backups\' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($name in @('plugin.dll','plugin.pdb','plugin.xml')) {
    $old = Join-Path $destination $name
    if (Test-Path -LiteralPath $old -PathType Leaf) { Copy-Item -LiteralPath $old -Destination $backup }
}
Copy-Item -LiteralPath $dll -Destination (Join-Path $destination 'plugin.dll') -Force
Copy-Item -LiteralPath (Join-Path $output 'Lanyard.pdb') -Destination (Join-Path $destination 'plugin.pdb') -Force
# A local plugin.xml overrides the runtime Pulsar infers from the DLL, so tag this build's runtime only.
$manifest = [IO.File]::ReadAllText((Join-Path $repo 'Lanyard.xml'))
if ($manifest -notmatch '<Runtimes>[^<]*</Runtimes>') { throw 'Lanyard.xml has no Runtimes element.' }
$manifest = [regex]::Replace($manifest, '<Runtimes>[^<]*</Runtimes>', "<Runtimes>$runtime</Runtimes>")
[IO.File]::WriteAllText((Join-Path $destination 'plugin.xml'), $manifest, [Text.UTF8Encoding]::new($false))
if ((Get-FileHash -LiteralPath $dll).Hash -ne (Get-FileHash -LiteralPath (Join-Path $destination 'plugin.dll')).Hash) { throw 'Deployed binary hash does not match the build.' }
Write-Output "Deployed Lanyard v$([Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion) ($Edition, $framework) to $destination"
Write-Output 'Select Lanyard manually in Pulsar; this script leaves your profile and other plugins alone. Restart after replacing a loaded build.'
