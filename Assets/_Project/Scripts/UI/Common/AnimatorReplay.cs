using NaughtyAttributes;
using UnityEngine;

// Test helper: Replay button (in Play mode) restarts the Animator from its default state.
[RequireComponent(typeof(Animator))]
public class AnimatorReplay : MonoBehaviour
{
    [Button]
    private void Replay()
    {
        var animator = GetComponent<Animator>();
        animator.Rebind();
        animator.Update(0f);
    }
}
