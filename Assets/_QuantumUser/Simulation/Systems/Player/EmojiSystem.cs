namespace Quantum
{
    using UnityEngine.Scripting;

    // Turns a PlayEmojiCommand into one EmojiPlayed event (see docs/emoji.md and Emoji.qtn). Always-on and
    // OUTSIDE GameplaySystemGroup so players can still react while a level-up/chest screen pauses the game.
    // Allowed in every life state - crying while Downed/KO is the point.
    [Preserve]
    public unsafe class EmojiSystem : SystemMainThreadFilter<EmojiSystem.Filter>
    {
        public struct Filter
        {
            public EntityRef Entity;
            public PlayerLink* PlayerLink;
        }

        public override void Update(Frame f, ref Filter filter)
        {
            PlayerRef player = filter.PlayerLink->Player;
            if (f.GetPlayerCommand(player) is not PlayEmojiCommand cmd)
                return;

            if (cmd.Kind > (byte)EmojiKind.Shock)
                return;

            if (PlayerInputUtility.IsBot(f, filter.Entity) == true)
                return;

            f.AddOrGet<PlayerEmoji>(filter.Entity, out var emoji);
            if (f.Number < emoji->NextAllowedTick)
                return;

            emoji->NextAllowedTick = f.Number + f.UpdateRate; // placeholder: one emoji per second per player
            f.Events.EmojiPlayed(player, filter.Entity, (EmojiKind)cmd.Kind);
        }
    }
}
