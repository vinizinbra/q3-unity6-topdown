# Simulation/View architecture

Quantum projects split into two assemblies:

- **Simulation** (`Assets/_QuantumUser/Simulation`) — the deterministic sim. Compiles to a
  separate assembly (`Quantum.Simulation.asmref` → `Quantum.Simulation.asmdef`). `.qtn` files here
  define ECS components/signals/enums; codegen turns them into C# structs under `View/Generated`
  (`QPrototype*.cs`, `Quantum.qtn.cs`, etc). Data assets (`AssetObject` subclasses like
  `EnemyDataAsset`, `WeaponDataAsset`, `EnemyActionData`) also live here.
- **View** (`Assets/_QuantumUser/View`) — regular Unity/MonoBehaviour code
  (`Quantum.Unity.asmref` → `Quantum.Unity.asmdef`) that reads verified/predicted frame state and
  drives visuals (sprites, particles, animation, UI). Nothing here runs in the simulation and
  nothing here is allowed to affect determinism. `Quantum.Unity.asmdef` references
  `Quantum.Simulation` (View reads Simulation state) — **not** the other way around.

Editing a `.qtn` file requires reopening Unity to regenerate `View/Generated/*` before the new
component/field is usable in code.

## What Simulation-side data assets actually can and can't hold

`Quantum.Simulation.asmdef` *can* see built-in `UnityEngine` types (`GameObject`, `ParticleSystem`,
`AudioClip`, etc. — confirmed by direct test, they compile fine on a Simulation-assembly class).
What it genuinely can't do is reference a *custom class defined in the View project*
(`WeaponView`, `EnemyAttackVisualsView`, ...) — `Quantum.Unity` already depends on
`Quantum.Simulation`, so the reverse reference would be circular, which the compiler flatly
rejects regardless of any asmdef setting. That's a hard wall, not a policy choice.

So a data asset **can** hold a direct `GameObject`/`ParticleSystem`/etc. field (View code that
resolves the asset can `GetComponent<TheViewClassItActuallyNeeds>()` off it), but it can never
hold a field typed as one of the View project's own classes directly.

**Pattern**: split the class into two files via `partial class` — e.g. `WeaponDataAsset.cs` (core
simulation fields) and a companion `WeaponDataAsset.View.cs` (View-only fields, built-in Unity
types only). Both compile into `Quantum.Simulation` either way (`partial` doesn't cross assembly
boundaries — it's file organization, not an assembly-boundary workaround), but keeping them in
separate files means the simulation logic file never visually mixes with presentation-only data.
This also isn't about `.qtn` regeneration risk — these are hand-written classes (unlike
`Enemy`/`Weapon`, which *are* `.qtn`-generated), so nothing auto-regenerates them either way; the
split is purely for keeping the two concerns apart on disk. See `WeaponDataAsset.cs`/
`WeaponDataAsset.View.cs` and `EnemyActionData.cs`/`EnemyActionData.View.cs` for the two examples
in this project.

## Two different "which prefab for this data" patterns

**Top-level entities (Player, Enemy, Projectile)** — each gets its own
`Assets/_QuantumUser/Entities/*/*.prefab` + matching `*EntityPrototype.qprototype`.
The prefab **is** the Quantum `EntityPrototype`: it carries the `Enemy`/`Weapon`/etc. component
overrides (including the `AssetRef<...DataAsset>`) *and* is itself instantiated as the view. No
runtime lookup needed — Quantum resolves data asset + view together because they're baked into
the same prefab. This is the pattern to follow for **enemies**: see
[enemies.md](enemies.md#adding-a-new-enemy-type).

**Attachments on another entity (Weapon on Player)** — a `Weapon` isn't its own entity, it's a
component on the player, and which gun to show has to be resolved at runtime from
`Weapon.WeaponData` (an `AssetRef<WeaponDataAsset>`). `WeaponDataAsset.ViewPrefab`
(`WeaponDataAsset.View.cs`) holds a direct `GameObject` reference to the weapon's visual prefab;
`WeaponViewController` resolves the `WeaponDataAsset` via `f.FindAsset` on `Initialize`,
`Instantiate`s `ViewPrefab` under the player's weapon socket, and `GetComponent<WeaponView>()`s
the result. See [weapons.md](weapons.md).

**Reusable non-entity data (EnemyActionData on Enemy)** — the same partial-class pattern is used
for `EnemyActionData` (an enemy's one action, `AssetRef<EnemyActionData>` on `EnemyDataAsset`), but
the shape is different: unlike a weapon, an enemy already has its own prefab/view (it's a top-level
entity), so this isn't for "which prefab to instantiate" — it's for "which body-animation
tell/particle/ground telegraph to play, and when." `EnemyActionData.View.cs` holds four
`AttackVisualStep`s (Anticipation/Begin/OnGoing/End - one shared reusable shape, not per-subclass
fields) plus an optional `Telegraph` (`AssetRef<TelegraphData>` - its own shared, reusable asset,
not inlined data), all built-in-Unity-typed data with no spawn logic of its own.
`EnemyAttackVisualsView` (View layer, on the enemy prefab) reads `Enemy.Phase` edges,
resolves the active `EnemyActionData` fresh off the frame, and drives
`EnemyBlobAnimationView.PlayAttackStep`/particle spawning/telegraph show-hide accordingly — since
the same tuned `EnemyActionData` can be shared across multiple enemy prefabs, this also makes its
visuals automatically shared, with zero per-prefab configuration. See [enemies.md](enemies.md).

Don't reach for a runtime-resolution pattern for a brand new top-level entity type — it solves a
problem only non-entity/shared data has.
