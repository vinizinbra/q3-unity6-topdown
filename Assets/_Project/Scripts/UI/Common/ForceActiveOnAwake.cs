using UnityEngine;

// Safety net for GameObjects toggled off in the Inspector during iteration and left that way by
// mistake - forces them back on at runtime regardless of their saved active state.
public class ForceActiveOnAwake : MonoBehaviour
{
    [SerializeField] private GameObject[] targets;

    private void Awake()
    {
        foreach (GameObject target in targets)
        {
            if (target != null)
                target.SetActive(true);
        }
    }
}
