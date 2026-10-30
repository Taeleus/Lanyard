# Lanyard changelog

## v0.3.4 — 2026-10-30

- Preserve the last complete cooldown record if a save is interrupted.
- Stop welder planner queries when the tool is holstered, closed or replaced by a weapon.

## v0.3.3 — 2026-10-30

- Hide the With tools HUD after holstering or switching to a weapon.
- Remove the HUD startup animation and its setting; show connection status immediately.
- Preserve existing appearance and other preferences when loading older settings.

## v0.3.2 — 2026-10-30

- Fix the HUD editor leaving an invisible screen in control of input after saving or cancelling.
- Keep HUD startup independent of menu styling and avoid waiting for its background texture to load.

## v0.3.1 — 2026-10-30

- Clarify connection controls, group tool settings, and make HUD choices explicit in the menu.
- Add clearer shortcut assignment and cancellation, with keyboard focus improvements.
- Adjust HUD scale and opacity while positioning it, with live preview and saved appearance.
- Redirect a legacy chat command to the current Lanyard commands with a local hint.
- Remove cooldowns caused only by joining or discovering a quiet grid or weapon.
- Pause active cooldown time while disconnected and resume the remaining time on return.
- Default the HUD to off with fresh settings; add persistent drag positioning and an option to reset all settings.
- Keep the HUD visible during background gameplay when the game loses input focus.
- Show full-inventory conditions in the HUD and connection panel until they clear.
- Clear the range warning when a link ends and slightly strengthen the green working-area outline.
- Add an optional LanyardOS HUD startup animation with varied messages.

## v0.3.0 — 2026-10-30

- Keep a working connection when an attempt to select another inventory fails.
- Show clearer connection states and the remaining combat cooldown.
- Add transfer feedback and a read-only build-planner availability summary.
- Pause and resume automatic transfers without unlinking.
- Choose which tool functions run, including an active-drilling-only option.
- Add an optional status indicator with display, size and position preferences.
- Show the linked working area and warn before exceeding the disconnect distance.
- Configure keyboard shortcuts for connection controls.
- Organize connection, tool, display and shortcut controls into separate views.

## v0.2.1 — 2026-10-29

- Keep the launcher display name as Lanyard across releases.
- Remember the selected inventory and exact access port through reconnects and server transfers that preserve container identity.
- Restore the connection after replication and fresh access, range and safety checks.
- Preserve cooldown expiry through reconnects, including time spent offline.
- Keep manual unlinking and confirmed broken links from being restored later.

## v0.2.0 — 2026-10-29

- Keep the selected inventory linked while combat or enemy overlap pauses transfers.
- Resume automatically after two minutes without a new detection; continued activity extends the pause.
- Continue to break the link when access is lost or the existing range limit is exceeded.

## v0.1.2 — 2026-10-29

- Keep the launcher display name as Lanyard across releases.
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
