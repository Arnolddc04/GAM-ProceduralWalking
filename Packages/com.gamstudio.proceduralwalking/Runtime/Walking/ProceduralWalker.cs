using System;
using System.Collections.Generic;
using UnityEngine;

namespace GAMStudio.ProceduralWalking
{
    public enum GaitMode
    {
        /// <summary>Legs step per group; a group only lifts while the other groups are planted.</summary>
        Groups,
        /// <summary>Each leg steps on its own, limited by <c>maxSimultaneousSteps</c>.</summary>
        Independent,
    }

    public enum GizmoDisplay
    {
        Never,
        WhenSelected,
        Always,
    }

    /// <summary>
    /// Procedural walking for a body with any number of <see cref="ProceduralLeg"/>s.
    /// Move this transform (from your own controller, in Update) and the walker places the feet,
    /// keeps the body at its height above the surface and aligns it to floors, walls and ceilings.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("GAM Studio/Procedural Walking/Procedural Walker")]
    public class ProceduralWalker : MonoBehaviour
    {
        const int RaycastBufferSize = 16;

        static readonly Color[] s_GroupColors =
        {
            new Color(1.00f, 0.55f, 0.10f),
            new Color(0.15f, 0.80f, 1.00f),
            new Color(0.95f, 0.30f, 0.75f),
            new Color(0.45f, 0.95f, 0.30f),
            new Color(1.00f, 0.90f, 0.20f),
            new Color(0.60f, 0.45f, 1.00f),
        };

        [SerializeField] List<ProceduralLeg> legs = new List<ProceduralLeg>();

        [Header("Surface Detection")]
        [Tooltip("Layers the feet can stand on. The walker's own colliders are always ignored.")]
        [SerializeField] LayerMask groundMask = ~0;
        [SerializeField] QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;
        [Tooltip("How far above the home position (along the body up) the ground ray starts.")]
        [SerializeField, Min(0.01f)] float castHeight = 0.6f;
        [Tooltip("How far below the home position the ground ray reaches.")]
        [SerializeField, Min(0.01f)] float castDepth = 1f;
        [Tooltip("Cast from the body towards each foot to find walls in the way (concave corners).")]
        [SerializeField] bool detectWalls = true;
        [Tooltip("When nothing is below a foot, cast back under the body to wrap around ledges (convex corners).")]
        [SerializeField] bool wrapAroundEdges = true;

