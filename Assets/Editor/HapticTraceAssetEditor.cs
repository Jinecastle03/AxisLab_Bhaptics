#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AxisLabHaptics
{
    [CustomEditor(typeof(HapticTraceAsset))]
    public class HapticTraceAssetEditor : Editor
    {
        private SerializedProperty pointsProp;

        private void OnEnable()
        {
            pointsProp = serializedObject.FindProperty("points");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var trace = (HapticTraceAsset)target;

            EditorGUILayout.LabelField("Haptic Trace", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Points: {trace.points.Count}");

            EditorGUILayout.Space(6);

            // 리스트 출력 (기본보다 보기 좋게)
            for (int i = 0; i < pointsProp.arraySize; i++)
            {
                var p = pointsProp.GetArrayElementAtIndex(i);
                var timeProp = p.FindPropertyRelative("time");
                var posProp = p.FindPropertyRelative("pos");
                var baseIProp = p.FindPropertyRelative("baseIntensity");
                var sigmaProp = p.FindPropertyRelative("sigma");

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"#{i}", GUILayout.Width(30));
                EditorGUILayout.PropertyField(timeProp, new GUIContent("Time"), GUILayout.MinWidth(120));
                if (GUILayout.Button("X", GUILayout.Width(24)))
                {
                    pointsProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(posProp, new GUIContent("Pos (x,y,z)"));
                EditorGUILayout.PropertyField(baseIProp, new GUIContent("Base Intensity"));
                EditorGUILayout.PropertyField(sigmaProp, new GUIContent("Sigma"));

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(8);

            // 유틸 버튼들
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear All"))
            {
                Undo.RecordObject(trace, "Clear Trace");
                trace.points.Clear();
                EditorUtility.SetDirty(trace);
            }

            if (GUILayout.Button("Sort By Time"))
            {
                Undo.RecordObject(trace, "Sort Trace By Time");
                trace.points.Sort((a, b) => a.time.CompareTo(b.time));
                EditorUtility.SetDirty(trace);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // fixed dt로 time 재구성 (Recorder의 fixedDt를 몰라도, 여기서 직접 입력해서 가능)
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Rebuild Times dt", GUILayout.Width(120));
            float dt = EditorPrefs.GetFloat("AxisLabTraceEditor_fixedDt", 0.5f);
            dt = EditorGUILayout.FloatField(dt);
            EditorPrefs.SetFloat("AxisLabTraceEditor_fixedDt", dt);

            if (GUILayout.Button("Rebuild", GUILayout.Width(90)))
            {
                Undo.RecordObject(trace, "Rebuild Times");
                for (int i = 0; i < trace.points.Count; i++)
                    trace.points[i].time = i * dt;
                EditorUtility.SetDirty(trace);
            }
            EditorGUILayout.EndHorizontal();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
