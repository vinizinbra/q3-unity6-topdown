using NaughtyAttributes;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Applies a WorldTheme's cosmetic-only settings to the scene: the camera background (Sky), the
    // 3D level tileset every chunk cube is rebuilt with (TilesetPlatformBuilder, see
    // docs/tileset-builder.md), the shared lake Materials' colours/opacity (Project/LakeShader) and
    // the enemy blood colour (forwarded to EffectsManager, its actual consumer).
    //
    // Level look (surface/wall colours, hatching, water depth fade, water line) is NOT driven from
    // here any more: every biome has its own tileset with its own ToonTerrain Material, so those
    // values are authored directly on that Material. Only things SHARED between worlds (camera,
    // water Material, VFX colour) or not a Material at all (which tileset) live on the theme.
    public class EnvironmentManager : MonoBehaviour
    {
        // Project/LakeShader (WorldWaterTheme).
        private static readonly int ShallowColorId = Shader.PropertyToID("_ShallowColor");
        private static readonly int DeepColorId = Shader.PropertyToID("_DeepColor");
        private static readonly int HighlightColorId = Shader.PropertyToID("_HighlightColor");
        private static readonly int FoamColorId = Shader.PropertyToID("_FoamColor");
        private static readonly int WaterOpacityId = Shader.PropertyToID("_WaterOpacity");

        [SerializeField, Tooltip("Shared Material (Project/Detail Sprite Height Fog shader) ChunkDetailScatter assigns to wall detail sprites.")]
        private Material detailSpriteMaterial;

        [SerializeField, Tooltip("Every Material that should take this world's water colours/opacity (Project/LakeShader). All of them get the SAME values, so only assign Materials meant to look alike. Leave empty on a world with no water.")]
        private Material[] waterMaterials;

        [SerializeField, Tooltip("Camera whose background colour is the theme's Sky.")]
        private Camera targetCamera;

        [SerializeField, Tooltip("Applied on Awake if set, so a theme can be previewed without whatever will eventually call Load() for the current world.")]
        [Expandable] private WorldTheme initialTheme;

        // Single source of truth for "which WorldTheme is currently active" - consumed by
        // ChunkDetailScatter (and anything else that needs the live theme).
        // Renderers that showed a waterMaterials entry when first looked up, with that original material -
        // WorldWaterTheme.SurfaceMaterial swaps onto these and a world without an override restores them.
        private readonly System.Collections.Generic.List<(Renderer renderer, Material original)> waterRenderers = new();

        public static EnvironmentManager Instance { get; private set; }
        public WorldTheme CurrentTheme { get; private set; }
        public Material DetailSpriteMaterial => detailSpriteMaterial;

        private void Awake()
        {
            Instance = this;

            if (initialTheme != null)
                Load(initialTheme);
        }

        // Lets initialTheme be tweaked and reapplied from the Inspector without entering Play Mode.
        [Button("Apply Environment")]
        private void ApplyInitialTheme()
        {
            if (initialTheme == null)
            {
                LogHelper.Warn("EnvironmentManager", "No initialTheme assigned to apply.", this);
                return;
            }

            Load(initialTheme);
        }

        public void Load(WorldTheme theme)
        {
            if (theme == null)
            {
                LogHelper.Error("EnvironmentManager", "Load called with a null WorldTheme.", this);
                return;
            }

            CurrentTheme = theme;

            if (targetCamera != null)
                targetCamera.backgroundColor = theme.Environment.Sky;

            // Swaps every chunk cube to this world's tileset, rebuilding what's already built (so this
            // also works from the Edit Mode "Apply Environment" button).
            TilesetPlatformBuilder.SetTilesetOverride(theme.Tileset.Tileset, rebuild: true);

            ApplyWater(theme.Water);
            ApplyWaterSurface(theme.Water.SurfaceMaterial);
            ApplyBloodColor(theme.Enemy.BloodColor);
        }

        // Warns rather than silently no-ops in Play Mode: EffectsManager is pinned to Awake first, so
        // a missing Instance there is a real ordering regression. Edit Mode is exempt (nothing has
        // Awake'd; the Apply buttons are used from Edit Mode routinely).
        private void ApplyBloodColor(Color bloodColor)
        {
            if (EffectsManager.Instance != null)
            {
                EffectsManager.Instance.SetBloodColor(bloodColor);
                return;
            }

            if (Application.isPlaying)
                LogHelper.Warn("EnvironmentManager", "No EffectsManager.Instance yet - this world's blood color was not applied to death VFX.", this);
        }

        // Swaps the water surface's material for the theme's override (sea of clouds etc.), or restores
        // the original water material. Renderers are found once, by which of them use a waterMaterials
        // entry, so no scene wiring is needed.
        private void ApplyWaterSurface(Material surface)
        {
            if (waterRenderers.Count == 0 && waterMaterials != null)
            {
                foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (r.sharedMaterial != null && System.Array.IndexOf(waterMaterials, r.sharedMaterial) >= 0)
                        waterRenderers.Add((r, r.sharedMaterial));
                }
            }

            foreach (var (r, original) in waterRenderers)
            {
                if (r != null)
                    r.sharedMaterial = surface != null ? surface : original;
            }
        }

        private void ApplyWater(WorldWaterTheme water)
        {
            if (waterMaterials == null || water.Apply == false)
                return;

            foreach (Material material in waterMaterials)
            {
                if (material == null)
                    continue;

                material.SetColor(ShallowColorId, water.ShallowColor);
                material.SetColor(DeepColorId, water.DeepColor);
                material.SetColor(HighlightColorId, water.GlimmerColor);
                material.SetColor(FoamColorId, water.FoamColor);
                if (water.Opacity > 0f)
                    material.SetFloat(WaterOpacityId, water.Opacity);
            }
        }
    }
}
