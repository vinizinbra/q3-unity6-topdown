using System;
using System.Collections.Generic;
using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Worlds that have a WorldTheme + tileset. Values are EXPLICIT because Unity serializes the number:
    // never renumber an entry (every authored WorldTheme would silently switch world) - a new world
    // takes the next unused number. 7-10 are free (unbuilt worlds removed 2026-09-24).
    public enum WorldThemeName
    {
        GrasslandOutpost = 0,
        DesertOilFields = 1, // aka Desert Oil Kingdom
        NeonFloodDistrict = 2,
        Haunted = 3, // Dracula castle (Castlevania style)
        Zaun = 4, // Arcane-style undercity (neon suburb)
        FeudalJapan = 5, // old Japanese style (ishigaki, lanterns, sakura)
        Inferno = 6, // hellish volcano (basalt, lava, obsidian spikes)
        AlienPlanet = 11,
        Moon = 12,
        ArcticFields = 13,
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
        [Tooltip("Camera background colour. The tileset Material's water depth colour is authored on that Material - keep it matching this so walls melt into the water/background.")]
        public Color Sky;
    }

    // Per-world override for the lake's colour (Project/LakeShader, a different Material from the
    // level's - see EnvironmentManager.waterMaterials): colours + opacity. Every other knob (facet
    // steps, pattern/wave scale and speed, foam distance/ring count) stays authored on the Material,
    // since those are the look of "water" in this game rather than of this particular world's water.
    //
    // Apply is opt-in: EnvironmentManager writes straight to the SHARED water Material, so a theme
    // with Apply off leaves it exactly as authored instead of stamping zeroed colours over it.
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

        [Range(0f, 1f), Tooltip("_WaterOpacity. The water alpha-blends over whatever is behind it, so below 1 the background always bleeds through and dark water (oil) can't get truly dark - use 1 for exact colours. 0 = leave the Material's value. The water Material is shared, so give every world a value or the previous world's opacity sticks.")]
        public float Opacity;

        [Tooltip("Optional: replaces the water surface's material in this world (e.g. a Project/CloudFog material for a sea of clouds instead of water). Empty = the normal water material, coloured by the fields above. The colours above are NOT applied to an override - it's authored on its own material.")]
        public Material SurfaceMaterial;
    }

    // The world's 3D level tileset (TilesetPlatformBuilder, docs/tileset-builder.md): which
    // TilesetDefinition every chunk cube is rebuilt with. Its look (colours, hatch, water depth
    // fade, water line) is authored on that tileset's own ToonTerrain Material.
    [Serializable]
    public struct WorldTilesetTheme
    {
        [Tooltip("Tileset every chunk cube uses in this world. Empty = each cube keeps the tileset authored on its prefab.")]
        public TilesetDefinition Tileset;
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
        [SerializeField] private WorldWaterTheme water;
        [SerializeField] private WorldTilesetTheme tileset;
        [SerializeField] private WorldObstacleTheme obstacles;
        [SerializeField] private WorldDetailTheme details;

        public WorldThemeName WorldName => worldName;
        public WorldEnemyTheme Enemy => enemy;
        public WorldEnvironmentTheme Environment => environment;
        public WorldWaterTheme Water => water;
        public WorldTilesetTheme Tileset => tileset;
        public WorldObstacleTheme Obstacles => obstacles;

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
