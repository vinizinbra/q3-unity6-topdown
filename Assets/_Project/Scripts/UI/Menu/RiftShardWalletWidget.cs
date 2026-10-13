using TMPro;
using UnityEngine;

/// <summary>
/// Menu top-bar Rift Shard counter: shows <see cref="MetaProgressionSave.RiftShards"/> and follows every save
/// change (purchases, the +Rift Shards cheat, shards banked after a run).
/// </summary>
public class RiftShardWalletWidget : MonoBehaviour
{
    [SerializeField] private TMP_Text amountText;

    private void Awake()
    {
        if (amountText == null)
            amountText = GetComponentInChildren<TMP_Text>(true);
    }

    private void OnEnable()
    {
        MetaProgressionSave.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        MetaProgressionSave.Changed -= Refresh;
    }

    private void Refresh()
    {
        if (amountText != null)
            amountText.text = MetaProgressionSave.RiftShards.ToString();
    }
}
