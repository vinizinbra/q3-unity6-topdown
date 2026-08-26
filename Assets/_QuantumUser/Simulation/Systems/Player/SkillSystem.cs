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
            var input = PlayerInputUtility.Resolve(f, filter.Entity, filter.PlayerLink);

            Button dashSkillButton = input->DashSkill;
            Button heroSkillButton = input->HeroSkill;

            // Downed/KO (see docs/revive.md) - no Dash, no Hero Skill cast/interaction redirect.
            // Self-revive is a separate, unrelated path (SelfReviveCommand, sent from a dedicated
            // View window, processed by PlayerLifeStateSystem) - not the Hero Skill button at all.
            // ReviveChannelSystem reads Input.HeroSkill directly for a TEAMMATE-driven channel's own
            // ongoing hold/release detection every tick, unaffected by this local neutralization
            // (never mutates the underlying Input struct).
            if (PlayerLifeStateUtility.IsIncapacitated(f, filter.Entity) == true)
            {
                dashSkillButton = default;
                heroSkillButton = default;
            }
            // A Cursed Rift/Store/Blacksmith/Revive-channel session open for this player (see
            // docs/breathing-poi.md/docs/store-blacksmith.md/docs/revive.md) locks both slots -
            // neutralized (not skipped) buttons still let UpdateSlot's own cooldown/stack-recovery
            // ticking run normally, just block the press edge, same reasoning TickCooldown already
            // runs regardless of State.
            else if (PoiInteractionLockUtility.IsInputLocked(f, filter.Entity) == true)
            {
                dashSkillButton = default;
                heroSkillButton = default;
            }
            // Base-Skill-button redirect (see ContextInteraction.qtn) - State == Available OR
            // NotNeeded (not just "something is nearby" - ContextInteraction.ActiveTarget is set
            // for a nearby-but-not-usable POI too, e.g. PhaseUnavailable/AlreadyUsed, purely so the
            // world-space prompt can explain itself; those still fall through to a normal Hero
            // Skill cast). NotNeeded specifically DOES still claim the press - a player standing at
            // a full-Health Healing Shrine pressing the button is clearly trying to interact, not
            // cast their Hero Skill, even though the attempt itself does nothing (see
            // HealingShrineUtility.TryInteract, which fires EventContextInteractionRejected for
            // quick-feedback toast purposes instead of healing). Dispatched by ActiveKind to
            // whichever POI's own utility actually owns that interaction (CursedRift/Store open a
            // multi-step Choice Window; HealingShrine resolves immediately, same tick; Blacksmith
            // rolls its 3 perk choices and opens a Choice Window, same shape as Cursed Rift's own
            // Sacrifice stage), and the button is neutralized for UpdateSlot this tick only (a real
            // cast never also fires on the same press). Re-validated fully in Quantum by each
            // utility's own resolver - never trusted from ContextInteraction alone.
            else if (heroSkillButton.WasPressed == true
                && f.Unsafe.TryGetPointer<ContextInteraction>(filter.Entity, out var context) == true
                && (context->State == ContextInteractionState.Available || context->State == ContextInteractionState.NotNeeded))
            {
                switch (context->ActiveKind)
                {
                    case InteractableKind.CursedRift:
                        CursedRiftUtility.TryBeginInteraction(f, filter.Entity, context->ActiveTarget);
                        break;

                    case InteractableKind.HealingShrine:
                        HealingShrineUtility.TryInteract(f, filter.Entity, context->ActiveTarget);
                        break;

                    case InteractableKind.Store:
                        StoreUtility.TryBeginInteraction(f, filter.Entity, context->ActiveTarget);
                        break;

                    case InteractableKind.Blacksmith:
                        BlacksmithUtility.TryBeginInteraction(f, filter.Entity, context->ActiveTarget);
                        break;

                    case InteractableKind.TraversalChallenge:
                        TraversalChallengeUtility.TryActivate(f, filter.Entity, context->ActiveTarget);
                        break;

                    case InteractableKind.Revive:
                        ReviveUtility.TryBeginInteraction(f, filter.Entity, context->ActiveTarget);
                        break;
                }

                heroSkillButton = default;
            }

            UpdateSlot(f, ref filter, &filter.CharacterSkills->DashSkill, SkillSlotId.DashSkill, input, dashSkillButton);
            UpdateSlot(f, ref filter, &filter.CharacterSkills->HeroSkill, SkillSlotId.HeroSkill, input, heroSkillButton);

            ProcessSkillUpgradeCommands(f, ref filter);
        }

        // GetPlayerCommand only returns non-null on the tick a sent command actually lands - unlike
        // polled Input, this fires exactly once per SendCommand call, not every tick, and a player
        // can only have one command in flight per tick - hence the single dispatch below rather than
        // three independent checks. See GrantSkillUpgradeCommand for why this has to be a command
        // rather than a direct call from the View (SkillUpgradeDebugTrigger today; a
        // level-up/pickup screen eventually for the Grant case - Remove/ClearAll are debug-only).
        private static void ProcessSkillUpgradeCommands(Frame f, ref Filter filter)
        {
            switch (f.GetPlayerCommand(filter.PlayerLink->Player))
            {
                case GrantSkillUpgradeCommand grant:
                    ProcessGrantUpgradeCommand(f, ref filter, grant);
                    break;

                case RemoveSkillUpgradeCommand remove:
                    ProcessRemoveUpgradeCommand(f, ref filter, remove);
                    break;

                case ClearSkillUpgradesCommand clear:
                    ProcessClearUpgradesCommand(f, ref filter, clear);
                    break;
            }
        }

        private static void ProcessGrantUpgradeCommand(Frame f, ref Filter filter, GrantSkillUpgradeCommand command)
        {
            SkillSlot* slot = ResolveSlot(ref filter, command.Slot);

            if (slot == null)
            {
                Log.Error($"[Skill] {filter.Entity} sent a GrantSkillUpgradeCommand with no slot selected");
                return;
            }

            if (AddUpgrade(f, slot, command.Upgrade) == true)
            {
                LevelUpUtility.RecordHistory(f, filter.Entity, LevelUpPoolKind.SkillUpgrade, new AssetRef<UpgradeData>(command.Upgrade.Id));
                Log.Debug($"[Skill] {filter.Entity} was granted {command.Upgrade} on {command.Slot} via command");
            }
        }

        private static void ProcessRemoveUpgradeCommand(Frame f, ref Filter filter, RemoveSkillUpgradeCommand command)
        {
            SkillSlot* slot = ResolveSlot(ref filter, command.Slot);

            if (slot == null)
            {
                Log.Error($"[Skill] {filter.Entity} sent a RemoveSkillUpgradeCommand with no slot selected");
                return;
            }

            if (RemoveUpgrade(f, slot, command.Upgrade) == true)
            {
                Log.Debug($"[Skill] {filter.Entity} had {command.Upgrade} removed from {command.Slot} via command");
            }
        }

        private static void ProcessClearUpgradesCommand(Frame f, ref Filter filter, ClearSkillUpgradesCommand command)
        {
            SkillSlot* slot = ResolveSlot(ref filter, command.Slot);

            if (slot == null)
            {
                Log.Error($"[Skill] {filter.Entity} sent a ClearSkillUpgradesCommand with no slot selected");
                return;
            }

            if (ClearUpgrades(f, slot) == true)
            {
                Log.Debug($"[Skill] {filter.Entity} had all upgrades cleared from {command.Slot} via command");
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
        // Returns how many seconds were ACTUALLY removed (0 if the slot was already off cooldown) -
        // a capped source (Zara's Sound Boost, budgeted per Totem per ally via AreaAllyBudget) needs
        // this so it only ever charges its allowance for reduction that genuinely landed, rather than
        // burning the cap against an already-ready skill. Every pre-existing caller ignores the
        // return value, unchanged.
        public static FP ReduceCooldown(Frame f, CharacterSkills* skills, SkillSlotId slotId, FP amount)
        {
            SkillSlot* slot = ResolveSlot(skills, slotId);

            if (slot == null || amount <= FP._0)
                return FP._0;

            FP applied = FPMath.Min(amount, slot->CooldownTimer);

            if (applied <= FP._0)
                return FP._0;

            slot->CooldownTimer -= applied;
            return applied;
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
                    // A spare charge (or a pending free cast) lets a fresh press cut the current
                    // activation short and immediately begin a new one, rather than blocking until
                    // this one finishes on its own - same availability check TryBegin itself uses,
                    // just evaluated while Active instead of Ready. FinishSkill runs the interrupted
                    // activation's own End/cleanup first (e.g. DashSkillData restoring KCC.SetActive)
                    // so the restart begins from a clean slate.
                    if (CanCancelAndRecast(slot, inputButton) == true)
                    {
                        SkillData activeSkill = f.FindAsset(slot->Skill);
                        FinishSkill(f, ref filter, slotId, slot, activeSkill);
                        TryBegin(f, ref filter, slotId, slot, input, inputButton);
                    }
                    else
                    {
                        UpdateActive(f, ref filter, slotId, slot);
                    }
                    break;
            }
        }

        private static bool CanCancelAndRecast(SkillSlot* slot, Button inputButton)
        {
            if (inputButton.WasPressed == false)
                return false;

            return slot->FreeCastPending == true || slot->CurrentStacks > 0;
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

        // Only progresses while Ready, not every tick regardless of State - a channel's own Duration
        // must not also count as recovery time (see FinishSkill, which is what actually arms a fresh
        // countdown once an activation finishes). Gating here rather than just skipping the arm is
        // required, not optional: CooldownTimer sits at its default 0 for the entire time between a
        // stack being spent and that activation finishing (unarmed), and ticking during that window
        // would misread "not yet armed" as "recovery already complete", instantly restoring the
        // stack mid-activation. Only one stack recovers at a time off a single CooldownTimer:
        // spending a stack while another is already mid-cooldown does not reset that timer's
        // progress - it only (re)starts fresh once a finishing activation finds nothing recovering.
        private static void TickCooldown(Frame f, EntityRef owner, SkillSlotId slotId, SkillSlot* slot)
        {
            if (slot->State != SkillState.Ready)
                return;

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
                // Stack itself is spent right away, but the cooldown that recovers it doesn't start
                // counting down until this activation actually finishes (FinishSkill) - a channel's
                // Duration is still time spent on this charge, not time it should also be recovering.
                slot->CurrentStacks--;
            }

            f.Signals.OnSkillActivated(filter.Entity, slotId);

            slot->StartPosition = filter.Transform3D->Position;
            slot->TargetPosition = filter.Transform3D->Position;
            slot->ActiveTime = FP._0;
            slot->TravelledDistance = FP._0;
            slot->LastStepDistance = FP._0;
            slot->LastPosition = filter.Transform3D->Position;
            slot->AreaMultiplier = FP._1;

            // Upgrades grant their Begin-phase state before the skill's own Begin runs, not after -
            // a skill whose Begin is itself the one-shot moment it acts (ProjectileSkillData firing,
            // reading ProjectileDamageUpgrade/PixieBombCharge) needs whatever it grants already
            // in place, since there's no later tick where that one-shot logic runs again to pick it
            // up. Every existing upgrade already reads state independent of this order (e.g.
            // LastStandSkillAction reads BerserkSkillData's own asset fields, not anything
            // Berserk.Begin computes), so nothing currently relies on the old order.
            InvokeActions(f, ref filter, slot, skill, SkillActionPhase.Begin);
            bool finished = skill.Begin(f, ref filter, input, slot);

            if (finished == true)
            {
                FinishSkill(f, ref filter, slotId, slot, skill);
            }
            else
            {
                slot->State = SkillState.Active;
            }

            Log.Debug($"[Skill] {filter.Entity} began {skill.Name} (stacks remaining={slot->CurrentStacks}/{slot->MaxStacks})");
        }

        private static void UpdateActive(Frame f, ref Filter filter, SkillSlotId slotId, SkillSlot* slot)
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
                FinishSkill(f, ref filter, slotId, slot, skill);
            }
        }

        // Single call site for every way a skill can finish (instant Begin(), Tick() reporting
        // done, or a cancel-and-recast in UpdateSlot) - mirrors EnemySystem.EnterRecovering.
        // Immediately re-usable from Ready if another stack is already banked, since availability is
        // governed by CurrentStacks, not a timer.
        private static void FinishSkill(Frame f, ref Filter filter, SkillSlotId slotId, SkillSlot* slot, SkillData skill)
        {
            skill.End(f, ref filter, slot);
            InvokeActions(f, ref filter, slot, skill, SkillActionPhase.End);

            slot->State = SkillState.Ready;
            FlushPendingUpgrades(slot);

            // Cooldown for the stack just spent starts counting down only now that the activation
            // has actually finished, not back when TryBegin cast it - see TryBegin's own comment.
            // Only arms a fresh countdown if nothing is already recovering (CooldownTimer <= 0) -
            // TickCooldown recovers one stack at a time off a single timer (see its own comment), so
            // an activation finishing while an earlier spent charge is still mid-recovery must not
            // reset that progress.
            if (slot->CooldownTimer <= FP._0 && slot->CurrentStacks < slot->MaxStacks)
            {
                slot->CooldownTimer = StatUtility.GetSkillCooldown(f, filter.Entity, slotId, skill.Cooldown);
            }
        }

        // The skill's authored baseline first, then whatever this run added on top - an upgrade can
        // therefore read state a baseline action already wrote this phase, but not the reverse -
        // unless Priority reorders them; see below.
        private static void InvokeActions(Frame f, ref Filter filter, SkillSlot* slot, SkillData skill, SkillActionPhase phase)
        {
            var upgrades = slot->Upgrades;
            int actionCount = skill.CheckActions == true ? skill.Actions.Count : 0;
            int upgradeCount = upgrades.Length;
            int total = actionCount + upgradeCount;

            if (phase == SkillActionPhase.Begin)
                Log.Debug($"[Skill] {filter.Entity} InvokeActions Begin for \"{skill.name}\" - CheckActions={skill.CheckActions}, skill.Actions.Count={skill.Actions.Count}, resolved actionCount={actionCount}, upgradeCount={upgradeCount}");

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
                if (TryGetPriority(f, skill.Actions[i], slot, phase, isUpgrade: false, out int p) == false)
                    continue;

                fromUpgrades[count] = false;
                index[count] = i;
                priority[count] = p;
                count++;
            }

            for (int i = 0; i < upgradeCount; i++)
            {
                if (TryGetPriority(f, upgrades[i], slot, phase, isUpgrade: true, out int p) == false)
                    continue;

                fromUpgrades[count] = true;
                index[count] = i;
                priority[count] = p;
                count++;
            }

            // Stable insertion sort - count is always tiny (a skill's own Actions plus at most 8
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
                Invoke(f, ref filter, slot, skill, phase, actionRef, isUpgrade: fromUpgrades[i]);
            }
        }

        // Resolves and phase-filters up front so InvokeActions can sort before executing anything -
        // false (and no Priority) for whatever Invoke would skip anyway: unassigned slot, wrong
        // phase, or an OnGoing/Spacing action not due this tick. Those never occupy a sort position.
        // isUpgrade ignores Activated (see SkillActionData's own comment) - a granted slot->Upgrades
        // entry always runs once granted, regardless of whatever the shared asset's baseline toggle
        // says; a plain skill.Actions entry still respects it.
        private static bool TryGetPriority(Frame f, AssetRef<SkillActionData> actionRef, SkillSlot* slot,
            SkillActionPhase phase, bool isUpgrade, out int priority)
        {
            priority = 0;

            if (actionRef.IsValid == false)
                return false;

            SkillActionData action = f.FindAsset(actionRef);

            if (action.ShouldExecute(f, slot, phase, ignoreActivated: isUpgrade) == false)
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
            SkillActionPhase phase, AssetRef<SkillActionData> actionRef, bool isUpgrade)
        {
            if (actionRef.IsValid == false)
                return;

            SkillActionData action = f.FindAsset(actionRef);
            bool shouldExecute = action.ShouldExecute(f, slot, phase, ignoreActivated: isUpgrade);

            if (shouldExecute == true)
            {
                Log.Debug($"[Skill] {filter.Entity} Executing \"{action.name}\" (isUpgrade={isUpgrade}, Activated={action.Activated}, phase={phase})");
                action.Execute(f, ref filter, slot, skill, phase, actionRef);
            }

            // Fired independent of shouldExecute for End specifically (still gated on Activated,
            // ignored the same way for a granted upgrade - see ShouldExecute's own isUpgrade) -
            // a BeginFx/OnGoingFx step spawned as SkillFxSpawnMode.HeldUntilEnd must be released once
            // this activation ends even if the action's own Phase field never opted into End for its
            // actual gameplay logic (e.g. Phase = OnGoing only, nothing to do on End besides letting
            // the particle go). Every other phase still only fires alongside a genuine Execute call.
            bool fireEndAnyway = phase == SkillActionPhase.End && (action.Activated == true || isUpgrade == true);

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
        // False when every slot (Upgrades, or PendingUpgrades if Active - see below) is already
        // taken, or the upgrade is already present/pending.
        //
        // Queued into PendingUpgrades instead of landing in Upgrades directly while the slot is
        // Active: InvokeActions re-reads slot->Upgrades fresh at both Begin and End time instead
        // of snapshotting it once, so a grant landing mid-activation would be present for that
        // same activation's End but was never there for its Begin - a phase this action never
        // actually saw start. For a Begin/End-paired action like MarkExplosiveDeathSkillAction,
        // that fires an unmatched End (Stacks-- with no prior Stacks++), which is exactly how
        // Max's mark once got stuck permanently on. FinishSkill flushes PendingUpgrades into
        // Upgrades right after this activation's own End already ran against the old set, so the
        // grant is honored starting the *next* activation instead of being silently dropped.
        public static bool AddUpgrade(Frame f, SkillSlot* slot, AssetRef<SkillActionData> upgradeRef)
        {
            if (upgradeRef.IsValid == false)
                return false;

            var upgrades = slot->Upgrades;
            var pending = slot->PendingUpgrades;

            // A ranked action (MaxRank > 1) stays a single slot entry across every rank - Execute
            // reads its live rank fresh via SkillUpgradeUtility.GetRank, so re-granting it (a rank-up
            // pick) is a no-op here beyond returning true, letting LevelUpUtility.GrantOption's
            // RecordHistory bump the rank count. Only a non-ranked action re-grant is still the bug
            // this duplicate check exists to catch.
            SkillActionData action = f.FindAsset(upgradeRef);

            for (int i = 0; i < upgrades.Length; i++)
            {
                if (upgrades[i] == upgradeRef)
                {
                    if (action.MaxRank > 1)
                        return true;

                    Log.Error($"[Skill] {upgradeRef} already on this slot - would fire twice per activation");
                    return false;
                }
            }

            for (int i = 0; i < pending.Length; i++)
            {
                if (pending[i] == upgradeRef)
                {
                    if (action.MaxRank > 1)
                        return true;

                    Log.Error($"[Skill] {upgradeRef} already pending on this slot - would fire twice per activation");
                    return false;
                }
            }

            var target = slot->State == SkillState.Ready ? upgrades : pending;
            int free = -1;

            for (int i = 0; i < target.Length; i++)
            {
                if (target[i].IsValid == false)
                {
                    free = i;
                    break;
                }
            }

            if (free < 0)
            {
                Log.Error($"[Skill] all {target.Length} upgrade slots taken - {upgradeRef} not granted");
                return false;
            }

            target[free] = upgradeRef;
            Log.Debug($"[Skill] granted upgrade {upgradeRef} in slot {free}" +
                      (slot->State == SkillState.Active ? " (pending - lands on next activation)" : ""));

            return true;
        }

        // Lands whatever AddUpgrade queued into PendingUpgrades while this slot was Active, now
        // that the just-finished activation's own End already ran against the old Upgrades set
        // (see AddUpgrade's own comment) - so a mid-activation grant is honored starting next
        // activation instead of never landing at all.
        private static void FlushPendingUpgrades(SkillSlot* slot)
        {
            var upgrades = slot->Upgrades;
            var pending = slot->PendingUpgrades;

            for (int i = 0; i < pending.Length; i++)
            {
                if (pending[i].IsValid == false)
                    continue;

                for (int j = 0; j < upgrades.Length; j++)
                {
                    if (upgrades[j].IsValid == false)
                    {
                        upgrades[j] = pending[i];
                        break;
                    }
                }

                pending[i] = default;
            }
        }

        // Debug counterpart to AddUpgrade - removes one previously-granted upgrade from a slot.
        // Same Ready-only guard and for the same reason: InvokeActions re-reads slot->Upgrades fresh
        // at both Begin and End, so pulling an entry mid-activation would desync a paired action's
        // Begin/End the same way a mid-activation grant would (see AddUpgrade's own comment).
        public static bool RemoveUpgrade(Frame f, SkillSlot* slot, AssetRef<SkillActionData> upgradeRef)
        {
            if (upgradeRef.IsValid == false)
                return false;

            if (slot->State != SkillState.Ready)
            {
                Log.Error($"[Skill] {upgradeRef} not removed - slot is {slot->State}, not Ready (would desync this activation's Begin/End actions)");
                return false;
            }

            var upgrades = slot->Upgrades;

            for (int i = 0; i < upgrades.Length; i++)
            {
                if (upgrades[i] != upgradeRef)
                    continue;

                upgrades[i] = default;
                Log.Debug($"[Skill] removed upgrade {upgradeRef} from slot {i}");
                return true;
            }

            Log.Error($"[Skill] {upgradeRef} not removed - not present on this slot");
            return false;
        }

        // Debug-only "remove everything at once" - same Ready-only guard as RemoveUpgrade/AddUpgrade.
        public static bool ClearUpgrades(Frame f, SkillSlot* slot)
        {
            if (slot->State != SkillState.Ready)
            {
                Log.Error($"[Skill] upgrades not cleared - slot is {slot->State}, not Ready (would desync this activation's Begin/End actions)");
                return false;
            }

            var upgrades = slot->Upgrades;

            for (int i = 0; i < upgrades.Length; i++)
            {
                upgrades[i] = default;
            }

            Log.Debug("[Skill] cleared all upgrades from slot");
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
