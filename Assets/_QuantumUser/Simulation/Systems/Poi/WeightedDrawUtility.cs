namespace Quantum
{
    using System;
    using System.Collections.Generic;

    // Generic weighted-draw-without-replacement helper - used by Blacksmith's own perk draw and
    // Store's own food-offer draw (see docs/store-blacksmith.md). Deliberately NOT used to unify
    // LevelUpUtility.DrawWeighted or CursedRiftUtility's own RollSacrificeOptions - each already has
    // its own working implementation tied to its own Candidate shape (LevelUpOption /
    // AssetRef<SacrificeDefinition>), and retrofitting either onto this generic would touch proven
    // code for no functional gain. This is the one shared implementation the two NEW draws use from
    // the start, rather than each hand-rolling a third near-identical copy of the same loop.
    public static unsafe class WeightedDrawUtility
    {
        public struct Candidate<T>
        {
            public T Value;
            public int Weight;
        }

        // Draws up to `count` distinct candidates, weighted, without replacement - stops early if
        // `candidates` runs dry or every remaining weight is 0. Mutates `candidates` (removes each
        // drawn entry) - pass a throwaway list. Returns a right-sized array (Length <= count).
        public static T[] Draw<T>(Frame f, List<Candidate<T>> candidates, int count)
        {
            int totalWeight = 0;

            for (int i = 0; i < candidates.Count; i++)
                totalWeight += candidates[i].Weight;

            T[] rolled = new T[count];
            int drawn = 0;

            for (int slot = 0; slot < count && totalWeight > 0 && candidates.Count > 0; slot++)
            {
                int roll = f.RNG->Next(0, totalWeight);
                int cursor = 0;
                int pick = candidates.Count - 1;

                for (int i = 0; i < candidates.Count; i++)
                {
                    cursor += candidates[i].Weight;

                    if (roll < cursor)
                    {
                        pick = i;
                        break;
                    }
                }

                rolled[drawn] = candidates[pick].Value;
                drawn++;

                totalWeight -= candidates[pick].Weight;
                candidates.RemoveAt(pick);
            }

            if (drawn == rolled.Length)
                return rolled;

            T[] trimmed = new T[drawn];
            Array.Copy(rolled, trimmed, drawn);
            return trimmed;
        }
    }
}
