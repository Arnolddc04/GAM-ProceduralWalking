using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GAMStudio.ProceduralWalking.Editor
{
    static class ProceduralWalkingMenus
    {
        const string GameObjectMenu = "GameObject/GAM Procedural Walking/";
        const string ToolsMenu = "Tools/GAM Procedural Walking/";

        [MenuItem(GameObjectMenu + "Add Walker And Detect Legs", false, 30)]
        static void AddWalker()
        {
            GameObject selected = Selection.activeGameObject;
            if (!selected.TryGetComponent(out ProceduralWalker walker))
                walker = Undo.AddComponent<ProceduralWalker>(selected);
            int count = WalkerSetupUtility.DetectAndSetupLegs(walker);
            ReportDetection(walker, count);
        }

        [MenuItem(GameObjectMenu + "Add Walker And Detect Legs", true)]
        static bool AddWalkerValidate() => Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject);

        [MenuItem(GameObjectMenu + "Create Leg From Selected Bones", false, 31)]
        static void CreateLegFromSelection()
        {
            if (!TryGetSelectedChain(out Transform root, out Transform tip))
            {
                EditorUtility.DisplayDialog("Create Leg", "Select two bones: the leg's first bone and its tip (a descendant of the first).", "OK");
                return;
            }

            var walker = root.GetComponentInParent<ProceduralWalker>();
            int group = walker != null ? walker.Legs.Count(l => l != null) % 2 : 0;
            ProceduralLeg leg = WalkerSetupUtility.SetupLeg(root, tip, group);

            if (walker != null && !walker.Legs.Contains(leg))
            {
                Undo.RecordObject(walker, "Create Leg");
                walker.SetLegs(walker.Legs.Append(leg).ToList());
                EditorUtility.SetDirty(walker);
            }
            Selection.activeGameObject = root.gameObject;
        }

        [MenuItem(GameObjectMenu + "Create Leg From Selected Bones", true)]
        static bool CreateLegFromSelectionValidate() => TryGetSelectedChain(out _, out _);

        [MenuItem(ToolsMenu + "Documentation (README)")]
        static void OpenReadme()
        {
            var readme = AssetDatabase.LoadAssetAtPath<TextAsset>("Packages/com.gamstudio.proceduralwalking/README.md");
            if (readme != null)
                AssetDatabase.OpenAsset(readme);
        }

        static bool TryGetSelectedChain(out Transform root, out Transform tip)
        {
            root = null;
            tip = null;
            Transform[] selected = Selection.transforms;
            if (selected.Length != 2)
                return false;
            if (selected[1].IsChildOf(selected[0]) && selected[1] != selected[0])
            {
                root = selected[0];
                tip = selected[1];
                return true;
            }
            if (selected[0].IsChildOf(selected[1]) && selected[1] != selected[0])
            {
                root = selected[1];
                tip = selected[0];
                return true;
            }
            return false;
        }

        internal static void ReportDetection(ProceduralWalker walker, int count)
        {
            if (count == 0)
            {
                EditorUtility.DisplayDialog("Detect Legs",
                    "No leg chains were found under this object.\n\nLegs are found as two or more chains (at least 2 bones each) branching from the same bone, " +
                    "that are named like legs or reach downwards. You can also select a leg's first bone and tip and use " +
                    "GameObject > GAM Procedural Walking > Create Leg From Selected Bones.", "OK");
                return;
            }
            Debug.Log($"[ProceduralWalker] Detected {count} legs on '{walker.name}' and configured the walker from the rig.", walker);
        }
    }
}
