using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GAMStudio.ProceduralWalking.Editor
{
    /// <summary>
    /// Scene view visualisation for walkers and IK chains. Works in edit mode (rest pose, cast preview)
    /// and play mode (homes, step radii, feet, step arcs, surface normal).
    /// </summary>
    static class ProceduralWalkingGizmos
    {
        static readonly List<Transform> s_Chain = new List<Transform>();
        static readonly Color s_ChainColor = new Color(0.85f, 0.85f, 0.85f);
        static readonly Color s_CastColor = new Color(1f, 1f, 1f, 0.25f);
        static readonly Color s_NormalColor = new Color(0.3f, 1f, 0.5f);

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
        static void DrawWalker(ProceduralWalker walker, GizmoType gizmoType)
        {
            if (!ShouldDraw(walker.GizmoDisplay, walker.transform, gizmoType))
                return;

            Transform body = walker.transform;
            bool live = Application.isPlaying && walker.IsInitialized;
            float stepDistance = walker.StepDistance;

            foreach (ProceduralLeg leg in walker.Legs)
            {
                if (leg == null || leg.Chain == null || leg.Chain.Tip == null)
                    continue;

                Color color = ProceduralWalker.GetGroupColor(leg.Group);
                float radius = stepDistance * leg.StepDistanceMultiplier;

                if (live)
                    DrawLiveLeg(leg, color, radius, body);
                else
                    DrawEditLeg(walker, leg, color, radius, body);
            }

            Vector3 normal = live ? walker.SurfaceNormal : body.up;
            float height = walker.BodyHeight;
            Handles.color = s_NormalColor;
            Handles.ArrowHandleCap(0, body.position, Quaternion.LookRotation(normal), height, EventType.Repaint);

            if (live && walker.IsGrounded)
            {
                Handles.color = new Color(s_NormalColor.r, s_NormalColor.g, s_NormalColor.b, 0.5f);
                Handles.DrawDottedLine(body.position, walker.SurfacePoint, 3f);
                Handles.DrawWireDisc(walker.SurfacePoint, normal, height * 0.25f);
            }

            if (walker.WallProbeDistance > 0f)
            {
                Vector3 direction = live && Vector3.ProjectOnPlane(walker.Velocity, body.up).sqrMagnitude > 1e-4f
                    ? Vector3.ProjectOnPlane(walker.Velocity, body.up).normalized
                    : body.forward;
                Handles.color = s_CastColor;
                Handles.DrawDottedLine(body.position, body.position + direction * walker.WallProbeDistance, 4f);
            }
        }

        static void DrawLiveLeg(ProceduralLeg leg, Color color, float radius, Transform body)
        {
            Handles.color = new Color(color.r, color.g, color.b, 0.35f);
            Handles.DrawWireDisc(leg.GroundPoint, leg.GroundNormal, radius);
            Handles.DrawLine(leg.HomePosition, leg.GroundPoint);

            Handles.color = color;
            Handles.DrawLine(leg.FootPosition, leg.GroundPoint);

            if (leg.IsStepping)
            {
                Handles.DrawDottedLine(leg.StepStart, leg.StepTarget, 2f);
                Handles.DrawWireDisc(leg.StepTarget, leg.StepTargetNormal, radius * 0.15f);
            }

            Gizmos.color = leg.IsFootGrounded ? color : Color.red;
            float size = radius * (leg.IsStepping ? 0.08f : 0.12f);
            Gizmos.DrawSphere(leg.FootPosition, size);

            if (!leg.HasGround)
            {
                Handles.color = Color.red;
                Handles.DrawWireDisc(leg.HomePosition, body.up, radius * 0.5f);
            }
        }

        static void DrawEditLeg(ProceduralWalker walker, ProceduralLeg leg, Color color, float radius, Transform body)
        {
            Vector3 up = body.up;
            Vector3 home = body.TransformPoint(body.InverseTransformPoint(leg.Chain.Tip.position) + leg.HomeOffset);

            Handles.color = new Color(color.r, color.g, color.b, 0.6f);
            Handles.DrawWireDisc(home, up, radius);

            Handles.color = s_CastColor;
            Handles.DrawDottedLine(home + up * walker.CastHeight, home - up * walker.CastDepth, 3f);

            Gizmos.color = color;
            Gizmos.DrawSphere(home, radius * 0.08f);
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        static void DrawChain(IKChain chain, GizmoType gizmoType)
        {
            var walker = chain.GetComponentInParent<ProceduralWalker>();
            GizmoDisplay display = walker != null ? walker.GizmoDisplay : GizmoDisplay.WhenSelected;
            Transform owner = walker != null ? walker.transform : chain.transform;
            if (!ShouldDraw(display, owner, gizmoType))
                return;

            if (!IKChain.TryCollectChain(chain.Root, chain.Tip, s_Chain))
            {
                if (chain.Tip != null)
                {
                    Handles.color = Color.red;
                    Handles.DrawDottedLine(chain.Root.position, chain.Tip.position, 4f);
                }
                return;
            }

            Color color = s_ChainColor;
            if (chain.TryGetComponent(out ProceduralLeg leg))
                color = ProceduralWalker.GetGroupColor(leg.Group);

            float length = 0f;
            for (int i = 0; i < s_Chain.Count - 1; i++)
                length += Vector3.Distance(s_Chain[i].position, s_Chain[i + 1].position);
            float jointSize = Mathf.Max(length * 0.03f, 0.005f);

            Handles.color = color;
            for (int i = 0; i < s_Chain.Count; i++)
            {
                Vector3 joint = s_Chain[i].position;
                if (i < s_Chain.Count - 1)
                    DrawBone(joint, s_Chain[i + 1].position, jointSize);
                Gizmos.color = i == 0 ? Color.white : color;
                Gizmos.DrawWireSphere(joint, jointSize);
            }

            if (chain.Target != null)
            {
                Handles.color = Color.yellow;
                Handles.DrawDottedLine(chain.Tip.position, chain.Target.position, 3f);
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(chain.Target.position, Vector3.one * jointSize * 2f);
            }

            if (chain.BendMode != IKChain.PoleMode.None && chain.TryGetPolePosition(out Vector3 pole))
            {
                Vector3 knee = s_Chain[Mathf.Min(1, s_Chain.Count - 1)].position;
                Handles.color = new Color(0.6f, 0.6f, 1f, 0.6f);
                Handles.DrawDottedLine(knee, pole, 2f);
                Gizmos.color = Handles.color;
                Gizmos.DrawWireSphere(pole, jointSize * 0.75f);
            }

            if ((gizmoType & GizmoType.Selected) != 0 && leg != null)
                Handles.Label(chain.Root.position + Vector3.up * jointSize * 3f, $"{chain.name}  (G{leg.Group})", EditorStyles.miniBoldLabel);

            if (ChainSelected(chain))
                DrawJointLimits(chain);
        }

        static bool ChainSelected(IKChain chain)
        {
            Transform active = Selection.activeTransform;
            return active != null && (active == chain.transform || active.IsChildOf(chain.Root) || active == chain.Root);
        }

        /// <summary>Hinges as an arc of the allowed range around their axis, ball joints as their swing cone.</summary>
        static void DrawJointLimits(IKChain chain)
        {
            List<JointLimit> limits = chain.JointLimits;
            for (int i = 0; i < s_Chain.Count - 1 && i < limits.Count; i++)
            {
                JointLimit limit = limits[i];
                if (limit == null || !limit.IsLimited)
                    continue;

                Transform bone = s_Chain[i];
                Transform child = s_Chain[i + 1];
                Vector3 joint = bone.position;
                float radius = Vector3.Distance(joint, child.position) * 0.4f;

                Quaternion parent = bone.parent != null ? bone.parent.rotation : Quaternion.identity;
                Quaternion jointSpace = parent * chain.GetRestLocalRotation(i, bone);
                Vector3 restDirection = (jointSpace * child.localPosition).normalized;

                if (limit.type == JointLimitType.Hinge)
                {
                    Vector3 axis = jointSpace * limit.HingeAxis;
                    Vector3 zero = Vector3.ProjectOnPlane(restDirection, axis);
                    if (zero.sqrMagnitude < 1e-8f)
                        continue;
                    zero.Normalize();

                    float min = Mathf.Min(limit.minAngle, limit.maxAngle);
                    float max = Mathf.Max(limit.minAngle, limit.maxAngle);
                    Vector3 from = Quaternion.AngleAxis(min, axis) * zero;

                    Handles.color = new Color(1f, 0.8f, 0.2f, 0.18f);
                    Handles.DrawSolidArc(joint, axis, from, max - min, radius);
                    Handles.color = new Color(1f, 0.8f, 0.2f, 0.9f);
                    Handles.DrawWireArc(joint, axis, from, max - min, radius);
                    Handles.DrawLine(joint, joint + from * radius);
                    Handles.DrawLine(joint, joint + Quaternion.AngleAxis(max, axis) * zero * radius);
                    Handles.DrawDottedLine(joint, joint + zero * radius * 1.15f, 2f);

                    Handles.color = new Color(0.4f, 0.7f, 1f, 0.9f);
                    Handles.DrawLine(joint - axis * radius * 0.35f, joint + axis * radius * 0.35f);
                }
                else
                {
                    float swing = Mathf.Clamp(limit.maxSwing, 0f, 180f) * Mathf.Deg2Rad;
                    Vector3 center = joint + restDirection * (radius * Mathf.Cos(swing));
                    float rimRadius = radius * Mathf.Sin(swing);
                    Vector3 side = Vector3.Cross(restDirection, Mathf.Abs(Vector3.Dot(restDirection, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up).normalized;
                    Vector3 side2 = Vector3.Cross(restDirection, side);

                    Handles.color = new Color(0.3f, 0.9f, 1f, 0.12f);
                    Handles.DrawSolidDisc(center, restDirection, rimRadius);
                    Handles.color = new Color(0.3f, 0.9f, 1f, 0.9f);
                    Handles.DrawWireDisc(center, restDirection, rimRadius);
                    Handles.DrawLine(joint, center + side * rimRadius);
                    Handles.DrawLine(joint, center - side * rimRadius);
                    Handles.DrawLine(joint, center + side2 * rimRadius);
                    Handles.DrawLine(joint, center - side2 * rimRadius);
                    Handles.DrawDottedLine(joint, joint + restDirection * radius * 1.15f, 2f);
                }
            }
        }

        /// <summary>Octahedral bone like the ones in DCC tools.</summary>
        static void DrawBone(Vector3 from, Vector3 to, float width)
        {
            Vector3 axis = to - from;
            float length = axis.magnitude;
            if (length < 1e-5f)
                return;

            Vector3 direction = axis / length;
            Vector3 side = Vector3.Cross(direction, Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up).normalized * width;
            Vector3 side2 = Vector3.Cross(direction, side).normalized * width;
            Vector3 waist = from + axis * 0.2f;

            Vector3 a = waist + side, b = waist + side2, c = waist - side, d = waist - side2;
            Handles.DrawLine(from, a); Handles.DrawLine(from, b); Handles.DrawLine(from, c); Handles.DrawLine(from, d);
            Handles.DrawLine(to, a); Handles.DrawLine(to, b); Handles.DrawLine(to, c); Handles.DrawLine(to, d);
            Handles.DrawLine(a, b); Handles.DrawLine(b, c); Handles.DrawLine(c, d); Handles.DrawLine(d, a);
        }

        static bool ShouldDraw(GizmoDisplay display, Transform owner, GizmoType gizmoType)
        {
            switch (display)
            {
                case GizmoDisplay.Never:
                    return false;
                case GizmoDisplay.Always:
                    return true;
                default:
                    if ((gizmoType & (GizmoType.Selected | GizmoType.InSelectionHierarchy)) != 0)
                        return true;
                    Transform active = Selection.activeTransform;
                    return active != null && (active == owner || active.IsChildOf(owner));
            }
        }
    }
}
