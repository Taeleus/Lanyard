# Development and release preparation

The development version is **Lanyard v0.2.1** on `dev`. Building or installing locally does not publish a release or register the plugin with PluginHub.

## Build

Use the .NET 10 SDK and an installed copy of Space Engineers 1. Windows builds also need the .NET Framework 4.8 targeting pack.

From the repository root:

```powershell
dotnet build ClientPlugin/ClientPlugin.csproj -c Release -p:RunPostBuildEvent=Never
```

Windows builds target `net48` and `net10.0`. Pulsar Legacy uses the `net48` build. If game auto-detection fails, pass `-p:Bin64="<SpaceEngineers installation>/Bin64"`.

`ClientPlugin/BuildVersion.cs` is the shared assembly and informational version source. Keep its three-part version consistent with the version documentation. Keep `FriendlyName` in `Lanyard.xml` set to `Lanyard` across releases.

## Install locally

Run `./Deploy-LocalDev.ps1` to build the Legacy target and install it in `%APPDATA%/Pulsar/Legacy/Local/Lanyard`. The script backs up previous local files and leaves the active Pulsar profile unchanged.

Enable Lanyard manually, disable other inventory-link variants, and restart the game after replacing a loaded build.

See [validation](docs/VALIDATION.md) for reproducible checks and the remaining in-game checklist.

## Prepare publication

The repository targets **master**. Commit the reviewed source there, then set the `Commit` field in `Lanyard.xml` to that exact source revision in a separate metadata commit. The pin identifies the code to compile and review; it should not name the metadata commit itself.

Before publishing, confirm the pinned source is reachable with the intended repository visibility, run the PluginHub manifest validator, and review the actual submitted source revision. Preparing local commits does not push them or change repository visibility.

Keep `LICENSE` and `LICENSES/client-plugin-template.MIT.txt` with distributions. Local skills, build outputs and development artifacts remain excluded from Git.
