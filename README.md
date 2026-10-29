# Lanyard

Lanyard connects your character's tools to one selected inventory port, helping you build, grind and drill without carrying every material yourself.

**Lanyard v0.1.2** is a Space Engineers 1 client plugin for **Pulsar Legacy**. No server mod is required.

## Start here

1. Enable **Lanyard v0.1.2** in Pulsar and restart the game.
2. Equip a welder, grinder or drill and aim at the inventory access port you want to use.
3. Type `/ly` to connect.
4. Open **K → Lanyard** for status and connection controls, or type `/ly menu` to open the standalone panel. Use `/ly bounds` to see your working area.

The link stays attached to the selected block. Losing that block or access to it disconnects the link.

## What it does

- **Welder:** withdraws components listed in your build planner.
- **Grinder:** returns ore, ingots and components to the linked inventory while you use the grinder.
- **Drill:** returns ore to the linked inventory while equipped.
- **Block placement:** requests one starting component if you try to place a block without it. Place again after it arrives.

Deposits go to the inventory you selected. Withdrawals use the normal accessible conveyor network through that port; Lanyard does not grant access to locked cargo. You still aim, place blocks and use your tools yourself.

## Commands

Every command also works with `/lanyard` in place of `/ly`. Commands are case-insensitive.

| Command | Action |
| --- | --- |
| `/ly` | Link to the targeted inventory port. |
| `/ly menu` | Open status and connection controls. |
| `/ly off` | Disconnect. |
| `/ly bounds` | Toggle the working-area preview. |
| `/ly bounds off` | Hide the preview. |

## Bounds, access and combat

Use a personally owned inventory or one explicitly faction-shared by a faction member. Unowned, enemy and Share All-only ports do not qualify.

Work inside the selected container's grid bounds. Transfers pause outside, and the link breaks more than **100 m** beyond those bounds. Separate subgrids do not enlarge the working area. An overlapping enemy grid can also block the link.

Combat disables Lanyard for **two minutes after the latest qualifying activity**, including ship damage, weapon fire and supported EMP or jump-inhibition effects. Friendly deconstruction is supported. Players see **Lanyard unavailable** when blocked; detailed reasons appear in the game log.

## Compatibility

Vanilla weapons are supported, with optional WeaponCore and Vanilla+ integration when present. Combat checks use information available to your client and are not server-enforced anti-cheat.

## Local development and testing

Publication is pending. See [development and release preparation](RELEASING.md) for build and local installation instructions, including `Deploy-LocalDev.ps1`. Restart the game after replacing a loaded build.

- [Validation and in-game checklist](docs/VALIDATION.md)
- [Version history](CHANGELOG.md)

## License

[MIT](LICENSE), copyright 2026 Taeleus. The required [client-plugin-template MIT notice](LICENSES/client-plugin-template.MIT.txt) is retained with the source.
