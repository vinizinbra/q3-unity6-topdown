using System;
using System.Collections.Generic;
using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;

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
        [SerializeField] private WorldObstacleTheme obstacles;
        [SerializeField] private WorldDetailTheme details;

        public WorldThemeName WorldName => worldName;
        public WorldEnemyTheme Enemy => enemy;
        public WorldEnvironmentTheme Environment => environment;
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
