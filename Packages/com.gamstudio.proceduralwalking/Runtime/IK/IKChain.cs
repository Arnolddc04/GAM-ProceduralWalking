using System.Collections.Generic;
using UnityEngine;

namespace GAMStudio.ProceduralWalking
{
    /// <summary>
    /// IK constraint for a chain of bones (root to tip, any length). Solved with FABRIK, with an optional
    /// pole (bend hint) and optional per-bone rotational limits (hinge or ball), refined with constrained CCD.
    /// Works stand-alone with a target transform, or driven by a <see cref="ProceduralWalker"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(110)]
    [AddComponentMenu("GAM Studio/Procedural Walking/IK Chain")]
    public class IKChain : MonoBehaviour
    {
        public enum PoleMode
        {
            /// <summary>No bend hint; the rest pose alone decides how the chain bends.</summary>
            None,
            /// <summary>Only the first interior joint (the "knee") is turned towards the pole.</summary>
            FirstJoint,
            /// <summary>Every interior joint is turned towards the pole.</summary>
            AllJoints,
        }

        [Tooltip("First bone of the chain. Defaults to this transform.")]
        [SerializeField] Transform root;
        [Tooltip("Last bone of the chain (foot / claw tip). Must be a descendant of the root.")]
        [SerializeField] Transform tip;
        [Tooltip("Optional target. Ignored while a ProceduralWalker drives this chain.")]
        [SerializeField] Transform target;
        [Tooltip("Optional bend hint. When empty, an automatic pole is derived from the rest pose.")]
        [SerializeField] Transform pole;
        [SerializeField] PoleMode poleMode = PoleMode.FirstJoint;
        [Tooltip("Automatic pole only: how far, in chain lengths, the pole is pushed along the parent's up axis.")]
        [SerializeField] float autoPoleUpBias = 0.5f;
        [SerializeField, Range(1, 64)] int iterations = 12;
        [SerializeField, Min(0f)] float tolerance = 0.001f;
        [SerializeField, Range(0f, 1f)] float weight = 1f;
        [Tooltip("Reset the bones to their rest pose before each solve. Disable when an Animator already poses the bones every frame.")]
        [SerializeField] bool resetToRestPose = true;
        [Tooltip("Rotational limit per bone, root first (the tip has none). Angles are relative to the rest pose.")]
        [SerializeField] List<JointLimit> jointLimits = new List<JointLimit>();
        [Tooltip("Constrained CCD passes run after FABRIK when any joint is limited, to pull the tip back onto the target.")]
        [SerializeField, Range(0, 64)] int constraintIterations = 10;

        static readonly List<Transform> s_ChainBuffer = new List<Transform>();

        Transform[] bones;
        Quaternion[] restLocalRotations;
        Vector3[] restBoneDirections;
        Vector3[] positions;
        float[] lengths;
        bool initialized;

        /// <summary>When true the chain does not solve itself in LateUpdate; the owner calls <see cref="Solve"/>.</summary>
        public bool ExternallyDriven { get; set; }

        public Transform Root => root != null ? root : transform;
        public Transform Tip => tip;
        public Transform Target { get => target; set => target = value; }
        public Transform Pole { get => pole; set => pole = value; }
        public PoleMode BendMode { get => poleMode; set => poleMode = value; }
        public float Weight { get => weight; set => weight = Mathf.Clamp01(value); }
        public bool ResetToRestPose { get => resetToRestPose; set => resetToRestPose = value; }
        public int ConstraintIterations { get => constraintIterations; set => constraintIterations = Mathf.Max(0, value); }
        public bool IsInitialized => initialized;
        public IReadOnlyList<Transform> Bones => bones;
        /// <summary>Limits per bone, root first. Index i limits bone i; the list has one entry less than the chain has transforms.</summary>
        public List<JointLimit> JointLimits => jointLimits;
        /// <summary>Sum of the segment lengths, measured at the last solve (or initialization).</summary>
        public float ChainLength { get; private set; }

