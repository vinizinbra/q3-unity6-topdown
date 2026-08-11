namespace Quantum
{
    // Marks an AssetRef field as editable in place, so a chain like WeaponDataAsset ->
    // ProjectileDataAsset -> ProjectileHitData -> HitEffectData is authored from one inspector.
    // See ExpandableAssetDrawer.
#if QUANTUM_UNITY
    public class ExpandableAssetAttribute : UnityEngine.PropertyAttribute
    {
    }
#else
    public class ExpandableAssetAttribute : System.Attribute
    {
    }
#endif
}
