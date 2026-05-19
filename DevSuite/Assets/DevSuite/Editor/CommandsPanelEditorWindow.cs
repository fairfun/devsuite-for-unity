#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ff.DevSuite.View.Editor
{
    internal class CommandsPanelEditorWindow : EditorWindow
    {
        private const string UxmlGuid = "6dc846c3a5d6a924aaaac3c7536ee211";
        private const string UssGuid = "9901fd956665334e4b7f721042c1e9d9";

        private CommandsPanelView _panelView;

        [MenuItem("Tools/DevSuite/Commands Panel")]
        public static void ShowWindow()
        {
            var window = GetWindow<CommandsPanelEditorWindow>();
            var icon = EditorGUIUtility.IconContent("CustomTool")?.image;
            window.titleContent = new GUIContent("DevSuite: Commands", icon);
        }

        private void CreateGUI()
        {
            var uxmlPath = AssetDatabase.GUIDToAssetPath(UxmlGuid);
            var ussPath = AssetDatabase.GUIDToAssetPath(UssGuid);

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);

            _panelView = new CommandsPanelView(uxml, uss);
            _panelView.style.flexGrow = 1;
            _panelView.AddToClassList("editor");

            if (!EditorGUIUtility.isProSkin)
            {
                _panelView.AddToClassList("light-theme");
            }

            rootVisualElement.Add(_panelView);

            _panelView.Initialize(DevSuiteContext.DefaultInternal, CommandsPanelView.ViewMode.Full);
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
                _panelView?.Reset();
            }
            else if (state == PlayModeStateChange.EnteredPlayMode)
            {
                _panelView?.Initialize(DevSuiteContext.DefaultInternal, CommandsPanelView.ViewMode.Full);
            }
        }
    }
}
#endif