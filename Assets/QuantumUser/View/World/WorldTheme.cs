using System;
using System.Collections.Generic;
using NaughtyAttributes;
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

    // Cosmetic-only per-world config - plain ScriptableObject, not AssetObject, since none of this
    // needs to be deterministic/Quantum-visible.
    [CreateAssetMenu(fileName = "WorldTheme", menuName = "Quantum/View/World Theme")]
    public class WorldTheme : ScriptableObject
    {
        [SerializeField] private WorldThemeName worldName;
        [SerializeField] private WorldEnemyTheme enemy;
        [SerializeField] private WorldEnvironmentTheme environment;
        [SerializeField] private WorldObstacleTheme obstacles;

        public WorldThemeName WorldName => worldName;
        public WorldEnemyTheme Enemy => enemy;
        public WorldEnvironmentTheme Environment => environment;
        public WorldObstacleTheme Obstacles => obstacles;

        // Debug-only shortcut for previewing this specific theme from its own asset Inspector,
        // without going through EnvironmentManager's own initialTheme field - finds whichever
        // EnvironmentManager is in the currently open scene and loads this theme into it.
        [Button("Apply To Scene (Debug)")]
        private void ApplyToScene()
        {
            EnvironmentManager environmentManager = FindFirstObjectByType<EnvironmentManager>();
            if (environmentManager == null)
            {
                Debug.LogWarning("[WorldTheme] No EnvironmentManager found in the open scene to apply to.");
                return;
            }

            environmentManager.Load(this);
        }
    }
}
