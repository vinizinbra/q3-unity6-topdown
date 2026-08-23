using System;
using System.Collections.Generic;
using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Quantum
{
    // Run order settled on for the world roster - ordinal values are what Unity serializes, so
    // reordering this enum reshuffles every already-authored WorldTheme's assigned name; append new
    // worlds at the end instead of inserting.
    public enum WorldThemeName
    {
        GrasslandOutpost,
        DesertOilFields, // aka Desert Oil Kingdom
        NeonFloodDistrict,
        FrozenShoppingMegaplex,
        CandyIndustrialComplex,
        VolcanicResortIsland,
        HauntedDataCathedral,
        JunkyardMoon,
        SuburbanApocalypse,
        CrystalDepths,
        AshenWastes,
    }

    [Serializable]
    public struct WorldEnemyTheme
    {
        [Tooltip("Tint applied to death VFX/decals for every enemy in this world - see EffectsManager.OnEnemyExploded.")]
        public Color BloodColor;
    }

    [Serializable]
    public struct WorldEnvironmentTheme
    {
        public Color Surface;
        public Color Walls;

        [Tooltip("Also drives the level shader's Height Fog color (see EnvironmentManager), not just the camera background - keep this LESS saturated than Surface/Walls, or geometry blending into it at low height reads as an artificial, overpowering tint instead of haze.")]
        public Color Sky;
    }

    // Per-world overrides for the level shader's "Surface World Noise" and "Surface" blocks - the
    // world-space mottling that keeps a large flat floor from reading as one dead colour, plus the
    // fade/ink treatment along a surface's own boundary. Field order and naming follow
    // Project/Mobile Toon Modular Level's own Inspector headers, so capturing a hand-tuned Material
    // into a theme (and reading the result back) lines up one-for-one with what the shader shows.
    //
    // Apply is opt-in and OFF by default on purpose: EnvironmentManager writes straight to the
    // SHARED level Material, and a theme authored before these fields existed deserializes to
    // all-zero - black colours, 0 steps, 0 width. Applying that blindly would silently wipe
    // whatever the Material is currently tuned to. Use WorldTheme's own "Capture From Level
    // Material" button to fill this in from the live look instead of typing every value by hand.
    [Serializable]
    public struct WorldSurfaceTheme
    {
        [Tooltip("Off = leave the level Material's own Surface Noise / Surface blocks exactly as authored. On = this world drives every field below.")]
        public bool Apply;

        [Tooltip("Greyscale mottling sampled in WORLD space (so it never tiles per-mesh across chunk seams), tinted between Dark and Light below - _GlobalNoiseMap. Left empty it falls back to flat grey, which reads as no noise at all.")]
        public Texture2D SurfaceNoiseMap;

        [Tooltip("World units the noise pattern repeats over - _GlobalNoiseScale. Smaller is a broader, slower mottle; larger breaks up into finer speckle. World-space, so it is independent of how big any individual chunk mesh is.")]
        public float SurfaceNoiseScale;

        [Tooltip("Colour the noise resolves to at its brightest - _GlobalNoiseLightColor.")]
        public Color NoiseLightColor;

        [Tooltip("Colour the noise resolves to at its darkest - _GlobalNoiseDarkColor.")]
        public Color NoiseDarkColor;

        [Tooltip("Colour a surface fades toward along its own edges - _SurfaceEdgeColor.")]
        public Color SurfaceFadeColor;

        [Range(1f, 6f), Tooltip("How many flat bands that edge fade is quantized into - _SurfaceFadeSteps. 1 is a single hard step.")]
        public float SurfaceFadeSteps;

        [Tooltip("Ink line drawn along a surface's boundary - _SurfaceLineColor.")]
        public Color EdgeLineColor;

        [Range(0f, 0.5f), Tooltip("Width of that boundary line - _SurfaceLineWidth. 0 removes it.")]
        public float EdgeLineWidth;
    }

    // Per-world override for the level shader's "World Height Wall Line" block - one horizontal ink
    // band stamped across every wall at a single world Y (a tidemark, strata line, scorch height).
    // Driven by world height rather than per-mesh UVs, so it stays continuous across chunk seams no
    // matter how the level generator placed or rotated the pieces.
    //
    // Same opt-in reasoning as WorldSurfaceTheme.Apply above.
    [Serializable]
    public struct WorldWallLineTheme
    {
        [Tooltip("Off = leave the level Material's own World Height Wall Line block exactly as authored. On = this world drives every field below - including Strength 0, which is how a world authors 'no line at all' rather than just leaving it unmanaged.")]
        public bool Apply;

        [Tooltip("_WallLineColor.")]
        public Color LineColor;

        [Tooltip("World-space Y the line sits at - _WallLineY.")]
        public float WorldY;

        [Tooltip("Vertical thickness in world units - _WallLineThickness.")]
        public float Thickness;

        [Range(0f, 1f), Tooltip("How strongly the line blends over the wall underneath - _WallLineStrength. 0 hides it.")]
        public float Strength;
    }

    // Per-world override for the lake's colour (Project/LakeShader, a different Material from the
    // level's - see EnvironmentManager.waterMaterials). Colour only: every non-colour knob (facet
    // steps, pattern/wave scale and speed, opacity, foam distance/ring count) stays authored on the
    // Material, since those are the look of "water" in this game rather than the look of this
    // particular world's water.
    //
    // Same opt-in reasoning as WorldSurfaceTheme.Apply.
    [Serializable]
    public struct WorldWaterTheme
    {
        [Tooltip("Off = leave the water Material(s) exactly as authored. On = this world drives every colour below.")]
        public bool Apply;

        [Tooltip("The water's main colour - _ShallowColor. The quantized sine bands resolve between this and Deep below, so this is the lighter half of what reads on screen as the wave shapes.")]
        public Color ShallowColor;

        [Tooltip("The darker half of those same bands - _DeepColor. The further apart these two are, the more the wave banding reads as distinct steps rather than one flat surface.")]
        public Color DeepColor;

        [Tooltip("Bright crest sitting on the pattern's peaks - _HighlightColor. A fake specular; the shader samples no light or normal for it.")]
        public Color GlimmerColor;

        [Tooltip("Shoreline foam - _FoamColor. Only visible on a Material with Shore Field Foam enabled, and only within its own _FoamDistance of the coast (see WaterShoreBaker).")]
        public Color FoamColor;
    }

    // Sprite pools keyed by grid footprint, matching the chunk-based level gen's own grid cells -
    // each list holds every skin variant available for that footprint in this world, picked between
    // for visual variety wherever a map-generated obstacle of that size gets placed.
    [Serializable]
    public struct WorldObstacleTheme
    {
        public List<Sprite> Size1x1;
        public List<Sprite> Size1x2;
        public List<Sprite> Size2x2;
    }

    // Ground/wall cosmetic prop pools for this world - see docs/environment-details.md. The
    // artist hand-places GroundDetailSlot/WallTopDetailSlot/WallMidDetailSlot GameObjects (position/
    // rotation/WorldSize authored directly in the chunk prefab, a placeholder Sprite assigned for
    // preview); ChunkDetailScatter deterministically rolls whether each placed slot shows anything
    // at all (the per-type *Chance field), and if so which sprite from these plain Sprite lists
    // (equal probability, no per-sprite weight), then rescales to that slot's own WorldSize; the
    // picked sprite's own pixel size/PPU is normalized away first
    // (ChunkDetailScatter.ResolveUnitScale), so swapping sprites never changes how big a slot reads
    // in the scene. Wall is split into Top/Mid (not one WallDetails pool) since a wall prop near its
    // top (vents, cracks) usually doesn't suit its middle/base (moss, pipes, scuffs) and vice versa -
    // both still get EnvironmentManager.DetailSpriteMaterial's height fog.
    [Serializable]
    public struct WorldDetailTheme
    {
        public List<Sprite> GroundDetails;

        [Range(0f, 1f), Tooltip("Chance a placed GroundDetailSlot actually shows a sprite at all - 0 hides every ground slot, 1 always shows one.")]
        public float GroundDetailChance;

        public List<Sprite> WallTopDetails;

        [Range(0f, 1f), Tooltip("Chance a placed WallTopDetailSlot actually shows a sprite at all - 0 hides every wall-top slot, 1 always shows one.")]
        public float WallTopDetailChance;

        public List<Sprite> WallMidDetails;

        [Range(0f, 1f), Tooltip("Chance a placed WallMidDetailSlot actually shows a sprite at all - 0 hides every wall-mid slot, 1 always shows one.")]
        public float WallMidDetailChance;
    }

    // Cosmetic-only per-world config - plain ScriptableObject, not AssetObject, since none of this
    // needs to be deterministic/Quantum-visible.
    [CreateAssetMenu(fileName = "WorldTheme", menuName = "Quantum/View/World Theme")]
    public class WorldTheme : ScriptableObject
    {
        [SerializeField] private WorldThemeName worldName;
        [SerializeField] private WorldEnemyTheme enemy;
        [SerializeField] private WorldEnvironmentTheme environment;
        [SerializeField] private WorldSurfaceTheme surface;
        [SerializeField] private WorldWallLineTheme wallLine;
        [SerializeField] private WorldWaterTheme water;
        [SerializeField] private WorldObstacleTheme obstacles;
        [SerializeField] private WorldDetailTheme details;

        public WorldThemeName WorldName => worldName;
        public WorldEnemyTheme Enemy => enemy;
        public WorldEnvironmentTheme Environment => environment;
        public WorldSurfaceTheme Surface => surface;
        public WorldWallLineTheme WallLine => wallLine;
        public WorldWaterTheme Water => water;
        public WorldObstacleTheme Obstacles => obstacles;

        // Written only by EnvironmentManager.CaptureInto - the material-property IDs all live over
        // there, so the read-back path stays in the one file that already owns the write path
        // rather than growing a second, drifting copy of the same property-name list here.
        internal void SetSurface(WorldSurfaceTheme value) => surface = value;
        internal void SetWallLine(WorldWallLineTheme value) => wallLine = value;
        internal void SetWater(WorldWaterTheme value) => water = value;
        public WorldDetailTheme Details => details;

        // Debug-only shortcut for previewing this specific theme from its own asset Inspector,
        // without going through EnvironmentManager's own initialTheme field - finds whichever
        // EnvironmentManager is in the currently open scene and loads this theme into it.
        [Button("Apply To Scene (Debug)")]
        private void ApplyToScene()
        {
            EnvironmentManager environmentManager = FindFirstObjectByType<EnvironmentManager>();
            if (environmentManager == null)
            {
                LogHelper.Warn("WorldTheme", "No EnvironmentManager found in the open scene to apply to.");
                return;
            }

            environmentManager.Load(this);
        }

        // The reverse of "Apply To Scene": bakes whatever the level Material currently looks like
        // into THIS theme's Surface/WallLine blocks, so a world can be tuned where it is actually
        // visible - the Material's own Inspector, live in the scene - and then committed to the
        // theme in one click, instead of being transcribed field by field. Both Apply flags come
        // back on, since a captured theme is by definition one that means to drive these blocks.
        //
        // Only the two blocks WorldSurfaceTheme/WorldWallLineTheme own; Surface/Walls/Sky are
        // deliberately left alone, since Sky also drives the camera background and Height Fog and
        // is the one part of a theme that is authored, not derived from the Material.
        [Button("Capture From Level Material")]
        private void CaptureFromLevelMaterial()
        {
            EnvironmentManager environmentManager = FindFirstObjectByType<EnvironmentManager>();
            if (environmentManager == null)
            {
                LogHelper.Warn("WorldTheme", "No EnvironmentManager found in the open scene to capture from.");
                return;
            }

            if (environmentManager.CaptureInto(this) == false)
            {
                LogHelper.Warn("WorldTheme", "EnvironmentManager has no levelMaterial assigned - nothing to capture.", environmentManager);
                return;
            }

#if UNITY_EDITOR
            // Struct fields were written through a plain setter, which Unity has no way to notice -
            // without SetDirty the capture is lost on the next reload. Saved straight away too, for
            // the same reason EnvironmentManager's own "Bake Into Theme" does.
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssetIfDirty(this);
#endif
            LogHelper.Log("WorldTheme", $"Captured Surface/Wall Line settings from '{environmentManager.LevelMaterial.name}' into '{name}'.", this);
        }

        // Debug-only: re-rolls every already-spawned chunk's ground/wall detail slots in the open
        // scene at once, so tuning Details (chance/scale range/sprite lists) doesn't require
        // restarting Play Mode or clicking each chunk's own ChunkDetailScatter.Regenerate individually.
        [Button("Regenerate All Chunk Details (Debug)")]
        private void RegenerateAllChunkDetails()
        {
            ChunkDetailScatter[] scatterers = FindObjectsByType<ChunkDetailScatter>(FindObjectsSortMode.None);
            if (scatterers.Length == 0)
            {
                LogHelper.Warn("WorldTheme", "No ChunkDetailScatter instances found in the open scene.");
                return;
            }

            foreach (ChunkDetailScatter scatterer in scatterers)
                scatterer.Regenerate();
        }
    }
}
