# Validation

Current version: **Lanyard v0.2.0** on `dev`. Checks were run on 2026-10-29.

## Automated checks

All **861 offline assertions** pass:

| Suite | Assertions | Coverage |
| --- | ---: | --- |
| CombatLockout.Tests | 455 | Combat, ownership, framework integration, independent WeaponCore listeners, overlap cooldown persistence and cleanup. |
| ContainerLink.Integration | 300 | Linked transfers, placement supply, grinder behavior and retained-link pause/resume. |
| TerminalMenu.Tests | 106 | Commands, native menu actions, focus and terminal lifecycle. |

The harnesses exercise production logic using game API doubles and real Harmony. Both `net48` and `net10.0` Release builds pass with zero warnings and errors. Pulsar's Legacy source compiler also compiles all **23 production source files** without diagnostics.

WeaponCore registration checks cover both observer load orders and cleanup orders, continued delivery to the remaining observer, and the shared API discovery channel. The provider double preserves the first callback registered under an ID and removes registrations by ID, matching WeaponCore's contract.

Pause checks retain the exact container and port, suppress every transfer path, refresh enemy restrictions while paused, and require a fresh clear scan before resuming. Access, range, block, port, character and session failures still disconnect during a pause. Separate tests exercise the production 120-second cooldown, extension, expiry and persistence; controller tests use combat and overlap doubles.

Source review checked the relevant game callbacks, inventory transfers and GUI lifecycle against **Space Engineers 1.210.014 b0**. It covered link permissions, native conveyor source selection, bounds and combat guards, patch cleanup and menu behavior.

Run the checks from the repository root:

```powershell
dotnet run --project tests/CombatLockout.Tests/CombatLockout.Tests.csproj -c Release
dotnet run --project tests/ContainerLink.Integration/ContainerLink.Integration.csproj -c Release
dotnet run --project tests/TerminalMenu.Tests/TerminalMenu.Tests.csproj -c Release
dotnet build ClientPlugin/ClientPlugin.csproj -c Release -p:RunPostBuildEvent=Never
```

## In-game checklist

Restart with Lanyard enabled and other inventory-link plugins disabled. Use a survival world, owned cargo with free space and a quiet grid.

- [ ] Link to cargo, trigger combat, then stop. Check that the panel shows a paused link, no materials move, and transfers resume after two quiet minutes without linking again.
- [ ] Repeat with an enemy overlap. Leave it present for more than two minutes to confirm the link stays paused; remove it and check automatic resumption after two clear minutes.
- [ ] While paused, move more than 100 m beyond the grid bounds or revoke cargo access. Confirm the link breaks and must be linked again after access and range are restored.
- [ ] After resumption, try the welder, grinder, drill and one missing-component placement. Check that each uses the original cargo and that an idle grinder does not send leftover materials merely because the pause ended.

## Limits

Fresh in-game plugin initialization, rendered GUI layout and live multiplayer or dedicated-server replication remain manual verification. Offline tests and successful compilation do not certify those paths.

Combat restrictions depend on information received by the client and are not server-enforced anti-cheat. Full cargo, character capacity and delayed inventory replication can affect transfers. Placement requests use a bounded retry window; an unusually late response can overlap a later request.
