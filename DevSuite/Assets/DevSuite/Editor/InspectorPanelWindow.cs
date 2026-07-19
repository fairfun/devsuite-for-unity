#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ff.DevSuite.View.Editor
{
    internal class InspectorPanelWindow : EditorWindow
    {
        [MenuItem("Tools/DevSuite/Inspector Panel")]
        public static void Open()
        {
            var window = GetWindow<InspectorPanelWindow>();
            var icon = EditorGUIUtility.IconContent("CustomTool")?.image;
            window.titleContent = new GUIContent("DevSuite: Inspector", icon);
        }

        private const string UxmlGuid = "a4603957be84451093a7eb2db0d21053";
        private const string UssGuid = "d9426f8aae8a491893a7eb2db0d21054";

        private InspectorPanelView _view;

        public void CreateGUI()
        {
            rootVisualElement.style.flexGrow = 1;

            var uxmlPath = AssetDatabase.GUIDToAssetPath(UxmlGuid);
            var ussPath = AssetDatabase.GUIDToAssetPath(UssGuid);

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);

            _view = new InspectorPanelView(uxml, uss);
            _view.style.flexGrow = 1;
            _view.AddToClassList("editor");

            if (!EditorGUIUtility.isProSkin)
            {
                _view.AddToClassList("light-theme");
            }

            rootVisualElement.Add(_view);

            _view.Initialize(DevSuiteContext.DefaultInternal);
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
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
