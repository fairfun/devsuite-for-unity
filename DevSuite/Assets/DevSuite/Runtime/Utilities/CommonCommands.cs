using DevSuite.Runtime.Utilities;
using Ff.DevSuite.Commands;
using Ff.DevSuite.Commands.Attributes;
using Ff.Prefs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting;

using Key =
#if ENABLE_INPUT_SYSTEM
    UnityEngine.InputSystem.Key;
#else
    UnityEngine.KeyCode;
#endif

namespace Ff.DevSuite
{
    [CommandCategory(CategoryCommon, Priority = 100)]
    public static class CommonCommands
    {
        public static Func<string, string> ModifySystemInfo { get; set; }
        public static Func<List<string>> CustomSystemInfoBuildTimeData { get; set; }

        public const string CategoryCommon = "Common";
        public const string GroupGame = "Game";
        public const string GroupData = "Data";
        public const string GroupSystem = "System";
        public const string GroupDevSuite = "Dev Suite";
        public const string GroupScenes = "Scenes";

        private const string ColorOrange = "#FFCC00";
        private const string ColorRed = "#FF6666";

        //Game

        private static float? _originalGameSpeed;
        [CommandGroup(GroupGame, Scope = AttributeScope.Continuous), Command(DisplayName = "Time Scale"), CommandValue(MinValue = 0.01f, MaxValue = 100f, ScaleType = ScaleType.Logarithmic)]
        [CommandValue(Flex = 0.5f)]
        public static SavedPrefsProperty<float?> TimeScale = new(nameof(TimeScale), null, onTouch: t =>
        {
            _originalGameSpeed ??= Time.timeScale;
            if (t.Type == SavedPrefsProperty<float?>.TouchType.Changed)
                Time.timeScale = t.Value ?? _originalGameSpeed ?? 1f;
        });

        //Data

        [CommandGroup(GroupData, Scope = AttributeScope.Continuous), Command(nameof(PlayerPrefsPath), DisplayName = "PlayerPrefs"), CommandValue(nameof(PlayerPrefsPath))]
        private static string PlayerPrefsPath => GetPlayerPrefsPath();

        [CommandButton(nameof(PlayerPrefsPath), Title = "Clear", Flex = 0f, Color = ColorRed)]
        private static void PlayerPrefs_DeleteAll() => PlayerPrefs.DeleteAll();

        [CommandButton(nameof(PlayerPrefsPath), Title = "Open", Flex = 0f)]
        private static void PlayerPrefs_Open()
        {
            var path = GetPlayerPrefsPath();
            if (string.IsNullOrEmpty(path))
                return;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            System.Diagnostics.Process.Start("regedit.exe");
#elif UNITY_WEBGL
            Debug.LogError("Cannot open PlayerPrefs path on WebGL platform.");
#else
            var dir = Path.GetDirectoryName(path);
            if (Directory.Exists(dir))
            {
                Application.OpenURL($"file://{dir}");
            }
#endif
        }

        private static string GetPlayerPrefsPath()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return $@"HKEY_CURRENT_USER\Software\{Application.companyName}\{Application.productName}";
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), $"Library/Preferences/unity.{Application.companyName}.{Application.productName}.plist");
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), $".config/unity3d/{Application.companyName}/{Application.productName}/");
#elif UNITY_ANDROID
            return $"/data/data/{Application.identifier}/shared_prefs/{Application.identifier}.xml";
#elif UNITY_IOS
            return Path.Combine(Path.GetDirectoryName(Application.persistentDataPath), "Library/Preferences", $"{Application.identifier}.plist");
#elif UNITY_WEBGL
            return "IndexedDB (Browser Storage)";
#else
            return string.Empty;
#endif
        }

