using System.Collections.Generic;
using NaughtyAttributes;
using QuantumUser.View.Util;
using UnityEngine;

namespace Quantum
{
    // Resolves hand-placed GroundDetailSlot/WallTopDetailSlot/WallMidDetailSlot props once a
    // chunk's view is instantiated - see docs/environment-details.md. The artist authors each slot's own
    // position/rotation directly in the chunk prefab (this used to be computed procedurally -
    // ScatterGround/ScatterWalls/wall-bounds math and everything that came with it, e.g. camera-
    // angle scale compensation, floor-Y/wall-bounds derivation - none of that is needed anymore,
    // since a hand-placed slot's transform is already correct however the artist wants it); this
    // component's only job per slot is deterministically deciding whether it shows anything at all
    // (its type's own *DetailChance) and, if so, which sprite from the matching WorldTheme.Details
    // pool (an empty pool just disables the slot regardless of chance) - seeded from
    // RuntimeConfig.Seed so every client/split-screen instance agrees. Entirely View-side: never
    // touches simulation state, only reads Chunk.OriginCellX/Z once for the seed.
    public class ChunkDetailScatter : CustomQuantumEntityViewComponent
    {
        private bool _generated;

        public override void Initialize(QuantumGame game)
        {
            base.Initialize(game);
            _generated = false;
            TryGenerate(game);
        }

        protected override void QUpdate(QuantumGame game)
        {
            TryGenerate(game);
        }

        // Public so WorldTheme's own "Regenerate All Chunk Details (Debug)" button can call this on
        // every ChunkDetailScatter in the scene at once (FindObjectsByType), not just this instance.
        [Button("Regenerate (Test)")]
        public void Regenerate()
        {
            if (Application.isPlaying == false || _game == null || _entityRef == EntityRef.None)
            {
                LogHelper.Warn("ChunkDetailScatter", "Can only regenerate in Play Mode, once this chunk's entity has spawned.", this);
                return;
            }

            _generated = false;
            TryGenerate(_game, verbose: true);
        }

        // Retried from QUpdate until this entity's own Chunk is actually readable and a WorldTheme
        // is actually active, same shape as ColliderVisualScaleView.TryApply - either can land after
        // Initialize does. verbose is only true for the manual "Regenerate (Test)" button - the
        // passive per-frame QUpdate retry stays silent on a "not ready yet" bail so it doesn't spam
        // the console every frame while waiting.
        private void TryGenerate(QuantumGame game, bool verbose = false)
        {
            if (_generated)
                return;

            Frame frame = game.Frames.Predicted;
            if (frame.TryGet<Chunk>(_entityRef, out Chunk chunk) == false)
            {
                if (verbose)
                    LogHelper.Warn("ChunkDetailScatter", "No Chunk component readable on this entity yet.", this);
                return;
            }

            WorldTheme theme = EnvironmentManager.Instance != null ? EnvironmentManager.Instance.CurrentTheme : null;
            if (theme == null)
            {
                if (verbose)
                {
                    string reason = EnvironmentManager.Instance == null
                        ? "No EnvironmentManager found in the scene."
                        : "EnvironmentManager.CurrentTheme is null - no WorldTheme is currently active (see WorldTheme's 'Apply To Scene (Debug)' button).";
                    LogHelper.Warn("ChunkDetailScatter", reason, this);
                }

                return;
            }

            _generated = true;

            WorldDetailTheme details = theme.Details;
            System.Random rng = new System.Random(CombineSeed(frame.RuntimeConfig.Seed, chunk.OriginCellX, chunk.OriginCellZ));
            Material wallMaterial = EnvironmentManager.Instance.DetailSpriteMaterial;

            int groundSlots = 0, groundShown = 0;
            foreach (GroundDetailSlot slot in GetComponentsInChildren<GroundDetailSlot>())
            {
                groundSlots++;
                if (ResolveSlot(slot.GetComponent<SpriteRenderer>(), slot.WorldSize, details.GroundDetails, details.GroundDetailChance, null, rng))
                    groundShown++;
            }

            // Only positions that actually passed their chance roll go in here - a slot that exists
            // but stayed hidden must NOT restrict nearby wall variants (see
            // CubeVisualBuilder.ShownDetailPositions's own comment for why this can't just be
            // inferred from slot presence).
            List<Vector3> shownWallPositions = new List<Vector3>();

            int wallTopSlots = 0, wallTopShown = 0;
            foreach (WallTopDetailSlot slot in GetComponentsInChildren<WallTopDetailSlot>())
            {
                wallTopSlots++;
                if (ResolveSlot(slot.GetComponent<SpriteRenderer>(), slot.WorldSize, details.WallTopDetails, details.WallTopDetailChance, wallMaterial, rng))
                {
                    wallTopShown++;
                    shownWallPositions.Add(slot.transform.position);
                }
            }

            int wallMidSlots = 0, wallMidShown = 0;
            foreach (WallMidDetailSlot slot in GetComponentsInChildren<WallMidDetailSlot>())
            {
                wallMidSlots++;
                if (ResolveSlot(slot.GetComponent<SpriteRenderer>(), slot.WorldSize, details.WallMidDetails, details.WallMidDetailChance, wallMaterial, rng))
                {
                    wallMidShown++;
                    shownWallPositions.Add(slot.transform.position);
                }
            }

            // Cubes that don't opt into detail avoidance already drew themselves normally at their
            // own Start() (see CubeVisualBuilder.Start()) - skip those, only trigger the ones that
            // deliberately waited for this. If nothing ended up shown this chunk (e.g. the chance
            // roll missed every slot, or none were placed), shownWallPositions is empty and every
            // avoidance cube just generates normally - no restriction applied anywhere.
            int avoidanceCubes = 0;
            foreach (CubeVisualBuilder cube in GetComponentsInChildren<CubeVisualBuilder>())
            {
                if (cube.HasDetailAvoidance == false)
                    continue;

                avoidanceCubes++;
                cube.ShownDetailPositions = shownWallPositions;
                cube.Generate();
            }

            LogHelper.Log("ChunkDetailScatter",
                $"{chunk.Type} @ ({chunk.OriginCellX},{chunk.OriginCellZ}) using theme '{theme.name}': " +
                $"ground {groundShown}/{groundSlots} slots shown (chance={details.GroundDetailChance}, sprites={CountAssigned(details.GroundDetails)}) | " +
                $"wall-top {wallTopShown}/{wallTopSlots} slots shown (chance={details.WallTopDetailChance}, sprites={CountAssigned(details.WallTopDetails)}) | " +
                $"wall-mid {wallMidShown}/{wallMidSlots} slots shown (chance={details.WallMidDetailChance}, sprites={CountAssigned(details.WallMidDetails)}) | " +
                $"{shownWallPositions.Count} shown wall detail(s) forwarded to {avoidanceCubes} avoidance-enabled cube(s).", this);
        }