        [Header("Stepping")]
        [SerializeField] GaitMode gaitMode = GaitMode.Groups;
        [Tooltip("How far a planted foot may drift from its home before it steps.")]
        [SerializeField, Min(0.01f)] float stepDistance = 0.5f;
        [SerializeField, Min(0.01f)] float stepDuration = 0.15f;
        [SerializeField, Min(0f)] float stepHeight = 0.25f;
        [Tooltip("Foot lift over the step (0 to 1 on both axes), scaled by Step Height.")]
        [SerializeField] AnimationCurve stepHeightCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));
        [Tooltip("How far ahead of the home position a foot lands, as a fraction of Step Distance.")]
        [SerializeField, Range(0f, 1f)] float overshoot = 0.4f;
        [Tooltip("Groups mode: a group may lift once the stepping group is this far from finishing (0 = strictly one group at a time).")]
        [SerializeField, Range(0f, 1f)] float stepOverlap = 0.2f;
        [Tooltip("A foot this many Step Distances from home steps immediately, ignoring the gait.")]
        [SerializeField, Min(1f)] float forceStepMultiplier = 1.75f;
        [Tooltip("Independent mode: maximum legs in the air at once.")]
        [SerializeField, Min(1)] int maxSimultaneousSteps = 4;
        [Tooltip("Seconds without movement before the feet settle back to their homes (0 = never).")]
        [SerializeField, Min(0f)] float settleDelay = 0.3f;
        [Tooltip("While settling, feet step when they are this fraction of Step Distance from home.")]
        [SerializeField, Range(0.01f, 1f)] float settleThreshold = 0.15f;

        [Header("Body")]
        [SerializeField] bool adaptBodyHeight = true;
        [Tooltip("Body height above the surface. 0 = measured from the rig's rest pose.")]
        [SerializeField, Min(0f)] float bodyHeight;
        [SerializeField, Min(0f)] float heightSmoothing = 10f;
        [Tooltip("Rotate the body so its up axis follows the surface (walls, ceilings, slopes).")]
        [SerializeField] bool alignToSurface = true;
        [SerializeField, Min(0f)] float rotationSmoothing = 6f;
        [Tooltip("0 = surface normals under the feet, 1 = plane through the foot positions.")]
        [SerializeField, Range(0f, 1f)] float feetPlaneWeight = 0.5f;
        [Tooltip("Ray along the movement direction that starts the climb before the body hits a wall (0 = off).")]
        [SerializeField, Min(0f)] float wallProbeDistance = 1f;
        [SerializeField] bool fallWhenUngrounded = true;
        [SerializeField] float gravity = 9.81f;

        [Header("Debug")]
        [SerializeField] GizmoDisplay gizmoDisplay = GizmoDisplay.Always;

        readonly RaycastHit[] hitBuffer = new RaycastHit[RaycastBufferSize];
        readonly List<ProceduralLeg> activeLegs = new List<ProceduralLeg>();
        readonly List<int> groupIds = new List<int>();
        readonly List<ProceduralLeg> perimeter = new List<ProceduralLeg>();
        readonly List<Vector3> perimeterPoints = new List<Vector3>();

        bool initialized;
        float resolvedBodyHeight;
        int nextGroupIndex;
        float idleTime;
        float fallSpeed;
        Vector3 lastPosition;

        /// <summary>Raised whenever any leg finishes a step.</summary>
        public event Action<ProceduralLeg> LegPlanted;

        public IReadOnlyList<ProceduralLeg> Legs => legs;
        public bool IsInitialized => initialized;
        public bool IsGrounded { get; private set; }
        /// <summary>Movement of the body over the last tick, in units per second.</summary>
        public Vector3 Velocity { get; private set; }
        /// <summary>The surface normal the body is aligning to.</summary>
        public Vector3 SurfaceNormal { get; private set; } = Vector3.up;
        /// <summary>The point on the surface the body height is measured from.</summary>
        public Vector3 SurfacePoint { get; private set; }
        /// <summary>True when the feet are settling back to their homes after the body stopped.</summary>
        public bool IsSettling => settleDelay > 0f && idleTime >= settleDelay;

        public LayerMask GroundMask { get => groundMask; set => groundMask = value; }
        public float CastHeight { get => castHeight; set => castHeight = Mathf.Max(0.01f, value); }
        public float CastDepth { get => castDepth; set => castDepth = Mathf.Max(0.01f, value); }
        public GaitMode Gait { get => gaitMode; set => gaitMode = value; }
        public float StepDistance { get => stepDistance; set => stepDistance = Mathf.Max(0.01f, value); }
        public float StepDuration { get => stepDuration; set => stepDuration = Mathf.Max(0.01f, value); }
        public float StepHeight { get => stepHeight; set => stepHeight = Mathf.Max(0f, value); }
        public float WallProbeDistance { get => wallProbeDistance; set => wallProbeDistance = Mathf.Max(0f, value); }
        public GizmoDisplay GizmoDisplay { get => gizmoDisplay; set => gizmoDisplay = value; }

        /// <summary>The body height in use: the configured value, or the one measured from the rest pose.</summary>
        public float BodyHeight
        {
            get => bodyHeight > 0f ? bodyHeight : (initialized ? resolvedBodyHeight : MeasureRestBodyHeight());
            set => bodyHeight = Mathf.Max(0f, value);
        }

        public static Color GetGroupColor(int group) => s_GroupColors[Mathf.Abs(group) % s_GroupColors.Length];

        /// <summary>Replaces the leg list. The walker re-initializes on the next tick.</summary>
        public void SetLegs(IEnumerable<ProceduralLeg> newLegs)
        {
            Deinitialize();
            legs.Clear();
            foreach (ProceduralLeg leg in newLegs)
            {
                if (leg != null && !legs.Contains(leg))
                    legs.Add(leg);
            }
        }

        /// <summary>
        /// Derives step distance, step height, cast lengths and wall probe distance from the legs' current pose.
        /// Works in edit mode.
        /// </summary>
        public void AutoConfigure()
        {
            float lengthSum = 0f;
            int count = 0;
            foreach (ProceduralLeg leg in legs)
            {
                if (leg == null || leg.Chain == null)
                    continue;
                float length = IKChain.MeasureChain(leg.Chain.Root, leg.Chain.Tip);
                if (length <= 0f)
                    continue;
                lengthSum += length;
                count++;
            }

            if (count == 0)
                return;

            float legLength = lengthSum / count;
            float height = Mathf.Max(MeasureRestBodyHeight(), legLength * 0.2f);
            stepDistance = legLength * 0.35f;
            stepHeight = legLength * 0.2f;
            castHeight = height;
            castDepth = height + legLength * 0.5f;
            wallProbeDistance = legLength * 0.75f;
        }

        void OnEnable()
        {
            Initialize();
        }

        void OnDisable()
        {
            Deinitialize();
        }

        void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime > 0f)
                Tick(deltaTime);
        }

        /// <summary>
        /// Captures the rest pose of every leg and plants the feet. Called automatically; call it again
        /// after changing the rig at runtime (or use <see cref="SetLegs"/>).
        /// </summary>
        public bool Initialize()
        {
            if (initialized)
                return true;

            activeLegs.Clear();
            foreach (ProceduralLeg leg in legs)
            {
                if (leg == null)
                    continue;
                IKChain chain = leg.Chain;
                if (chain == null || !chain.Initialize())
                {
                    Debug.LogWarning($"[ProceduralWalker] Leg '{leg.name}' has no valid IK chain (is the tip a child of the root?). It is skipped.", leg);
                    continue;
                }
                activeLegs.Add(leg);
            }

            if (activeLegs.Count == 0)
                return false;

            Transform body = transform;
            float heightSum = 0f;
            foreach (ProceduralLeg leg in activeLegs)
            {
                leg.Chain.ExternallyDriven = true;
                Vector3 restTip = leg.Chain.Tip.position;
                leg.homeLocal = body.InverseTransformPoint(restTip) + leg.HomeOffset;
                heightSum += Vector3.Dot(body.position - restTip, body.up);
            }

            resolvedBodyHeight = Mathf.Max(heightSum / activeLegs.Count, 0.01f);

            // Order the legs around the body so the feet form a polygon for the surface plane fit.
            perimeter.Clear();
            perimeter.AddRange(activeLegs);
            perimeter.Sort((a, b) => Mathf.Atan2(a.homeLocal.z, a.homeLocal.x).CompareTo(Mathf.Atan2(b.homeLocal.z, b.homeLocal.x)));

            groupIds.Clear();
            foreach (ProceduralLeg leg in activeLegs)
            {
                if (!groupIds.Contains(leg.Group))
                    groupIds.Add(leg.Group);
            }
            groupIds.Sort();

            Vector3 up = body.up;
            foreach (ProceduralLeg leg in activeLegs)
            {
                Vector3 home = body.TransformPoint(leg.homeLocal);
                leg.HomePosition = home;
                leg.lastHome = home;
                leg.HomeVelocity = Vector3.zero;
                leg.IsStepping = false;
                leg.StepProgress = 0f;
                leg.HasGround = FindSurface(home, up, out Vector3 point, out Vector3 normal);
                leg.GroundPoint = point;
                leg.GroundNormal = normal;
                leg.FootPosition = point;
                leg.FootNormal = normal;
                leg.IsFootGrounded = leg.HasGround;
            }

            SurfaceNormal = up;
            SurfacePoint = body.position - up * resolvedBodyHeight;
            lastPosition = body.position;
            nextGroupIndex = 0;
            idleTime = 0f;
            fallSpeed = 0f;
            initialized = true;
            return true;
        }

        /// <summary>Restores the legs to their rest pose and forgets the runtime state.</summary>
        public void Deinitialize()
        {
            if (!initialized)
                return;
            foreach (ProceduralLeg leg in activeLegs)
            {
                if (leg != null && leg.Chain != null)
                {
                    leg.Chain.RestoreRestPose();
                    leg.Chain.ExternallyDriven = false;
                }
            }
            activeLegs.Clear();
            initialized = false;
        }

        /// <summary>Advances the walker by <paramref name="deltaTime"/>. Called from LateUpdate.</summary>
        public void Tick(float deltaTime)
        {
            if (!initialized && !Initialize())
                return;
            if (deltaTime <= 0f)
                return;

            Transform body = transform;
            Velocity = (body.position - lastPosition) / deltaTime;

            SenseSurfaces(deltaTime);
            UpdateBody(deltaTime);
            UpdateIdle(deltaTime);
            UpdateGait();

            foreach (ProceduralLeg leg in activeLegs)
                UpdateFoot(leg, deltaTime);

            foreach (ProceduralLeg leg in activeLegs)
                leg.Chain.Solve(leg.FootPosition + leg.FootNormal * leg.FootOffset);

            lastPosition = body.position;
        }

        void SenseSurfaces(float deltaTime)
        {
            Transform body = transform;
            Vector3 up = body.up;
            foreach (ProceduralLeg leg in activeLegs)
            {
                Vector3 home = body.TransformPoint(leg.homeLocal);
                leg.HomeVelocity = (home - leg.lastHome) / deltaTime;
                leg.lastHome = home;
                leg.HomePosition = home;
                leg.HasGround = FindSurface(home, up, out Vector3 point, out Vector3 normal);
                leg.GroundPoint = point;
                leg.GroundNormal = normal;
            }
        }

        void UpdateBody(float deltaTime)
        {
            Transform body = transform;
            Vector3 up = body.up;

            int count = 0;
            Vector3 pointSum = Vector3.zero;
            Vector3 normalSum = Vector3.zero;
            foreach (ProceduralLeg leg in activeLegs)
            {
                if (!leg.HasGround)
                    continue;
                pointSum += leg.GroundPoint;
                normalSum += leg.GroundNormal;
                count++;
            }

            IsGrounded = count > 0;
            if (!IsGrounded)
            {
                if (fallWhenUngrounded)
                {
                    fallSpeed += gravity * deltaTime;
                    body.position += Vector3.down * (fallSpeed * deltaTime);
                    if (alignToSurface)
                        RotateTowards(Vector3.up, deltaTime);
                }
                return;
            }

            fallSpeed = 0f;
            Vector3 targetNormal = normalSum.sqrMagnitude > 1e-8f ? normalSum.normalized : up;

            if (count >= 3 && feetPlaneWeight > 0f)
            {
                Vector3 planeNormal = PerimeterNormal(up);
                if (planeNormal.sqrMagnitude > 1e-8f)
                    targetNormal = Vector3.Lerp(targetNormal, planeNormal, feetPlaneWeight).normalized;
            }

            if (wallProbeDistance > 0f)
            {
                Vector3 planarVelocity = Vector3.ProjectOnPlane(Velocity, up);
                Vector3 probeDirection = planarVelocity.sqrMagnitude > 1e-4f ? planarVelocity.normalized : body.forward;
                if (Cast(body.position, probeDirection, wallProbeDistance, out RaycastHit wall))
                {
                    float closeness = 1f - wall.distance / wallProbeDistance;
                    targetNormal = Vector3.Slerp(targetNormal, wall.normal, closeness).normalized;
                }
            }

            SurfaceNormal = targetNormal;
            SurfacePoint = pointSum / count;

            if (alignToSurface)
                RotateTowards(targetNormal, deltaTime);

            if (adaptBodyHeight)
            {
                float height = Vector3.Dot(body.position - SurfacePoint, targetNormal);
                float correction = (BodyHeight - height) * Blend(heightSmoothing, deltaTime);
                body.position += targetNormal * correction;
            }
        }

        void RotateTowards(Vector3 targetUp, float deltaTime)
        {
            Transform body = transform;
            Quaternion aligned = Quaternion.FromToRotation(body.up, targetUp) * body.rotation;
            body.rotation = Quaternion.Slerp(body.rotation, aligned, Blend(rotationSmoothing, deltaTime));
        }

        /// <summary>Newell's method over the ground points, ordered around the body.</summary>
        Vector3 PerimeterNormal(Vector3 up)
        {
            perimeterPoints.Clear();
            foreach (ProceduralLeg leg in perimeter)
            {
                if (leg.HasGround)
                    perimeterPoints.Add(leg.GroundPoint);
            }

            Vector3 normal = Vector3.zero;
            int count = perimeterPoints.Count;
            for (int i = 0; i < count; i++)
            {
                Vector3 current = perimeterPoints[i];
                Vector3 next = perimeterPoints[(i + 1) % count];
                normal.x += (current.y - next.y) * (current.z + next.z);
                normal.y += (current.z - next.z) * (current.x + next.x);
                normal.z += (current.x - next.x) * (current.y + next.y);
            }

            if (normal.sqrMagnitude < 1e-10f)
                return Vector3.zero;
            normal.Normalize();
            return Vector3.Dot(normal, up) < 0f ? -normal : normal;
        }

        void UpdateIdle(float deltaTime)
        {
            float idleSpeed = stepDistance * 0.25f;
            bool moving = false;
            foreach (ProceduralLeg leg in activeLegs)
            {
                if (leg.HomeVelocity.sqrMagnitude > idleSpeed * idleSpeed)
                {
                    moving = true;
                    break;
                }
            }
            idleTime = moving ? 0f : idleTime + deltaTime;
        }

        float StepThreshold(ProceduralLeg leg) => stepDistance * leg.StepDistanceMultiplier;

        float FootDrift(ProceduralLeg leg) => Vector3.Distance(leg.FootPosition, leg.GroundPoint);

        void UpdateGait()
        {
            float settleFactor = IsSettling ? settleThreshold : 1f;

            // Feet that are dangling, or stretched far too far, step right away.
            foreach (ProceduralLeg leg in activeLegs)
            {
                if (leg.IsStepping)
                    continue;

                if (!leg.HasGround)
                {
                    if (leg.IsFootGrounded && Vector3.Distance(leg.FootPosition, leg.HomePosition) > StepThreshold(leg) * forceStepMultiplier)
                        leg.IsFootGrounded = false;
                    continue;
                }

                if (!leg.IsFootGrounded || FootDrift(leg) > StepThreshold(leg) * forceStepMultiplier)
                    StartStep(leg);
            }

            if (gaitMode == GaitMode.Independent)
            {
                int stepping = 0;
                foreach (ProceduralLeg leg in activeLegs)
                {
                    if (leg.IsStepping)
                        stepping++;
                }

                foreach (ProceduralLeg leg in activeLegs)
                {
                    if (stepping >= maxSimultaneousSteps)
                        break;
                    if (leg.IsStepping || !leg.HasGround || FootDrift(leg) <= StepThreshold(leg) * settleFactor)
                        continue;
                    StartStep(leg);
                    stepping++;
                }
                return;
            }

            int groupCount = groupIds.Count;
            for (int offset = 0; offset < groupCount; offset++)
            {
                int index = (nextGroupIndex + offset) % groupCount;
                int group = groupIds[index];
                if (!OtherGroupsPlanted(group))
                    continue;

                bool triggered = false;
                foreach (ProceduralLeg leg in activeLegs)
                {
                    if (leg.Group == group && !leg.IsStepping && leg.HasGround && FootDrift(leg) > StepThreshold(leg) * settleFactor)
                    {
                        triggered = true;
                        break;
                    }
                }

                if (!triggered)
                    continue;

                // Lift the whole group, skipping feet that are already (nearly) home.
                foreach (ProceduralLeg leg in activeLegs)
                {
                    if (leg.Group == group && !leg.IsStepping && leg.HasGround && FootDrift(leg) > StepThreshold(leg) * settleFactor * 0.25f)
                        StartStep(leg);
                }

                nextGroupIndex = (index + 1) % groupCount;
                break;
            }
        }

        bool OtherGroupsPlanted(int group)
        {
            foreach (ProceduralLeg leg in activeLegs)
            {
                if (leg.Group != group && leg.IsStepping && leg.StepProgress < 1f - stepOverlap)
                    return false;
            }
            return true;
        }

        void StartStep(ProceduralLeg leg)
        {
            leg.IsStepping = true;
            leg.StepProgress = 0f;
            leg.StepStart = leg.FootPosition;
            leg.stepStartNormal = leg.IsFootGrounded ? leg.FootNormal : leg.GroundNormal;
            leg.StepTarget = leg.GroundPoint;
            leg.StepTargetNormal = leg.GroundNormal;
            UpdateStepTarget(leg);
        }

        void UpdateStepTarget(ProceduralLeg leg)
        {
            Vector3 up = transform.up;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(leg.HomeVelocity, up);
            Vector3 lead = planarVelocity * (stepDuration * (1f - leg.StepProgress));
            if (planarVelocity.sqrMagnitude > 1e-6f)
                lead += planarVelocity.normalized * (StepThreshold(leg) * overshoot);
            lead = Vector3.ClampMagnitude(lead, StepThreshold(leg) * 1.5f);

            if (FindSurface(leg.HomePosition + lead, up, out Vector3 point, out Vector3 normal))
            {
                leg.StepTarget = point;
                leg.StepTargetNormal = normal;
            }
            else if (leg.HasGround)
            {
                leg.StepTarget = leg.GroundPoint;
                leg.StepTargetNormal = leg.GroundNormal;
            }
        }

        void UpdateFoot(ProceduralLeg leg, float deltaTime)
        {
            if (leg.IsStepping)
            {
                UpdateStepTarget(leg);
                leg.StepProgress = Mathf.Min(1f, leg.StepProgress + deltaTime / stepDuration);

                float t = leg.StepProgress;
                float eased = t * t * (3f - 2f * t);
                Vector3 lift = leg.stepStartNormal + leg.StepTargetNormal;
                lift = lift.sqrMagnitude > 1e-6f ? lift.normalized : transform.up;

                leg.FootPosition = Vector3.Lerp(leg.StepStart, leg.StepTarget, eased) + lift * (stepHeight * stepHeightCurve.Evaluate(t));
                leg.FootNormal = Vector3.Slerp(leg.stepStartNormal, leg.StepTargetNormal, eased);

                if (t >= 1f)
                {
                    leg.IsStepping = false;
                    leg.IsFootGrounded = true;
                    leg.FootPosition = leg.StepTarget;
                    leg.FootNormal = leg.StepTargetNormal;
                    leg.RaisePlanted();
                    LegPlanted?.Invoke(leg);
                }
            }
            else if (!leg.IsFootGrounded)
            {
                // Nothing to stand on: let the foot hang near its home position.
                leg.FootPosition = Vector3.Lerp(leg.FootPosition, leg.HomePosition, Blend(10f, deltaTime));
                leg.FootNormal = transform.up;
            }
        }

        /// <summary>
        /// Finds the surface for a foot whose rest position is <paramref name="home"/>: first a wall between
        /// the body and the foot, then straight down, then back under the body (around a ledge).
        /// </summary>
        public bool FindSurface(Vector3 home, Vector3 up, out Vector3 point, out Vector3 normal)
        {
            Vector3 bodyPosition = transform.position;
            Vector3 origin = home + up * castHeight;
            RaycastHit hit;

            if (detectWalls && CastSegment(bodyPosition, origin, out hit))
            {
                point = hit.point;
                normal = hit.normal;
                return true;
            }

            if (Cast(origin, -up, castHeight + castDepth, out hit))
            {
                point = hit.point;
                normal = hit.normal;
                return true;
            }

            if (wrapAroundEdges)
            {
                Vector3 below = home - up * castDepth;
                Vector3 underBody = bodyPosition - up * BodyHeight;
                if (CastSegment(below, underBody, out hit))
                {
                    point = hit.point;
                    normal = hit.normal;
                    return true;
                }
            }

            point = home;
            normal = up;
            return false;
        }

        bool CastSegment(Vector3 from, Vector3 to, out RaycastHit hit)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 1e-4f)
            {
                hit = default;
                return false;
            }
            return Cast(from, delta / length, length, out hit);
        }

        /// <summary>Nearest hit that does not belong to this walker's own hierarchy.</summary>
        bool Cast(Vector3 origin, Vector3 direction, float distance, out RaycastHit best)
        {
            best = default;
            PhysicsScene physicsScene = gameObject.scene.IsValid() ? gameObject.scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
            int count = physicsScene.Raycast(origin, direction, hitBuffer, distance, groundMask, triggerInteraction);

            bool found = false;
            float bestDistance = float.MaxValue;
            Transform self = transform;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hitBuffer[i];
                if (hit.collider == null || hit.distance >= bestDistance || hit.transform.IsChildOf(self))
                    continue;
                best = hit;
                bestDistance = hit.distance;
                found = true;
            }
            return found;
        }

        float MeasureRestBodyHeight()
        {
            Transform body = transform;
            float sum = 0f;
            int count = 0;
            foreach (ProceduralLeg leg in legs)
            {
                if (leg == null || leg.Chain == null || leg.Chain.Tip == null)
                    continue;
                sum += Vector3.Dot(body.position - leg.Chain.Tip.position, body.up);
                count++;
            }
            return count > 0 ? Mathf.Max(sum / count, 0.01f) : 0.5f;
        }

        static float Blend(float sharpness, float deltaTime) => sharpness <= 0f ? 1f : 1f - Mathf.Exp(-sharpness * deltaTime);
    }
}