#if UNITY_EDITOR
        [Command(nameof(EditorPrefs), "EditorPrefs")][CommandValue(nameof(EditorPrefs))]
        private static string EditorPrefs => GetEditorPrefsPath();

        [CommandButton(nameof(EditorPrefs), Title = "Clear", Flex = 0f, Color = ColorRed)]
        private static void EditorPrefs_Clear() => UnityEditor.EditorPrefs.DeleteAll();

        [CommandButton(nameof(EditorPrefs), Title = "Open", Flex = 0f)]
        private static void EditorPrefs_Open()
        {
            var path = GetEditorPrefsPath();
            if (string.IsNullOrEmpty(path))
                return;
#if UNITY_EDITOR_WIN
            System.Diagnostics.Process.Start("regedit.exe");
#else
            var dir = Path.GetDirectoryName(path);
            if (Directory.Exists(dir))
            {
                Application.OpenURL($"file://{dir}");
            }
#endif
        }

        private static string GetEditorPrefsPath()
        {
#if UNITY_EDITOR_WIN
            return @"HKEY_CURRENT_USER\Software\Unity Technologies\UnityEditor";
#elif UNITY_EDITOR_OSX
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Preferences/com.unity3d.UnityEditor.plist");
#elif UNITY_EDITOR_LINUX
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/unity3d/prefs");
#else
            return string.Empty;
#endif
        }
#endif

#if !UNITY_WEBGL || UNITY_EDITOR
        private static string _cachingPath; // to avoid allocations of calling Caching.currentCacheForWriting.path
        [Command(DisplayName = "Asset Bundles (Caching)"), CommandValue(nameof(AssetBundles))]
        private static string AssetBundles => _cachingPath ??= Caching.currentCacheForWriting.path;
        [CommandButton(nameof(AssetBundles), Title = "Clear", Flex = 0f, Color = ColorRed)]
        private static void AssetBundles_ClearCache() => Caching.ClearCache();
        [CommandButton(nameof(AssetBundles), Title = "Open", Flex = 0f)]
        private static void AssetBundles_Open() => Application.OpenURL($"file://{AssetBundles}");
