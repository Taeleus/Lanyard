# Validation

Current version: **Lanyard v0.1.2**. Checks were run on 2026-10-29.

## Automated checks

All **782 offline assertions** pass:

| Suite | Assertions | Coverage |
| --- | ---: | --- |
| CombatLockout.Tests | 445 | Combat, ownership, framework integration, independent WeaponCore listeners and cleanup. |
| ContainerLink.Integration | 231 | Linked transfers, placement supply and grinder behavior. |
| TerminalMenu.Tests | 106 | Commands, native menu actions, focus and terminal lifecycle. |

The harnesses exercise production logic using game API doubles and real Harmony. Both `net48` and `net10.0` Release builds pass with zero warnings and errors. Pulsar's Legacy source compiler also compiles all **23 production source files** without diagnostics.

WeaponCore registration checks cover both observer load orders and cleanup orders, continued delivery to the remaining observer, and the shared API discovery channel. The provider double preserves the first callback registered under an ID and removes registrations by ID, matching WeaponCore's contract.

Source review checked the relevant game callbacks, inventory transfers and GUI lifecycle against **Space Engineers 1.210.014 b0**. It covered link permissions, native conveyor source selection, bounds and combat guards, patch cleanup and menu behavior.

Run the checks from the repository root:

```powershell
dotnet run --project tests/CombatLockout.Tests/CombatLockout.Tests.csproj -c Release
dotnet run --project tests/ContainerLink.Integration/ContainerLink.Integration.csproj -c Release
dotnet run --project tests/TerminalMenu.Tests/TerminalMenu.Tests.csproj -c Release
dotnet build ClientPlugin/ClientPlugin.csproj -c Release -p:RunPostBuildEvent=Never
```

## In-game checklist

These checks have not been recorded as complete. Keep the PluginHub submission in draft until their results are recorded.

Restart with Lanyard enabled and other inventory-link plugins disabled. Use a survival world, owned cargo with free space and a quiet grid.

- [ ] Open **K → Lanyard** and `/ly menu`. Check status, keyboard focus, linking, unlinking, bounds and Close; confirm `/lanyard` aliases work.
- [ ] Weld from the build planner, grind a block down completely and drill ore. Check that materials move through the selected port as expected and stop when unlinked or using another tool.
- [ ] Check outside-bounds pausing, the 100 m disconnect, access revocation, friendly deconstruction and the two-minute combat restriction.
- [ ] Attempt placement with its first component missing, then place again after it arrives. Repeat the tool and link checks in live multiplayer.

## Limits

Fresh in-game plugin initialization, rendered GUI layout and live multiplayer or dedicated-server replication remain manual verification. Offline tests and successful compilation do not certify those paths.

Combat restrictions depend on information received by the client and are not server-enforced anti-cheat. Full cargo, character capacity and delayed inventory replication can affect transfers. Placement requests use a bounded retry window; an unusually late response can overlap a later request.
