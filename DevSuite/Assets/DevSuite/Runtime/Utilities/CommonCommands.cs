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
    [CommandCategory(CategoryCommon, Priority = 100, Description = "Common commands.\n\n<b><i>Hint: </i></b>Check the <b><color=#ffc800>CommonCommands</color></b> class implementation for hints on how to use the Attributes and API.")]
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
        [CommandGroup(GroupGame, Scope = AttributeScope.Continuous), Command(DisplayName = "Time Scale", Scope = AttributeScope.Continuous, Description = "Adjust game time scale.\n\nControls <b><color=#ffc800>Time.timeScale</color></b> in code."), CommandValue(MinValue = 0.01f, MaxValue = 100f, ScaleType = ScaleType.Logarithmic)]
        public static SavedPrefsProperty<float?> TimeScale = new(nameof(TimeScale), null, onTouch: t =>
        {
            _originalGameSpeed ??= Time.timeScale;
            if (t.Type == SavedPrefsProperty<float?>.TouchType.Changed)
                Time.timeScale = t.Value ?? _originalGameSpeed ?? 1f;
        });

        [CommandValue(Flex = 0.2f, Description = "Current game time scale.\n\nReturns <b><color=#ffc800>Time.timeScale</color></b> in code.")]
        private static float ActualGameSpeed => Time.timeScale;

        //Data

        [CommandGroup(GroupData, Scope = AttributeScope.Continuous), Command(nameof(PlayerPrefsPath), DisplayName = "PlayerPrefs", Description = "PlayerPrefs storage path.\n\nValues are accessed via <b><color=#ffc800>PlayerPrefs</color></b> in code."), CommandValue(nameof(PlayerPrefsPath))]
        private static string PlayerPrefsPath => GetPlayerPrefsPath();

        [CommandButton(nameof(PlayerPrefsPath), Title = "Clear", Flex = 0f, Color = ColorRed, CliEnabled = false, Description = "Delete all PlayerPrefs keys.\n\nExecutes <b><color=#ffc800>PlayerPrefs.DeleteAll()</color></b> in code.")]
        private static void PlayerPrefs_DeleteAll() => PlayerPrefs.DeleteAll();

        [CommandButton(nameof(PlayerPrefsPath), Title = "Open", Flex = 0f, CliEnabled = false, Description = "Open PlayerPrefs storage location in file manager / registry.")]
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
        [Command(nameof(EditorPrefs), "EditorPrefs", Description = "EditorPrefs storage path.\n\nValues are accessed via <b><color=#ffc800>EditorPrefs</color></b> in code.")][CommandValue(nameof(EditorPrefs))]
        private static string EditorPrefs => GetEditorPrefsPath();

        [CommandButton(nameof(EditorPrefs), Title = "Clear", Flex = 0f, Color = ColorRed, CliEnabled = false, Description = "Delete all EditorPrefs keys.\n\nExecutes <b><color=#ffc800>EditorPrefs.DeleteAll()</color></b> in code.")]
        private static void EditorPrefs_Clear() => UnityEditor.EditorPrefs.DeleteAll();

        [CommandButton(nameof(EditorPrefs), Title = "Open", Flex = 0f, CliEnabled = false, Description = "Open EditorPrefs storage location in file manager / registry.")]
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
        [Command(DisplayName = "Asset Bundles (Caching)", Description = "AssetBundles cache directory.\n\nReturns <b><color=#ffc800>Caching.currentCacheForWriting.path</color></b> in code."), CommandValue(nameof(AssetBundles))]
        private static string AssetBundles => _cachingPath ??= Caching.currentCacheForWriting.path;
        [CommandButton(nameof(AssetBundles), Title = "Clear", Flex = 0f, Color = ColorRed, CliEnabled = false, Description = "Clear AssetBundles cache.\n\nExecutes <b><color=#ffc800>Caching.ClearCache()</color></b> in code.")]
        private static void AssetBundles_ClearCache() => Caching.ClearCache();
        [CommandButton(nameof(AssetBundles), Title = "Open", Flex = 0f, CliEnabled = false, Description = "Open AssetBundles cache directory in file manager.")]
        private static void AssetBundles_Open() => Application.OpenURL($"file://{AssetBundles}");