#endif

        private static string _persistentDataPath; // to avoid allocations of calling Application.persistentDataPath
        [CommandValue(nameof(Persistent))]
        private static string Persistent => _persistentDataPath ??= Application.persistentDataPath;
        [CommandButton(nameof(Persistent), Title = "Clear", Flex = 0f, Color = ColorRed)]
        private static void PersistentDataPath_Delete() => Directory.Delete(Persistent, true);
        [CommandButton(nameof(Persistent), Title = "Open", Flex = 0f)]
        private static void PersistentDataPath_Open() => Application.OpenURL($"file://{Persistent}");

        private static string _dataPath; // to avoid allocations of calling Application.dataPath
        [CommandValue(nameof(Data))]
        private static string Data => _dataPath ??= Application.dataPath;
        [CommandButton(nameof(Data), Title = "Open", Flex = 0f)]
        private static void DataPath_Open() => Application.OpenURL($"file://{Data}");

        private static string _streamingAssetsPath; // to avoid allocations of calling Application.streamingAssetsPath
        [CommandValue(nameof(Streaming))]
        private static string Streaming => _streamingAssetsPath ??= Application.streamingAssetsPath;
        [CommandButton(nameof(Streaming), Title = "Open", Flex = 0f)]
        private static void StreamingAssetsPath_Open() => Application.OpenURL($"file://{Streaming}");

        private static string _temporaryCachePath; // to avoid allocations of calling Application.temporaryCachePath
        [CommandValue(nameof(Temporary))]
        private static string Temporary => _temporaryCachePath ??= Application.temporaryCachePath;
        [CommandButton(nameof(Temporary), Title = "Open", Flex = 0f)]
        private static void TemporaryCachePath_Open() => Application.OpenURL($"file://{Temporary}");

        //System

        private static string _systemInfoText;
        [CommandGroup(GroupSystem, Scope = AttributeScope.Continuous),
         Command(DisplayName = "Info", HeightMultiplier = 8.55f, Description = "You can change the information here by assigning CommonCommands.ModifySystemInfo. Compiler defines are not guaranteed to be 100% accurate. For adding custom build-time data use CommonCommands.CustomSystemInfoBuildTimeData."),
         CommandValue]
        private static string SystemInfoText
        {
            get
            {
                if (_systemInfoText == null)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"Product: {Application.identifier} ({Application.productName}, {Application.companyName})");
                    var versionStr = DevSuiteContext.Default?.BuildVersionToDisplay?.Invoke();
                    var buildNum = DevSuiteBuildTimeData.Default.BuildNumber;
                    if (!string.IsNullOrEmpty(buildNum))
                    {
                        versionStr = $"{versionStr} ({buildNum})";
                    }
                    sb.AppendLine($"Build: {versionStr} {(Debug.isDebugBuild ? "Debug" : "Release")}");
                    sb.AppendLine($"Unity: {Application.unityVersion}");
                    sb.AppendLine($"Platform: {Application.platform}, {SystemInfo.operatingSystem}, {Application.systemLanguage}");
                    sb.AppendLine($"Processor: {SystemInfo.processorType} ({SystemInfo.processorCount} cores)");
                    sb.AppendLine($"Memory: {SystemInfo.systemMemorySize} MB");
                    sb.AppendLine($"Graphics: {SystemInfo.graphicsMemorySize}MB {SystemInfo.graphicsDeviceName} ({RenderingPipeline()})");
                    sb.AppendLine($"Display: {DisplayInfo()}");
                    sb.AppendLine($"Device: {SystemInfo.deviceModel}, {SystemInfo.deviceType}");
                    sb.AppendLine($"Battery: {SystemInfo.batteryLevel * 100:F0}% ({SystemInfo.batteryStatus}), Run In Background {Application.runInBackground}");
                    sb.AppendLine($"Rendering: {RenderingInfo()}");
                    sb.AppendLine($"Explicit Defines: {string.Join(", ", DevSuiteBuildTimeData.Default.CompilerDefinesExplicit)}");
                    sb.AppendLine($"Other Defines: {string.Join(", ", DevSuiteBuildTimeData.Default.CompilerDefinesOther)}");
                    sb.AppendLine($"Player Settings: {string.Join(", ", DevSuiteBuildTimeData.Default.PlayerSettings)}");
                    sb.AppendLine($"Build Scenes: {string.Join(", ", DevSuiteBuildTimeData.Default.BuildSettingsScenes)}");
                    sb.AppendLine($"Dependencies: {string.Join(", ", DevSuiteBuildTimeData.Default.Dependencies)}");
                    if (DevSuiteBuildTimeData.Default.NugetDependencies?.Count > 0)
                    {
                        sb.AppendLine($"Nuget: {string.Join(", ", DevSuiteBuildTimeData.Default.NugetDependencies)}");
                    }
                    if (DevSuiteBuildTimeData.Default.CustomData?.Count > 0)
                    {
                        sb.AppendLine($"Custom: {string.Join(", ", DevSuiteBuildTimeData.Default.CustomData)}");
                    }
                    var res = sb.ToString().TrimEnd();
                    res = ModifySystemInfo?.Invoke(res) ?? res;
                    _systemInfoText = res;
                }
                return _systemInfoText;
            }
        }

        [CommandButton(nameof(SystemInfoText), Title = "\uf0e2", Flex = 0f)]
        public static void SystemInfoReset()
        {
            _systemInfoText = null;
        }

        private static string DisplayInfo()
        {
            var displayInfo = $"{Screen.width}x{Screen.height} @ {DevSuiteUtils.DisplayFrameRate:F0}Hz, dpi {Screen.dpi:F0}, {Screen.fullScreenMode}, {Screen.orientation}, {Display.displays.Length} displays";
            if (Screen.safeArea is { width: var safeW, height: var safeH } && (safeW != Screen.width || safeH != Screen.height))
                displayInfo += $", Safe Area: {Screen.safeArea}";
            return displayInfo;
        }

        private static string RenderingInfo()
        {
            var info = $"{RenderingPipeline()}, {QualitySettings.names[QualitySettings.GetQualityLevel()]}, vSync {QualitySettings.vSyncCount}";
            info += QualitySettings.antiAliasing > 0 ? $", MSAA {QualitySettings.antiAliasing}x" : ", MSAA off";
            info += $", shadows {QualitySettings.shadows}";
            if (QualitySettings.shadows != ShadowQuality.Disable)
                info += $" ({QualitySettings.shadowResolution}, {QualitySettings.shadowCascades} cascades, {QualitySettings.shadowDistance:F0}m)";
            info += $", lights {QualitySettings.pixelLightCount}, LOD bias {QualitySettings.lodBias:F1}";
            if (QualitySettings.globalTextureMipmapLimit > 0)
                info += $", texture mip limit {QualitySettings.globalTextureMipmapLimit}";
            if (QualitySettings.anisotropicFiltering != AnisotropicFiltering.Disable)
                info += $", aniso {QualitySettings.anisotropicFiltering}";
            if (QualitySettings.resolutionScalingFixedDPIFactor != 1f)
                info += $", render scale {QualitySettings.resolutionScalingFixedDPIFactor:P0}";
            if (QualitySettings.streamingMipmapsActive)
                info += ", streaming mipmaps";
            return info;
        }

        private static string RenderingPipeline()
        {
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                return $"{GraphicsSettings.currentRenderPipeline.GetType().Name} {GraphicsSettings.currentRenderPipeline.name}.asset";
            }
            return "Legacy (Built-in) renderer";
        }

        private static int? _originalVSyncCount;
        [CommandValue("vSyncCount", MinValue = 0, MaxValue = 4)]
        [CommandValue(Flex = 0.5f)]
        public static SavedPrefsProperty<int?> VSyncCount = new(nameof(VSyncCount), null, onTouch: t =>
        {
            _originalVSyncCount ??= QualitySettings.vSyncCount;

            if (t.Type == SavedPrefsProperty<int?>.TouchType.Changed)
                QualitySettings.vSyncCount = t.Value ?? _originalVSyncCount.Value;

            if (t.Value != null && t is { Type: SavedPrefsProperty<int?>.TouchType.Changed, PreviousValue: { HasValue: true, Value: null } })
                t.SetValue(_originalVSyncCount);
        });

        [Command(DisplayName = "GC"), CommandButton(Title = "System.GC.Collect", Flex = 1f)]
        private static void ForceGC() => GC.Collect();
        [CommandButton(nameof(ForceGC), Title = "GarbageCollector.CollectIncremental", Flex = 0f)]
        private static void ForceGCIncremental() => GarbageCollector.CollectIncremental();

        [Command(DisplayName = "Test Log"), CommandButton(Title = "Log", Flex = 1f)]
        private static void LogMessageLog() => Debug.Log("Test Log");
        [CommandButton(nameof(LogMessageLog), Title = "Warning", Flex = 1f)]
        private static void LogMessageWarning() => Debug.LogWarning("Test Log");
        [CommandButton(nameof(LogMessageLog), Title = "Error", Flex = 1f)]
        private static void LogMessageError() => Debug.LogError("Test Log");

        [Command(DisplayName = "Test Exception"), CommandButton(Title = "Throw", Color = ColorOrange, SuppressExceptions = false)]
        private static void ThrowException() => throw new Exception("DevSuite: Forced Exception");
        [CommandButton(nameof(ThrowException), Title = "Quit", Color = ColorRed, SuppressExceptions = false)]
        private static void ForceQuit() => Application.Quit(1);
        [CommandButton(nameof(ThrowException), Title = "Crash", Color = ColorRed, SuppressExceptions = false)]
        private static void ForceCrash() => UnityEngine.Diagnostics.Utils.ForceCrash(UnityEngine.Diagnostics.ForcedCrashCategory.AccessViolation);

        //DevSuite
        [CommandGroup(GroupDevSuite, Scope = AttributeScope.Continuous), Command(DisplayName = "Toggle Dev Suite Panel"), CommandButton(Title = "Toggle", Flex = 1f,
#if ENABLE_INPUT_SYSTEM
            Shortcut = new[] { Key.LeftCtrl, Key.Backquote }
#else
            Shortcut = new[] { Key.LeftControl, Key.BackQuote }
#endif
        )]
        private static void TogglePanel()
        {
            DevSuiteContext.DefaultInternal.PanelExpanded = !DevSuiteContext.DefaultInternal.PanelExpanded;
        }

        [CommandValue(nameof(SavedPrefs))]
        private static string SavedPrefs => Prefs.SavedPrefs.Default.FilePath;
        [CommandButton(nameof(SavedPrefs), Title = "Clear", Flex = 0f, Color = ColorRed)]
        private static void SavedPrefs_Clear() => Prefs.SavedPrefs.Default.Clear();
        [CommandButton(nameof(SavedPrefs), Title = "Open", Flex = 0f)]
        private static void SavedPrefs_Open() => Application.OpenURL($"file://{SavedPrefs}");

        private static int? _originalTargetFPS;
        [CommandValue("Target FPS", MinValue = 0f, MaxValue = 2000f, ScaleType = ScaleType.Logarithmic)]
        [CommandValue(Flex = 0.5f)]
        public static SavedPrefsProperty<int?> TargetFPS = new(nameof(TargetFPS), null, onTouch: t =>
        {
            _originalTargetFPS ??= Application.targetFrameRate;
            if (t.Type == SavedPrefsProperty<int?>.TouchType.Changed)
                Application.targetFrameRate = t.Value ?? _originalTargetFPS ?? -1;

            if (t.Value != null && t is { Type: SavedPrefsProperty<int?>.TouchType.Changed, PreviousValue: { HasValue: true, Value: null } })
                t.SetValue(_originalTargetFPS);
        });

        private static float? _originalTargetRamMb;
        [CommandValue("Target RAM", MinValue = 0f, MaxValue = 32000f, ScaleType = ScaleType.Logarithmic)]
        [CommandValue(Flex = 0.5f)]
        public static SavedPrefsProperty<float?> TargetRamMb = new(nameof(TargetRamMb), null, onTouch: t =>
        {
            _originalTargetRamMb ??= (float?)(DevSuiteContext.Default as DevSuiteContext).GetPerformancePanelGraphReferenceValue<Performance.SystemRamGraphDataProvider>();
            if (t.Type == SavedPrefsProperty<float?>.TouchType.Changed)
            {
                var val = t.Value ?? _originalTargetRamMb;
                DevSuiteContext.Default.SetPerformanceReferenceValue<Performance.SystemRamGraphDataProvider>(() => val);
            }

            if (t.Value != null && t is { Type: SavedPrefsProperty<float?>.TouchType.Changed, PreviousValue: { HasValue: true, Value: null } })
                t.SetValue(_originalTargetRamMb);
        });

        private static int? _originalTargetDrawCallsCount;
        [CommandValue("Target Draw Calls", MinValue = 0f, MaxValue = 10000f, ScaleType = ScaleType.Logarithmic)]
        [CommandValue(Flex = 0.5f)]
        public static SavedPrefsProperty<int?> TargetDrawCallsCount = new(nameof(TargetDrawCallsCount), null, onTouch: t =>
        {
            _originalTargetDrawCallsCount ??= (int?)(DevSuiteContext.Default as DevSuiteContext).GetPerformancePanelGraphReferenceValue<Performance.DrawCallsCountDataProvider>();
            if (t.Type == SavedPrefsProperty<int?>.TouchType.Changed)
            {
                var val = t.Value ?? _originalTargetDrawCallsCount;
                DevSuiteContext.Default.SetPerformanceReferenceValue<Performance.DrawCallsCountDataProvider>(() => val);
            }

            if (t.Value != null && t is { Type: SavedPrefsProperty<int?>.TouchType.Changed, PreviousValue: { HasValue: true, Value: null } })
                t.SetValue(_originalTargetDrawCallsCount);
        });

        [Command(DisplayName = "Destroy DevSuite"), CommandButton(Title = "All", Color = ColorOrange, Flex = 0.5f)]
        private static void DestroyDevSuite()
        {
            DestroyDevSuitePanel();
            DestroyDevSuiteContext();
        }

        [CommandButton(nameof(DestroyDevSuite), Title = "Panel", Color = ColorOrange, Flex = 0.5f)]
        private static void DestroyDevSuitePanel()
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<View.DevSuitePanelUI>();
            UnityEngine.Object.Destroy(panel.gameObject);
        }

        [CommandButton(nameof(DestroyDevSuite), Title = "Context.Default", Color = ColorOrange)]
        private static void DestroyDevSuiteContext()
        {
            DevSuiteContext.Default?.Reset();
            DevSuiteContext.Default = null;
        }

        public static void RegisterScenes()
        {
            var sceneNames = new List<string>();
            var registeredSet = new HashSet<string>();
            var editorOnlySet = new HashSet<string>();
            var packageScenesSet = new HashSet<string>();
            var buildIndices = new Dictionary<string, int>();
            var scenePaths = new Dictionary<string, string>();

            // get all scenes that are available in a build (runtime scenes)
            for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings; i++)
            {
                var scenePath = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i);
                var sceneName = Path.GetFileNameWithoutExtension(scenePath);
                if (IsValidScene(sceneName) && registeredSet.Add(sceneName))
                {
                    sceneNames.Add(sceneName);
                    buildIndices[sceneName] = i;
                    scenePaths[sceneName] = scenePath;
                    if (scenePath != null && scenePath.StartsWith("Packages/"))
                    {
                        packageScenesSet.Add(sceneName);
                    }
                }
            }

