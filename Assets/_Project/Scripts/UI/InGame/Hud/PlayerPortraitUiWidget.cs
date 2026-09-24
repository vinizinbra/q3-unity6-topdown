using Quantum;
using QuantumUser.View.Managers;
using UnityEngine;
using UnityEngine.UI;

// One player's head icon. Prefers CharacterData.UIHead - a flat portrait authored specifically for
// UI - resolved off the entity's own CharacterStats.CharacterData so it works with no live rig
// present. Falls back to snapshotting whatever sprite the bound entity's BlobAnimationView.Head is
// currently showing for any hero that hasn't had UIHead authored yet, so nothing regresses. Resolved
// once in Initialize rather than every frame - the portrait doesn't change after a hero is chosen,
// and re-reading it every tick would just be wasted work.
public class PlayerPortraitUiWidget : MonoBehaviour
{
    [SerializeField] private Image iconImage;

    public void Initialize(EntityRef entityRef)
    {
        if (iconImage == null)
            return;

        Sprite sprite = ResolveUIHeadSprite(entityRef) ?? ResolveRigHeadSprite(entityRef);

        if (sprite == null)
            return;

        iconImage.sprite = sprite;
        iconImage.enabled = true;
    }

    private static Sprite ResolveUIHeadSprite(EntityRef entityRef)
    {
        Frame frame = QuantumRunner.Default != null ? QuantumRunner.Default.Game?.Frames.Predicted : null;

        if (frame == null || frame.TryGet<CharacterStats>(entityRef, out var stats) == false || stats.CharacterData.IsValid == false)
            return null;

        CharacterData data = frame.FindAsset(stats.CharacterData);
        return data != null ? data.UIHead : null;
    }

    private static Sprite ResolveRigHeadSprite(EntityRef entityRef)
    {
        CharView charView = EntityViewManager.Instance != null ? EntityViewManager.Instance.GetCharViewByEntityRef(entityRef) : null;
        BlobAnimationView blobView = charView != null ? charView.GetComponent<BlobAnimationView>() : null;
        SpriteRenderer headRenderer = blobView != null && blobView.Head != null ? blobView.Head.GetComponentInChildren<SpriteRenderer>() : null;

        return headRenderer != null ? headRenderer.sprite : null;
    }
}
