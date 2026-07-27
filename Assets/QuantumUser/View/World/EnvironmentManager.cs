using NaughtyAttributes;
using QuantumUser.View.Managers;
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

        [SerializeField, Tooltip("Shared Material used by every procedurally-placed ground/wall piece (MobileToonModularLevel shader) - colored directly, not instanced, so edits are visible immediately but persist on the asset after Play Mode stops.")]
        private Material levelMaterial;

        [SerializeField, Tooltip("Sky isn't part of the level shader - this is just the camera's background color.")]
        private Camera targetCamera;

        [SerializeField, Tooltip("Applied on Awake if set, so a theme can be previewed without whatever will eventually call Load() for the current world.")]
        [Expandable]private WorldTheme initialTheme;

        private void Awake()
        {
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
                Debug.LogWarning("[EnvironmentManager] No initialTheme assigned to apply.");
                return;
            }

            Load(initialTheme);
        }

        public void Load(WorldTheme theme)
        {
            if (theme == null)
            {
                Debug.LogError("[EnvironmentManager] Load called with a null WorldTheme.");
                return;
            }

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
        }
    }
}
