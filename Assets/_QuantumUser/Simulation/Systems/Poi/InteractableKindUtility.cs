namespace Quantum
{
    // Generic kind-based priority for ContextInteractionSystem's own proximity scan (see
    // docs/revive.md) - the existing Interactable.Priority field is only an exact-distance
    // tie-break, so making Revive always beat an ordinary POI regardless of distance needs this
    // extra tier ahead of it. A small, standalone resolver (not a hardcoded "if kind == Revive" in
    // the scan loop itself) so any future always-wins interactable reuses this same mechanism.
    public static class InteractableKindUtility
    {
        public static int GetPriorityTier(InteractableKind kind)
        {
            switch (kind)
            {
                case InteractableKind.Revive: return 1;
                default: return 0;
            }
        }
    }
}
