#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ff.DevSuite.View.Editor
{
    internal class ControlPanelWindow : EditorWindow
    {
        private const string UxmlGuid = "450fe3e3746309b379f324aac83a6d7c";
        private const string UssGuid = "872b69fa69ba506cca8912a7152f46e0";

        private ControlPanelView _view;

        [MenuItem("Tools/DevSuite/Control Panel")]
        public static void ShowWindow()
        {
            var window = GetWindow<ControlPanelWindow>();
            var icon = EditorGUIUtility.IconContent("CustomTool")?.image;
            window.titleContent = new GUIContent("DevSuite: Control", icon);
        }

        private void CreateGUI()
        {
            var uxmlPath = AssetDatabase.GUIDToAssetPath(UxmlGuid);
            var ussPath = AssetDatabase.GUIDToAssetPath(UssGuid);

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);

            _view = new ControlPanelView(uxml, uss, false, DevSuitePanelActivationMode.SingleClick, ControlPanelExpandButtonVisibility.Visible);
            _view.AddToClassList("editor");

            if (!EditorGUIUtility.isProSkin)
            {
                _view.AddToClassList("light-theme");
            }

            rootVisualElement.Add(_view);

            if (Application.isPlaying)
            {
                _view.Initialize(DevSuiteContext.DefaultInternal);
            }
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            if (Application.isPlaying && _view != null)
            {
                _view.Initialize(DevSuiteContext.DefaultInternal);
            }
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            _view?.Reset();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _view?.Reset();
            }
            else if (state == PlayModeStateChange.EnteredPlayMode)
            {
                _view?.Initialize(DevSuiteContext.DefaultInternal);
            }
        }
    }
}
#endif