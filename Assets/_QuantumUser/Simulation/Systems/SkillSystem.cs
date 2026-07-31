namespace Quantum
{
    using Photon.Deterministic;
    using UnityEngine.Scripting;

    // Player-input-driven equivalent of EnemySystem: one filter, per-slot 2-state machine
    // (Ready/Active - availability is governed by CurrentStacks, not a blocking third "Cooldown"
    // state), never branches on concrete SkillData/SkillActionData type - adding a new skill or
    // composable action is zero changes here. Must run after KCCSystem (DashSkillData's
    // KCC.SetActive/Teleport calls need this tick's normal movement already resolved) and after
    // AimSystem (DashSkillData reads Aim.Angle as a facing fallback) - see SystemSetup.User.cs.
    [Preserve]
    public unsafe class SkillSystem : SystemMainThreadFilter<SkillSystem.Filter>
    {
        public override void Update(Frame f, ref Filter filter)
        {
            var input = f.GetPlayerInput(filter.PlayerLink->Player);

            UpdateSlot(f, ref filter, &filter.CharacterSkills->DashSkill, SkillSlotId.DashSkill, input, input->DashSkill);
            UpdateSlot(f, ref filter, &filter.CharacterSkills->HeroSkill, SkillSlotId.HeroSkill, input, input->HeroSkill);

            ProcessGrantUpgradeCommand(f, ref filter);
        }

        // GetPlayerCommand only returns non-null on the tick a sent command actually lands - unlike
        // polled Input, this fires exactly once per SendCommand call, not every tick. See
        // GrantSkillUpgradeCommand for why this has to be a command rather than a direct call from
        // the View (SkillUpgradeDebugTrigger today; a level-up/pickup screen eventually).
        private static void ProcessGrantUpgradeCommand(Frame f, ref Filter filter)
        {
            if (f.GetPlayerCommand(filter.PlayerLink->Player) is not GrantSkillUpgradeCommand command)
                return;

            SkillSlot* slot = ResolveSlot(ref filter, command.Slot);

            if (slot == null)
            {
                Log.Error($"[Skill] {filter.Entity} sent a GrantSkillUpgradeCommand with no slot selected");
                return;
            }

            if (AddUpgrade(f, slot, command.Upgrade) == true)
            {
                Log.Debug($"[Skill] {filter.Entity} was granted {command.Upgrade} on {command.Slot} via command");
            }
        }

        private static SkillSlot* ResolveSlot(ref Filter filter, SkillSlotId slotId) =>
            ResolveSlot(filter.CharacterSkills, slotId);

        // Public so callers with only a CharacterSkills* (no full Filter) can resolve a slot too -
        // e.g. LevelUpUtility.GrantOption, granting a SkillUpgrade option outside this system's own
        // filtered Update.
        public static SkillSlot* ResolveSlot(CharacterSkills* skills, SkillSlotId slotId)
        {
            switch (slotId)
            {
                case SkillSlotId.DashSkill: return &skills->DashSkill;
                case SkillSlotId.HeroSkill: return &skills->HeroSkill;
                default: return null;
            }
        }

        // Nothing reduces a skill's cooldown as a side effect of anything else today - added for
        // Combat Reboot (emptying the magazine reduces the Hero Skill's cooldown, see
        // WeaponSystem.StartReload). Clamped at 0 rather than letting it go negative and "banking"
        // toward the next cast.
        public static void ReduceCooldown(Frame f, CharacterSkills* skills, SkillSlotId slotId, FP amount)
        {
            SkillSlot* slot = ResolveSlot(skills, slotId);

            if (slot == null || amount <= FP._0)
                return;

            slot->CooldownTimer = FPMath.Max(FP._0, slot->CooldownTimer - amount);
        }

        // Marks a slot's *next* activation as free - added for Lux's Scrap Collector passive (10
        // Scrap pickups makes the Hero Skill's next cast cost nothing, see
        // ScrapUtility.TryGrantFreeCharge). Deliberately does not touch CurrentStacks/CooldownTimer
        // here: this only takes effect once TryBegin is actually pressed, not the instant it's
        // granted - see SkillSlot.FreeCastPending. A no-op if one is already pending, rather than
        // stacking multiple free casts.
        public static void GrantFreeCast(Frame f, CharacterSkills* skills, SkillSlotId slotId)
        {
            SkillSlot* slot = ResolveSlot(skills, slotId);

            if (slot == null || slot->FreeCastPending == true)
                return;

            slot->FreeCastPending = true;
        }

        private static void UpdateSlot(Frame f, ref Filter filter, SkillSlot* slot, SkillSlotId slotId, Input* input, Button inputButton)
        {
            EnsureInitialized(f, slot);
            TickCooldown(f, filter.Entity, slotId, slot);

            switch (slot->State)
            {
                case SkillState.Ready:
                    TryBegin(f, ref filter, slotId, slot, input, inputButton);
                    break;

                case SkillState.Active:
                    UpdateActive(f, ref filter, slot);
                    break;
            }
        }

        // MaxStacks is component-owned (baked on the prototype, see CharacterSkills.qtn) - 0 means
        // "never baked", not "intentionally zero stacks". Done lazily here every tick (cheap: a
        // single byte compare) rather than only once at spawn time (PlayerSpawnUtility.Spawn), so a
        // slot is correctly seeded regardless of how its entity came to exist - including a player
        // placed directly in a scene for testing (same pattern BasicEnemy.prefab uses per
        // enemies.md), which never goes through PlayerSpawnUtility.Spawn at all. No-ops once
        // MaxStacks is nonzero, so this never re-seeds CurrentStacks after a runtime upgrade
        // ("+1 charge" perk) raises MaxStacks past its baked value.
        private static void EnsureInitialized(Frame f, SkillSlot* slot)
        {
            if (slot->Skill == default || slot->MaxStacks > 0)
                return;

            SkillData skill = f.FindAsset(slot->Skill);
            slot->MaxStacks = 1;
            slot->CurrentStacks = skill.InitStacks < slot->MaxStacks ? skill.InitStacks : slot->MaxStacks;
        }

        // Runs every tick regardless of State - a stack can regenerate while the slot is Active on
        // a different banked charge, not just while sitting idle in Ready. Only one stack recovers
        // at a time off a single CooldownTimer: spending a stack while another is already
        // mid-cooldown does not reset that timer's progress (see TryBegin) - it only (re)starts
        // fresh the instant the slot goes from full to not-full.
        private static void TickCooldown(Frame f, EntityRef owner, SkillSlotId slotId, SkillSlot* slot)
        {
            if (slot->CurrentStacks >= slot->MaxStacks)
                return;

            slot->CooldownTimer -= f.DeltaTime;

            if (slot->CooldownTimer > FP._0)
                return;

            slot->CurrentStacks++;

            if (slot->Skill != default && slot->CurrentStacks < slot->MaxStacks)
            {
                SkillData skill = f.FindAsset(slot->Skill);
                slot->CooldownTimer = StatUtility.GetSkillCooldown(f, owner, slotId, skill.Cooldown);
            }
        }

        private static void TryBegin(Frame f, ref Filter filter, SkillSlotId slotId, SkillSlot* slot, Input* input, Button inputButton)
        {
            if (inputButton.WasPressed == false)
                return;

            if (slot->Skill == default)
            {
                Log.Debug($"[Skill] {filter.Entity} pressed a skill button with no Skill assigned in this slot");
                return;
            }

            bool freeCast = slot->FreeCastPending;

            if (freeCast == false && slot->CurrentStacks == 0)
            {
                Log.Debug($"[Skill] {filter.Entity} pressed a skill button with 0 stacks available");
                return;
            }

            SkillData skill = f.FindAsset(slot->Skill);

            // A pending free cast (see SkillSlot.FreeCastPending) spends itself instead of a stack -
            // no CurrentStacks change, no cooldown start. OnFreeCastUsed fires here, at the moment
            // it's actually spent, not when ScrapUtility.TryGrantFreeCharge granted it - that's what
            // lets Lux's own Scrap counter reset on use instead of on threshold-reached.
            if (freeCast == true)
            {
                slot->FreeCastPending = false;
                f.Signals.OnFreeCastUsed(filter.Entity, slotId);
            }
            else
            {
                bool wasFull = slot->CurrentStacks >= slot->MaxStacks;
                slot->CurrentStacks--;

                if (wasFull == true)
                {
                    slot->CooldownTimer = StatUtility.GetSkillCooldown(f, filter.Entity, slotId, skill.Cooldown);
                }
            }

            slot->StartPosition = filter.Transform3D->Position;
            slot->TargetPosition = filter.Transform3D->Position;
            slot->ActiveTime = FP._0;
            slot->TravelledDistance = FP._0;
            slot->LastStepDistance = FP._0;
            slot->LastPosition = filter.Transform3D->Position;
            slot->AreaMultiplier = FP._1;

            // Upgrades grant their Begin-phase state before the skill's own Begin runs, not after -
            // a skill whose Begin is itself the one-shot moment it acts (ProjectileSkillData firing,
            // reading ProjectileDamageUpgrade/DecoyOnThrowUpgrade) needs whatever it grants already
            // in place, since there's no later tick where that one-shot logic runs again to pick it
            // up. Every existing upgrade already reads state independent of this order (e.g.
            // RageOverdriveSkillAction reads BerserkSkillData's own asset fields, not anything
            // Berserk.Begin computes), so nothing currently relies on the old order.
            InvokeActions(f, ref filter, slot, skill, SkillActionPhase.Begin);
            bool finished = skill.Begin(f, ref filter, input, slot);

            if (finished == true)
            {
                FinishSkill(f, ref filter, slot, skill);
            }
            else
            {
                slot->State = SkillState.Active;
            }

            Log.Debug($"[Skill] {filter.Entity} began {skill.Name} (stacks remaining={slot->CurrentStacks}/{slot->MaxStacks})");
        }

        private static void UpdateActive(Frame f, ref Filter filter, SkillSlot* slot)
        {
            SkillData skill = f.FindAsset(slot->Skill);

            // Ordering matches ChargeDeliveryData.Tick's own hit-then-move pattern: OnGoing actions
            // (e.g. an interval-paced SpawnEntitySkillAction) see this tick's pre-move position before Tick()
            // advances it.
            InvokeActions(f, ref filter, slot, skill, SkillActionPhase.OnGoing);

            bool finished = skill.Tick(f, ref filter, slot);

            // Advanced after this tick's OnGoing actions, so the first of them reads 0 and an
            // interval-paced action fires on the tick the skill goes Active rather than one whole
            // interval late.
            slot->ActiveTime += f.DeltaTime;

            // Distance counterpart of ActiveTime, advanced the same place for the same reason: the
            // first OnGoing reads 0, and a Spacing-paced SpawnEntitySkillAction fires the tick its
            // travel crosses a boundary. Summed from the real per-tick move so a path bent or cut
            // short by a wall spaces its spawns along ground covered, not a straight line.
            FPVector3 position = filter.Transform3D->Position;
            slot->LastStepDistance = FPVector3.Distance(slot->LastPosition, position);
            slot->TravelledDistance += slot->LastStepDistance;
            slot->LastPosition = position;

            if (finished == true)
            {
                FinishSkill(f, ref filter, slot, skill);
            }
        }

        // Single call site for every way a skill can finish (instant Begin(), or Tick() reporting
        // done) - mirrors EnemySystem.EnterRecovering. Immediately re-usable from Ready if another
        // stack is already banked, since availability is governed by CurrentStacks, not a timer.
        private static void FinishSkill(Frame f, ref Filter filter, SkillSlot* slot, SkillData skill)
        {
            skill.End(f, ref filter, slot);
            InvokeActions(f, ref filter, slot, skill, SkillActionPhase.End);

            slot->State = SkillState.Ready;
        }

        // The skill's authored baseline first, then whatever this run added on top - an upgrade can
        // therefore read state a baseline action already wrote this phase, but not the reverse -
        // unless Priority reorders them; see below.
        private static void InvokeActions(Frame f, ref Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase phase)
        {
            var upgrades = slot->Upgrades;
            int actionCount = skill.Actions.Count;
            int upgradeCount = upgrades.Length;
            int total = actionCount + upgradeCount;

            // Executes in ascending SkillActionData.Priority order, not list/array position - an
            // upgrade granted into whichever Upgrades slot happened to be free (see AddUpgrade)
            // still runs wherever its Priority says, and a baseline Actions entry can be reordered
            // after an upgrade the same way. Ties keep the old behavior: baseline Actions before
            // Upgrades, then original list order - so leaving every action at the shared default
            // Priority (0) reproduces plain list-order execution exactly. Stack-allocated, not a
            // List<T>: this runs every OnGoing tick for every active skill slot.
            bool* fromUpgrades = stackalloc bool[total];
            int* index = stackalloc int[total];
            int* priority = stackalloc int[total];
            int count = 0;

            for (int i = 0; i < actionCount; i++)
            {
                if (TryGetPriority(f, skill.Actions[i], slot, phase, out int p) == false)
                    continue;

                fromUpgrades[count] = false;
                index[count] = i;
                priority[count] = p;
                count++;
            }

            for (int i = 0; i < upgradeCount; i++)
            {
                if (TryGetPriority(f, upgrades[i], slot, phase, out int p) == false)
                    continue;

                fromUpgrades[count] = true;
                index[count] = i;
                priority[count] = p;
                count++;
            }

            // Stable insertion sort - count is always tiny (a skill's own Actions plus at most 5
            // Upgrades), so this is cheaper than anything requiring a heap-allocated collection.
            for (int i = 1; i < count; i++)
            {
                bool movingFromUpgrades = fromUpgrades[i];
                int movingIndex = index[i];
                int movingPriority = priority[i];
                int j = i - 1;

                while (j >= 0 && priority[j] > movingPriority)
                {
                    fromUpgrades[j + 1] = fromUpgrades[j];
                    index[j + 1] = index[j];
                    priority[j + 1] = priority[j];
                    j--;
                }

                fromUpgrades[j + 1] = movingFromUpgrades;
                index[j + 1] = movingIndex;
                priority[j + 1] = movingPriority;
            }

            for (int i = 0; i < count; i++)
            {
                AssetRef<SkillActionData> actionRef = fromUpgrades[i] == true ? upgrades[index[i]] : skill.Actions[index[i]];
                Invoke(f, ref filter, slot, skill, phase, actionRef);
            }
        }

        // Resolves and phase-filters up front so InvokeActions can sort before executing anything -
        // false (and no Priority) for whatever Invoke would skip anyway: unassigned slot, wrong
        // phase, or an OnGoing/Spacing action not due this tick. Those never occupy a sort position.
        private static bool TryGetPriority(Frame f, AssetRef<SkillActionData> actionRef, SkillSlot* slot,
            SkillActionPhase phase, out int priority)
        {
            priority = 0;

            if (actionRef.IsValid == false)
                return false;

            SkillActionData action = f.FindAsset(actionRef);

            if (action.ShouldExecute(f, slot, phase) == false)
                return false;

            priority = action.Priority;
            return true;
        }

        // Each action's own Phase and Interval (configurable per asset instance, not hardcoded by
        // which method it overrides) decide whether it fires at this lifecycle point - see
        // SkillActionData. Re-resolves and re-checks ShouldExecute (already known true from
        // TryGetPriority above) rather than threading the resolved action through the sort - both
        // are cheap asset-DB lookups over a pure read, and this keeps Invoke usable on its own.
        private static void Invoke(Frame f, ref Filter filter, SkillSlot* slot, SkillData skill,
            SkillActionPhase phase, AssetRef<SkillActionData> actionRef)
        {
            if (actionRef.IsValid == false)
                return;

            SkillActionData action = f.FindAsset(actionRef);
            bool shouldExecute = action.ShouldExecute(f, slot, phase);

            if (shouldExecute == true)
            {
                action.Execute(f, ref filter, slot, skill, phase);
            }

            // Fired independent of shouldExecute for End specifically (still gated on Activated) -
            // a BeginFx/OnGoingFx step spawned as SkillFxSpawnMode.HeldUntilEnd must be released once
            // this activation ends even if the action's own Phase field never opted into End for its
            // actual gameplay logic (e.g. Phase = OnGoing only, nothing to do on End besides letting
            // the particle go). Every other phase still only fires alongside a genuine Execute call.
            bool fireEndAnyway = phase == SkillActionPhase.End && action.Activated == true;

            if (shouldExecute == true || fireEndAnyway == true)
            {
                FireFxEvent(f, filter.Entity, actionRef, action, phase, filter.Transform3D->Position);
            }
        }

        // Lets any SkillActionData get a feedback particle just by filling in BeginFx/OnGoingFx/EndFx
        // in the Editor (see SkillActionFxView) - no individual Execute() override needs to fire its
        // own event. Gated by HasFx rather than firing unconditionally like this file's other events,
        // since this runs from the one call site every equipped action of every active skill slot
        // passes through every tick it fires, not a single specific perk.
        private static void FireFxEvent(Frame f, EntityRef entity, AssetRef<SkillActionData> actionRef,
            SkillActionData action, SkillActionPhase phase, FPVector3 position)
        {
            if (action.HasFx(phase) == false)
                return;

            switch (phase)
            {
                case SkillActionPhase.Begin:
                    f.Events.SkillActionBeginExecuted(entity, actionRef, position);
                    break;
                case SkillActionPhase.OnGoing:
                    f.Events.SkillActionOnGoingExecuted(entity, actionRef, position);
                    break;
                case SkillActionPhase.End:
                    f.Events.SkillActionEndExecuted(entity, actionRef, position);
                    break;
            }
        }

        // Grants a mid-run upgrade to a slot. Takes effect on the skill's next activation - no
        // re-seeding needed, since an upgrade is behavior the slot runs, not a stat baked into it.
        // False when every slot is already taken, or the upgrade is already present.
        //
        // Rejected outright while the slot is Active rather than Ready: InvokeActions re-reads
        // slot->Upgrades fresh at both Begin and End time instead of snapshotting it once, so a
        // grant landing mid-activation would be present for that same activation's End but was
        // never there for its Begin - a phase this action never actually saw start. For a
        // Begin/End-paired action like MarkExplosiveDeathSkillAction, that fires an unmatched End
        // (Stacks-- with no prior Stacks++), which is exactly how Max's mark once got stuck
        // permanently on. Rejecting here keeps "next activation" honest for every paired action,
        // not just the ones that happen to tolerate an extra End.
        public static bool AddUpgrade(Frame f, SkillSlot* slot, AssetRef<SkillActionData> upgradeRef)
        {
            if (upgradeRef.IsValid == false)
                return false;

            if (slot->State != SkillState.Ready)
            {
                Log.Error($"[Skill] {upgradeRef} not granted - slot is {slot->State}, not Ready (would desync this activation's Begin/End actions)");
                return false;
            }

            var upgrades = slot->Upgrades;
            int free = -1;

            for (int i = 0; i < upgrades.Length; i++)
            {
                if (upgrades[i] == upgradeRef)
                {
                    Log.Error($"[Skill] {upgradeRef} already on this slot - would fire twice per activation");
                    return false;
                }

                if (free < 0 && upgrades[i].IsValid == false)
                    free = i;
            }

            if (free < 0)
            {
                Log.Error($"[Skill] all {upgrades.Length} upgrade slots taken - {upgradeRef} not granted");
                return false;
            }

            upgrades[free] = upgradeRef;
            Log.Debug($"[Skill] granted upgrade {upgradeRef} in slot {free}");

            return true;
        }

        public struct Filter
        {
            public EntityRef Entity;
            public PlayerLink* PlayerLink;
            public CharacterSkills* CharacterSkills;
            public Transform3D* Transform3D;
            public KCC* KCC;
            public Aim* Aim;
        }
    }
}
