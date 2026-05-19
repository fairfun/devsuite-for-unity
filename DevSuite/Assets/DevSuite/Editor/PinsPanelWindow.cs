#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ff.DevSuite.View.Editor
{
    internal class PinsPanelWindow : EditorWindow
    {
        private const string UxmlGuid = "6dc846c3a5d6a924aaaac3c7536ee211";
        private const string UssGuid = "9901fd956665334e4b7f721042c1e9d9";

        private CommandsPanelView _view;

        [MenuItem("Tools/DevSuite/Pins Panel")]
        public static void ShowWindow()
        {
            var window = GetWindow<PinsPanelWindow>();
            var icon = EditorGUIUtility.IconContent("CustomTool")?.image;
            window.titleContent = new GUIContent("DevSuite: Pins", icon);
        }

        private void CreateGUI()
        {
            var uxmlPath = AssetDatabase.GUIDToAssetPath(UxmlGuid);
            var ussPath = AssetDatabase.GUIDToAssetPath(UssGuid);

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);

            _view = new CommandsPanelView(uxml, uss);
            _view.style.flexGrow = 1;
            _view.AddToClassList("editor");

            if (!EditorGUIUtility.isProSkin)
            {
                _view.AddToClassList("light-theme");
            }

            var root = _view.Q<VisualElement>("commands-panel");
            if (root != null)
            {
                root.style.maxWidth = StyleKeyword.None;
                root.style.minWidth = StyleKeyword.None;
                root.style.flexGrow = 1;
                root.style.alignSelf = Align.Stretch;
            }

            rootVisualElement.Add(_view);

            if (Application.isPlaying)
            {
                _view.Initialize(DevSuiteContext.DefaultInternal, CommandsPanelView.ViewMode.Pinned);
            }
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            if (Application.isPlaying && _view != null)
            {
                _view.Initialize(DevSuiteContext.DefaultInternal, CommandsPanelView.ViewMode.Pinned);
            }
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            _view?.Initialize(null, CommandsPanelView.ViewMode.Pinned);
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _view?.Reset();
            }
            else if (state == PlayModeStateChange.EnteredPlayMode)
            {
                _view?.Initialize(DevSuiteContext.DefaultInternal, CommandsPanelView.ViewMode.Pinned);
            }
        }
    }
}
#endif