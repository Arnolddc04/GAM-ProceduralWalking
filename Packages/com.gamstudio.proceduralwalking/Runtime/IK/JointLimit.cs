using System;
using UnityEngine;

namespace GAMStudio.ProceduralWalking
{
    public enum JointLimitType
    {
        /// <summary>No rotational limit.</summary>
        Free,
        /// <summary>Rotation around a single axis, between a min and max angle.</summary>
        Hinge,
        /// <summary>Swing inside a cone around the bone direction, plus a twist range around it.</summary>
        Ball,
    }

    /// <summary>
    /// Rotational limit for one bone of an <see cref="IKChain"/>. All angles are relative to the rest pose
    /// (0 = rest) and all axes are in the bone's local space at rest.
    /// </summary>
    [Serializable]
    public class JointLimit
    {
        public JointLimitType type = JointLimitType.Free;

        [Tooltip("Hinge axis in the bone's local space.")]
        public Vector3 axis = Vector3.right;
        [Range(-180f, 180f)] public float minAngle = -60f;
        [Range(-180f, 180f)] public float maxAngle = 60f;

        [Tooltip("Maximum swing away from the rest direction, in degrees.")]
        [Range(0f, 180f)] public float maxSwing = 60f;
        [Range(-180f, 180f)] public float minTwist = -15f;
        [Range(-180f, 180f)] public float maxTwist = 15f;

        public bool IsLimited => type != JointLimitType.Free;

        public Vector3 HingeAxis => axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.right;

        /// <summary>
        /// Clamps <paramref name="delta"/>, the bone's rotation relative to its rest local rotation.
        /// </summary>
        /// <param name="boneDirection">Direction to the child bone in the bone's local space (the twist axis of a ball joint).</param>
        public Quaternion Constrain(Quaternion delta, Vector3 boneDirection)
        {
            switch (type)
            {
                case JointLimitType.Hinge:
                {
                    Vector3 hingeAxis = HingeAxis;
                    float angle = Mathf.Clamp(TwistAngle(delta, hingeAxis), Mathf.Min(minAngle, maxAngle), Mathf.Max(minAngle, maxAngle));
                    return Quaternion.AngleAxis(angle, hingeAxis);
                }
                case JointLimitType.Ball:
                {
                    Vector3 twistAxis = boneDirection.sqrMagnitude > 1e-8f ? boneDirection.normalized : Vector3.forward;
                    Decompose(delta, twistAxis, out Quaternion swing, out Quaternion twist);

                    float twistAngle = Mathf.Clamp(TwistAngle(twist, twistAxis), Mathf.Min(minTwist, maxTwist), Mathf.Max(minTwist, maxTwist));

                    swing.ToAngleAxis(out float swingAngle, out Vector3 swingAxis);
                    if (swingAngle > 180f)
                        swingAngle -= 360f;
                    if (Mathf.Abs(swingAngle) > maxSwing && !float.IsNaN(swingAxis.x))
                        swing = Quaternion.AngleAxis(Mathf.Sign(swingAngle) * maxSwing, swingAxis);

                    return swing * Quaternion.AngleAxis(twistAngle, twistAxis);
                }
                default:
                    return delta;
            }
        }

        /// <summary>Signed rotation of <paramref name="rotation"/> around <paramref name="axis"/> (unit length), in -180 to 180 degrees.</summary>
        public static float TwistAngle(Quaternion rotation, Vector3 axis)
        {
            float projection = rotation.x * axis.x + rotation.y * axis.y + rotation.z * axis.z;
            float angle = 2f * Mathf.Atan2(projection, rotation.w) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(0f, angle);
        }

        /// <summary>Swing-twist decomposition: <paramref name="rotation"/> = swing * twist, with twist around <paramref name="axis"/>.</summary>
        public static void Decompose(Quaternion rotation, Vector3 axis, out Quaternion swing, out Quaternion twist)
        {
            float projection = rotation.x * axis.x + rotation.y * axis.y + rotation.z * axis.z;
            var twistVector = new Vector4(axis.x * projection, axis.y * projection, axis.z * projection, rotation.w);
            float magnitude = twistVector.magnitude;
            twist = magnitude > 1e-8f
                ? new Quaternion(twistVector.x / magnitude, twistVector.y / magnitude, twistVector.z / magnitude, twistVector.w / magnitude)
                : Quaternion.identity;
            swing = rotation * Quaternion.Inverse(twist);
        }
    }
}
