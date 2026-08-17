namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Cursed Rift's own interaction session - see CursedRift.qtn/docs/breathing-poi.md. Deliberately
    // a Cursed-Rift-specific interaction (not a generic transaction engine) - what's generic is
    // POI availability/usage (Poi.qtn) and the Base-Skill redirect (ContextInteraction.qtn), not
    // this two-step sacrifice/mutation flow itself.
    public static unsafe class CursedRiftUtility
    {
        private struct Candidate
        {
            public AssetRef<SacrificeDefinition> Sacrifice;
            public int Weight;
        }

        // Re-checked by TryBeginInteraction right before actually starting - "am I still fully
        // eligible the instant the button press is processed this same tick," defense-in-depth
        // independent of whatever ContextInteractionSystem last resolved. Busy is checked here too
        // (not just at the ContextInteractionSystem call site) so this stays a safe, standalone
        // entry point on its own.
        public static bool CanInteract(Frame f, EntityRef player, EntityRef rift)
        {
            if (f.Has<CursedRiftInteraction>(player) == true)
                return false;

            return ResolveInteractionState(f, player, rift) == ContextInteractionState.Available;
        }

        // Read by ContextInteractionSystem's own per-kind dispatch (radius/closest-candidate
        // resolution already happened there via the sibling Interactable component; busy-ness is
        // also checked at that call site, uniformly across every InteractableKind, not repeated
        // here) - the richer WHY behind CanInteract's bool, so the world-space prompt
        // (InteractionPromptWidget) can explain itself instead of silently hiding.
        public static ContextInteractionState ResolveInteractionState(Frame f, EntityRef player, EntityRef rift)
        {
            if (f.Unsafe.TryGetPointer<CursedRift>(rift, out var cursedRift) == false)
                return ContextInteractionState.None;

            if (PoiAvailabilityUtility.IsAvailable(f, cursedRift->Availability) == false)
                return ContextInteractionState.PhaseUnavailable;

            if (PoiUsageUtility.CanUse(f, player, rift, cursedRift->UsagePolicy) == false)
                return ContextInteractionState.AlreadyUsed;

            return ContextInteractionState.Available;
        }

        // Called from SkillSystem when a locked-in ContextInteraction.ActiveTarget's Base Skill
        // button is pressed. Re-validates in full (never trusts the View/target resolution alone -
        // see docs/breathing-poi.md's own "Interaction validity confirmed in deterministic Quantum
        // simulation" requirement), rolls up to CursedRiftConfig.SacrificeChoiceCount eligible
        // sacrifices, and opens the Sacrifice stage. A no-op (logged, not silently swallowed) if
        // nothing is eligible - the player simply gets no interaction that tick, same as pressing a
        // skill button with 0 charges.
        public static void TryBeginInteraction(Frame f, EntityRef player, EntityRef rift)
        {
            if (CanInteract(f, player, rift) == false)
                return;

            if (f.RuntimeConfig.CursedRiftConfig.IsValid == false)
            {
                Log.Error("[CursedRift] interaction requested but RuntimeConfig has no CursedRiftConfig assigned - ignored");
                return;
            }

            CursedRiftConfig config = f.FindAsset(f.RuntimeConfig.CursedRiftConfig);

            if (config.SacrificePool.IsValid == false)
            {
                Log.Error("[CursedRift] CursedRiftConfig has no SacrificePool assigned - ignored");
                return;
            }

            SacrificePoolData pool = f.FindAsset(config.SacrificePool);
            AssetRef<SacrificeDefinition>[] rolled = RollSacrificeOptions(f, player, pool, config.SacrificeChoiceCount);

            if (rolled.Length == 0)
            {
                Log.Debug($"[CursedRift] {player} has no eligible sacrifices right now - interaction skipped");
                return;
            }

            f.AddOrGet<CursedRiftInteraction>(player, out var interaction);
            interaction->Rift = rift;
            interaction->State = CursedRiftInteractionState.SelectingSacrifice;

            var choices = interaction->SacrificeChoices;

            for (int i = 0; i < choices.Length; i++)
            {
                choices[i] = i < rolled.Length ? rolled[i] : default;
            }

            interaction->SacrificeChoiceCount = (byte)rolled.Length;
            interaction->MutationChoiceCount = 0;

            Log.Debug($"[CursedRift] {player} began an interaction with {rift} - {rolled.Length} sacrifice option(s)");
        }

        // Weighted draw without replacement among currently-eligible sacrifices only - same shape
        // LevelUpUtility.RollOptionsFor/DrawWeighted uses, kept as its own small implementation
        // rather than forced through a shared generic helper since AssetRef<SacrificeDefinition>
        // isn't a LevelUpOption (no Kind/Slot/WeaponData baggage to carry).
        private static AssetRef<SacrificeDefinition>[] RollSacrificeOptions(Frame f, EntityRef player, SacrificePoolData pool, int choiceCount)
        {
            List<Candidate> candidates = new List<Candidate>();
            int totalWeight = 0;

            for (int i = 0; i < pool.Sacrifices.Count; i++)
            {
                AssetRef<SacrificeDefinition> sacrificeRef = pool.Sacrifices[i];

                if (sacrificeRef.IsValid == false)
                    continue;

                SacrificeDefinition data = f.FindAsset(sacrificeRef);

                if (data == null || data.IsEligible(f, player) == false)
                    continue;

                candidates.Add(new Candidate { Sacrifice = sacrificeRef, Weight = data.Weight });
                totalWeight += data.Weight;
            }

            int drawCount = choiceCount < candidates.Count ? choiceCount : candidates.Count;
            AssetRef<SacrificeDefinition>[] rolled = new AssetRef<SacrificeDefinition>[drawCount];

            for (int slot = 0; slot < drawCount && totalWeight > 0; slot++)
            {
                int roll = f.RNG->Next(0, totalWeight);
                int cursor = 0;
                int pick = candidates.Count - 1;

                for (int i = 0; i < candidates.Count; i++)
                {
                    cursor += candidates[i].Weight;

                    if (roll < cursor)
                    {
                        pick = i;
                        break;
                    }
                }

                rolled[slot] = candidates[pick].Sacrifice;
                totalWeight -= candidates[pick].Weight;
                candidates.RemoveAt(pick);
            }

            return rolled;
        }

        // Called from CursedRiftSystem when a SelectSacrificeCommand lands - clicking a sacrifice
        // card commits immediately (applies its cost) and rolls straight into SelectingMutation,
        // same "one click = one irreversible pick" idiom every other Choose Window screen already
        // uses. No separate confirm step - re-validates eligibility one last time (defensive
        // against it having become invalid between roll and click; nothing currently runs
        // mid-interaction to actually cause that, but the check is cheap and correct either way).
        public static void SelectSacrifice(Frame f, EntityRef player, CursedRiftInteraction* interaction, int optionIndex)
        {
            if (interaction->State != CursedRiftInteractionState.SelectingSacrifice)
                return;

            if (optionIndex < 0 || optionIndex >= interaction->SacrificeChoiceCount)
            {
                Log.Error($"[CursedRift] {player} sent SelectSacrifice OptionIndex {optionIndex}, outside 0-{interaction->SacrificeChoiceCount - 1} - ignored");
                return;
            }

            AssetRef<SacrificeDefinition> sacrificeRef = interaction->SacrificeChoices[optionIndex];

            if (sacrificeRef.IsValid == false)
                return;

            SacrificeDefinition sacrifice = f.FindAsset(sacrificeRef);

            if (sacrifice == null || sacrifice.IsEligible(f, player) == false)
            {
                Log.Debug($"[CursedRift] {player}'s selected sacrifice {sacrificeRef} is no longer eligible - selection ignored");
                return;
            }

            sacrifice.ApplyCost(f, player);

            RollMutationChoices(f, player, interaction);
            interaction->State = CursedRiftInteractionState.SelectingMutation;

            Log.Debug($"[CursedRift] {player} chose sacrifice {sacrificeRef} - payment applied, rolling mutation reward");
        }

        // Called from CursedRiftSystem when a CancelCursedRiftCommand lands - only meaningful
        // pre-payment (SelectingSacrifice; a no-op once SelectingMutation, since SelectSacrifice
        // above already applied the cost by then - irreversible past that point, same as any other
        // Choose Window pick).
        public static void Cancel(Frame f, EntityRef player, CursedRiftInteraction* interaction)
        {
            if (interaction->State != CursedRiftInteractionState.SelectingSacrifice)
                return;

            f.Remove<CursedRiftInteraction>(player);
            Log.Debug($"[CursedRift] {player} cancelled their interaction before paying anything");
        }

        private static void RollMutationChoices(Frame f, EntityRef player, CursedRiftInteraction* interaction)
        {
            var choices = interaction->MutationChoices;

            if (f.RuntimeConfig.LevelUpConfig.IsValid == false || f.RuntimeConfig.CursedRiftConfig.IsValid == false)
            {
                Log.Error("[CursedRift] LevelUpConfig/CursedRiftConfig not assigned on RuntimeConfig - no mutation reward rolled");
                interaction->MutationChoiceCount = 0;
                return;
            }

            LevelUpConfig levelUpConfig = f.FindAsset(f.RuntimeConfig.LevelUpConfig);
            CursedRiftConfig cursedRiftConfig = f.FindAsset(f.RuntimeConfig.CursedRiftConfig);
            LevelUpOption[] rolled = LevelUpUtility.RollMutationOptions(f, player, levelUpConfig, cursedRiftConfig.MutationChoiceCount);

            for (int i = 0; i < choices.Length; i++)
            {
                choices[i] = i < rolled.Length ? rolled[i] : default;
            }

            interaction->MutationChoiceCount = (byte)rolled.Length;
        }

        // Called from CursedRiftSystem when a SelectMutationCommand lands - grants the chosen Rift
        // Mutation via the existing RiftMutationUtility.Grant (100% reused, zero duplication),
        // marks this Rift consumed for this player under its own configured usage policy, and
        // completes the interaction (component removed - its absence IS "Completed", same
        // convention LevelUpChoice already uses).
        public static void SelectMutation(Frame f, EntityRef player, CursedRiftInteraction* interaction, int optionIndex)
        {
            if (interaction->State != CursedRiftInteractionState.SelectingMutation)
                return;

            if (optionIndex < 0 || optionIndex >= interaction->MutationChoiceCount)
            {
                Log.Error($"[CursedRift] {player} sent SelectMutation OptionIndex {optionIndex}, outside 0-{interaction->MutationChoiceCount - 1} - ignored");
                return;
            }

            LevelUpOption option = interaction->MutationChoices[optionIndex];

            if (option.Kind == LevelUpPoolKind.RiftMutation && option.Upgrade.IsValid == true)
            {
                var mutationRef = new AssetRef<RiftMutationData>(option.Upgrade.Id);
                RiftMutationUtility.Grant(f, player, mutationRef);
                LevelUpUtility.RecordHistory(f, player, LevelUpPoolKind.RiftMutation, option.Upgrade);
            }

            EntityRef rift = interaction->Rift;

            if (f.Unsafe.TryGetPointer<CursedRift>(rift, out var cursedRift) == true)
            {
                PoiUsageUtility.MarkUsed(f, player, rift, cursedRift->UsagePolicy);
            }

            f.Remove<CursedRiftInteraction>(player);

            Log.Debug($"[CursedRift] {player} chose mutation option {optionIndex} - interaction complete");
        }

    }
}
