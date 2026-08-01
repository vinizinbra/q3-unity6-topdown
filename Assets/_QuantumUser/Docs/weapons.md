# Weapons

See [architecture.md](architecture.md) for the Simulation/View split this doc assumes, and for
why weapons resolve their view via a direct prefab reference instead of the entity-prototype
pattern enemies use.

## How a weapon is put together

| Layer | Asset | Role |
|---|---|---|
| Simulation | `WeaponDataAsset` (e.g. `Resources/Weapon/BasicWeapon.asset`) | Stats: `FireType` (Hitscan / ProjectileStraight / ProjectileArc), damage, fire rate (shots/sec), range, muzzle offset/height, target aim height, and projectile params (speed for straight; target distance/arc height/gravity for arc — arc solves initial velocity so the shot always peaks at `ArcHeight` and lands at `TargetDistance`, flight time is a result, not an input). `partial class` — see `WeaponDataAsset.View.cs` below. |
| Simulation (View-only field) | `WeaponDataAsset.View.cs` | Adds `GameObject ViewPrefab` to the same `WeaponDataAsset` via the `partial class` split (see [architecture.md](architecture.md)) — a direct reference to the weapon's visual prefab, set once per data asset in the Inspector. |
| Simulation | `Weapon` component (`QTN/Weapon.qtn`) | Just `AssetRef<WeaponDataAsset> WeaponData` + `FireCooldownTimer`. A component **on the player**, not its own entity. |
| Simulation | `WeaponSystem` | Reads `WeaponDataAsset` via `f.FindAsset`, branches on `FireType` to do the hitscan raycast or spawn a straight/arc projectile. Generic over whatever `WeaponDataAsset` the player's `Weapon.WeaponData` points at. |
| View | `WeaponView` (one per weapon's view prefab — pistol, shotgun, etc.) | Owns everything specific to *this* weapon's look: position offsets per aim direction, hand-grip anchor points, shoot recoil (position kick + rotation kick + knockback punch, spring-damped), muzzle particle, projectile-impact effect. `PlayerGunAimView` computes the generic aim/sway/follow pose every frame and hands it to `WeaponView.ApplyAim()` rather than touching the transform itself. |
| View | `WeaponViewController` (`CustomQuantumEntityViewComponent` on the player) | Reads `Weapon.WeaponData` off the verified frame on `Initialize`, resolves the `WeaponDataAsset` via `f.FindAsset`, `Instantiate`s its `ViewPrefab` under the player's `weaponSocket` (`WeaponLocator` transform), and `GetComponent<WeaponView>()`s the result. `SpawnWeaponView` is public/re-callable for a future weapon-switch flow — not wired to any event yet since each player only ever has one weapon today. Also lerps `weaponSocket`'s local position toward `CharacterData.WeaponPosition` every frame, mirroring X by the character's facing (via `BlobAnimationView.FacingSign`) - `WeaponSystem` mirrors the same offset in sim (see `StatUtility.GetWeaponHoldOffset`) so the muzzle stays lined up with the socket. |

`WeaponHandGripView` doesn't need to know about any of this — it already falls back through
`GetComponent`/`GetComponentInParent`/`transform.root` to find its `WeaponView` reference, so it
picks up whatever `WeaponViewController` spawns automatically.

## View resolution

```
Weapon.WeaponData (AssetRef<WeaponDataAsset>, just a numeric ID at the Simulation layer)
        │
        ▼  WeaponViewController.Initialize() reads this off the verified frame
f.FindAsset(weaponDataRef)  →  WeaponDataAsset instance  →  .ViewPrefab
        │
        ▼
Instantiate(ViewPrefab, weaponSocket).GetComponent<WeaponView>()
```

There used to be a separate `WeaponViewCatalog` ScriptableObject (a hand-maintained
`AssetRef<WeaponDataAsset> → WeaponView` array) as the bridge instead. It's gone — `ViewPrefab` is
just a field on the data asset itself now, so there's exactly one place to set the pairing.

## Roster

| Name | `WeaponDataAsset` | View prefab | Notes |
|---|---|---|---|
| Basic Weapon | `Assets/_QuantumUser/Resources/Weapon/BasicWeapon.asset` | `Assets/_QuantumUser/Resources/WeaponViews/BasicWeapon.prefab` | The only weapon in the game today. `BasicWeapon.asset`'s `ViewPrefab` field must point at this prefab. |

## Adding a new weapon type

1. **Create the data asset.** Duplicate `BasicWeapon.asset` under `Resources/Weapon/`, set
   `FireType` and the relevant stat block (straight-projectile fields vs. arc-solve fields vs.
   just hitscan range/damage).
2. **Create the view prefab.** Duplicate an existing `WeaponView` prefab, retune its position
   offsets, hand-grip anchors, recoil/knockback tuning, and swap the muzzle/impact particle
   references.
3. **Wire them together.** Set the new data asset's `ViewPrefab` field to the new prefab — this is
   the *only* place the pairing needs to be registered.
4. **Wire it to a player.** There's no weapon-switch/pickup flow yet — the player's starting
   `Weapon.WeaponData` is whatever's set on their entity prototype. Point that at the new asset to
   test it.
5. **Add a row to the Roster table above.**

No `WeaponSystem`/`Weapon.qtn` changes needed for a new weapon as long as it fits Hitscan /
ProjectileStraight / ProjectileArc.
