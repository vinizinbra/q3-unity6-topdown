using Quantum;
using QuantumUser.View.Managers;
using UnityEngine;
using UnityEngine.UI;

// One player's head icon - not a separately-authored portrait asset, just a snapshot of whatever
// sprite the bound entity's own BlobAnimationView.Head is currently showing, so it always matches
// the hero actually equipped without needing per-hero art authored twice. Resolved once in
// Initialize rather than every frame - the head sprite doesn't change after a hero is chosen, and
// re-reading it every tick would just be wasted work.
public class PlayerPortraitUiWidget : MonoBehaviour
{
    [SerializeField] private Image iconImage;

    public void Initialize(EntityRef entityRef)
    {
        if (iconImage == null)
            return;

        CharView charView = EntityViewManager.Instance != null ? EntityViewManager.Instance.GetCharViewByEntityRef(entityRef) : null;
        BlobAnimationView blobView = charView != null ? charView.GetComponent<BlobAnimationView>() : null;
        SpriteRenderer headRenderer = blobView != null && blobView.Head != null ? blobView.Head.GetComponentInChildren<SpriteRenderer>() : null;

        if (headRenderer == null || headRenderer.sprite == null)
            return;

        iconImage.sprite = headRenderer.sprite;
        iconImage.enabled = true;
    }
}
