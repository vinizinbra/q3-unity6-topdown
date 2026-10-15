namespace Quantum
{
    using Photon.Deterministic;

    // Sent by the local EmojiWidget (right stick flick / keys 1-4 / mobile picker) - see docs/emoji.md.
    // Kind is an EmojiKind; EmojiSystem re-validates it (range, cooldown, bots) before raising EmojiPlayed.
    public unsafe class PlayEmojiCommand : DeterministicCommand
    {
        public byte Kind;

        public override void Serialize(BitStream stream)
        {
            stream.Serialize(ref Kind);
        }
    }
}
