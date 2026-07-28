using UnityEngine;

namespace QuantumUser.View.Util
{
    // FABRIK (Forward And Backward Reaching IK) solver for a flat 2D bone chain. Pure math, no
    // MonoBehaviour - callers own the joints/lengths buffers so several limbs can solve every
    // frame without allocating.
    public static class IkChain2D
    {
        public static void Solve(Vector2[] joints, float[] lengths, Vector2 root, Vector2 target, int iterations = 10, float tolerance = 0.001f)
        {
            float totalLength = 0f;
            for (int i = 0; i < lengths.Length; i++)
                totalLength += lengths[i];

            if (Vector2.Distance(root, target) >= totalLength)
            {
                // Target out of reach: fully extend straight toward it instead of iterating.
                Vector2 dir = (target - root).normalized;
                joints[0] = root;
                for (int i = 0; i < lengths.Length; i++)
                    joints[i + 1] = joints[i] + dir * lengths[i];
                return;
            }

            for (int iter = 0; iter < iterations; iter++)
            {
                if (Vector2.Distance(joints[joints.Length - 1], target) < tolerance)
                    break;

                // Backward pass: pin the tip to the target, walk lengths back toward the root.
                joints[joints.Length - 1] = target;
                for (int i = joints.Length - 2; i >= 0; i--)
                {
                    Vector2 dir = (joints[i] - joints[i + 1]).normalized;
                    joints[i] = joints[i + 1] + dir * lengths[i];
                }

                // Forward pass: pin the root back to its fixed position, walk lengths out to the tip.
                joints[0] = root;
                for (int i = 0; i < lengths.Length; i++)
                {
                    Vector2 dir = (joints[i + 1] - joints[i]).normalized;
                    joints[i + 1] = joints[i] + dir * lengths[i];
                }
            }
        }
    }
}
