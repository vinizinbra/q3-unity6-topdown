namespace Quantum
{
    using System.Collections.Generic;

    // Grant/offer-eligibility for the RiftMutation pool (see docs/rift-mutations.md).
    public static unsafe class RiftMutationUtility
    {
        // The single grant funnel - every path in (a level-up pick, a Chest, a Cursed Rift reward,
        // the debug command) lands here. IsBlocked is re-checked rather than trusted from the
        // caller: the collectors already filter offers, but the debug-grant path has no collector
        // at all, and a Run-scope mutation could in principle be confirmed by two players on the
        // same tick from two independently-rolled screens.
        public static void Grant(Frame f, EntityRef entity, AssetRef<RiftMutationData> mutationRef)
        {
            RiftMutationData mutation = f.FindAsset(mutationRef);

            if (mutation == null)
            {
                Log.Error($"[RiftMutation] {mutationRef} does not resolve to a RiftMutationData - not granted");
                return;
            }

            if (IsBlocked(f, entity, mutationRef) == true)
                return;

            mutation.Apply(f, entity);
            RecordPick(f, entity, mutationRef);

            // Run-scope effects live on Frame.Global, so the run itself has to remember them -
            // RiftMutationPicks is per-entity and would happily let a second player re-apply the
            // same shared modifier.
            if (mutation.Scope == MutationScope.Run)
            {
                RecordRunPick(f, mutationRef);
            }

            RiftMutationDebugUtility.LogGranted(f, entity, mutation);
        }

        // Is this mutation ineligible for `entity` right now - already owned, already applied
        // run-wide, or incompatible with something they already have? This is the ONE place those
        // three rules live; LevelUpUtility's two mutation collectors and Grant all call it.
        public static bool IsBlocked(Frame f, EntityRef entity, AssetRef<RiftMutationData> mutationRef)
        {
            if (mutationRef.IsValid == false)
                return true;

            if (IsAlreadyPicked(f, entity, mutationRef) == true)
            {
                RiftMutationDebugUtility.LogFiltered(f, mutationRef, "already picked");
                return true;
            }

            RiftMutationData mutation = f.FindAsset(mutationRef);

            if (mutation == null)
                return true;

            if (mutation.Scope == MutationScope.Run && IsRunPickRecorded(f, mutationRef) == true)
            {
                RiftMutationDebugUtility.LogFiltered(f, mutationRef, "run-scope, already applied this run");
                return true;
            }

            // Prerequisite gate - e.g. an Accessory-dependent mutation stops being offered to a
            // player whose Accessory was removed outright by Last Bastion. Checked here rather than
            // in the collectors so Chests, Cursed Rift and the debug-grant path are covered too.
            if (mutation.IsEligible(f, entity) == false)
            {
                RiftMutationDebugUtility.LogFiltered(f, mutationRef, "prerequisite not met");
                return true;
            }

            if (TryFindIncompatiblePick(f, entity, mutationRef, mutation, out AssetRef<RiftMutationData> blocker) == true)
            {
                RiftMutationDebugUtility.LogFiltered(f, mutationRef, $"incompatible with owned {RiftMutationDebugUtility.ResolveName(f, blocker)}");
                return true;
            }

            return false;
        }

        public static bool IsAlreadyPicked(Frame f, EntityRef entity, AssetRef<RiftMutationData> mutationRef)
        {
            if (f.Unsafe.TryGetPointer<RiftMutationPicks>(entity, out var picks) == false)
                return false;

            var picked = picks->Picked;

            for (int i = 0; i < picked.Length; i++)
            {
                if (picked[i] == mutationRef)
                    return true;
            }

            return false;
        }

        // Incompatibility is checked in BOTH directions - the candidate may list an owned mutation,
        // or an owned mutation may list the candidate. That symmetry is what lets a designer author
        // an exclusive pair on just one of its two assets instead of having to remember to mirror
        // it (a mirror that would silently half-work the first time someone forgot).
        private static bool TryFindIncompatiblePick(Frame f, EntityRef entity, AssetRef<RiftMutationData> mutationRef,
            RiftMutationData mutation, out AssetRef<RiftMutationData> blocker)
        {
            blocker = default;

            if (f.Unsafe.TryGetPointer<RiftMutationPicks>(entity, out var picks) == false)
                return false;

            var picked = picks->Picked;

            for (int i = 0; i < picked.Length; i++)
            {
                if (picked[i].IsValid == false)
                    continue;

                if (ListsMutation(mutation.IncompatibleWith, picked[i]) == true)
                {
                    blocker = picked[i];
                    return true;
                }

                RiftMutationData owned = f.FindAsset(picked[i]);

                if (owned != null && ListsMutation(owned.IncompatibleWith, mutationRef) == true)
                {
                    blocker = picked[i];
                    return true;
                }
            }

            return false;
        }

        private static bool ListsMutation(List<AssetRef<RiftMutationData>> list, AssetRef<RiftMutationData> mutationRef)
        {
            if (list == null)
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == mutationRef)
                    return true;
            }

            return false;
        }

        private static bool IsRunPickRecorded(Frame f, AssetRef<RiftMutationData> mutationRef)
        {
            var runPicks = f.Global->RunMutationPicks;

            for (int i = 0; i < runPicks.Length; i++)
            {
                if (runPicks[i] == mutationRef)
                    return true;
            }

            return false;
        }

        private static void RecordRunPick(Frame f, AssetRef<RiftMutationData> mutationRef)
        {
            var runPicks = f.Global->RunMutationPicks;

            for (int i = 0; i < runPicks.Length; i++)
            {
                if (runPicks[i].IsValid == true)
                    continue;

                runPicks[i] = mutationRef;
                return;
            }

            Log.Error($"[RiftMutation] no free RunMutationPicks slot for {mutationRef} - it could be applied run-wide a second time");
        }

        private static void RecordPick(Frame f, EntityRef entity, AssetRef<RiftMutationData> mutationRef)
        {
            f.AddOrGet<RiftMutationPicks>(entity, out var picks);
            var picked = picks->Picked;

            for (int i = 0; i < picked.Length; i++)
            {
                if (picked[i].IsValid == true)
                    continue;

                picked[i] = mutationRef;
                return;
            }

            Log.Error($"[RiftMutation] {entity} has no free RiftMutationPicks slot for {mutationRef} - pick not recorded, it could be offered again");
        }
    }
}
