using System;
using UnityEngine;

namespace GAMStudio.ProceduralWalking
{
    /// <summary>
    /// Marks an <see cref="IKChain"/> as a walking leg of a <see cref="ProceduralWalker"/>.
    /// Holds per-leg settings and the runtime stepping state (read-only for other scripts).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(IKChain))]
    [AddComponentMenu("GAM Studio/Procedural Walking/Procedural Leg")]
    public class ProceduralLeg : MonoBehaviour
    {
        [Tooltip("Legs in the same group step together; groups take turns.")]
        [SerializeField, Min(0)] int group;
        [Tooltip("Distance the IK target is kept above the surface, along the surface normal.")]
        [SerializeField] float footOffset;
        [Tooltip("Offset added to the rest (home) foot position, in the walker's local space.")]
        [SerializeField] Vector3 homeOffset;
        [Tooltip("Scales the walker's step distance for this leg.")]
        [SerializeField, Min(0.05f)] float stepDistanceMultiplier = 1f;

        IKChain chain;

        public IKChain Chain => chain != null ? chain : (chain = GetComponent<IKChain>());
        public int Group { get => group; set => group = Mathf.Max(0, value); }
        public float FootOffset { get => footOffset; set => footOffset = value; }
        public Vector3 HomeOffset { get => homeOffset; set => homeOffset = value; }
        public float StepDistanceMultiplier { get => stepDistanceMultiplier; set => stepDistanceMultiplier = Mathf.Max(0.05f, value); }

        /// <summary>Raised when the foot touches down at the end of a step.</summary>
        public event Action<ProceduralLeg> Planted;

        /// <summary>Rest position of the foot, following the body.</summary>
        public Vector3 HomePosition { get; internal set; }
        public Vector3 HomeVelocity { get; internal set; }
        /// <summary>Whether a surface was found for the home position this frame.</summary>
        public bool HasGround { get; internal set; }
        public Vector3 GroundPoint { get; internal set; }
        public Vector3 GroundNormal { get; internal set; }

        /// <summary>Current foot (IK target) position, before <see cref="FootOffset"/>.</summary>
        public Vector3 FootPosition { get; internal set; }
        public Vector3 FootNormal { get; internal set; }
        /// <summary>False while the foot is dangling in the air with nothing to stand on.</summary>
        public bool IsFootGrounded { get; internal set; }

        public bool IsStepping { get; internal set; }
        /// <summary>0 to 1 progress of the current step.</summary>
        public float StepProgress { get; internal set; }
        public Vector3 StepStart { get; internal set; }
        public Vector3 StepTarget { get; internal set; }
        public Vector3 StepTargetNormal { get; internal set; }

        internal Vector3 homeLocal;
        internal Vector3 lastHome;
        internal Vector3 stepStartNormal;

        internal void RaisePlanted() => Planted?.Invoke(this);
    }
}
