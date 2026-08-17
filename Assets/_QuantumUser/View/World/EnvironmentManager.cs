using NaughtyAttributes;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Applies a WorldTheme's cosmetic-only settings to the actual scene - the level's shared
    // MobileToonModularLevel Material (Surface/Walls, plus Height Fog color - see HeightFogColorId)
    // and the camera's background (Sky). Enemy blood color is forwarded to EffectsManager since
    // that's the actual consumer. Nothing yet calls Load() from a real "current world" source - see
    // initialTheme for previewing one without that system existing yet.
    public class EnvironmentManager : MonoBehaviour
    {
        private static readonly int SurfaceColorId = Shader.PropertyToID("_SurfaceColor");
        private static readonly int WallColorId = Shader.PropertyToID("_WallColor");

        // The shader's actual fog (not the "Base Height Gradient", which is a plain multiplicative
        // tint, not fog) - blends low geometry toward this color the same way distance fog would,
        // so it has to match Sky or the ground reads as fading into a color that doesn't exist
        // anywhere else in the scene. Derived from Sky rather than authored on WorldTheme, so
        // there's nothing to keep in sync by hand across worlds. HeightFogStrength/TopY/Falloff stay
        // at whatever the Material's own defaults are - only the color is world-specific.
        private static readonly int HeightFogColorId = Shader.PropertyToID("_HeightFogColor");
        private static readonly int HeightFogTopYId = Shader.PropertyToID("_HeightFogTopY");
        private static readonly int HeightFogFalloffId = Shader.PropertyToID("_HeightFogFalloff");
        private static readonly int HeightFogStrengthId = Shader.PropertyToID("_HeightFogStrength");

        [SerializeField, Tooltip("Shared Material used by every procedurally-placed ground/wall piece (MobileToonModularLevel shader) - colored directly, not instanced, so edits are visible immediately but persist on the asset after Play Mode stops.")]
        private Material levelMaterial;

        [SerializeField, Tooltip("Shared Material (Project/Detail Sprite Height Fog shader) for ChunkDetailScatter's procedural sprites - a SpriteRenderer-compatible reimplementation of just levelMaterial's Height Fog block (that shader is opaque/mesh-oriented and can't be assigned to a SpriteRenderer directly). Its own _HeightFogTopY/Falloff/Strength are overwritten from levelMaterial's current values every Load(), so there's nothing to keep in sync by hand - only assign the material/shader once.")]
        private Material detailSpriteMaterial;

        [SerializeField, Tooltip("Sky isn't part of the level shader - this is just the camera's background color.")]
        private Camera targetCamera;

        [SerializeField, Tooltip("Applied on Awake if set, so a theme can be previewed without whatever will eventually call Load() for the current world.")]
        [Expandable]private WorldTheme initialTheme;

        // Single source of truth for "which WorldTheme is currently active" - consumed by
        // ChunkDetailScatter (and anything else that needs the live theme, e.g. the still-unused
        // Obstacles pool) so nothing else has to independently track it.
        public static EnvironmentManager Instance { get; private set; }
        public WorldTheme CurrentTheme { get; private set; }
        public Material DetailSpriteMaterial => detailSpriteMaterial;

        private void Awake()
        {
            Instance = this;

            if (initialTheme != null)
                Load(initialTheme);
        }

        // Lets initialTheme be tweaked and reapplied from the Inspector without entering Play Mode
        // (Load only ever touches the Material/Camera/EffectsManager directly, no frame dependency).
        [Button("Apply Environment")]
        private void ApplyInitialTheme()
        {
            if (initialTheme == null)
            {
                LogHelper.Warn("EnvironmentManager", "No initialTheme assigned to apply.");
                return;
            }

            Load(initialTheme);
        }

        public void Load(WorldTheme theme)
        {
            if (theme == null)
            {
                LogHelper.Error("EnvironmentManager", "Load called with a null WorldTheme.");
                return;
            }

            CurrentTheme = theme;
            ApplyEnvironment(theme.Environment);
            EffectsManager.Instance?.SetBloodColor(theme.Enemy.BloodColor);
        }

        private void ApplyEnvironment(WorldEnvironmentTheme environment)
        {
            if (targetCamera != null)
                targetCamera.backgroundColor = environment.Sky;

            if (levelMaterial == null)
                return;

            levelMaterial.SetColor(SurfaceColorId, environment.Surface);
            levelMaterial.SetColor(WallColorId, environment.Walls);
            levelMaterial.SetColor(HeightFogColorId, environment.Sky);

            ApplyDetailSpriteHeightFog();
        }

        // Keeps detailSpriteMaterial's own Height Fog block matching levelMaterial's - Color comes
        // from environment.Sky like the level material's own, and TopY/Falloff/Strength (which
        // ApplyEnvironment never sets on levelMaterial itself, only ever authored by hand on that
        // Material asset) are copied straight from levelMaterial's current values, so there's
        // nothing to keep in sync across two separate assets by hand.
        private void ApplyDetailSpriteHeightFog()
        {
            if (detailSpriteMaterial == null || levelMaterial == null)
                return;

            detailSpriteMaterial.SetColor(HeightFogColorId, levelMaterial.GetColor(HeightFogColorId));
            detailSpriteMaterial.SetFloat(HeightFogTopYId, levelMaterial.GetFloat(HeightFogTopYId));
            detailSpriteMaterial.SetFloat(HeightFogFalloffId, levelMaterial.GetFloat(HeightFogFalloffId));
            detailSpriteMaterial.SetFloat(HeightFogStrengthId, levelMaterial.GetFloat(HeightFogStrengthId));
        }
    }
}
