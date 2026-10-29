# Lanyard changelog

## v0.1.2 — 2026-10-29

- Give Lanyard's WeaponCore damage listener its own stable registration ID, keeping registration and cleanup independent of other observers.
- Add regression checks for listener coexistence, load order and cleanup.

## v0.1.1 — 2026-10-29

- Simplify the plugin description and menu help around tool functions and everyday usage.

## v0.1.0 — 2026-10-29

- Link tools to one personally owned or explicitly faction-shared inventory port.
- Use the build planner for welder components and return grinder materials and drill ore to the selected inventory.
- Add shared native controls in **K → Lanyard** and `/ly menu`, with connection status, linking, unlinking and bounds preview.
- Support `/ly` and `/lanyard` commands.
- Request one missing starting component on a block-placement attempt, leaving placement to the player.
- Apply grid bounds, access checks, enemy-overlap checks and persistent combat restrictions, with friendly deconstruction support.
- Support vanilla weapons and optional WeaponCore and Vanilla+ integration.
