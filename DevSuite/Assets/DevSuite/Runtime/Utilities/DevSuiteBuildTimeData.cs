using Ff.DevSuite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DevSuite.Runtime.Utilities
{
    public class DevSuiteBuildTimeData : ScriptableObject
    {
        [SerializeField] private List<string> _compilerDefinesOther = new();
        [SerializeField] private List<string> _compilerDefinesExplicit = new();
        [SerializeField] private List<string> _playerSettings = new();
        [SerializeField] private string _buildNumber;
        [SerializeField] private List<string> _dependencies = new();
        [SerializeField] private List<string> _nugetDependencies = new();
        [SerializeField] private List<string> _buildSettingsScenes = new();
        [SerializeField] internal List<string> _customData = new();

        public IReadOnlyList<string> CompilerDefinesOther => _compilerDefinesOther;
        public IReadOnlyList<string> CompilerDefinesExplicit => _compilerDefinesExplicit;

        public IReadOnlyList<string> PlayerSettings => _playerSettings;
        public string BuildNumber => _buildNumber;
        public IReadOnlyList<string> Dependencies => _dependencies;
        public IReadOnlyList<string> NugetDependencies => _nugetDependencies;
        public IReadOnlyList<string> BuildSettingsScenes => _buildSettingsScenes;
        public IReadOnlyList<string> CustomData => _customData;

        private static DevSuiteBuildTimeData _default;
        public static DevSuiteBuildTimeData Default => _default ??= Resources.Load<DevSuiteBuildTimeData>("DevSuiteBuildTimeData");

        public void UpdateData(bool isBuilding = false)
        {
#if DEVSUITE_COLLECT_PLAYERSETTINGS_DISABLED
            return;
#endif

#if UNITY_EDITOR
            if (CommonCommands.CustomSystemInfoBuildTimeData != null)
            {
                _customData = CommonCommands.CustomSystemInfoBuildTimeData.Invoke();
            }

            var custom = new List<string>();
            var common = new List<string>();

            var targetGroup = UnityEditor.EditorUserBuildSettings.selectedBuildTargetGroup;
            var namedBuildTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(targetGroup);
            var defines = UnityEditor.PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
            if (!string.IsNullOrEmpty(defines))
            {
                foreach (var symbol in defines.Split(';', ',', ' '))
                {
                    var trimmedSymbol = symbol.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmedSymbol) && !custom.Contains(trimmedSymbol))
                    {
                        custom.Add(trimmedSymbol);
                    }
                }
            }

            try
            {
                var rspFiles = Directory.GetFiles(Application.dataPath, "*.rsp", SearchOption.AllDirectories);
                foreach (var file in rspFiles)
                {
                    if (File.Exists(file))
                    {
                        var lines = File.ReadAllLines(file);
                        foreach (var line in lines)
                        {
                            ParseAndAddDefines(line, custom);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DevSuite] Failed to parse .rsp files: {e}");
            }

            try
            {
                var rootPath = Path.Combine(Application.dataPath, "..");
                var csprojFiles = Directory.GetFiles(rootPath, "*.csproj", SearchOption.TopDirectoryOnly);
                foreach (var file in csprojFiles)
                {
                    if (Path.GetFileName(file).Contains("editor", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var content = File.ReadAllText(file);
                    var matches = Regex.Matches(content, @"<DefineConstants>(.*?)</DefineConstants>", RegexOptions.IgnoreCase);
                    foreach (Match match in matches)
                    {
                        var defineContent = match.Groups[1].Value;
                        if (!string.IsNullOrEmpty(defineContent))
                        {
                            var symbols = defineContent.Split(';', ',', ' ');
                            foreach (var symbol in symbols)
                            {
                                var trimmedSymbol = symbol.Trim();
                                if (!string.IsNullOrWhiteSpace(trimmedSymbol) &&
                                    !custom.Contains(trimmedSymbol) &&
                                    !common.Contains(trimmedSymbol))
                                {
                                    common.Add(trimmedSymbol);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DevSuite] Failed to parse .csproj files: {e}");
            }

            SortDirectives(common);
            SortDirectives(custom);

            if (isBuilding)
            {
                common = common.Where(e => !e.Contains("UNITY_EDITOR")).ToList();
                custom = custom.Where(e => !e.Contains("UNITY_EDITOR")).ToList();
            }

            var settings = new List<string>();
            var buildNum = string.Empty;

            try
            {
#if UNITY_6000_0_OR_NEWER
                var scriptingBackend = UnityEditor.PlayerSettings.GetScriptingBackend(namedBuildTarget);
#else
                var scriptingBackend = UnityEditor.PlayerSettings.GetScriptingBackend(targetGroup);
#endif
                settings.Add($"Scripting Backend: {scriptingBackend}");
            }
            catch {}

            try
            {
#if UNITY_6000_0_OR_NEWER
                var apiCompat = UnityEditor.PlayerSettings.GetApiCompatibilityLevel(namedBuildTarget);
#else
                var apiCompat = UnityEditor.PlayerSettings.GetApiCompatibilityLevel(targetGroup);
#endif
                settings.Add($"API Compatibility: {apiCompat}");
            }
            catch {}

            try
            {
                var apis = UnityEditor.PlayerSettings.GetGraphicsAPIs(UnityEditor.EditorUserBuildSettings.activeBuildTarget);
                settings.Add($"Graphics APIs: {string.Join(", ", apis)}");
            }
            catch {}

            try
            {
#if UNITY_6000_0_OR_NEWER
                var strippingLevel = UnityEditor.PlayerSettings.GetManagedStrippingLevel(namedBuildTarget);
#else
                var strippingLevel = UnityEditor.PlayerSettings.GetManagedStrippingLevel(targetGroup);
#endif
                settings.Add($"Managed Stripping: {strippingLevel}");
            }
            catch {}

            try
            {
                var stripEngine = UnityEditor.PlayerSettings.stripEngineCode;
                settings.Add($"Strip Engine Code: {stripEngine}");
            }
            catch {}

            try
            {
                var colorSpace = UnityEditor.PlayerSettings.colorSpace;
                settings.Add($"Color Space: {colorSpace}");
            }
            catch {}

            try
            {
                var mtRendering = UnityEditor.PlayerSettings.MTRendering;
                settings.Add($"Multithreaded Rendering: {mtRendering}");
            }
            catch {}

            try
            {
                var graphicsJobs = UnityEditor.PlayerSettings.graphicsJobs;
                settings.Add($"Graphics Jobs: {graphicsJobs}");
            }
            catch {}

            if (targetGroup == UnityEditor.BuildTargetGroup.Android)
            {
                try
                {
                    var targetArch = UnityEditor.PlayerSettings.Android.targetArchitectures;
                    settings.Add($"Android Architectures: {targetArch}");
                }
                catch {}
                try
                {
                    var minSdk = UnityEditor.PlayerSettings.Android.minSdkVersion;
                    var targetSdk = UnityEditor.PlayerSettings.Android.targetSdkVersion;
                    settings.Add($"Android SDK: Min={minSdk}, Target={targetSdk}");
                }
                catch {}
                try
                {
                    buildNum = UnityEditor.PlayerSettings.Android.bundleVersionCode.ToString();
                }
                catch {}
            }
            else if (targetGroup == UnityEditor.BuildTargetGroup.iOS)
            {
                try
                {
                    var targetOS = UnityEditor.PlayerSettings.iOS.targetOSVersionString;
                    settings.Add($"iOS Target OS: {targetOS}");
                }
                catch {}
                try
                {
                    buildNum = UnityEditor.PlayerSettings.iOS.buildNumber;
                }
                catch {}
            }

            var dependencies = new List<string>();
            try
            {
                var lockPath = Path.Combine(Application.dataPath, "../Packages/packages-lock.json");
                if (File.Exists(lockPath))
                {
                    var content = File.ReadAllText(lockPath);
                    var matches = Regex.Matches(content, @"""([^""]+)""\s*:\s*\{\s*""version""\s*:\s*""([^""]+)""");
                    foreach (Match match in matches)
                    {
                        var packageName = match.Groups[1].Value;
                        var packageVersion = match.Groups[2].Value;
                        if (packageName != "dependencies")
                        {
                            dependencies.Add($"{packageName}@{packageVersion}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DevSuite] Failed to parse packages-lock.json: {e}");
            }

            var nugetDependencies = new List<string>();
            try
            {
                var packagesPath = Path.Combine(Application.dataPath, "packages.config");
                if (File.Exists(packagesPath))
                {
                    var doc = new System.Xml.XmlDocument();
                    doc.Load(packagesPath);
                    var nodes = doc.SelectNodes("//package");
                    if (nodes != null)
                    {
                        foreach (System.Xml.XmlNode node in nodes)
                        {
                            var id = node.Attributes?["id"]?.Value;
                            var version = node.Attributes?["version"]?.Value;
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
                            {
                                nugetDependencies.Add($"{id}@{version}");
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DevSuite] Failed to parse packages.config: {e}");
            }

            var buildSettingsScenes = new List<string>();
            try
            {
                foreach (var scene in UnityEditor.EditorBuildSettings.scenes)
                {
                    buildSettingsScenes.Add(scene.enabled ? scene.path : $"{scene.path} (disabled)");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DevSuite] Failed to read EditorBuildSettings scenes: {e}");
            }

            _compilerDefinesOther = common;
            _compilerDefinesExplicit = custom;
            _playerSettings = settings;
            _buildNumber = buildNum;
            _dependencies = dependencies;
            _nugetDependencies = nugetDependencies;
            _buildSettingsScenes = buildSettingsScenes;

            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
#endif
        }

#if UNITY_EDITOR
        private static void SortDirectives(List<string> list)
        {
            list.Sort((a, b) =>
            {
                int priorityA = GetPrefixPriority(a);
                int priorityB = GetPrefixPriority(b);
                if (priorityA != priorityB)
                {
                    return priorityA.CompareTo(priorityB);
                }
                return string.Compare(a, b, StringComparison.Ordinal);
            });
        }

        private static int GetPrefixPriority(string name)
        {
            if (name.StartsWith("PLATFORM_", StringComparison.Ordinal)) return 1;
            if (name.StartsWith("CSHARP_", StringComparison.Ordinal)) return 2;
            if (name.StartsWith("NET_", StringComparison.Ordinal)) return 3;
            if (name.StartsWith("NET_STANDARD_", StringComparison.Ordinal)) return 4;
            if (name.StartsWith("NETSTANDARD_", StringComparison.Ordinal)) return 5;
            if (name.StartsWith("ENABLE_", StringComparison.Ordinal)) return 6;
            if (name.StartsWith("UNITY_", StringComparison.Ordinal)) return 7;
            return 0;
        }

        private static void ParseAndAddDefines(string text, List<string> directives)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var trimmed = text.Trim();
            string defineContent = null;
            if (trimmed.StartsWith("-define:", StringComparison.OrdinalIgnoreCase))
            {
                defineContent = trimmed.Substring(8);
            }
            else if (trimmed.StartsWith("-d:", StringComparison.OrdinalIgnoreCase))
            {
                defineContent = trimmed.Substring(3);
            }

            if (string.IsNullOrEmpty(defineContent))
                return;

            var symbols = defineContent.Split(';', ',', ' ');
            foreach (var symbol in symbols)
            {
                var trimmedSymbol = symbol.Trim();
                if (!string.IsNullOrWhiteSpace(trimmedSymbol) && !directives.Contains(trimmedSymbol))
                {
                    directives.Add(trimmedSymbol);
                }
            }
        }

        [UnityEditor.InitializeOnLoadMethod]
        private static void Initialize()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.EnteredPlayMode)
            {
                var data = Default;
                if (data != null)
                {
                    data.UpdateData();
                }
            }
        }
#endif
    }

#if UNITY_EDITOR
    internal class DevSuiteBuildTimeDataBuildProcessor : UnityEditor.Build.IPreprocessBuildWithReport
    {
        public int callbackOrder => int.MaxValue;

        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
        {
            DevSuiteBuildTimeData.Default.UpdateData(isBuilding: true);
        }
    }
#endif
}