        public bool HasJointLimits
        {
            get
            {
                foreach (JointLimit limit in jointLimits)
                {
                    if (limit != null && limit.IsLimited)
                        return true;
                }
                return false;
            }
        }

        /// <summary>Assigns the chain bones. The chain re-initializes on the next solve.</summary>
        public void Configure(Transform chainRoot, Transform chainTip)
        {
            if (initialized)
                RestoreRestPose();
            root = chainRoot;
            tip = chainTip;
            initialized = false;
            SyncJointLimits();
        }

        /// <summary>
        /// Collects the transforms from <paramref name="chainRoot"/> down to <paramref name="chainTip"/> (inclusive).
        /// Returns false when the tip is not a descendant of the root.
        /// </summary>
        public static bool TryCollectChain(Transform chainRoot, Transform chainTip, List<Transform> result)
        {
            result.Clear();
            if (chainRoot == null || chainTip == null || chainRoot == chainTip)
                return false;

            for (Transform t = chainTip; t != null; t = t.parent)
            {
                result.Add(t);
                if (t == chainRoot)
                {
                    result.Reverse();
                    return true;
                }
            }

            result.Clear();
            return false;
        }

        /// <summary>Measures the chain length from the current pose without initializing.</summary>
        public static float MeasureChain(Transform chainRoot, Transform chainTip)
        {
            if (!TryCollectChain(chainRoot, chainTip, s_ChainBuffer))
                return 0f;
            float length = 0f;
            for (int i = 0; i < s_ChainBuffer.Count - 1; i++)
                length += Vector3.Distance(s_ChainBuffer[i].position, s_ChainBuffer[i + 1].position);
            return length;
        }

        void OnEnable()
        {
            Initialize();
        }

        void OnValidate()
        {
            SyncJointLimits();
        }

        /// <summary>Resizes <see cref="JointLimits"/> to one entry per bone (the tip excluded), keeping existing entries.</summary>
        public void SyncJointLimits()
        {
            if (!TryCollectChain(Root, tip, s_ChainBuffer))
                return;
            int count = s_ChainBuffer.Count - 1;
            while (jointLimits.Count < count)
                jointLimits.Add(new JointLimit());
            if (jointLimits.Count > count)
                jointLimits.RemoveRange(count, jointLimits.Count - count);
            for (int i = 0; i < jointLimits.Count; i++)
            {
                if (jointLimits[i] == null)
                    jointLimits[i] = new JointLimit();
            }
        }

        /// <summary>Builds the bone list and captures the current pose as the rest pose.</summary>
        public bool Initialize()
        {
            if (initialized)
                return true;

            var chain = new List<Transform>();
            if (!TryCollectChain(Root, tip, chain))
                return false;

            int count = chain.Count;
            bones = chain.ToArray();
            restLocalRotations = new Quaternion[count];
            restBoneDirections = new Vector3[count];
            positions = new Vector3[count];
            lengths = new float[count - 1];
            initialized = true;
            SyncJointLimits();
            CaptureRestPose();
            return true;
        }

        /// <summary>Stores the current bone rotations as the rest pose.</summary>
        public void CaptureRestPose()
        {
            if (!initialized)
                return;
            float total = 0f;
            for (int i = 0; i < bones.Length; i++)
            {
                restLocalRotations[i] = bones[i].localRotation;
                if (i < bones.Length - 1)
                {
                    restBoneDirections[i] = bones[i + 1].localPosition.normalized;
                    total += Vector3.Distance(bones[i].position, bones[i + 1].position);
                }
            }
            ChainLength = total;
        }

