using System.Collections.Generic;
using UnityEngine;

namespace Quantum
{
    // Purely cosmetic variety for Pixie's Fireworks projectiles (see FireworksSkillAction /
    // AreaHitData.TrySpawnFireworks) - each spawned firework carries several pre-authored visual
    // variants as children, and this just disables all but one at random so repeated launches
    // don't all look identical. Not Quantum-entity-bound and not deterministic, since it never
    // affects gameplay.
    public class PixieFireworksVariant : MonoBehaviour
    {
        [SerializeField] private List<GameObject> variants;

        private void Awake()
        {
            ActivateRandomVariant();
        }

        public void ActivateRandomVariant()
        {
            if (variants == null || variants.Count == 0)
                return;

            int chosenIndex = Random.Range(0, variants.Count);

            for (int i = 0; i < variants.Count; i++)
            {
                if (variants[i] != null)
                    variants[i].SetActive(i == chosenIndex);
            }
        }
    }
}