#if UNITY_EDITOR
            var guids = UnityEditor.AssetDatabase.FindAssets("t:Scene");
            var editorFound = new List<(string name, string path)>();
            foreach (var guid in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var sceneName = Path.GetFileNameWithoutExtension(path);
                if (IsValidScene(sceneName) && !registeredSet.Contains(sceneName) && !editorFound.Exists(x => x.name == sceneName))
                {
                    editorFound.Add((sceneName, path));
                }
            }
            editorFound.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            foreach (var item in editorFound)
            {
                if (registeredSet.Add(item.name))
                {
                    sceneNames.Add(item.name);
                    editorOnlySet.Add(item.name);
                    scenePaths[item.name] = item.path;
                    if (item.path != null && item.path.StartsWith("Packages/"))
                    {
                        packageScenesSet.Add(item.name);
                    }
                }
            }
#endif

            sceneNames.Sort((a, b) =>
            {
                var cmp = packageScenesSet.Contains(a).CompareTo(packageScenesSet.Contains(b));
                if (cmp == 0)
                    cmp = (!buildIndices.ContainsKey(a)).CompareTo(!buildIndices.ContainsKey(b));
                if (cmp == 0)
                    cmp = buildIndices.GetValueOrDefault(a).CompareTo(buildIndices.GetValueOrDefault(b));
                if (cmp == 0)
                    cmp = string.Compare(a, b, StringComparison.Ordinal);
                return cmp;
            });

            if (sceneNames.Count == 0)
                return;

            var api = DevSuiteContext.Default.CommandsApi;

            api.AddGroup(new CommandGroup(GroupScenes, CategoryCommon, -100f, null).WithCollapsed(true));

            foreach (var sceneName in sceneNames)
            {
                var isEditorOnly = editorOnlySet.Contains(sceneName);
                var isPackage = packageScenesSet.Contains(sceneName);
                AddSceneCommand(api, sceneName, isEditorOnly, isPackage, buildIndices, scenePaths);
            }

            bool IsValidScene(string sName)
            {
                return !string.IsNullOrEmpty(sName) && !sName.StartsWith('~');
            }
        }

        private static void AddSceneCommand(DevSuiteCommandsApi api, string sceneName, bool isEditorOnly, bool isPackage, Dictionary<string, int> buildIndices, Dictionary<string, string> scenePaths)
        {
            var command = new Command(
                sceneName,
                GroupScenes,
                CategoryCommon,
                0f,
                null,
                null,
                null
            );

            scenePaths.TryGetValue(sceneName, out var scenePath);
            scenePath ??= string.Empty;

            string displayName;
            if (isEditorOnly)
            {
                var prefix = isPackage ? "Packages: " : "Project: ";
                displayName = prefix + $"<i>{sceneName}</i>";
                command.WithDescription($"Editor-only scene\n{scenePath}");
            }
            else
            {
                var buildIndex = buildIndices[sceneName];
                displayName = $"{buildIndex}: {sceneName}";
                command.WithDescription($"Build settings scene\n{scenePath}");
            }

            command.WithDisplayName(displayName);

            api.AddCommand(command);

            var commandKey = new CommandKey(sceneName, GroupScenes, CategoryCommon, null);

            var countUnit = new CommandUnitValue(typeof(int), () => GetSceneInstanceCount(sceneName));
            api.AttachCommandUnit(commandKey, countUnit);

            var unloadUnit = new CommandUnitButton("Unload", () => UnloadLastSceneInstance(sceneName), flex: 0f);
            api.AttachCommandUnit(commandKey, unloadUnit);

            var loadNormalUnit = new CommandUnitButton("Load", () => LoadSceneNormal(sceneName), flex: 0f);
            api.AttachCommandUnit(commandKey, loadNormalUnit);

            var loadAdditiveUnit = new CommandUnitButton("Load Additive", () => LoadSceneAdditive(sceneName), flex: 0f);
            api.AttachCommandUnit(commandKey, loadAdditiveUnit);
        }

        private static int GetSceneInstanceCount(string sceneName)
        {
            var count = 0;
            for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene.name == sceneName)
                {
                    count++;
                }
            }
            return count;
        }

        private static void UnloadLastSceneInstance(string sceneName)
        {
            for (var i = UnityEngine.SceneManagement.SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene.name == sceneName)
                {
                    UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
                    break;
                }
            }
        }

        private static void LoadSceneNormal(string sceneName)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }

        private static void LoadSceneAdditive(string sceneName)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName, UnityEngine.SceneManagement.LoadSceneMode.Additive);
        }
    }
}