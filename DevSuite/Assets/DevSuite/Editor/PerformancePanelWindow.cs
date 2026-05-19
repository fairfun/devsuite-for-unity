#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ff.DevSuite.View.Editor
{
    internal class PerformancePanelWindow : EditorWindow
    {
        [MenuItem("Tools/DevSuite/Performance Panel")]
        public static void Open()
        {
            var window = GetWindow<PerformancePanelWindow>();
            var icon = EditorGUIUtility.IconContent("CustomTool")?.image;
            window.titleContent = new GUIContent("DevSuite: Performance", icon);
        }

        private const string UxmlGuid = "6f9cdce0a67f45e679a6552e17492ef6";
        private const string UssGuid = "8950058c38edd0e0aad44eea47879f64";

        private PerformancePanelView _monitorView;

        public void CreateGUI()
        {
            rootVisualElement.style.flexGrow = 1;

            var uxmlPath = AssetDatabase.GUIDToAssetPath(UxmlGuid);
            var ussPath = AssetDatabase.GUIDToAssetPath(UssGuid);

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);

            _monitorView = new PerformancePanelView(uxml, uss);
            _monitorView.style.flexGrow = 1;
            _monitorView.AddToClassList("editor");

            if (!EditorGUIUtility.isProSkin)
            {
                _monitorView.AddToClassList("light-theme");
            }

            rootVisualElement.Add(_monitorView);

            _monitorView.Initialize(DevSuiteContext.DefaultInternal);
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _monitorView?.Reset();
            }
            else if (state == PlayModeStateChange.EnteredPlayMode)
            {
                _monitorView?.Initialize(DevSuiteContext.DefaultInternal);
            }
        }
    }
}
#endif