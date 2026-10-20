using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GAMStudio.ProceduralWalking.Editor
{
    /// <summary>Undo-aware setup operations shared by the inspectors and menu items.</summary>
    public static class WalkerSetupUtility
    {
        /// <summary>Detects the legs under the walker, adds IK Chain + Procedural Leg to each and auto-configures the walker.</summary>
        public static int DetectAndSetupLegs(ProceduralWalker walker)
        {
            List<LegDetector.LegCandidate> candidates = LegDetector.Detect(walker.transform);
            if (candidates.Count == 0)
                return 0;

            int undoGroup = BeginUndo("Auto-Detect Legs");
            var legs = new List<ProceduralLeg>();
            foreach (LegDetector.LegCandidate candidate in candidates)
                legs.Add(SetupLeg(candidate.Root, candidate.Tip, candidate.Group));

            Undo.RecordObject(walker, "Auto-Detect Legs");
            walker.SetLegs(legs);
            walker.AutoConfigure();
            EditorUtility.SetDirty(walker);
            Undo.CollapseUndoOperations(undoGroup);
            return legs.Count;
        }

        /// <summary>Adds (or reuses) the IK Chain and Procedural Leg on <paramref name="root"/>.</summary>
        public static ProceduralLeg SetupLeg(Transform root, Transform tip, int group)
        {
            if (!root.TryGetComponent(out IKChain chain))
                chain = Undo.AddComponent<IKChain>(root.gameObject);
            Undo.RecordObject(chain, "Set Up Leg");
            chain.Configure(root, tip);
            EditorUtility.SetDirty(chain);

            if (!root.TryGetComponent(out ProceduralLeg leg))
                leg = Undo.AddComponent<ProceduralLeg>(root.gameObject);
            Undo.RecordObject(leg, "Set Up Leg");
            leg.Group = group;
            EditorUtility.SetDirty(leg);
            return leg;
        }

        public static void CollectLegsFromChildren(ProceduralWalker walker)
        {
            Undo.RecordObject(walker, "Collect Legs");
            walker.SetLegs(walker.GetComponentsInChildren<ProceduralLeg>(true));
            EditorUtility.SetDirty(walker);
        }

        public static void AssignAlternatingGroups(ProceduralWalker walker)
        {
            var legs = new List<ProceduralLeg>();
            var positions = new List<Vector3>();
            foreach (ProceduralLeg leg in walker.Legs)
            {
                if (leg == null)
                    continue;
                legs.Add(leg);
                positions.Add(leg.transform.position);
            }

            int[] groups = LegDetector.ComputeAlternatingGroups(walker.transform, positions);
            int undoGroup = BeginUndo("Assign Alternating Groups");
            for (int i = 0; i < legs.Count; i++)
            {
                Undo.RecordObject(legs[i], "Assign Alternating Groups");
                legs[i].Group = groups[i];
                EditorUtility.SetDirty(legs[i]);
            }
            Undo.CollapseUndoOperations(undoGroup);
        }

        /// <summary>Runs <see cref="IKChain.AutoConfigureJointLimits"/> on every leg's chain.</summary>
        public static void AutoSetupJointLimits(ProceduralWalker walker)
        {
            int undoGroup = BeginUndo("Auto-Setup Joint Limits");
            foreach (ProceduralLeg leg in walker.Legs)
            {
                if (leg == null || leg.Chain == null)
                    continue;
                Undo.RecordObject(leg.Chain, "Auto-Setup Joint Limits");
                leg.Chain.AutoConfigureJointLimits();
                EditorUtility.SetDirty(leg.Chain);
            }
            Undo.CollapseUndoOperations(undoGroup);
        }

        public static void ClearJointLimits(ProceduralWalker walker)
        {
            int undoGroup = BeginUndo("Clear Joint Limits");
            foreach (ProceduralLeg leg in walker.Legs)
            {
                if (leg == null || leg.Chain == null)
                    continue;
                Undo.RecordObject(leg.Chain, "Clear Joint Limits");
                foreach (JointLimit limit in leg.Chain.JointLimits)
                    limit.type = JointLimitType.Free;
                EditorUtility.SetDirty(leg.Chain);
            }
            Undo.CollapseUndoOperations(undoGroup);
        }

        public static void AutoConfigure(ProceduralWalker walker)
        {
            Undo.RecordObject(walker, "Auto-Configure Walker");
            walker.AutoConfigure();
            EditorUtility.SetDirty(walker);
        }

        /// <summary>Creates an empty child at the chain tip and assigns it as the chain's target.</summary>
        public static Transform CreateTarget(IKChain chain)
        {
            Transform parent = chain.GetComponentInParent<ProceduralWalker>() is ProceduralWalker walker && walker.transform.parent != null
                ? walker.transform.parent
                : null;
            var target = new GameObject($"{chain.name}_Target").transform;
            Undo.RegisterCreatedObjectUndo(target.gameObject, "Create IK Target");
            target.SetParent(parent, false);
            target.position = chain.Tip != null ? chain.Tip.position : chain.transform.position;
            Undo.RecordObject(chain, "Create IK Target");
            chain.Target = target;
            EditorUtility.SetDirty(chain);
            return target;
        }

        /// <summary>Creates a pole above the chain's knee, parented to the chain root's parent.</summary>
        public static Transform CreatePole(IKChain chain)
        {
            var bones = new List<Transform>();
            if (!IKChain.TryCollectChain(chain.Root, chain.Tip, bones) || bones.Count < 3)
                return null;

            Transform parent = bones[0].parent;
            Vector3 up = parent != null ? parent.up : Vector3.up;
            float length = IKChain.MeasureChain(chain.Root, chain.Tip);

            var pole = new GameObject($"{chain.name}_Pole").transform;
            Undo.RegisterCreatedObjectUndo(pole.gameObject, "Create IK Pole");
            pole.SetParent(parent, false);
            pole.position = bones[1].position + up * (length * 0.5f);
            Undo.RecordObject(chain, "Create IK Pole");
            chain.Pole = pole;
            EditorUtility.SetDirty(chain);
            return pole;
        }

        static int BeginUndo(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }
    }
}
