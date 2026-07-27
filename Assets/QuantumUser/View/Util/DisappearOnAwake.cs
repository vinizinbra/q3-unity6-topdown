using UnityEngine;

namespace Quantum
{
    public class DisappearOnAwake : MonoBehaviour
    {
        private void Awake()
        {
            gameObject.SetActive(false);
        }
    }
}
