#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ff.DevSuite.View.Editor
{
    public static class DevSuiteEditorCommands
    {
        [MenuItem("Tools/DevSuite/Utils/Reload Domain", priority = 110)]
        public static void ReloadDomain()
        {
            EditorUtility.RequestScriptReload();
            Debug.Log("[DevSuite] Domain reload finished.");
        }

        [MenuItem("Tools/DevSuite/Utils/Clear PlayerPrefs", priority = 111)]
        public static void ClearPlayerPrefs()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            Debug.Log("[DevSuite] PlayerPrefs cleared.");
        }

        [MenuItem("Tools/DevSuite/Utils/Clear Persistent Data", priority = 112)]
        public static void ClearPersistentData()
        {
            var path = Application.persistentDataPath;
            if (Directory.Exists(path))
            {
                var directory = new DirectoryInfo(path);
                foreach (FileInfo file in directory.GetFiles())
                {
                    file.Delete();
                }
                foreach (DirectoryInfo dir in directory.GetDirectories())
                {
                    dir.Delete(true);
                }
                Debug.Log($"[DevSuite] Persistent Data cleared at: {path}");
            }
        }

        [MenuItem("Tools/DevSuite/Utils/Clear Asset Bundles", priority = 113)]
        public static void ClearAssetBundles()
        {
            if (Caching.ClearCache())
            {
                Debug.Log("[DevSuite] Asset bundles cache cleared.");
            }
            else
            {
                Debug.LogWarning("[DevSuite] Failed to clear asset bundles cache. It might be in use.");
            }
        }
    }
}
#endif