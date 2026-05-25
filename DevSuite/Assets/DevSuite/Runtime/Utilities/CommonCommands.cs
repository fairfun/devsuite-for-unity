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
    [CommandCategory("Common", Priority = 100)]
    public static class CommonCommands
    {
        public static Func<string, string> ModifySystemInfo { get; set; }
        public static Func<List<string>> CustomSystemInfoBuildTimeData { get; set; }

        public const string GroupGame = "Game";
        public const string GroupData = "Data";
        public const string GroupSystem = "System";
        public const string GroupDevSuite = "Dev Suite";

        private const string ColorOrange = "#FFCC00";
        private const string ColorRed = "#FF6666";

        //Game

        private static float? _originalGameSpeed;
        [CommandGroup(GroupGame), Command(DisplayName = "Time Scale"), CommandValue(MinValue = 0.01f, MaxValue = 100f, ScaleType = ScaleType.Logarithmic)]
        [CommandValue(Flex = 0.5f)]
        public static SavedPrefsProperty<float?> TimeScale = new(nameof(TimeScale), null, onTouch: t =>
        {
            _originalGameSpeed ??= Time.timeScale;
            if (t.Type == SavedPrefsProperty<float?>.TouchType.Changed)
                Time.timeScale = t.Value ?? _originalGameSpeed ?? 1f;
        });

        //Data

        [CommandGroup(GroupData), CommandButton("Player Prefs", Title = "Clear", Flex = 0f, Color = ColorRed)]
        private static void PlayerPrefs_DeleteAll() => PlayerPrefs.DeleteAll();

#if !UNITY_WEBGL || UNITY_EDITOR
        private static string _cachingPath; // to avoid allocations of calling Caching.currentCacheForWriting.path
        [CommandGroup(GroupData), Command(DisplayName = "Asset Bundles (Caching)"), CommandValue(nameof(AssetBundles))]
        private static string AssetBundles => _cachingPath ??= Caching.currentCacheForWriting.path;
        [CommandGroup(GroupData), CommandButton(nameof(AssetBundles), Title = "Clear", Flex = 0f, Color = ColorRed)]
        private static void AssetBundles_ClearCache() => Caching.ClearCache();
        [CommandGroup(GroupData), CommandButton(nameof(AssetBundles), Title = "Open", Flex = 0f)]
        private static void AssetBundles_Open() => Application.OpenURL($"file://{AssetBundles}");
#endif

        private static string _persistentDataPath; // to avoid allocations of calling Application.persistentDataPath
        [CommandGroup(GroupData), CommandValue(nameof(Persistent))]
        private static string Persistent => _persistentDataPath ??= Application.persistentDataPath;
        [CommandGroup(GroupData), CommandButton(nameof(Persistent), Title = "Clear", Flex = 0f, Color = ColorRed)]
        private static void PersistentDataPath_Delete() => Directory.Delete(Persistent, true);
        [CommandGroup(GroupData), CommandButton(nameof(Persistent), Title = "Open", Flex = 0f)]
        private static void PersistentDataPath_Open() => Application.OpenURL($"file://{Persistent}");

        private static string _dataPath; // to avoid allocations of calling Application.dataPath
        [CommandGroup(GroupData), CommandValue(nameof(Data))]
        private static string Data => _dataPath ??= Application.dataPath;
        [CommandGroup(GroupData), CommandButton(nameof(Data), Title = "Open", Flex = 0f)]
        private static void DataPath_Open() => Application.OpenURL($"file://{Data}");

        private static string _streamingAssetsPath; // to avoid allocations of calling Application.streamingAssetsPath
        [CommandGroup(GroupData), CommandValue(nameof(Streaming))]
        private static string Streaming => _streamingAssetsPath ??= Application.streamingAssetsPath;
        [CommandGroup(GroupData), CommandButton(nameof(Streaming), Title = "Open", Flex = 0f)]
        private static void StreamingAssetsPath_Open() => Application.OpenURL($"file://{Streaming}");

        private static string _temporaryCachePath; // to avoid allocations of calling Application.temporaryCachePath
        [CommandGroup(GroupData), CommandValue(nameof(Temporary))]
        private static string Temporary => _temporaryCachePath ??= Application.temporaryCachePath;
        [CommandGroup(GroupData), CommandButton(nameof(Temporary), Title = "Open", Flex = 0f)]
        private static void TemporaryCachePath_Open() => Application.OpenURL($"file://{Temporary}");

        //System

        private static string _systemInfoText;
        [CommandGroup(GroupSystem),
         Command(DisplayName = "Info", HeightMultiplier = 10.55f, Description = "You can change the information here by assigning CommonCommands.ModifySystemInfo. Compiler defines are not guaranteed to be 100% accurate. For adding custom build-time data use CommonCommands.CustomSystemInfoBuildTimeData."),
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

        [CommandGroup(GroupSystem), CommandButton(nameof(SystemInfoText), Title = "\uf0e2", Flex = 0f)]
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
        [CommandGroup(GroupSystem), CommandValue("vSyncCount", MinValue = 0, MaxValue = 4)]
        [CommandValue(Flex = 0.5f)]
        public static SavedPrefsProperty<int?> VSyncCount = new(nameof(VSyncCount), null, onTouch: t =>
        {
            _originalVSyncCount ??= QualitySettings.vSyncCount;

            if (t.Type == SavedPrefsProperty<int?>.TouchType.Changed)
                QualitySettings.vSyncCount = t.Value ?? _originalVSyncCount.Value;

            if (t.Value != null && t is { Type: SavedPrefsProperty<int?>.TouchType.Changed, PreviousValue: { HasValue: true, Value: null } })
                t.SetValue(_originalVSyncCount);
        });

        [CommandGroup(GroupSystem), Command(DisplayName = "GC"), CommandButton(Title = "System.GC.Collect", Flex = 1f)]
        private static void ForceGC() => GC.Collect();
        [CommandGroup(GroupSystem), CommandButton(nameof(ForceGC), Title = "GarbageCollector.CollectIncremental", Flex = 0f)]
        private static void ForceGCIncremental() => GarbageCollector.CollectIncremental();

        [CommandGroup(GroupSystem), Command(DisplayName = "Test Log"), CommandButton(Title = "Log", Flex = 1f)]
        private static void LogMessageLog() => Debug.Log("Test Log");
        [CommandGroup(GroupSystem), CommandButton(nameof(LogMessageLog), Title = "Warning", Flex = 1f)]
        private static void LogMessageWarning() => Debug.LogWarning("Test Log");
        [CommandGroup(GroupSystem), CommandButton(nameof(LogMessageLog), Title = "Error", Flex = 1f)]
        private static void LogMessageError() => Debug.LogError("Test Log");

        [CommandGroup(GroupSystem), Command(DisplayName = "Test Exception"), CommandButton(Title = "Throw", Color = ColorOrange, SuppressExceptions = false)]
        private static void ThrowException() => throw new Exception("DevSuite: Forced Exception");
        [CommandGroup(GroupSystem), CommandButton(nameof(ThrowException), Title = "Quit", Color = ColorRed, SuppressExceptions = false)]
        private static void ForceQuit() => Application.Quit(1);
        [CommandGroup(GroupSystem), CommandButton(nameof(ThrowException), Title = "Crash", Color = ColorRed, SuppressExceptions = false)]
        private static void ForceCrash() => UnityEngine.Diagnostics.Utils.ForceCrash(UnityEngine.Diagnostics.ForcedCrashCategory.AccessViolation);

        //DevSuite
        [CommandGroup(GroupDevSuite), Command(DisplayName = "Toggle Dev Suite Panel"), CommandButton(Title = "Toggle", Flex = 1f,
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

        [CommandGroup(GroupDevSuite), CommandValue(nameof(SavedPrefs))]
        private static string SavedPrefs => Prefs.SavedPrefs.Default.FilePath;
        [CommandGroup(GroupDevSuite), CommandButton(nameof(SavedPrefs), Title = "Clear", Flex = 0f, Color = ColorRed)]
        private static void SavedPrefs_Clear() => Prefs.SavedPrefs.Default.Clear();
        [CommandGroup(GroupDevSuite), CommandButton(nameof(SavedPrefs), Title = "Open", Flex = 0f)]
        private static void SavedPrefs_Open() => Application.OpenURL($"file://{SavedPrefs}");

        private static int? _originalTargetFPS;
        [CommandGroup(GroupDevSuite), CommandValue("Target FPS", MinValue = 0f, MaxValue = 2000f, ScaleType = ScaleType.Logarithmic)]
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
        [CommandGroup(GroupDevSuite), CommandValue("Target RAM", MinValue = 0f, MaxValue = 32000f, ScaleType = ScaleType.Logarithmic)]
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
        [CommandGroup(GroupDevSuite), CommandValue("Target Draw Calls", MinValue = 0f, MaxValue = 10000f, ScaleType = ScaleType.Logarithmic)]
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

        [CommandGroup(GroupDevSuite), Command(DisplayName = "Destroy DevSuite"), CommandButton(Title = "All", Color = ColorOrange, Flex = 0.5f)]
        private static void DestroyDevSuite()
        {
            DestroyDevSuitePanel();
            DestroyDevSuiteContext();
        }

        [CommandGroup(GroupDevSuite), CommandButton(nameof(DestroyDevSuite), Title = "Panel", Color = ColorOrange, Flex = 0.5f)]
        private static void DestroyDevSuitePanel()
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<View.DevSuitePanelUI>();
            UnityEngine.Object.Destroy(panel.gameObject);
        }

        [CommandGroup(GroupDevSuite), CommandButton(nameof(DestroyDevSuite), Title = "Context.Default", Color = ColorOrange)]
        private static void DestroyDevSuiteContext()
        {
            DevSuiteContext.Default?.Reset();
            DevSuiteContext.Default = null;
        }
    }
}