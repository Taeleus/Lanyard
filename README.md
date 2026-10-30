# Lanyard

Lanyard links your tools to one inventory port, helping you build, grind and drill without carrying every material yourself.

**Lanyard v0.3.5** is a Space Engineers 1 client plugin for **Pulsar Legacy**. No server mod is required.

## Start here

1. Once installed, enable **Lanyard** in Pulsar Legacy and restart the game.
2. Equip a welder, grinder or drill and aim at the inventory port you want to use.
3. Type `/ly` to link.
4. Open **K → Lanyard** or `/ly menu` for status, controls and preferences.

## What it does

- **Welder:** supplies components listed in your build planner while you hold the welder.
- **Grinder:** returns ore, ingots and components to the linked inventory while you use the grinder.
- **Drill:** returns ore while equipped, with an option to return it only while drilling.
- **Block placement:** requests one starting component when you try to place a block without it. Place again after it arrives.

Returned materials go to your selected inventory. Supplies come from storage normally accessible through that port. You still aim, place blocks and operate your tools yourself.

## Commands

Use `/lanyard` instead of `/ly` for any command. Commands are case-insensitive.

| Command | Action |
| --- | --- |
| `/ly` | Link to the targeted inventory port. |
| `/ly menu` | Open the controls. |
| `/ly off` | Disconnect and forget the saved link. |
| `/ly pause` | Pause transfers and keep your link. |
| `/ly resume` | Allow transfers when safety checks permit. |
| `/ly bounds` | Preview the aimed grid, or hide the current preview. |
| `/ly bounds linked` | Toggle the linked grid's preview. |
| `/ly bounds off` | Hide the preview. |

## Working area and safety

Use your own inventory or one explicitly faction-shared by a faction member. Unowned inventories and Share All-only access do not qualify.

Transfers run inside the selected container's grid bounds. They pause outside, and the link disconnects more than **100 m** beyond those bounds. Attached subgrids do not enlarge the working area. Losing the selected block or access to it also disconnects.

Combat or hostile grid overlap **pauses transfers while keeping your link**. Transfers can resume after **two minutes of connected time without a new detection**, while you are inside the working area with access. New activity or continued hostile overlap extends the pause. Time away preserves the remaining cooldown; reconnecting or unlinking does not clear it.

Saved links wait for the same player, container and port after reconnecting, then check access and range again. If your saved container is unavailable, use `/ly off` to forget it before linking another. Re-link manually if a server transfer replaces the saved player or container.

## Make it yours

The menu shows connection state, cooldown, planner availability and transfer feedback. Full-backpack and full-container warnings remain visible while the condition persists. Tool preferences let you enable each automatic function separately.

The HUD starts **Off**. Choose **With tools**, **Only when paused** or **Always during gameplay**, then select a corner or open **Customize HUD…**. Drag the preview and adjust size and opacity. **Save appearance** keeps your edits; **Cancel** or Escape discards them. The HUD follows the game's HUD visibility.

Assign Ctrl/Alt key combinations or function keys for linking, opening the menu, pausing and showing bounds. Shortcuts start unassigned and run during gameplay. Choose unused combinations; chat and menus keep their normal controls.

Preferences save locally across reconnects and restarts. **Reset all settings** restores tool choices, HUD appearance, manual pause and shortcuts while keeping your connection and combat cooldown.

See [version history](CHANGELOG.md), [source build instructions](RELEASING.md) and the [MIT license](LICENSE). Copyright holders: Bushfiend, TheCrunchy and Taeleus. The [template license notice](LICENSES/client-plugin-template.MIT.txt) is retained with the source.
