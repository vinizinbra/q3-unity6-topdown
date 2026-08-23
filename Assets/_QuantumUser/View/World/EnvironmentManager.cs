using NaughtyAttributes;
using QuantumUser.View.Managers;
using QuantumUser.View.Util;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Quantum
{
    // Applies a WorldTheme's cosmetic-only settings to the actual scene - the level's shared
    // MobileToonModularLevel Material (Surface/Walls, plus Height Fog color - see HeightFogColorId;
    // and, when the theme opts in, its Surface World Noise / Surface / World Height Wall Line
    // blocks), the lake Materials' own colors (Project/LakeShader, when the theme opts in) and the
    // camera's background (Sky). Enemy blood color is forwarded to EffectsManager
    // since that's the actual consumer. CaptureInto goes the other way, baking the Material's
    // current look back into a theme. Nothing yet calls Load() from a real "current world" source -
    // see initialTheme for previewing one without that system existing yet.
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

        // "Surface World Noise" + "Surface" blocks (WorldSurfaceTheme) and the "World Height Wall
        // Line" block (WorldWallLineTheme). Unlike Surface/Walls/Sky above, these are only touched
        // when the theme opts in - see the Apply flags on both structs for why.
        private static readonly int GlobalNoiseMapId = Shader.PropertyToID("_GlobalNoiseMap");
        private static readonly int GlobalNoiseScaleId = Shader.PropertyToID("_GlobalNoiseScale");
        private static readonly int GlobalNoiseLightColorId = Shader.PropertyToID("_GlobalNoiseLightColor");
        private static readonly int GlobalNoiseDarkColorId = Shader.PropertyToID("_GlobalNoiseDarkColor");
        private static readonly int SurfaceEdgeColorId = Shader.PropertyToID("_SurfaceEdgeColor");
        private static readonly int SurfaceFadeStepsId = Shader.PropertyToID("_SurfaceFadeSteps");
        private static readonly int SurfaceLineColorId = Shader.PropertyToID("_SurfaceLineColor");
        private static readonly int SurfaceLineWidthId = Shader.PropertyToID("_SurfaceLineWidth");
        private static readonly int WallLineColorId = Shader.PropertyToID("_WallLineColor");
        private static readonly int WallLineYId = Shader.PropertyToID("_WallLineY");
        private static readonly int WallLineThicknessId = Shader.PropertyToID("_WallLineThickness");
        private static readonly int WallLineStrengthId = Shader.PropertyToID("_WallLineStrength");

        // Project/LakeShader (WorldWaterTheme) - a different Material entirely from the level's.
        private static readonly int ShallowColorId = Shader.PropertyToID("_ShallowColor");
        private static readonly int DeepColorId = Shader.PropertyToID("_DeepColor");
        private static readonly int HighlightColorId = Shader.PropertyToID("_HighlightColor");
        private static readonly int FoamColorId = Shader.PropertyToID("_FoamColor");

        [SerializeField, Tooltip("Shared Material used by every procedurally-placed ground/wall piece (MobileToonModularLevel shader) - colored directly, not instanced, so edits are visible immediately but persist on the asset after Play Mode stops.")]
        private Material levelMaterial;

        [SerializeField, Tooltip("Shared Material (Project/Detail Sprite Height Fog shader) for ChunkDetailScatter's procedural sprites - a SpriteRenderer-compatible reimplementation of just levelMaterial's Height Fog block (that shader is opaque/mesh-oriented and can't be assigned to a SpriteRenderer directly). Its own _HeightFogTopY/Falloff/Strength are overwritten from levelMaterial's current values every Load(), so there's nothing to keep in sync by hand - only assign the material/shader once.")]
        private Material detailSpriteMaterial;

        [SerializeField, Tooltip("Every Material that should take this world's water colors (Project/LakeShader). All of them get the SAME four colors, so only assign Materials meant to look alike - as of writing, Water is blue and WaterBorder is green, deliberately different palettes, so assigning both here would flatten one into the other. An array rather than one slot because a level can have several water bodies that should share a palette. Leave empty on a world with no water.")]
        private Material[] waterMaterials;

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
        public Material LevelMaterial => levelMaterial;

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

        // The exact reverse of "Apply Environment", and the one you want while actually tuning:
        // the level Material is edited in its own Inspector, in the scene, where the result is
        // visible - this bakes whatever it currently looks like straight back into initialTheme
        // without navigating to that asset. WorldTheme carries the same action ("Capture From Level
        // Material") for when you are already sitting on the theme asset instead; both funnel
        // through CaptureInto, so neither can drift from the other.
        //
        // Saved immediately rather than just marked dirty - "bake" that silently evaporates on the
        // next domain reload is worse than no button at all.
        [Button("Bake Into Theme")]
        private void BakeIntoInitialTheme()
        {
            if (initialTheme == null)
            {
                LogHelper.Warn("EnvironmentManager", "No initialTheme assigned to bake into.", this);
                return;
            }

            if (CaptureInto(initialTheme) == false)
            {
                LogHelper.Warn("EnvironmentManager", "No levelMaterial assigned - nothing to bake.", this);
                return;
            }

#if UNITY_EDITOR
            EditorUtility.SetDirty(initialTheme);
            AssetDatabase.SaveAssetIfDirty(initialTheme);
#endif
            LogHelper.Log("EnvironmentManager", $"Baked '{levelMaterial.name}' Surface/Wall Line settings into '{initialTheme.name}'.", initialTheme);
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
            ApplySurface(theme.Surface);
            ApplyWallLine(theme.WallLine);
            ApplyWater(theme.Water);
            ApplyBloodColor(theme.Enemy.BloodColor);
        }

        // Was a silent `EffectsManager.Instance?.SetBloodColor(...)`, which is how a real ordering
        // bug hid: Load runs from Awake, and if EffectsManager's own Awake had not set Instance yet
        // the colour was quietly dropped and every world used the serialized default red until
        // something re-applied the theme by hand. EffectsManager is pinned to run first now, so this
        // should not trigger in Play Mode - it warns rather than no-ops so a future regression is
        // visible instead of silent. Edit Mode is exempt: nothing has Awake'd there, and the
        // "Apply Environment"/"Apply To Scene (Debug)" buttons are used from Edit Mode routinely.
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

        // Both of these no-op unless the theme opted in, so the eleven themes authored before these
        // blocks existed (and any new world that only wants to re-colour Surface/Walls/Sky) leave
        // the Material's own hand-tuned values completely alone rather than stamping struct
        // defaults over them. See WorldSurfaceTheme.Apply.
        private void ApplySurface(WorldSurfaceTheme surface)
        {
            if (levelMaterial == null || surface.Apply == false)
                return;

            // Set even when null: the shader declares _GlobalNoiseMap's default as "gray", so an
            // unassigned map resolves to flat grey (no visible noise) rather than silently
            // inheriting whichever texture the previously-loaded world happened to leave behind.
            levelMaterial.SetTexture(GlobalNoiseMapId, surface.SurfaceNoiseMap);
            levelMaterial.SetFloat(GlobalNoiseScaleId, surface.SurfaceNoiseScale);
            levelMaterial.SetColor(GlobalNoiseLightColorId, surface.NoiseLightColor);
            levelMaterial.SetColor(GlobalNoiseDarkColorId, surface.NoiseDarkColor);

            levelMaterial.SetColor(SurfaceEdgeColorId, surface.SurfaceFadeColor);
            levelMaterial.SetFloat(SurfaceFadeStepsId, surface.SurfaceFadeSteps);
            levelMaterial.SetColor(SurfaceLineColorId, surface.EdgeLineColor);
            levelMaterial.SetFloat(SurfaceLineWidthId, surface.EdgeLineWidth);
        }

        private void ApplyWallLine(WorldWallLineTheme wallLine)
        {
            if (levelMaterial == null || wallLine.Apply == false)
                return;

            levelMaterial.SetColor(WallLineColorId, wallLine.LineColor);
            levelMaterial.SetFloat(WallLineYId, wallLine.WorldY);
            levelMaterial.SetFloat(WallLineThicknessId, wallLine.Thickness);
            levelMaterial.SetFloat(WallLineStrengthId, wallLine.Strength);
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
            }
        }

        // Reverse of ApplySurface/ApplyWallLine: pulls whatever levelMaterial is currently tuned to
        // back into a theme asset, so the workflow is "tune the Material in its own Inspector until
        // the world looks right, then bake it into that world's theme" rather than transcribing a
        // dozen values by hand. Lives here rather than on WorldTheme so the property-ID list has
        // exactly one home. Both Apply flags are switched on, since a captured theme is by
        // definition one that means to drive these blocks.
        public bool CaptureInto(WorldTheme theme)
        {
            if (theme == null || levelMaterial == null)
                return false;

            theme.SetSurface(new WorldSurfaceTheme
            {
                Apply = true,
                SurfaceNoiseMap = levelMaterial.GetTexture(GlobalNoiseMapId) as Texture2D,
                SurfaceNoiseScale = levelMaterial.GetFloat(GlobalNoiseScaleId),
                NoiseLightColor = levelMaterial.GetColor(GlobalNoiseLightColorId),
                NoiseDarkColor = levelMaterial.GetColor(GlobalNoiseDarkColorId),
                SurfaceFadeColor = levelMaterial.GetColor(SurfaceEdgeColorId),
                SurfaceFadeSteps = levelMaterial.GetFloat(SurfaceFadeStepsId),
                EdgeLineColor = levelMaterial.GetColor(SurfaceLineColorId),
                EdgeLineWidth = levelMaterial.GetFloat(SurfaceLineWidthId),
            });

            theme.SetWallLine(new WorldWallLineTheme
            {
                Apply = true,
                LineColor = levelMaterial.GetColor(WallLineColorId),
                WorldY = levelMaterial.GetFloat(WallLineYId),
                Thickness = levelMaterial.GetFloat(WallLineThicknessId),
                Strength = levelMaterial.GetFloat(WallLineStrengthId),
            });

            Material water = ResolveCaptureWaterMaterial();
            if (water != null)
            {
                theme.SetWater(new WorldWaterTheme
                {
                    Apply = true,
                    ShallowColor = water.GetColor(ShallowColorId),
                    DeepColor = water.GetColor(DeepColorId),
                    GlimmerColor = water.GetColor(HighlightColorId),
                    FoamColor = water.GetColor(FoamColorId),
                });
            }

            return true;
        }

        // Captures from the FIRST assigned water Material only - ApplyWater writes the same four
        // colors to every entry, so any of them round-trips the same values back; reading them all
        // and hoping they agree would just invent a conflict that cannot exist. A world with no
        // water Materials assigned leaves the theme's water block untouched rather than stamping
        // black over it.
        private Material ResolveCaptureWaterMaterial()
        {
            if (waterMaterials == null)
                return null;

            foreach (Material material in waterMaterials)
            {
                if (material != null)
                    return material;
            }

            return null;
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
