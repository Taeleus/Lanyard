[CmdletBinding()]
param([string]$PulsarRoot = (Join-Path $env:APPDATA 'Pulsar'), [string]$Bin64)
$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$arguments = @('build', (Join-Path $repo 'ClientPlugin\ClientPlugin.csproj'), '-c', 'Release', '-f', 'net48', '-p:RunPostBuildEvent=Never', '-v', 'minimal')
if ($Bin64) { $arguments += "-p:Bin64=$Bin64" }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed; no deployed files were changed.' }
$output = Join-Path $repo 'ClientPlugin\bin\Release\net48'
$dll = Join-Path $output 'Lanyard.dll'
if ([Reflection.AssemblyName]::GetAssemblyName($dll).Name -ne 'Lanyard') { throw 'Unexpected assembly identity.' }
$legacy = Join-Path $PulsarRoot 'Legacy'
if (!(Test-Path -LiteralPath $legacy -PathType Container)) { throw 'Pulsar Legacy folder not found.' }
$destination = Join-Path $legacy 'Local\Lanyard'
$backup = Join-Path $repo ('artifacts\local-backups\' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($name in @('plugin.dll','plugin.pdb','plugin.xml')) {
    $old = Join-Path $destination $name
    if (Test-Path -LiteralPath $old -PathType Leaf) { Copy-Item -LiteralPath $old -Destination $backup }
}
Copy-Item -LiteralPath $dll -Destination (Join-Path $destination 'plugin.dll') -Force
Copy-Item -LiteralPath (Join-Path $output 'Lanyard.pdb') -Destination (Join-Path $destination 'plugin.pdb') -Force
Copy-Item -LiteralPath (Join-Path $repo 'Lanyard.xml') -Destination (Join-Path $destination 'plugin.xml') -Force
if ((Get-FileHash -LiteralPath $dll).Hash -ne (Get-FileHash -LiteralPath (Join-Path $destination 'plugin.dll')).Hash) { throw 'Deployed binary hash does not match the build.' }
Write-Output "Deployed Lanyard v$([Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion) to $destination"
Write-Output 'Select Lanyard manually in Pulsar; this script leaves your profile and other plugins alone. Restart after replacing a loaded build.'
