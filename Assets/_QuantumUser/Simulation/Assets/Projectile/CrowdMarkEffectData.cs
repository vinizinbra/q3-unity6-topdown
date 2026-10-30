namespace Quantum
{
    using System.Collections.Generic;
    using Photon.Deterministic;

    // Dono do Céu's whistle ("A Geral Não Perdoa"): marks a player the whistle cone actually caught and, if the
    // boss isn't already running one, starts its crowd barrage (CrowdBarrage on the boss, BossCrowdBarrageSystem).
    // A whistle that hits nobody never calls this, so a miss produces no mark and no trash. No damage, no slow -
    // the slow is a separate StickySlowEffectData on the same action (non-elemental: never Ice/Freeze).
    //
    // This asset is also the barrage's tuning (CrowdBarrage.Config points back at it).
    public unsafe class CrowdMarkEffectData : HitEffectData
    {
        public FP MarkDuration = FP.FromString("4.5");

        [UnityEngine.Header("Barrage")]
        public int Salvos = 3;
        public int ThrowsPerSalvo = 3;
        // Total objects for the whole barrage, whatever the party size - salvos alternate between marked players
        // instead of multiplying per player.
        public int MaxTotalThrows = 9;
        public FP FirstSalvoDelay = FP.FromString("0.6");
        public FP SalvoInterval = FP.FromString("1.2");
        public FP ThrowInterval = FP.FromString("0.18");
        public FP ThrowDamage = 10;

        // The first throw of a salvo lands where the target stands when it's thrown (led, see below); the rest land
        // in a ring around that point. Sampled once at throw time and committed (ballistic, no homing).
        public FP LandingSpreadMin = FP.FromString("0.8");
        public FP LandingSpreadMax = FP.FromString("1.8");

        // Aims ahead of a moving target along its velocity over the flight time (ProjectileAimUtility.LeadAimPoint,
        // difficulty-scaled like every enemy lead). Partial (< 1) keeps a strafing player able to walk out of the
        // committed warning; 0 = aim at where it stands.
        public FP LeadFactor = FP._0;
        public FP MaxLeadDistance = FP._2;

        [UnityEngine.Header("Crowd (throw origin)")]
        // Thrown from OUTSIDE the boss arena: a point on the arena chunk's perimeter pushed this far out, raised
        // ThrowHeight above the landing floor (a crowd up on the lajes). Not in an arena (debug spawn elsewhere):
        // a point FallbackDistance away from the landing point instead.
        public FP OutsideMargin = 3;
        public FP ThrowHeight = 5;
        public FP MaxThrowDistance = 20;
        public FP FallbackDistance = 12;

        // Can / cup / paper / bottle - same behaviour, picked deterministically per throw (ThrowCounter % Count).
        [ExpandableAsset] public List<AssetRef<ProjectileDataAsset>> TrashProjectiles = new();

        public override void Apply(Frame f, ref HitEffectContext context)
        {
            EntityRef player = context.Target;
            EntityRef boss = context.Owner;

            if (player == EntityRef.None || f.Has<PlayerLink>(player) == false || PlayerLifeStateUtility.IsIncapacitated(f, player) == true)
                return;

            if (BossHazardUtility.IsOwnerAlive(f, boss) == false)
                return;

            f.AddOrGet<CrowdMark>(player, out var mark);
            mark->Boss = boss;
            mark->Remaining = FPMath.Max(mark->Remaining, MarkDuration);

            if (f.Has<CrowdBarrage>(boss) == true)
                return;

            f.Add(boss, new CrowdBarrage
            {
                Config = new AssetRef<CrowdMarkEffectData>(Guid),
                SalvosRemaining = (byte)System.Math.Clamp(Salvos, 0, 255),
                Timer = FirstSalvoDelay,
                LastTargetIndex = -1,
                ThrowsRemaining = MaxTotalThrows,
            });
        }
    }
}
