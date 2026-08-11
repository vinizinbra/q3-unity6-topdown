# Game Design Document (Core)

## Pillars
2D top-down co-op survivors-roguelite. Gunfire Reborn (loot/build depth, weapon perks) x
Vampire Survivors (swarm density, auto-combat pacing) + light map exploration.

## Run Structure
- Run length: ~15 min (not final).
- Continuous mob spawns, minute-by-minute pacing (Survival Director).
- Scheduled events at specific minutes: boss kill, timed mini-challenge (player teleported,
  world time stops for the arena).
- Optional map-found challenges: same teleport-and-stop-time shape, player-triggered via
  Rift Fracture/Portal.
- Enemy HP scales with run time (and player count) — see `run-curves-coop-scaling.md`.

## Character Base Stats
- Health: 100
- Shield: 50
- Weapon baseline: 50 DPS (all weapons tuned to this before perks/upgrades)

## Enemy Tiers (base HP, pre-scaling)
| Tier | HP |
|---|---|
| Filler | 20 |
| Normal | 60 |
| Specialist | 125 |
| Heavy | 250 |
| Elite | 600 |
| Boss | 2000 |

Scales up over run time and with player count (see `run-curves-coop-scaling.md`).

## Enemy Roster (by tier, stereotype → behavior)
**Filler**
- Filler — low HP, walks straight at player
- Swarm — low HP, walks at player, faster than Filler

**Normal**
- Melee
- Ranged
- Grenadier/Mortar
- Suicider
- Healer
- Ranged (flying)

**Specialist**
- Sniper
- Summoner
- Healer
- Flying Shielder
- Flying Healer

**Heavy**
- Grenadier/Mortar
- Shielder
- Charger
- Jumper/Leaper
- Shotgun
- Slammer

Roster is open — more stereotypes added later.

## Progression — Level-Up
On level-up, roll order is fixed: **Hero Upgrade → Hero Upgrade → Global Upgrade**.

Three upgrade types:
- **Rift Mutation** — rare, run-wide, usually a tradeoff. Not from level-up; found via Rift Stone.
- **Global Upgrade** — flat stat upgrade, stackable up to 3x.
- **Hero Upgrade** — modifies the hero's active skill or passive; this is what differentiates characters.

## Weapons & Perks
- Each weapon: max 5 Weapon Perks.
- Perks acquired from map Chests or the in-map Store, random.

## Map Exploration
Hidden/scattered elements to find mid-run:
- Rift Stone — grants a Rift Mutation
- Weapon Chest
- Global Upgrade Chest
- Hero Upgrade Chest
- Weapon Perk Stone — adds a perk to the currently equipped weapon
- Rift Fracture / Rift Portal — enters an optional event (teleport, time-stopped)

## Open Questions
- Final run length (15 min placeholder).
- Full enemy roster beyond current stereotype list.
- Exact minute-by-minute event schedule.
