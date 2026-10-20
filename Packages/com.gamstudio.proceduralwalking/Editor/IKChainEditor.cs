using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GAMStudio.ProceduralWalking.Editor
{
    [CustomEditor(typeof(IKChain))]
    [CanEditMultipleObjects]
    class IKChainEditor : UnityEditor.Editor
    {
        static readonly List<Transform> s_Chain = new List<Transform>();
        static readonly string[] s_Excluded = { "m_Script", "jointLimits" };
        static readonly HashSet<(IKChain, int)> s_Expanded = new HashSet<(IKChain, int)>();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, s_Excluded);
            serializedObject.ApplyModifiedProperties();

            if (targets.Length != 1)
                return;

            var chain = (IKChain)target;
            EditorGUILayout.Space();

            bool validChain = false;
            if (chain.Tip == null)
            {
                EditorGUILayout.HelpBox("Assign the Tip: the last bone of the chain (foot or claw tip).", MessageType.Warning);
            }
            else if (!IKChain.TryCollectChain(chain.Root, chain.Tip, s_Chain))
            {
                EditorGUILayout.HelpBox("The Tip is not a descendant of the Root.", MessageType.Error);
            }
            else
            {
                validChain = true;
                float length = IKChain.MeasureChain(chain.Root, chain.Tip);
                EditorGUILayout.HelpBox($"{s_Chain.Count - 1} bones, length {length:0.###}: {string.Join(" → ", s_Chain.ConvertAll(t => t.name))}", MessageType.None);
            }

            bool drivenByWalker = chain.TryGetComponent(out ProceduralLeg _) && chain.GetComponentInParent<ProceduralWalker>() != null;
            if (drivenByWalker)
                EditorGUILayout.HelpBox("Driven by the Procedural Walker at runtime; the Target field is ignored.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(chain.Tip == null))
            {
                using (new EditorGUI.DisabledScope(chain.Target != null || drivenByWalker))
                {
                    if (GUILayout.Button("Create Target"))
                        Selection.activeTransform = WalkerSetupUtility.CreateTarget(chain);
                }
                using (new EditorGUI.DisabledScope(chain.Pole != null))
                {
                    if (GUILayout.Button("Create Pole"))
                    {
                        Transform pole = WalkerSetupUtility.CreatePole(chain);
                        if (pole != null)
                            Selection.activeTransform = pole;
                    }
                }
            }

            if (validChain)
                DrawJointLimits(chain);
        }

        void DrawJointLimits(IKChain chain)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Joint Limits", EditorStyles.boldLabel);

            if (chain.JointLimits.Count != s_Chain.Count - 1)
            {
                Undo.RecordObject(chain, "Sync Joint Limits");
                chain.SyncJointLimits();
                EditorUtility.SetDirty(chain);
                serializedObject.Update();
            }

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Auto-Setup", "Root bone becomes a ball joint, every other bone a hinge around the axis it bends on in the rest pose.")))
                    ModifyChain(chain, "Auto-Setup Joint Limits", c => c.AutoConfigureJointLimits());
                if (GUILayout.Button(new GUIContent("Detect Hinge Axes", "Recompute the axis of every hinge from the rest pose, keeping the ranges.")))
                    ModifyChain(chain, "Detect Hinge Axes", c => c.DetectHingeAxes());
                if (GUILayout.Button(new GUIContent("Clear", "Make every joint free.")))
                    ModifyChain(chain, "Clear Joint Limits", c => { foreach (JointLimit limit in c.JointLimits) limit.type = JointLimitType.Free; });
            }

            EditorGUILayout.HelpBox("Angles are relative to the rest pose (0° = rest). Axes are in the bone's local space. " +
                                    "Select the chain to see each hinge's allowed arc and each ball joint's cone in the Scene view.", MessageType.None);

            serializedObject.Update();
            SerializedProperty limits = serializedObject.FindProperty("jointLimits");
            for (int i = 0; i < limits.arraySize && i < s_Chain.Count - 1; i++)
                DrawJointLimit(chain, limits.GetArrayElementAtIndex(i), i, s_Chain[i]);
            serializedObject.ApplyModifiedProperties();
        }

        void DrawJointLimit(IKChain chain, SerializedProperty limit, int index, Transform bone)
        {
            SerializedProperty type = limit.FindPropertyRelative("type");
            var key = (chain, index);
            bool expanded = s_Expanded.Contains(key);

            using (new EditorGUILayout.HorizontalScope())
            {
                bool newExpanded = EditorGUILayout.Foldout(expanded, bone.name, true);
                if (newExpanded != expanded)
                {
                    if (newExpanded) s_Expanded.Add(key);
                    else s_Expanded.Remove(key);
                }
                EditorGUILayout.PropertyField(type, GUIContent.none, GUILayout.Width(80));
            }

            if (!expanded || type.enumValueIndex == (int)JointLimitType.Free)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                if (type.enumValueIndex == (int)JointLimitType.Hinge)
                {
                    EditorGUILayout.PropertyField(limit.FindPropertyRelative("axis"), new GUIContent("Axis (local)"));
                    MinMax(limit.FindPropertyRelative("minAngle"), limit.FindPropertyRelative("maxAngle"), "Angle Range");
                }
                else
                {
                    EditorGUILayout.PropertyField(limit.FindPropertyRelative("maxSwing"), new GUIContent("Max Swing"));
                    MinMax(limit.FindPropertyRelative("minTwist"), limit.FindPropertyRelative("maxTwist"), "Twist Range");
                }
            }
        }

        static void MinMax(SerializedProperty min, SerializedProperty max, string label)
        {
            float minValue = min.floatValue;
            float maxValue = max.floatValue;
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                int indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                minValue = EditorGUILayout.FloatField(minValue, GUILayout.Width(44));
                EditorGUILayout.MinMaxSlider(ref minValue, ref maxValue, -180f, 180f);
                maxValue = EditorGUILayout.FloatField(maxValue, GUILayout.Width(44));
                EditorGUI.indentLevel = indent;
            }
            if (EditorGUI.EndChangeCheck())
            {
                min.floatValue = Mathf.Clamp(Mathf.Round(minValue), -180f, 180f);
                max.floatValue = Mathf.Clamp(Mathf.Round(Mathf.Max(maxValue, minValue)), -180f, 180f);
            }
        }

        void ModifyChain(IKChain chain, string undoName, System.Action<IKChain> action)
        {
            Undo.RecordObject(chain, undoName);
            action(chain);
            EditorUtility.SetDirty(chain);
            serializedObject.Update();
            SceneView.RepaintAll();
            GUIUtility.ExitGUI();
        }
    }
}
