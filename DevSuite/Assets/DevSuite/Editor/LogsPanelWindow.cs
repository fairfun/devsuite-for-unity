#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ff.DevSuite.View.Editor
{
    internal class LogsPanelWindow : EditorWindow
    {
        [MenuItem("Tools/DevSuite/Logs Panel")]
        public static void Open()
        {
            var window = GetWindow<LogsPanelWindow>();
            var icon = EditorGUIUtility.IconContent("CustomTool")?.image;
            window.titleContent = new GUIContent("DevSuite: Logs", icon);
        }

        private const string UxmlGuid = "f3014af3f8c999aab896e40704ee239b";
        private const string UssGuid = "a01a825176b137d0abd36ebee3cc4df6";

        private LogsPanelView _logsView;

        public void CreateGUI()
        {
            rootVisualElement.style.flexGrow = 1;

            var uxmlPath = AssetDatabase.GUIDToAssetPath(UxmlGuid);
            var ussPath = AssetDatabase.GUIDToAssetPath(UssGuid);

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);

            _logsView = new LogsPanelView(uxml, uss);
            _logsView.style.flexGrow = 1;
            _logsView.AddToClassList("editor");

            if (!EditorGUIUtility.isProSkin)
            {
                _logsView.AddToClassList("light-theme");
            }

            rootVisualElement.Add(_logsView);

            _logsView.Initialize(DevSuiteContext.DefaultInternal);
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            _logsView?.Reset();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _logsView?.Reset();
            }
            else if (state == PlayModeStateChange.EnteredPlayMode)
            {
                _logsView?.Initialize(DevSuiteContext.DefaultInternal);
            }
        }
    }
}
#endif