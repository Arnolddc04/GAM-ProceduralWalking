using UnityEngine;

namespace GAMStudio.ProceduralWalking
{
    /// <summary>
    /// Allocation-free FABRIK solver that works on joint positions only.
    /// Converting the solved positions back into bone rotations is done by <see cref="IKChain"/>.
    /// </summary>
    public static class FabrikSolver
    {
        const float Epsilon = 1e-6f;

        /// <summary>
        /// Solves the chain in place. <paramref name="positions"/>[0] is the fixed root.
        /// </summary>
        /// <param name="positions">Joint positions, root first. Modified in place.</param>
        /// <param name="lengths">Segment lengths; lengths[i] is the distance between joint i and i + 1.</param>
        /// <returns>True when the tip ended within <paramref name="tolerance"/> of the target.</returns>
        public static bool Solve(Vector3[] positions, float[] lengths, Vector3 target, int iterations, float tolerance)
        {
            int last = positions.Length - 1;
            if (last < 1)
                return false;

            Vector3 root = positions[0];
            float totalLength = 0f;
            for (int i = 0; i < last; i++)
                totalLength += lengths[i];

            Vector3 toTarget = target - root;
            if (toTarget.sqrMagnitude >= totalLength * totalLength)
            {
                // Out of reach: stretch straight towards the target.
                Vector3 direction = SafeDirection(toTarget, positions[last] - root);
                for (int i = 1; i <= last; i++)
                    positions[i] = positions[i - 1] + direction * lengths[i - 1];
                return false;
            }

            float toleranceSq = tolerance * tolerance;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                if ((positions[last] - target).sqrMagnitude <= toleranceSq)
                    return true;

                // Backward pass: pin the tip to the target.
                positions[last] = target;
                for (int i = last - 1; i >= 0; i--)
                    positions[i] = positions[i + 1] + SafeDirection(positions[i] - positions[i + 1], Vector3.up) * lengths[i];

                // Forward pass: pin the root back in place.
                positions[0] = root;
                for (int i = 1; i <= last; i++)
                    positions[i] = positions[i - 1] + SafeDirection(positions[i] - positions[i - 1], Vector3.down) * lengths[i - 1];
            }

            return (positions[last] - target).sqrMagnitude <= toleranceSq;
        }

        /// <summary>
        /// Rotates the interior joints around the line through their neighbours so they bend towards
        /// <paramref name="pole"/>. Segment lengths and the tip position are preserved.
        /// </summary>
        /// <param name="maxJoints">How many interior joints (starting at the root side) are affected.</param>
        public static void ApplyPole(Vector3[] positions, Vector3 pole, int maxJoints = int.MaxValue)
        {
            int lastInterior = Mathf.Min(positions.Length - 2, maxJoints);
            for (int i = 1; i <= lastInterior; i++)
            {
                Vector3 axis = positions[i + 1] - positions[i - 1];
                if (axis.sqrMagnitude < Epsilon)
                    continue;
                axis.Normalize();

                Vector3 joint = Vector3.ProjectOnPlane(positions[i] - positions[i - 1], axis);
                Vector3 hint = Vector3.ProjectOnPlane(pole - positions[i - 1], axis);
                if (joint.sqrMagnitude < Epsilon || hint.sqrMagnitude < Epsilon)
                    continue;

                float angle = Vector3.SignedAngle(joint, hint, axis);
                positions[i] = positions[i - 1] + Quaternion.AngleAxis(angle, axis) * (positions[i] - positions[i - 1]);
            }
        }

        static Vector3 SafeDirection(Vector3 vector, Vector3 fallback)
        {
            float magnitude = vector.magnitude;
            if (magnitude > Epsilon)
                return vector / magnitude;
            float fallbackMagnitude = fallback.magnitude;
            return fallbackMagnitude > Epsilon ? fallback / fallbackMagnitude : Vector3.down;
        }
    }
}
