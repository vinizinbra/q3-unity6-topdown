namespace Quantum
{
    public unsafe partial class Frame
    {
        // The shared game tuning (Store, Blacksmith, currencies, Director, balance...) - see
        // OverallGameConfig. Resolved through the asset DB on every read (a dictionary lookup).
        public OverallGameConfig GameConfig => FindAsset(RuntimeConfig.GameConfig);

#if UNITY_ENGINE

#endif
    }
}