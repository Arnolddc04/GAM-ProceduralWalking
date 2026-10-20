using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GAMStudio.ProceduralWalking.Editor
{
    [CustomEditor(typeof(ProceduralWalker))]
    class ProceduralWalkerEditor : UnityEditor.Editor
    {
        static readonly List<Transform> s_Chain = new List<Transform>();
        static readonly string[] s_Excluded = { "m_Script", "legs" };

        bool showLegs = true;

        public override void OnInspectorGUI()
        {
            var walker = (ProceduralWalker)target;
            serializedObject.Update();

            DrawSetup(walker);
            EditorGUILayout.Space();
            DrawLegs(walker);
            EditorGUILayout.Space();
            DrawPropertiesExcluding(serializedObject, s_Excluded);

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying && walker.IsInitialized)
                DrawRuntimeState(walker);
        }

        void DrawSetup(ProceduralWalker walker)
        {
            EditorGUILayout.LabelField("Setup", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Auto-Detect Legs", "Find leg chains in the hierarchy, add IK Chain + Procedural Leg to each, assign alternating groups and configure the settings.")))
                    {
                        int count = WalkerSetupUtility.DetectAndSetupLegs(walker);
                        ProceduralWalkingMenus.ReportDetection(walker, count);
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button(new GUIContent("Collect From Children", "Use every Procedural Leg under this object.")))
                    {
                        WalkerSetupUtility.CollectLegsFromChildren(walker);
                        GUIUtility.ExitGUI();
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Alternate Groups", "Assign a two-group alternating gait (tripod / tetrapod) based on leg side and order.")))
                    {
                        WalkerSetupUtility.AssignAlternatingGroups(walker);
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button(new GUIContent("Auto-Configure", "Derive step distance, step height and cast lengths from the leg lengths.")))
                    {
                        WalkerSetupUtility.AutoConfigure(walker);
                        GUIUtility.ExitGUI();
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Auto Joint Limits", "On every leg: the first bone becomes a ball joint, the others hinges around the axis they bend on in the rest pose.")))
                    {
                        WalkerSetupUtility.AutoSetupJointLimits(walker);
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button(new GUIContent("Clear Joint Limits", "Make every joint of every leg free.")))
                    {
                        WalkerSetupUtility.ClearJointLimits(walker);
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        void DrawLegs(ProceduralWalker walker)
        {
            int count = 0;
            foreach (ProceduralLeg leg in walker.Legs)
            {
                if (leg != null)
                    count++;
            }

            showLegs = EditorGUILayout.Foldout(showLegs, $"Legs ({count})", true, EditorStyles.foldoutHeader);
            if (count == 0)
            {
                EditorGUILayout.HelpBox("No legs yet. Use Auto-Detect Legs, or add an IK Chain + Procedural Leg to each leg's first bone and press Collect From Children.", MessageType.Info);
                return;
            }
            if (count == 1)
                EditorGUILayout.HelpBox("A walker with one leg cannot balance; the gait needs at least two legs.", MessageType.Warning);

            if (!showLegs)
                return;

            SerializedProperty legsProperty = serializedObject.FindProperty("legs");
            for (int i = 0; i < legsProperty.arraySize; i++)
            {
                SerializedProperty element = legsProperty.GetArrayElementAtIndex(i);
                var leg = element.objectReferenceValue as ProceduralLeg;

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (leg == null)
                    {
                        EditorGUILayout.PropertyField(element, GUIContent.none);
                    }
                    else
                    {
                        Color previous = GUI.color;
                        GUI.color = ProceduralWalker.GetGroupColor(leg.Group);
                        GUILayout.Label("■", GUILayout.Width(14));
                        GUI.color = previous;

                        if (GUILayout.Button(leg.name, EditorStyles.label, GUILayout.MinWidth(80)))
                            EditorGUIUtility.PingObject(leg);

                        bool valid = IKChain.TryCollectChain(leg.Chain.Root, leg.Chain.Tip, s_Chain);
                        GUILayout.Label(valid ? $"{s_Chain.Count - 1} bones" : "invalid chain", valid ? EditorStyles.miniLabel : EditorStyles.boldLabel, GUILayout.Width(70));

                        EditorGUI.BeginChangeCheck();
                        int group = EditorGUILayout.IntField(leg.Group, GUILayout.Width(36));
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(leg, "Change Leg Group");
                            leg.Group = group;
                            EditorUtility.SetDirty(leg);
                        }
                    }

                    if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
                    {
                        legsProperty.DeleteArrayElementAtIndex(i);
                        break;
                    }
                }
            }

            if (GUILayout.Button("Add Slot", EditorStyles.miniButton, GUILayout.Width(70)))
                legsProperty.arraySize++;
        }

        static void DrawRuntimeState(ProceduralWalker walker)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            int stepping = 0, grounded = 0;
            foreach (ProceduralLeg leg in walker.Legs)
            {
                if (leg == null)
                    continue;
                if (leg.IsStepping) stepping++;
                if (leg.HasGround) grounded++;
            }
            EditorGUILayout.LabelField("Grounded", walker.IsGrounded ? "Yes" : "No");
            EditorGUILayout.LabelField("Speed", walker.Velocity.magnitude.ToString("0.00"));
            EditorGUILayout.LabelField("Legs stepping", stepping.ToString());
            EditorGUILayout.LabelField("Legs with ground", grounded.ToString());
            EditorGUILayout.LabelField("Settling", walker.IsSettling ? "Yes" : "No");
        }

        public override bool RequiresConstantRepaint() => Application.isPlaying;
    }
}
