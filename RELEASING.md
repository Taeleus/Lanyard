# Development and release preparation

The current version is **Lanyard v0.3.6**. Approved releases are merged to `master`; ongoing development stays on `dev`. Building or installing locally does not publish a release or register the plugin with PluginHub.

## Build

Use the .NET 10 SDK and an installed copy of Space Engineers 1. Windows builds also need the .NET Framework 4.8 targeting pack.

From the repository root:

```powershell
dotnet build ClientPlugin/ClientPlugin.csproj -c Release -p:RunPostBuildEvent=Never
```

Windows builds target `net48` and `net10.0`. Pulsar Legacy (`Legacy.exe`, runtime `CLR`) uses the `net48` build, and Pulsar Interim (`Interim.exe`, .NET 10, runtime `CoreCLR`) uses the `net10.0` build. `Lanyard.xml` declares `<Runtimes>CLR;CoreCLR</Runtimes>`, so one PluginHub entry serves both: Pulsar compiles the pinned source for the running edition. If game auto-detection fails, pass `-p:Bin64="<SpaceEngineers installation>/Bin64"`.

`ClientPlugin/BuildVersion.cs` is the shared assembly and informational version source. Keep its three-part version consistent with the version documentation. Keep `FriendlyName` in `Lanyard.xml` set to `Lanyard` across releases.

## Install locally

Run `./Deploy-LocalDev.ps1` for Legacy or `./Deploy-LocalDev.ps1 -Edition Interim` for Interim. The script builds the selected edition and backs up the previous local files.

The script defaults to `%APPDATA%\Pulsar`. Use that default when the launcher runs with `-useHome` or the executable is already in `%APPDATA%\Pulsar`. For a portable launch without `-useHome`, pass the folder containing `Legacy.exe` or `Interim.exe` as `-PulsarRoot`. For example, if the launcher is in `C:\Games\Pulsar`, run the command for your edition.

```powershell
./Deploy-LocalDev.ps1 -Edition Legacy -PulsarRoot 'C:\Games\Pulsar'
./Deploy-LocalDev.ps1 -Edition Interim -PulsarRoot 'C:\Games\Pulsar'
```

Legacy installs to `<root>\Legacy\Local\Lanyard`. Interim installs to `<root>\Interim\Local\Lanyard` when `<root>\Interim` exists. Otherwise, Interim uses `<root>\Legacy\Local\Lanyard`. Keep `<root>\Interim` absent when you want Interim to keep using the shared profile. The script follows that fallback without creating an Interim data folder.

Each local `plugin.xml` declares the DLL's runtime because Pulsar uses that tag to override its runtime detection. A shared folder holds one build at a time. The runtime tag makes the other edition ignore the installed build, and the script warns when it replaces a build for the other edition.

To deploy automatically during a build, set `Pulsar` to your Pulsar root in `Directory.Build.props.user` and omit `-p:RunPostBuildEvent=Never`. Each target stages a runtime-specific `plugin.xml` alongside its intermediate files. A Windows build deploys `net48` to Legacy and `net10.0` to an existing Interim folder. If only Legacy exists, run the explicit Interim deployment command for that root to replace the shared copy with the Interim build.

Enable Lanyard manually and restart the game after replacing a loaded build.

Offline development harnesses are available in `tests/CombatLockout.Tests`, `tests/ContainerLink.Integration`, `tests/TerminalMenu.Tests` and `tests/Preferences.Tests`.

## Prepare a dev update

Keep ongoing development on **dev**. Commit the reviewed source, tests and documentation together on that branch. Leave **master** for a separately approved stable release.

Before a dev push, check the pending file list so that new source and tests are included while local settings, binaries and validation artifacts remain excluded. Run the four offline harnesses and both build targets when code changes; documentation-only edits need link and consistency checks.

The first push of a new remote dev branch needs an upstream, such as `git push -u origin dev`, after its commits are ready. A readiness review does not itself commit or push anything.

## Prepare registry publication

Pushing a source branch does not update PluginHub. When preparing a registry update, first commit the reviewed source, then set the registry manifest's `Commit` field to that exact source revision in a separate metadata commit. The pin identifies the code to compile and review; it should not name the metadata commit itself. Update the local reference manifest deliberately for that publication rather than guessing a future source SHA.

Confirm the pinned source is reachable with the intended repository visibility, run the PluginHub manifest validator from the actual hub revision, and review the submitted source revision. Stable publication from master is separate from ongoing dev work. Preparing commits does not push them or change repository visibility.

Keep `LICENSE` and `LICENSES/client-plugin-template.MIT.txt` with distributions. Local skills, build outputs and development artifacts remain excluded from Git.
