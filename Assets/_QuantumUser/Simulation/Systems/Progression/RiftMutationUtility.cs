namespace Quantum
{
    // Grant path for LevelUpPoolKind.RiftMutation - see LevelUpUtility.GrantOption. Mirrors
    // GlobalUpgradeUtility's dispatch shape, but simpler: every Rift Mutation is non-stackable (see
    // RiftMutationData's own comment), so Grant always records the pick - no MaxPicks > 0 gate to
    // check first.
    public static unsafe class RiftMutationUtility
    {
        public static void Grant(Frame f, EntityRef entity, AssetRef<RiftMutationData> mutationRef)
        {
            RiftMutationData mutation = f.FindAsset(mutationRef);
            mutation.Apply(f, entity);
            RecordPick(f, entity, mutationRef);
        }

        // Read by LevelUpUtility.CollectRiftMutationCandidates to exclude an already-granted
        // mutation from every future roll for this entity - offering it again would just be a dead
        // card, same reasoning IsCappedOut/AlreadyGranted already use elsewhere in LevelUpUtility.
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

            Log.Error($"[LevelUp] {entity} has no free RiftMutationPicks slot for {mutationRef} - pick not recorded, it could be offered again");
        }
    }
}
