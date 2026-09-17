using System.Collections.Generic;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Generic per-WEAPON look for a fired shot: a billboard sprite (rotation offset only) plus two
    // optional particles (spark trail, glow) whose enabled state and color come from
    // WeaponDataAsset.ProjectileVisuals instead of being hand-authored per prefab. Sibling of
    // ProjectileView, not merged into it - same one-MonoBehaviour-per-visual-concern split
    // ProjectileElementalFxView uses.
    //
    // The point: one generic sprite-based projectile prefab can now serve every weapon that wants a
    // 2D bullet - only WeaponDataAsset.ProjectileVisuals' fields change per weapon, not the prefab.
    // Resolved off Projectile.WeaponData (seeded at fire time, see Projectile.qtn's own comment)
    // rather than the owner's currently-equipped Weapon - a shot keeps the look of whatever weapon
    // actually fired it even if the owner has since swapped weapons. No-op (leaves the prefab's own
    // authored/default state) for anything not fired by a Weapon - a skill, an enemy attack, Kai's
    // Ghost Shot echo - since none of those carry a WeaponData ref.
    //
    // Both particle slots are captured by ProjectileVisualController's own GetComponentsInChildren
    // scan (it runs on Detach, called from ProjectileView.Initialize) regardless of the SetActive
    // below happening before or after that scan - includeInactive:true means the reference is kept
    // either way, and Play()/Stop() on an inactive GameObject is a no-op, so a disabled slot never
    // has to fight the controller's own show/hide-on-SpawnDelay logic.
    //
    // On a real impact, ProjectileVisualController.Finish destroys the whole detached visual root -
    // fine for a plain Renderer (SpriteRenderer included - it just vanishes with everything else
    // exactly when Finish runs, same as a mesh bullet's own body), wrong for sparkTrail/glow (ambient
    // trailing particles that should keep fading like ProjectileView's own trailParticle does).
    // Whichever particles are actually enabled, plus the sprite's own Renderer, get registered with
    // ProjectileView (RegisterExtraTrailParticles/RegisterExtraRenderers) explicitly rather than
    // relying on them already living under visualRoot in every prefab - that registration is also what
    // gives the two particles the exact same graceful-fade timing TrailParticle gets: stopped only
    // once the impact tween reaches the resolved hit point, NOT the instant the raw
    // EventProjectileDestroyed fires (this used to subscribe to that event directly and stop them
    // immediately, which cut the trail dead while the visual was still tweening the last stretch onto
    // the target).
    public class ProjectileDataVisualsView : CustomQuantumEntityViewComponent
    {
        [Header("Sprite (billboard)")]
        [SerializeField, Tooltip("Optional - the projectile's own sprite. Its Sprite image, tint, scale and rotation offset come from WeaponDataAsset.ProjectileVisuals (ProjectileSprite/ProjectileColor/ProjectileScale/ProjectileSpriteRotationOffset). Needs a BillboardVelocityAlignedSprite alongside it to receive the rotation offset (auto-added if this GameObject doesn't already have one). Leave empty for a projectile with no sprite (e.g. a 3D mesh bullet).")]
        private SpriteRenderer sprite;

        [Header("Particles - leave 'Play On Awake' off, this drives them explicitly")]
        [SerializeField, Tooltip("Enabled/disabled, tinted, scaled and lifetime-overridden from WeaponDataAsset.ProjectileVisuals (EnableProjectileSparkTrail/ProjectileSparkTrailColor/ProjectileSparkTrailScale/ProjectileSparkTrailLifetimeOverride). Faded out gracefully (not cut off) on a real impact. Leave empty if this prefab has no spark trail slot.")]
        private ParticleSystem sparkTrail;
        [SerializeField, Tooltip("Enabled/disabled, tinted and scaled from WeaponDataAsset.ProjectileVisuals (EnableProjectileGlow/ProjectileGlowColor/ProjectileGlowScale). Faded out gracefully (not cut off) on a real impact. Leave empty if this prefab has no glow slot.")]
        private ParticleSystem glow;

        [Header("Ground light")]
        [SerializeField, Tooltip("Enabled/disabled and tinted from WeaponDataAsset.ProjectileVisuals (EnableProjectileLight/ProjectileLightColor) - the pooled GroundBlobManager light, not a real Light. Needs no extra registration with ProjectileView: HasLight releases its blob on OnDisable, which Unity already calls right before this GameObject is destroyed, whether that's the whole detached visual (on a real impact) or this component alone. Leave empty for a projectile with no ground light.")]
        private HasLight light;

        // Cached in ApplySprite, fed the real Projectile.Velocity every tick in QUpdate below - see
        // BillboardVelocityAlignedSprite.SetVelocityOverride's own comment for why.
        private BillboardVelocityAlignedSprite _billboard;

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);

            Frame frame = game.Frames.Predicted;
            if (frame == null || frame.TryGet<Projectile>(_entityRef, out var projectile) == false)
            {
                LogHelper.Log("ProjFlow", $"[{_entityRef}] DataVisuals: no Projectile component on this frame - skipping", this);
                return;
            }

            WeaponDataAsset weaponData = frame.FindAsset(projectile.WeaponData);
            if (weaponData == null)
            {
                LogHelper.Log("ProjFlow", $"[{_entityRef}] DataVisuals: WeaponData asset ref is invalid/unresolved (projectile.WeaponData.Id={projectile.WeaponData.Id}) - skipping, prefab keeps its authored defaults", this);
                return;
            }

            ProjectileVisualsConfig visuals = weaponData.ProjectileVisuals;

            LogHelper.Log("ProjFlow", $"[{_entityRef}] DataVisuals: weaponData={weaponData.name} sprite={(sprite != null)} sparkTrail={(sparkTrail != null)}(enable={visuals.EnableProjectileSparkTrail}) glow={(glow != null)}(enable={visuals.EnableProjectileGlow}) light={(light != null)}(enable={visuals.EnableProjectileLight})", this);

            ApplySprite(visuals);
            ApplyParticle(sparkTrail, visuals.EnableProjectileSparkTrail, visuals.ProjectileSparkTrailColor, visuals.ProjectileSparkTrailScale, visuals.ProjectileSparkTrailLifetimeOverride);
            ApplyParticle(glow, visuals.EnableProjectileGlow, visuals.ProjectileGlowColor, visuals.ProjectileGlowScale, lifetimeOverride: 0f);
            ApplyLight(visuals);

            RegisterWithProjectileView(visuals);
        }

        private void RegisterWithProjectileView(ProjectileVisualsConfig visuals)
        {
            ProjectileView projectileView = GetComponent<ProjectileView>();
            if (projectileView == null)
            {
                LogHelper.Warn("ProjFlow", $"[{_entityRef}] DataVisuals: no ProjectileView on this GameObject - extra particles/renderer will NOT get graceful-fade/catch-up treatment, they'll be cut off instantly on impact", this);
                return;
            }

            if (sprite != null)
                projectileView.RegisterExtraRenderers(new Renderer[] { sprite });

            // Dedicated ProjectileDestroyColor/ProjectileDestroyGlowColor/ProjectileDestroyScale -
            // the impact burst is tuned independently of how the live projectile looks in flight, not
            // a reuse of ProjectileGlowColor/ProjectileSparkTrailColor. Alpha forced to 1 on both
            // colors regardless of their own alpha - unrelated to how opaque the shared destroy burst
            // should read, and a low alpha there would wash the whole impact effect out.
            Color destroyRootColor = visuals.ProjectileDestroyColor;
            destroyRootColor.a = 1f;
            projectileView.RegisterDestroyEffectColor(destroyRootColor);

            Color destroyChildColor = visuals.ProjectileDestroyGlowColor;
            destroyChildColor.a = 1f;
            projectileView.RegisterDestroyEffectChildColor(destroyChildColor);

            projectileView.RegisterDestroyEffectScale(visuals.ProjectileDestroyScale);

            var activeTrails = new List<ParticleSystem>(2);
            if (visuals.EnableProjectileSparkTrail && sparkTrail != null)
                activeTrails.Add(sparkTrail);
            if (visuals.EnableProjectileGlow && glow != null)
                activeTrails.Add(glow);

            LogHelper.Log("ProjFlow", $"[{_entityRef}] DataVisuals: registered {activeTrails.Count} extra trail particle(s), extraRenderer={(sprite != null)}", this);

            if (activeTrails.Count > 0)
                projectileView.RegisterExtraTrailParticles(activeTrails.ToArray());
        }

        private void ApplySprite(ProjectileVisualsConfig visuals)
        {
            if (sprite == null)
                return;

            if (visuals.ProjectileSprite != null)
                sprite.sprite = visuals.ProjectileSprite;

            sprite.color = visuals.ProjectileColor;
            sprite.transform.localScale = visuals.ProjectileScale;

            _billboard = sprite.GetComponent<BillboardVelocityAlignedSprite>();
            if (_billboard == null)
                _billboard = sprite.gameObject.AddComponent<BillboardVelocityAlignedSprite>();

            _billboard.AngleOffset = visuals.ProjectileSpriteRotationOffset;
        }

        private static void ApplyParticle(ParticleSystem particle, bool enable, Color color, Vector3 scale, float lifetimeOverride)
        {
            if (particle == null)
                return;

            particle.gameObject.SetActive(enable);
            if (enable == false)
                return;

            particle.transform.localScale = scale;

            ParticleSystem.MainModule main = particle.main;
            main.startColor = color;

            if (lifetimeOverride > 0f)
                main.startLifetime = lifetimeOverride;

            particle.Play(withChildren: false);
        }

        private void ApplyLight(ProjectileVisualsConfig visuals)
        {
            if (light == null)
                return;

            // Force through disabled first regardless of the prefab's authored default - HasLight
            // bakes its color into the acquired ground blob once, in OnEnable, so if this were already
            // enabled at spawn (Acquire already ran with whatever color/default it started with),
            // setting LightColor afterward alone wouldn't retint the already-active blob.
            light.enabled = false;
            if (visuals.EnableProjectileLight == false)
                return;

            light.LightColor = visuals.ProjectileLightColor;
            light.enabled = true;
        }

        // The only per-tick work: feed the billboard sprite the REAL simulation velocity instead of
        // letting it derive one from its own frame-to-frame position delta, which needs a settled
        // frame of real movement first and reads as a lag right after spawn or through
        // ProjectileVisualController's own catch-up ramp - see BillboardVelocityAlignedSprite.
        // SetVelocityOverride's own comment. Everything else this component does is one-time
        // spawn-time setup (Initialize above).
        protected override void QUpdate(QuantumGame game)
        {
            if (_billboard == null)
                return;

            Frame frame = game.Frames.Predicted;
            if (frame != null && frame.TryGet<Projectile>(_entityRef, out var projectile))
                _billboard.SetVelocityOverride(projectile.Velocity.ToUnityVector3());
        }
    }
}
