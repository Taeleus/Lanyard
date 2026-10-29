# Validation

Current version: **Lanyard v0.2.1** on `dev`. Checks were run on 2026-10-29.

## Automated checks

All **969 offline assertions** pass:

| Suite | Assertions | Coverage |
| --- | ---: | --- |
| CombatLockout.Tests | 506 | Combat, ownership, framework observers, cooldown round trips and restored observation history. |
| ContainerLink.Integration | 357 | Tool transfers, pause/resume, exact-port recovery, persistence and session lifecycle. |
| TerminalMenu.Tests | 106 | Commands, native menu actions, focus and terminal lifecycle. |

The harnesses exercise production logic using game API doubles and real Harmony. Both `net48` and `net10.0` Release builds pass with zero warnings and errors. Pulsar's Legacy source compiler also compiles all **27 production source files** without diagnostics.

WeaponCore registration checks cover both observer load orders and cleanup orders, continued delivery to the remaining observer, and the shared API discovery channel. The provider double preserves the first callback registered under an ID and removes registrations by ID, matching WeaponCore's contract.

Pause checks retain the exact container and port, suppress every transfer path, refresh enemy restrictions while paused, and require a fresh clear scan before resuming. Access loss, range breaks, confirmed target loss and death still end the link. World unloading releases old live references while retaining saved intent. Separate tests exercise the production 120-second cooldown, extension, expiry and persistence; controller tests use combat and overlap doubles.

Recovery checks link the production XML store, recovery coordinator, controller, port selection, session component and plugin lifecycle. They cover atomic replacement, interrupted writes, unlink tombstones, delayed replication, exact port matching, identity isolation, callback failures and repeated hops. Separate combat tests preserve the original expiry, count offline time, retain known observation history and keep new activity authoritative while recovery is pending.

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

- [ ] Link to cargo and reconnect. Confirm the same inventory and port return automatically once loaded.
- [ ] Start a combat pause, wait about a minute, then reconnect. Confirm only the remaining time is served and the link resumes without selecting cargo again.
- [ ] Hop between servers and return with the same player/container identities. Confirm delayed loading shows a saved link and transfers resume only after access, range and safety checks pass.
- [ ] Unlink, then reconnect. Confirm the old connection stays off. Repeat after losing cargo access or breaking the range limit.

## Limits

Fresh in-game plugin initialization, rendered GUI layout and live multiplayer or dedicated-server replication remain manual verification. Offline tests and successful compilation do not certify those paths.

Server transfers must preserve the player identity and exact container/port identity. Lanyard waits for a missing target to replicate; it cannot discover a replacement when a server remaps those identities. Known observer IDs preserve their initialization history, while new observers and newly detected activity retain their normal restrictions. Activity occurring entirely while disconnected is limited to the history exposed by the game and optional frameworks when rejoining.

Combat restrictions depend on information received by the client and are not server-enforced anti-cheat. Full cargo, character capacity and delayed inventory replication can affect transfers. Placement requests use a bounded retry window; an unusually late response can overlap a later request.