        // One hand-placed slot's whole resolution: roll whether it shows anything at all
        // (rng.NextDouble() < chance), and if so pick a sprite (equal probability, no per-sprite
        // weight - empty pool just disables the slot either way) and rescale to worldSize,
        // independent of the picked sprite's own pixel size/PPU (ResolveUnitScale). Never touches
        // position/rotation - those stay exactly as the artist placed them. material is only
        // non-null for wall slots (EnvironmentManager.DetailSpriteMaterial, assigned via
        // sharedMaterial - NOT .material, which would silently instantiate a per-renderer copy and
        // defeat the whole "one material, tinted once by EnvironmentManager" point).
        private static bool ResolveSlot(SpriteRenderer renderer, float worldSize, List<Sprite> pool, float chance, Material material, System.Random rng)
        {
            bool show = chance > 0f && pool != null && pool.Count > 0 && rng.NextDouble() < chance;
            Sprite sprite = show ? PickRandom(pool, rng) : null;

            renderer.enabled = sprite != null;
            if (sprite == null)
                return false;

            renderer.sprite = sprite;
            if (material != null)
                renderer.sharedMaterial = material;

            renderer.transform.localScale = Vector3.one * (worldSize * ResolveUnitScale(sprite));
            return true;
        }

        // Distinguishes "list has N slots" (Count) from "N of those slots actually have a Sprite
        // dragged in" - a list resized in the Inspector without filling every element has a nonzero
        // Count but a lower (or zero) assigned count, and would otherwise silently place nothing.
        private static int CountAssigned(List<Sprite> sprites)
        {
            if (sprites == null)
                return 0;

            int assigned = 0;
            foreach (Sprite sprite in sprites)
            {
                if (sprite != null)
                    assigned++;
            }

            return assigned;
        }

        // Equal-probability pick - no per-sprite weight, kept deliberately simple.
        private static Sprite PickRandom(List<Sprite> sprites, System.Random rng)
        {
            return sprites[rng.Next(sprites.Count)];
        }

        private static float ResolveUnitScale(Sprite sprite)
        {
            float largestDimension = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            return largestDimension > 0f ? 1f / largestDimension : 1f;
        }

        // Manual deterministic combine - NOT HashCode.Combine, which mixes in a per-process random
        // seed by design (hash-flood mitigation) and would give a different layout on every client.
        private static int CombineSeed(int seed, int originCellX, int originCellZ)
        {
            unchecked
            {
                int hash = seed;
                hash = hash * 486187739 + originCellX;
                hash = hash * 486187739 + originCellZ;
                return hash;
            }
        }
    }
}
