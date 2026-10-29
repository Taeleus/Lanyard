# Lanyard regression checks

Run the combat/ownership harness with:

```powershell
dotnet run --project tests/CombatLockout.Tests/CombatLockout.Tests.csproj -c Release
```

The executable links production combat, cleanup, persistence and optional-framework sources. It uses game API doubles and real Harmony patches. These tests verify local logic, not multiplayer replication or in-game UI appearance.

See [validation](../../docs/VALIDATION.md) for current results, the container-link integration harness and the manual checklist.
