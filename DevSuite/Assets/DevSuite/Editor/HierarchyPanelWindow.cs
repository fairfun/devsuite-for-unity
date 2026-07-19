#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ff.DevSuite.View.Editor
{
    internal class HierarchyPanelWindow : EditorWindow
    {
        [MenuItem("Tools/DevSuite/Hierarchy Panel")]
        public static void Open()
        {
            var window = GetWindow<HierarchyPanelWindow>();
            var icon = EditorGUIUtility.IconContent("CustomTool")?.image;
            window.titleContent = new GUIContent("DevSuite: Hierarchy", icon);
        }

        private const string UxmlGuid = "d31a8ca9b28a49c693a7eb2db0d21051";
        private const string UssGuid = "f713c49e29a94e8093a7eb2db0d21052";

        private HierarchyPanelView _view;

        public void CreateGUI()
        {
            rootVisualElement.style.flexGrow = 1;

            var uxmlPath = AssetDatabase.GUIDToAssetPath(UxmlGuid);
            var ussPath = AssetDatabase.GUIDToAssetPath(UssGuid);

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);

            _view = new HierarchyPanelView(uxml, uss);
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