#endif

        private static string _persistentDataPath; // to avoid allocations of calling Application.persistentDataPath
        [Command(nameof(Persistent), DisplayName = "Persistent", Description = "Persistent data directory.\n\nReturns <b><color=#ffc800>Application.persistentDataPath</color></b> in code."), CommandValue(nameof(Persistent))]
        private static string Persistent => _persistentDataPath ??= Application.persistentDataPath;
        [CommandButton(nameof(Persistent), Title = "Clear", Flex = 0f, Color = ColorRed, CliEnabled = false, Description = "Delete persistent data directory.\n\nDeletes files in <b><color=#ffc800>Application.persistentDataPath</color></b> in code.")]
        private static void PersistentDataPath_Delete() => Directory.Delete(Persistent, true);
        [CommandButton(nameof(Persistent), Title = "Open", Flex = 0f, CliEnabled = false, Description = "Open persistent data directory in file manager.")]
        private static void PersistentDataPath_Open() => Application.OpenURL($"file://{Persistent}");

        private static string _dataPath; // to avoid allocations of calling Application.dataPath
        [Command(nameof(Data), DisplayName = "Data", Description = "Application data path.\n\nReturns <b><color=#ffc800>Application.dataPath</color></b> in code."), CommandValue(nameof(Data))]
        private static string Data => _dataPath ??= Application.dataPath;
        [CommandButton(nameof(Data), Title = "Open", Flex = 0f, CliEnabled = false, Description = "Open application data directory in file manager.")]
        private static void DataPath_Open() => Application.OpenURL($"file://{Data}");

        private static string _streamingAssetsPath; // to avoid allocations of calling Application.streamingAssetsPath
        [Command(nameof(Streaming), DisplayName = "Streaming", Description = "Streaming assets path.\n\nReturns <b><color=#ffc800>Application.streamingAssetsPath</color></b> in code."), CommandValue(nameof(Streaming))]
        private static string Streaming => _streamingAssetsPath ??= Application.streamingAssetsPath;
        [CommandButton(nameof(Streaming), Title = "Open", Flex = 0f, CliEnabled = false, Description = "Open streaming assets directory in file manager.")]
        private static void StreamingAssetsPath_Open() => Application.OpenURL($"file://{Streaming}");

        private static string _temporaryCachePath; // to avoid allocations of calling Application.temporaryCachePath
        [Command(nameof(Temporary), DisplayName = "Temporary", Description = "Temporary cache path.\n\nReturns <b><color=#ffc800>Application.temporaryCachePath</color></b> in code."), CommandValue(nameof(Temporary))]
        private static string Temporary => _temporaryCachePath ??= Application.temporaryCachePath;
        [CommandButton(nameof(Temporary), Title = "Open", Flex = 0f, CliEnabled = false, Description = "Open temporary cache directory in file manager.")]
        private static void TemporaryCachePath_Open() => Application.OpenURL($"file://{Temporary}");

        //System

        private static string _systemInfoText;
        [CommandGroup(GroupSystem, Scope = AttributeScope.Continuous),
         Command(DisplayName = "", HeightMultiplier = 9f, Description = "Application and system information.\n\nCompiler defines are not guaranteed to be 100% accurate.\n\n<b><i>Hint: </i></b>Change the information here by assigning <b><color=#ffc800>CommonCommands.ModifySystemInfo</color></b>.\n\n<b><i>Hint: </i></b>For adding custom build-time data use <b><color=#ffc800>CommonCommands.CustomSystemInfoBuildTimeData</color></b>."),
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
                    sb.AppendLine($"Graphics: {SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsMemorySize}MB {SystemInfo.graphicsDeviceName} ({RenderingPipeline()})");
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

        [CommandButton(nameof(SystemInfoText), Title = "\uf021", Flex = 0f, FontResource = "Font Awesome 7 Free-Solid-900 SDF", CliEnabled = false, Description = "Reset system info.\n\nClears cached system info text.")]
        public static void SystemInfoReset()
        {
            _systemInfoText = null;
        }

        private static string DisplayInfo()
        {
            var displayInfo = $"{Screen.width}x{Screen.height} @ {DevSuiteUtils.DisplayFrameRate:F0}Hz, dpi {Screen.dpi:F0}, fullscreen {Screen.fullScreenMode}, {Screen.orientation}, {Display.displays.Length} displays";
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

        private static int? _originalTargetFPS;
        [Command(nameof(TargetFPS), Description = "Set target frame rate.\n\nControls <b><color=#ffc800>Application.targetFrameRate</color></b> in code."), CommandValue("Target FPS", MinValue = 0f, MaxValue = 2000f, ScaleType = ScaleType.Logarithmic)]
        public static SavedPrefsProperty<int?> TargetFPS = new(nameof(TargetFPS), null, onTouch: t =>
        {
            _originalTargetFPS ??= Application.targetFrameRate;
            if (t.Type == SavedPrefsProperty<int?>.TouchType.Changed)
                Application.targetFrameRate = t.Value ?? _originalTargetFPS ?? -1;

            if (t.Value != null && t is { Type: SavedPrefsProperty<int?>.TouchType.Changed, PreviousValue: { HasValue: true, Value: null } })
                t.SetValue(_originalTargetFPS);
        });

        [Command(nameof(TargetFPS)), CommandValue(Flex = 0.2f, Description = "Current target frame rate.\n\nReturns <b><color=#ffc800>Application.targetFrameRate</color></b> in code.")]
        private static float ActualTargetFps => Application.targetFrameRate;

        private static int? _originalVSyncCount;
        [Command(nameof(VSyncCount), Description = "Set vertical sync count.\n\nControls <b><color=#ffc800>QualitySettings.vSyncCount</color></b> in code."), CommandValue("vSyncCount", MinValue = 0, MaxValue = 4)]
        public static SavedPrefsProperty<int?> VSyncCount = new(nameof(VSyncCount), null, onTouch: t =>
        {
            _originalVSyncCount ??= QualitySettings.vSyncCount;

            if (t.Type == SavedPrefsProperty<int?>.TouchType.Changed)
                QualitySettings.vSyncCount = t.Value ?? _originalVSyncCount.Value;

            if (t.Value != null && t is { Type: SavedPrefsProperty<int?>.TouchType.Changed, PreviousValue: { HasValue: true, Value: null } })
                t.SetValue(_originalVSyncCount);
        });

        [Command(nameof(VSyncCount)), CommandValue(Flex = 0.2f, Description = "Current vertical sync count.\n\nReturns <b><color=#ffc800>QualitySettings.vSyncCount</color></b> in code.")]
        private static float ActualVSyncCount => QualitySettings.vSyncCount;

        [Command(DisplayName = "GC", Description = "Garbage collection controls."), CommandButton(Title = "System.GC.Collect", Flex = 1f, CliEnabled = false, Description = "Force garbage collection.\n\nExecutes <b><color=#ffc800>System.GC.Collect()</color></b> in code.")]
        private static void ForceGC() => GC.Collect();
        [CommandButton(nameof(ForceGC), Title = "GarbageCollector.CollectIncremental", Flex = 0f, CliEnabled = false, Description = "Run incremental garbage collection.\n\nExecutes <b><color=#ffc800>GarbageCollector.CollectIncremental()</color></b> in code.")]
        private static void ForceGCIncremental() => GarbageCollector.CollectIncremental();

        [Command(DisplayName = "Test Log", Description = "Send a test log message to Unity console.\n\nExecutes <b><color=#ffc800>Debug.LogFormat()</color></b> in code."), CommandButton(Title = "Send Log", CliEnabled = false, Description = "Send a test log message to Unity console.\n\nExecutes <b><color=#ffc800>Debug.LogFormat()</color></b> in code.")]
        private static void SendLogMessage(LogType logType = LogType.Error)
        {
            Debug.LogFormat(logType, LogOption.None, null, "Test Log");
        }

        [Command(DisplayName = "Test Exception", Description = "Testing utilities for exceptions and crashes."), CommandButton(Title = "Throw", Color = ColorOrange, SuppressExceptions = false, CliEnabled = false, Description = "Throw a test exception.\n\nThrows <b><color=#ffc800>System.Exception</color></b> in code.")]
        private static void ThrowException() => throw new Exception("DevSuite: Forced Exception");
        [CommandButton(nameof(ThrowException), Title = "Quit", Color = ColorRed, SuppressExceptions = false, CliEnabled = false, Description = "Force application quit.\n\nExecutes <b><color=#ffc800>Application.Quit(1)</color></b> in code.")]
        private static void ForceQuit() => Application.Quit(1);
        [CommandButton(nameof(ThrowException), Title = "Crash", Color = ColorRed, SuppressExceptions = false, CliEnabled = false, Description = "Force crash application.\n\nExecutes <b><color=#ffc800>UnityEngine.Diagnostics.Utils.ForceCrash()</color></b> in code.")]
        private static void ForceCrash() => UnityEngine.Diagnostics.Utils.ForceCrash(UnityEngine.Diagnostics.ForcedCrashCategory.AccessViolation);

        //DevSuite
        [CommandGroup(GroupDevSuite, Scope = AttributeScope.Continuous), Command(DisplayName = "Toggle Dev Suite Panel", Description = "Toggle DevSuite panel visibility.\n\nControls <b><color=#ffc800>DevSuiteContext.Default.PanelExpanded</color></b> in code."), CommandButton(Title = "Toggle", Flex = 1f, CliEnabled = false, Description = "Toggle DevSuite panel visibility.\n\nControls <b><color=#ffc800>DevSuiteContext.Default.PanelExpanded</color></b> in code.",
#if ENABLE_INPUT_SYSTEM
            Shortcut = new[] { Key.LeftCtrl, Key.Backquote }
#else
            Shortcut = new[] { Key.LeftControl, Key.BackQuote }
#endif
        )]
        private static void TogglePanel()
        {
            var context = DevSuiteContext.DefaultInternal;
            if (!context.PanelExpanded)
            {
                context.PanelExpanded = true;
                context.LogsVisible = true;
                context.RequestFocusCli();
            }
            else
            {
                context.PanelExpanded = false;
            }
        }

        private static float? _pausedGameSpeed;
        [Command(DisplayName = "Set Game Speed", Scope = AttributeScope.Continuous, Description = "Set game speed.\n\nControls <b><color=#ffc800>Time.timeScale</color></b> in code."), CommandButton(Title = "Game Speed", CliCommand = "timescale", Description = "Set game speed.\n\nControls <b><color=#ffc800>Time.timeScale</color></b> in code.")]
        public static void GameSpeed(float speed = 1f)
        {
            if (_originalGameSpeed == null && Time.timeScale > 0f)
            {
                _originalGameSpeed = Time.timeScale;
            }

            speed = Mathf.Max(0f, speed);
            if (speed > 0f)
            {
                _pausedGameSpeed = null;
            }
            else if (Time.timeScale > 0f)
            {
                _pausedGameSpeed = Time.timeScale;
            }

            Time.timeScale = speed;
        }

        [CommandButton(nameof(GameSpeed), Title = "Pause", CliCommand = "pause", Description = "Pause the game.\n\nSets <b><color=#ffc800>Time.timeScale = 0</color></b> in code.")]
        public static void Pause()
        {
            if (_originalGameSpeed == null && Time.timeScale > 0f)
            {
                _originalGameSpeed = Time.timeScale;
            }

            if (Time.timeScale > 0f)
            {
                _pausedGameSpeed = Time.timeScale;
            }

            Time.timeScale = 0f;
        }

        [CommandButton(nameof(GameSpeed), Title = "Unpause", CliCommand = "unpause", Description = "Unpause the game.\n\nRestores <b><color=#ffc800>Time.timeScale</color></b> in code.")]
        public static void Unpause()
        {
            var targetSpeed = _pausedGameSpeed ?? _originalGameSpeed ?? 1f;
            if (targetSpeed <= 0f)
            {
                targetSpeed = 1f;
            }

            Time.timeScale = targetSpeed;
            _pausedGameSpeed = null;
        }

        [Command(DisplayName = "CLI Commands", Description = "Available CLI commands.\n\nType <b><color=#ffc800>help</color></b> in CLI to execute."), CommandButton(Title = "Show Cli Commands", CliCommand = "help", Description = "Show available CLI commands in console.\n\nType <b><color=#ffc800>help</color></b> in CLI to execute.")]
        public static void ShowCliCommands()
        {
            var commands = DevSuiteContext.DefaultInternal.GetActiveCliCommands();
            Debug.Log(FormatCliCommands(commands));
        }

        [CommandButton(nameof(ShowCliCommands), Title = "\uf0c5", Flex = 0f, FontResource = "Font Awesome 7 Free-Solid-900 SDF", Description = "Copy CLI commands to clipboard.\n\nCopies formatted command list via <b><color=#ffc800>DevSuiteUtils.CopyToClipboard()</color></b>.", CliEnabled = false)]
        public static void CopyCliCommands()
        {
            var commands = DevSuiteContext.DefaultInternal.GetActiveCliCommands();
            var text = FormatCliCommands(commands);
            DevSuiteUtils.CopyToClipboard(text);
            Debug.Log("Copied CLI commands to clipboard.");
        }

        private static string FormatCliCommands(IReadOnlyList<CliCommandData> commands)
        {
            if (commands == null || commands.Count == 0)
            {
                return "No active CLI commands found.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Available CLI Commands ({commands.Count}):");
            foreach (var cmd in commands)
            {
                var fullPath = $"{cmd.CategoryName}/{cmd.GroupName}/{cmd.CommandId}/";
                var paramsList = new List<string>();
                if (cmd.Parameters != null)
                {
                    foreach (var p in cmd.Parameters)
                    {
                        var typeName = DevSuiteUtils.GetFriendlyTypeName(p.Type);
                        var val = p.GetValue?.Invoke();
                        var valStr = val != null ? (val is string s ? $"\"{s}\"" : val.ToString()) : null;
                        if (!string.IsNullOrEmpty(valStr))
                        {
                            paramsList.Add($"<{typeName} {p.ParameterName} = {valStr}>");
                        }
                        else
                        {
                            paramsList.Add($"<{typeName} {p.ParameterName}>");
                        }
                    }
                }
                var paramsText = paramsList.Count > 0 ? " " + string.Join(" ", paramsList) : string.Empty;
                sb.AppendLine($"    {fullPath}{cmd.CliCommand}{paramsText}");

                if (!string.IsNullOrEmpty(cmd.Description))
                {
                    var descLines = DevSuiteUtils.NewLineRegex.Split(cmd.Description);
                    foreach (var line in descLines)
                    {
                        sb.AppendLine($"        {line}");
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        [Command(nameof(SavedPrefs), DisplayName = "SavedPrefs", Description = "DevSuite SavedPrefs file path.\n\nValues are accessed via <b><color=#ffc800>SavedPrefs.Default</color></b> in code."), CommandValue(nameof(SavedPrefs))]
        private static string SavedPrefs => Prefs.SavedPrefs.Default.FilePath;
        [CommandButton(nameof(SavedPrefs), Title = "Clear", Flex = 0f, Color = ColorRed, CliEnabled = false, Description = "Clear saved preferences file.\n\nExecutes <b><color=#ffc800>SavedPrefs.Default.Clear()</color></b> in code.")]
        private static void SavedPrefs_Clear() => Prefs.SavedPrefs.Default.Clear();
        [CommandButton(nameof(SavedPrefs), Title = "Open", Flex = 0f, CliEnabled = false, Description = "Open SavedPrefs directory in file manager.")]
        private static void SavedPrefs_Open() => Application.OpenURL($"file://{SavedPrefs}");

        [Command(DisplayName = "Destroy DevSuite", Description = "Destroy DevSuite panel and context.\n\nDestroys <b><color=#ffc800>DevSuitePanelUI</color></b> and resets <b><color=#ffc800>DevSuiteContext.Default</color></b> in code."), CommandButton(Title = "All", Color = ColorOrange, Flex = 0.5f, CliEnabled = false, Description = "Destroy DevSuite panel and context.\n\nDestroys <b><color=#ffc800>DevSuitePanelUI</color></b> and resets <b><color=#ffc800>DevSuiteContext.Default</color></b> in code.")]
        private static void DestroyDevSuite()
        {
            DestroyDevSuitePanel();
            DestroyDevSuiteContext();
        }

        [CommandButton(nameof(DestroyDevSuite), Title = "Panel", Color = ColorOrange, Flex = 0.5f, CliEnabled = false, Description = "Destroy DevSuite UI panel.\n\nDestroys <b><color=#ffc800>DevSuitePanelUI</color></b> GameObject in code.")]
        private static void DestroyDevSuitePanel()
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<View.DevSuitePanelUI>();
            UnityEngine.Object.Destroy(panel.gameObject);
        }

        [CommandButton(nameof(DestroyDevSuite), Title = "Context.Default", Color = ColorOrange, CliEnabled = false, Description = "Reset DevSuite context.\n\nResets <b><color=#ffc800>DevSuiteContext.Default</color></b> in code.")]
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
                command.WithDescription($"Editor-only scene\n{scenePath}\n\nFound via <b><color=#ffc800>AssetDatabase.FindAssets(\"t:Scene\")</color></b> in Editor.");
            }
            else
            {
                var buildIndex = buildIndices[sceneName];
                displayName = $"{buildIndex}: {sceneName}";
                command.WithDescription($"Build settings scene ({buildIndex})\n{scenePath}\n\nConfigured in Unity <b><color=#ffc800>EditorBuildSettings</color></b>.");
            }

            command.WithDisplayName(displayName);

            api.AddCommand(command);

            var commandKey = new CommandKey(sceneName, GroupScenes, CategoryCommon, null);

            var countUnit = new CommandUnitValue(typeof(int), () => GetSceneInstanceCount(sceneName), description: "Number of currently loaded instances of this scene.\n\nEvaluated via <b><color=#ffc800>SceneManager.GetSceneAt()</color></b> in code.");
            api.AttachCommandUnit(commandKey, countUnit);

            var unloadUnit = new CommandUnitButton("Unload", () => UnloadLastSceneInstance(sceneName), flex: 0f, cliEnabled: false, description: "Unload the last loaded instance of this scene.\n\nExecutes <b><color=#ffc800>SceneManager.UnloadSceneAsync()</color></b> in code.");
            api.AttachCommandUnit(commandKey, unloadUnit);

            var loadNormalUnit = new CommandUnitButton("Load", () => LoadSceneNormal(sceneName), flex: 0f, cliEnabled: false, description: "Load this scene (Single mode).\n\nExecutes <b><color=#ffc800>SceneManager.LoadScene(sceneName, LoadSceneMode.Single)</color></b> in code.");
            api.AttachCommandUnit(commandKey, loadNormalUnit);

            var loadAdditiveUnit = new CommandUnitButton("Load Additive", () => LoadSceneAdditive(sceneName), flex: 0f, cliEnabled: false, description: "Load this scene additively (Additive mode).\n\nExecutes <b><color=#ffc800>SceneManager.LoadScene(sceneName, LoadSceneMode.Additive)</color></b> in code.");
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