        /// <summary>Puts the bones back into the captured rest pose.</summary>
        public void RestoreRestPose()
        {
            if (!initialized)
                return;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null)
                    bones[i].localRotation = restLocalRotations[i];
            }
        }

        /// <summary>The rest local rotation of bone <paramref name="index"/>; before initialization, its current local rotation.</summary>
        public Quaternion GetRestLocalRotation(int index, Transform bone)
        {
            return initialized && index < restLocalRotations.Length ? restLocalRotations[index] : bone.localRotation;
        }

        void LateUpdate()
        {
            if (!ExternallyDriven && target != null)
                Solve(target.position);
        }

        /// <summary>Rotates the bones so the tip reaches <paramref name="targetPosition"/> as closely as the limits allow.</summary>
        public void Solve(Vector3 targetPosition)
        {
            if (!initialized && !Initialize())
                return;

            if (resetToRestPose)
                RestoreRestPose();

            int last = bones.Length - 1;
            float total = 0f;
            for (int i = 0; i <= last; i++)
            {
                positions[i] = bones[i].position;
                if (i > 0)
                {
                    lengths[i - 1] = Vector3.Distance(positions[i - 1], positions[i]);
                    total += lengths[i - 1];
                }
            }
            ChainLength = total;

            if (weight <= 0f)
                return;

            Vector3 goal = weight >= 1f ? targetPosition : Vector3.Lerp(positions[last], targetPosition, weight);

            Vector3 polePosition = default;
            bool hasPole = poleMode != PoleMode.None && TryGetPolePosition(out polePosition);
            FabrikSolver.Solve(positions, lengths, goal, iterations, tolerance);
            if (hasPole)
                FabrikSolver.ApplyPole(positions, polePosition, poleMode == PoleMode.FirstJoint ? 1 : int.MaxValue);

            bool limited = HasJointLimits;
            for (int i = 0; i < last; i++)
            {
                Vector3 from = bones[i + 1].position - bones[i].position;
                Vector3 to = positions[i + 1] - positions[i];
                if (from.sqrMagnitude > 1e-10f && to.sqrMagnitude > 1e-10f)
                    bones[i].rotation = Quaternion.FromToRotation(from, to) * bones[i].rotation;
                if (limited)
                    ApplyLimit(i);
            }

            if (!limited)
                return;

            float toleranceSq = tolerance * tolerance;
            for (int iteration = 0; iteration < constraintIterations; iteration++)
            {
                if ((bones[last].position - goal).sqrMagnitude <= toleranceSq)
                    break;
                for (int i = last - 1; i >= 0; i--)
                    ConstrainedCcdStep(i, goal);
            }
        }

        /// <summary>Clamps bone <paramref name="index"/> to its joint limit.</summary>
        void ApplyLimit(int index)
        {
            if (index >= jointLimits.Count)
                return;
            JointLimit limit = jointLimits[index];
            if (limit == null || !limit.IsLimited)
                return;

            Quaternion rest = restLocalRotations[index];
            Quaternion delta = Quaternion.Inverse(rest) * bones[index].localRotation;
            bones[index].localRotation = rest * limit.Constrain(delta, restBoneDirections[index]);
        }

        /// <summary>One CCD step: turn bone <paramref name="index"/> so the tip points at the goal, then re-apply its limit.</summary>
        void ConstrainedCcdStep(int index, Vector3 goal)
        {
            Transform bone = bones[index];
            Vector3 toTip = bones[bones.Length - 1].position - bone.position;
            Vector3 toGoal = goal - bone.position;

            JointLimit limit = index < jointLimits.Count ? jointLimits[index] : null;
            if (limit != null && limit.type == JointLimitType.Hinge)
            {
                // Rotate only around the hinge so the limit does not throw the step away.
                Vector3 axis = GetJointSpaceRotation(index) * limit.HingeAxis;
                toTip = Vector3.ProjectOnPlane(toTip, axis);
                toGoal = Vector3.ProjectOnPlane(toGoal, axis);
            }

            if (toTip.sqrMagnitude < 1e-10f || toGoal.sqrMagnitude < 1e-10f)
                return;

            bone.rotation = Quaternion.FromToRotation(toTip, toGoal) * bone.rotation;
            ApplyLimit(index);
        }

        /// <summary>World rotation of bone <paramref name="index"/> in its rest pose, under its current parent: the frame joint limits are expressed in.</summary>
        public Quaternion GetJointSpaceRotation(int index)
        {
            Transform bone = initialized ? bones[index] : null;
            if (bone == null)
                return Quaternion.identity;
            Quaternion parent = bone.parent != null ? bone.parent.rotation : Quaternion.identity;
            return parent * restLocalRotations[index];
        }

        /// <summary>
        /// Sets up limits from the current (rest) pose: the root becomes a ball joint and every other bone a hinge
        /// whose axis is the normal of the plane its parent, itself and its child bend in.
        /// </summary>
        public void AutoConfigureJointLimits(float hingeRange = 70f, float rootSwing = 60f, float rootTwist = 60f)
        {
            RestoreRestPose();
            if (!TryCollectChain(Root, tip, s_ChainBuffer))
                return;
            SyncJointLimits();

            var chain = new List<Transform>(s_ChainBuffer);
            for (int i = 0; i < jointLimits.Count; i++)
            {
                JointLimit limit = jointLimits[i];
                if (i == 0)
                {
                    limit.type = JointLimitType.Ball;
                    limit.maxSwing = rootSwing;
                    limit.minTwist = -rootTwist;
                    limit.maxTwist = rootTwist;
                }
                else
                {
                    limit.type = JointLimitType.Hinge;
                    limit.axis = ComputeHingeAxis(chain, i);
                    limit.minAngle = -hingeRange;
                    limit.maxAngle = hingeRange;
                }
            }
        }

        /// <summary>Recomputes the axis of every hinge from the current (rest) pose, keeping the angle ranges.</summary>
        public void DetectHingeAxes()
        {
            RestoreRestPose();
            if (!TryCollectChain(Root, tip, s_ChainBuffer))
                return;
            SyncJointLimits();

            var chain = new List<Transform>(s_ChainBuffer);
            for (int i = 0; i < jointLimits.Count; i++)
            {
                if (jointLimits[i].type == JointLimitType.Hinge)
                    jointLimits[i].axis = ComputeHingeAxis(chain, i);
            }
        }

        /// <summary>
        /// Hinge axis for bone <paramref name="index"/> in its local space: the normal of the bend plane at that joint,
        /// falling back to the chain's overall bend plane and finally to the bone's local right.
        /// </summary>
        public static Vector3 ComputeHingeAxis(IReadOnlyList<Transform> chain, int index)
        {
            Transform bone = chain[index];
            Vector3 toChild = chain[index + 1].position - bone.position;
            Vector3 normal = Vector3.zero;

            if (index > 0)
                normal = Vector3.Cross(bone.position - chain[index - 1].position, toChild);

            if (normal.sqrMagnitude < 1e-8f)
            {
                Transform parent = chain[0].parent;
                Vector3 up = parent != null ? parent.up : Vector3.up;
                normal = Vector3.Cross(up, toChild);
            }

            if (normal.sqrMagnitude < 1e-8f)
                return Vector3.right;

            return (Quaternion.Inverse(bone.rotation) * normal).normalized;
        }

        /// <summary>
        /// The pole used for the current pose: the assigned pole transform, or the average of the
        /// interior joints pushed up along the root's parent up axis.
        /// </summary>
        public bool TryGetPolePosition(out Vector3 polePosition)
        {
            if (pole != null)
            {
                polePosition = pole.position;
                return true;
            }

            if (!initialized || bones.Length < 3)
            {
                polePosition = default;
                return false;
            }

            Vector3 interior = Vector3.zero;
            for (int i = 1; i < bones.Length - 1; i++)
                interior += bones[i].position;
            interior /= bones.Length - 2;

            Transform parent = bones[0].parent;
            Vector3 up = parent != null ? parent.up : Vector3.up;
            polePosition = interior + up * (autoPoleUpBias * ChainLength);
            return true;
        }
    }
}
