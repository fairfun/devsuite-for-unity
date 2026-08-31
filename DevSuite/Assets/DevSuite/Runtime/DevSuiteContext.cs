using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Ff.DevSuite.Commands;
using Ff.DevSuite.Commands.Attributes;
using Ff.DevSuite.Performance;
using Ff.Prefs;
using MemoryPack;
using MessagePack;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
#endif

using Key =
#if ENABLE_INPUT_SYSTEM
    UnityEngine.InputSystem.Key;
#else
    UnityEngine.KeyCode;
#endif

[assembly: InternalsVisibleTo("DevSuite.Editor")]
[assembly: InternalsVisibleTo("DevSuite.Examples")]

namespace Ff.DevSuite
{
    public interface IDevSuiteContext : IDisposable
    {
        CommandAttributesParser AttributesParser { get; }
        DevSuiteCommandsApi CommandsApi { get; }
        IDisposable SuspendEvents(object requestor);
        bool Disposed { get; }
        void Initialize(MonoBehaviour coroutineStarter, IList<Assembly> staticCommandsAssemblies = null, ISavedPrefs savedPrefs = null, bool registerCommonCommands = true);
        void Reset();
        void RegisterPerformanceGraph<T>(T provider, GraphDataProviderSettings overrideSettings = null) where T : BaseGraphDataProvider;
        void SetPerformanceGraphSettings<T>(GraphDataProviderSettings settings) where T : BaseGraphDataProvider;
        Func<string> BuildVersionToDisplay { get; set; }
        GameObject SelectedGameObject { get; set; }
        string GetAllLogsText();
        void ClearLogs();
        void ClearSettings();
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    public class DevSuiteContext : IDevSuiteContext
    {
        public static bool Enabled { get; set; } = true; // global kill-switch

#if UNITY_EDITOR
        static DevSuiteContext()
        {
            UnityEditor.EditorApplication.playModeStateChanged += m =>
            {
                if (m is UnityEditor.PlayModeStateChange.ExitingEditMode or UnityEditor.PlayModeStateChange.ExitingPlayMode)
                {
                    ResetStatic();
                }
            };
        }
#endif

        private static void ResetStatic()
        {
            _default?.Dispose();
            _default?.OnChanged?.Invoke();
            _default = null;
        }

        private static DevSuiteContext _default;

        public static IDevSuiteContext Default
        {
            get => _default ??= new DevSuiteContext();
            internal set
            {
                if (_default == value)
                {
                    return;
                }
                ResetStatic();
                _default = value as DevSuiteContext;
            }
        }

        internal static DevSuiteContext DefaultInternal => Default as DevSuiteContext;

        public CommandAttributesParser AttributesParser { get; internal set; }
        public DevSuiteCommandsApi CommandsApi { get; internal set; }

        public IDisposable SuspendEvents(object requestor)
        {
            return Block.SetAndTrack(true, 1, requestor);
        }

        internal static string PinnedMockId { get; set; } = "<Default$Pinned>";
        internal static string DefaultGroupId { get; set; } = "Default";
        internal static string PinnedCategoryId { get; set; } = "Pinned";
        internal static string NullRepresentation { get; set; } = "null";

        internal ValueStack<bool> Block { get; } = new();
        internal readonly ValueStack<float> _pauseHandlerGameSpeed = new(-1f, 0);
        private float? _savedGameSpeed;

        private event Action OnApiCalled;
        internal event Action OnChanged;
        internal event Action OnEveryFrame;
        internal event Action OnPerformancePanelChanged;
        internal event Action<BaseGraphDataProvider, bool> OnPerformanceGraphCollapsedChanged;
        internal event Action OnLogMessagesChanged;
        internal event Action<LogMessageData> OnLogMessagesMessageAdded;
        internal event Action OnLogMessagesVisibilityChanged;
        internal event Action OnHierarchyChanged;
        private readonly BlockableDispatcher _apiCalledDispatcher;
        private readonly BlockableDispatcher _onChangedDispatcher;
        private readonly BlockableDispatcher _onEveryFrameDispatcher;
        private readonly BlockableDispatcher _onPerformancePanelDispatcher;
        private readonly BlockableDispatcher _onHierarchyPanelDispatcher;
        private BlockableDispatcher OnEveryFrameDispatcher => _onEveryFrameDispatcher;
        internal BlockableDispatcher ApiCalledDispatcher => _apiCalledDispatcher;

        private readonly List<BaseGraphDataProvider> _performancePanelProviders = new();
        internal IReadOnlyList<BaseGraphDataProvider> PerformancePanelProviders => _performancePanelProviders;

        private readonly List<LogMessageData> _allLogMessages = new();
        internal IReadOnlyList<LogMessageData> AllLogMessages => _allLogMessages;

        private Regex _logsFilterRegex;

        internal Regex LogsFilterRegex
        {
            get
            {
                if (_logsFilterRegex == null)
                {
                    UpdateLogsFilterRegex();
                }
                return _logsFilterRegex;
            }
        }

        private CommandCategory _categoryPinned;
        private CommandGroup _groupPinned;
        internal Dictionary<CategoryKey, CommandCategory> Categories { get; } = new();
        internal Dictionary<GroupKey, CommandGroup> Groups { get; } = new();
        internal Dictionary<CommandKey, Command> Commands { get; } = new();

        internal List<CommandValueAdapter> CommandValueAdapters { get; } = new();
        internal Dictionary<Type, CommandValuesProvider> ValuesProviders { get; } = new();

        internal Dictionary<Type, CommandFunctionsSourceProvider> TargetsForFunctionsProviders { get; } = new();

        private readonly Dictionary<Type, GraphDataProviderSettings> _performanceGraphSettings = new();

        public Func<string> BuildVersionToDisplay { get; set; } = () => "v" + Application.version;

        private readonly List<GameObject> _selectedGameObjects = new();
        public IReadOnlyList<GameObject> SelectedGameObjects => _selectedGameObjects;

        internal HashSet<string> HierarchyCollapsedScenes { get; } = new();
        internal HashSet<int> HierarchyExpandedGameObjects { get; } = new();
        internal GameObject HierarchySelectionAnchor { get; set; }
        internal void NotifyHierarchyChanged() => _onHierarchyPanelDispatcher.Dispatch();

#if UNITY_EDITOR
        private bool _isSyncingEditorSelection;

        private void SyncEditorSelection()
        {
            if (_isSyncingEditorSelection)
            {
                return;
            }

            try
            {
                _isSyncingEditorSelection = true;

                var currentSelection = UnityEditor.Selection.gameObjects;
                var matches = currentSelection.Length == _selectedGameObjects.Count;
                if (matches)
                {
                    for (var i = 0; i < currentSelection.Length; i++)
                    {
                        if (currentSelection[i] != _selectedGameObjects[i])
                        {
                            matches = false;
                            break;
                        }
                    }
                }

                if (!matches)
                {
                    if (_selectedGameObjects.Count == 0)
                    {
                        UnityEditor.Selection.objects = Array.Empty<UnityEngine.Object>();
                    }
                    else if (_selectedGameObjects.Count == 1)
                    {
                        UnityEditor.Selection.activeGameObject = _selectedGameObjects[0];
                    }
                    else
                    {
                        UnityEditor.Selection.objects = _selectedGameObjects.ToArray();
                    }
                }
            }
            finally
            {
                _isSyncingEditorSelection = false;
            }
        }
#endif

        private bool _isSelectedFromDevSuite;

        internal void SetSelectedGameObjects(IEnumerable<GameObject> gameObjects)
        {
            _selectedGameObjects.Clear();
            if (gameObjects != null)
            {
                _selectedGameObjects.AddRange(gameObjects);
            }
#if UNITY_EDITOR
            SyncEditorSelection();
#endif
            _isSelectedFromDevSuite = _selectedGameObjects.Count > 0;
            UpdateInspectorAutoPause();
            _onChangedDispatcher.Dispatch();
        }

        internal void SetSelectedGameObjectsFromEditor(IEnumerable<GameObject> gameObjects)
        {
            _selectedGameObjects.Clear();
            if (gameObjects != null)
            {
                _selectedGameObjects.AddRange(gameObjects);
            }
            _isSelectedFromDevSuite = false;
            UpdateInspectorAutoPause();
            _onChangedDispatcher.Dispatch();
        }

        internal void ToggleSelectedGameObject(GameObject go)
        {
            if (go == null)
            {
                return;
            }
            if (_selectedGameObjects.Contains(go))
            {
                _selectedGameObjects.Remove(go);
            }
            else
            {
                _selectedGameObjects.Add(go);
            }
#if UNITY_EDITOR
            SyncEditorSelection();
#endif
            _isSelectedFromDevSuite = _selectedGameObjects.Count > 0;
            UpdateInspectorAutoPause();
            _onChangedDispatcher.Dispatch();
        }

        public GameObject SelectedGameObject
        {
            get => _selectedGameObjects.Count > 0 ? _selectedGameObjects[0] : null;
            set
            {
                if (value == null)
                {
                    if (_selectedGameObjects.Count == 0)
                    {
                        return;
                    }
                    _selectedGameObjects.Clear();
                }
                else
                {
                    if (_selectedGameObjects.Count == 1 && _selectedGameObjects[0] == value)
                    {
                        return;
                    }
                    _selectedGameObjects.Clear();
                    _selectedGameObjects.Add(value);
                }
#if UNITY_EDITOR
                SyncEditorSelection();
#endif
                _isSelectedFromDevSuite = _selectedGameObjects.Count > 0;
                UpdateInspectorAutoPause();
                _onChangedDispatcher.Dispatch();
            }
        }

        internal void SelectGameObjectViaPick(GameObject go)
        {
            SelectedGameObject = go;
        }

        internal event Action<bool> OnPickModeChanged;
        private bool _pickModeActive;

        internal bool PickModeActive
        {
            get => _pickModeActive;
            set
            {
                if (_pickModeActive == value)
                {
                    return;
                }

                _pickModeActive = value;
                UpdatePickModePause();
                OnPickModeChanged?.Invoke(_pickModeActive);
                _onChangedDispatcher.Dispatch();
            }
        }

        private void UpdatePickModePause()
        {
            _pauseHandlerGameSpeed.Toggle(0f, 1, _pickModeActive, this);
        }

        private void UpdateInspectorAutoPause()
        {
            var hasSelected = false;
            for (var i = _selectedGameObjects.Count - 1; i >= 0; i--)
            {
                var go = _selectedGameObjects[i];
                if (go == null)
                {
                    _selectedGameObjects.RemoveAt(i);
                }
                else
                {
                    hasSelected = true;
                }
            }

            if (!hasSelected)
            {
                _isSelectedFromDevSuite = false;
            }

            var shouldPause = InspectorAutoPause && hasSelected && _isSelectedFromDevSuite;
            _pauseHandlerGameSpeed.Toggle(0f, 2, shouldPause, this);
        }

        private readonly CommandCategory _defaultCategory = new(DefaultGroupId, -1f, null);
        internal int RegistrationOrderCounter { get; set; }

        private ISavedPrefs _savedPrefs;

        internal IReadOnlyList<TreeCategory> Tree { get; private set; }

        internal SavedPrefsProperty<PersistentSettings> Settings { get; set; }

        private const string ErrorNoAdapter = "<No Adapter>";
        private const string ErrorException = "<Exception>";
        private const string ErrorNoFunction = "<No Function>";
        private const string ErrorNotAvailable = "<Not Available>";

        private MonoBehaviour _coroutineStarter;
        private Coroutine _updateCoroutine;
        private readonly HashSet<Key> _holdKeys = new();

        private bool _initialized;

        public DevSuiteContext()
        {
            _apiCalledDispatcher = new BlockableDispatcher(Block, () => OnApiCalled?.Invoke());
            _onChangedDispatcher = new BlockableDispatcher(Block, () => OnChanged?.Invoke());
            _onEveryFrameDispatcher = new BlockableDispatcher(Block, () => OnEveryFrame?.Invoke());
            _onPerformancePanelDispatcher = new BlockableDispatcher(Block, () => OnPerformancePanelChanged?.Invoke());
            _onHierarchyPanelDispatcher = new BlockableDispatcher(Block, () => OnHierarchyChanged?.Invoke());

            _pauseHandlerGameSpeed.OnChanged += speed =>
            {
                if (speed >= 0f)
                {
                    if (!_savedGameSpeed.HasValue)
                    {
                        _savedGameSpeed = Time.timeScale;
                    }
                    Time.timeScale = speed;
                }
                else
                {
                    if (_savedGameSpeed.HasValue)
                    {
                        Time.timeScale = _savedGameSpeed.Value;
                        _savedGameSpeed = null;
                    }
                }
            };

            Reset();

            try
            {
                Application.logMessageReceivedThreaded += HandleUnityLog; // start collecting messages already, even though everything else awaits to be initialized yet
            }
            catch
            {
            }
        }

        public bool Disposed { get; private set; }

        public void Dispose()
        {
            PickModeActive = false;
            OnPickModeChanged = null;
            OnApiCalled = null;
            OnChanged = null;

            Unsubscribe();
            Reset();
            Disposed = true;
        }

        /// <summary>
        /// </summary>
        /// <param name="coroutineStarter">MonoBehaviour used to start the update loop coroutine.</param>
        /// <param name="staticCommandsAssemblies">Its rcommended specifying your game assembly (i.e. Assembly.GetAssembly(typeof(SomeClassOfYours)). Otherwise, a broader set of assemblies will be checked, and that is slow.</param>
        /// <param name="savedPrefs"></param>
        /// <param name="registerCommonCommands"></param>
        public void Initialize(MonoBehaviour coroutineStarter, IList<Assembly> staticCommandsAssemblies = null, ISavedPrefs savedPrefs = null, bool registerCommonCommands = true)
        {
            if (!Enabled)
            {
                Debug.Log($"Initializing {nameof(DevSuiteContext)} is skipped because {nameof(DevSuiteContext)}.{nameof(DevSuiteContext.Enabled)}=false");
                return;
            }

            if (_initialized)
            {
                throw new Exception("Already initialized. If you need to reinitialize call Reset() before.");
            }

            _initialized = true;
            _coroutineStarter = coroutineStarter;
            _savedPrefs = savedPrefs ?? SavedPrefs.Factory.Invoke("DevSuiteContext.Default");
            Settings = new SavedPrefsProperty<PersistentSettings>("DevSuiteContext_Settings", new PersistentSettings(), true, _savedPrefs);
            _savedPrefs?.EnsureReady().Wait();
            Settings.Value.InitializeDefaultsIfNeeded();

            using var _ = Block.SetAndTrack(true, 1, this);

            AttributesParser = new CommandAttributesParser(this);
            CommandsApi = new DevSuiteCommandsApi(this);

            foreach (var defaultAdapter in DefaultCommandValueAdapters.Get())
            {
                CommandsApi.RegisterAdapter(defaultAdapter, true);
            }

            foreach (var valueProvider in DefaultCommandValuesProviders.Get())
            {
                CommandsApi.RegisterValuesProvider(valueProvider, true);
            }

            RegisterPerformanceGraph(new FrameTimeGraphDataProvider());
            RegisterPerformanceGraph(new CpuFrameTimeGraphDataProvider());
            RegisterPerformanceGraph(new GpuFrameTimeGraphDataProvider());
            RegisterPerformanceGraph(new CpuRenderThreadFrameTimeGraphDataProvider());
            RegisterPerformanceGraph(new FpsGraphDataProvider());
            RegisterPerformanceGraph(new GcMemoryGraphDataProvider());
            RegisterPerformanceGraph(new SystemRamGraphDataProvider());
            RegisterPerformanceGraph(new DrawCallsCountDataProvider());
            RegisterPerformanceGraph(new BatchesCountDataProvider());
            RegisterPerformanceGraph(new TrianglesCountDataProvider());

            if (registerCommonCommands)
            {
                AttributesParser.RegisterStatic(typeof(CommonCommands));
                CommonCommands.RegisterScenes();
            }
            AttributesParser.RegisterStatic(staticCommandsAssemblies);
            _apiCalledDispatcher.Dispatch();

            Subscribe();
        }

        public void Reset()
        {
            _initialized = false;

            if (_pickModeActive)
            {
                PickModeActive = false;
            }
            _selectedGameObjects.Clear();
            _isSelectedFromDevSuite = false;
            _pauseHandlerGameSpeed.Remove(1, this);
            _pauseHandlerGameSpeed.Remove(2, this);

            AttributesParser = null;
            CommandsApi = null;

            ClearLogs();
            Tree?.AsEditable().Clear();
            Tree = null;
            Categories.Clear();
            Groups.Clear();
            Commands.Clear();
            CommandValueAdapters.Clear();
            ValuesProviders.Clear();
            TargetsForFunctionsProviders.Clear();
            RegistrationOrderCounter = 0;

            _getGroupByCategory?.Clear();
            _cachedRegexForSearch = null;

            _apiCalledDispatcher.Reset();
            _onChangedDispatcher.Reset();
            _onEveryFrameDispatcher.Reset();
            _onPerformancePanelDispatcher.Reset();
            _onHierarchyPanelDispatcher.Reset();
            HierarchyCollapsedScenes.Clear();
            HierarchyExpandedGameObjects.Clear();
            HierarchySelectionAnchor = null;

            foreach (var provider in _performancePanelProviders)
            {
                provider.Dispose();
            }
            _performancePanelProviders.Clear();

            Unsubscribe();
            _coroutineStarter = null;

            InvalidateCache();

            OnChanged?.Invoke();
        }

        private readonly Dictionary<Type, bool> _performancePanelDefaultCollapsed = new();

        public void RegisterPerformanceGraph<T>(T provider, GraphDataProviderSettings overrideSettings = null) where T : BaseGraphDataProvider
        {
            if (overrideSettings != null)
            {
                SetPerformanceGraphSettings<T>(overrideSettings);
            }

            var type = provider.GetType();
            overrideSettings = _performanceGraphSettings.GetValueOrDefault(type);
            var settings = overrideSettings ?? provider.Settings;

            var shouldRegister = overrideSettings?.Register ?? settings?.Register ?? true;
            if (!shouldRegister)
                return;

            var isExpanded = overrideSettings?.ExpandedByDefault ?? settings?.ExpandedByDefault ?? false;
            var refProvider = overrideSettings?.ReferenceValueProvider ?? settings?.ReferenceValueProvider;
            if (refProvider != null)
            {
                provider.Settings.ReferenceValueProvider = refProvider;
            }

            _performancePanelDefaultCollapsed[type] = !isExpanded;
            _performancePanelProviders.Add(provider);
            _onPerformancePanelDispatcher.Dispatch();
        }

        public bool IsPerformanceGraphCollapsed(BaseGraphDataProvider provider)
        {
            return CheckSettingsInitialized(true) && Settings.Value.PerformanceGraphCollapsedState.TryGetValue(provider.GetType().Name, out var collapsed)
                ? collapsed
                : _performancePanelDefaultCollapsed.GetValueOrDefault(provider.GetType(), false);
        }

        public void SetPerformanceGraphCollapsed(BaseGraphDataProvider provider, bool collapsed)
        {
            if (!CheckSettingsInitialized())
            {
                return;
            }
            Settings.Value.PerformanceGraphCollapsedState[provider.GetType().Name] = collapsed;
            Settings.ForceSave();
            OnPerformanceGraphCollapsedChanged?.Invoke(provider, collapsed);
        }

        public void SetPerformanceGraphSettings<T>(GraphDataProviderSettings settings) where T : BaseGraphDataProvider
        {
            var type = typeof(T);

            if (!_performanceGraphSettings.TryGetValue(type, out var currentSettings))
            {
                currentSettings = new GraphDataProviderSettings();
                _performanceGraphSettings[type] = currentSettings;
            }

            if (settings.ReferenceValueProvider != null)
            {
                currentSettings.ReferenceValueProvider = settings.ReferenceValueProvider;
                foreach (var provider in _performancePanelProviders)
                {
                    if (provider is T p)
                    {
                        p.Settings.ReferenceValueProvider = settings.ReferenceValueProvider;
                    }
                }
            }

            if (settings.ExpandedByDefault.HasValue)
            {
                currentSettings.ExpandedByDefault = settings.ExpandedByDefault.Value;
                _performancePanelDefaultCollapsed[type] = !settings.ExpandedByDefault.Value;
            }

            if (settings.Register.HasValue)
            {
                currentSettings.Register = settings.Register.Value;
            }
        }

        internal double? GetPerformancePanelGraphReferenceValue<T>() where T : BaseGraphDataProvider
        {
            if (_performanceGraphSettings.TryGetValue(typeof(T), out var settings) && settings.ReferenceValueProvider != null)
            {
                return settings.ReferenceValueProvider.Invoke();
            }
            foreach (var provider in _performancePanelProviders)
            {
                if (provider is T p)
                {
                    return p.Settings?.ReferenceValueProvider?.Invoke();
                }
            }
            return null;
        }

        private void HandleApiCalled()
        {
            using var _ = Block.SetAndTrack(true, 1, _onChangedDispatcher);
            RebuildTree();
            _onChangedDispatcher.Dispatch();
        }


        private LazyCache<Type, string, bool, Func<object, object>> _getTryGetValueFromTargetsCache;

        internal bool TryGetValueFromTargets<T>(Type classType, string memberName, object @object, out T value, out string error)
        {
            _getTryGetValueFromTargetsCache ??= new LazyCache<Type, string, bool, Func<object, object>>(
                (classType, memberName, @static) =>
                {
                    var getInstance = new Func<object, object>(o => o);
                    var directMember = (@static ? GetReadableMemberFromType(classType, memberName, true) : null)
                                       ?? GetReadableMemberFromType(classType, memberName, false);
                    if (directMember == null)
                    {
                        foreach (var provider in TargetsForFunctionsProviders.Values)
                        {
                            var contains = true;
                            if (provider.FunctionsNames != null)
                            {
                                contains = false;
                                foreach (var name in provider.FunctionsNames)
                                {
                                    if (name == memberName)
                                    {
                                        contains = true;
                                        break;
                                    }
                                }
                            }
                            if (!contains)
                            {
                                continue;
                            }

                            if (provider.TargetInstance != null)
                            {
                                directMember = GetReadableMemberFromType(provider.Type, memberName, false);
                                getInstance = _ => provider.TargetInstance;
                            }

                            directMember ??= GetReadableMemberFromType(provider.Type, memberName, true);
                            if (directMember != null)
                            {
                                break;
                            }
                        }
                    }

                    if (directMember != null)
                    {
                        return o => directMember.GetValueByMember(getInstance(o));
                    }
                    return _ => null;
                }
            );

            try
            {
                var getter = _getTryGetValueFromTargetsCache[classType, memberName, @object == null];
                if (getter == null)
                {
                    value = default;
                    error = ErrorNoFunction;
                    Debug.LogWarning($"Could not find member '{memberName}'");
                    return false;
                }

                var val = getter(@object);
                if (val is T valT)
                {
                    value = valT;
                    error = null;
                    return true;
                }
            }
            catch (Exception e)
            {
                value = default;
                error = ErrorException;
                Debug.LogWarning($"Exception while evaluating member '{memberName}': {e}");
            }

            value = default;
            error = ErrorNoFunction;
            Debug.LogWarning($"Could not find member '{memberName}' on instance '{@object}'");
            return false;
        }

        private LazyCache<Type, string, bool, MemberInfo> _getReadableMemberFromTypeCache;

        private MemberInfo GetReadableMemberFromType(Type type, string memberName, bool @static)
        {
            _getReadableMemberFromTypeCache ??= new LazyCache<Type, string, bool, MemberInfo>(
                (type, memberName, @static) =>
                {
                    var flags = BindingFlags.Public | BindingFlags.NonPublic;
                    if (@static)
                    {
                        flags |= BindingFlags.Static;
                    }
                    else
                    {
                        flags |= BindingFlags.Instance;
                    }

                    var field = type.GetField(memberName, flags);
                    if (field != null)
                    {
                        return field;
                    }

                    var property = type.GetProperty(memberName, flags);
                    if (property != null && property.CanRead)
                    {
                        return property;
                    }

                    var methods = type.GetMethods(flags);
                    foreach (var method in methods)
                    {
                        if (method.Name == memberName && method.GetParameters().Length <= 0)
                        {
                            return method;
                        }
                    }

                    return null;
                }
            );
            return _getReadableMemberFromTypeCache[type, memberName, @static];
        }

        internal class AllowedValuesResult
        {
            public IEnumerable Values { get; }
            public Type Type { get; }
            public object CurrentValue { get; }

            public AllowedValuesResult(IEnumerable values, Type type, object currentValue)
            {
                Values = values;
                Type = type;
                CurrentValue = currentValue;
            }
        }

        internal bool HasLimitedValues(CommandUnitValue unit)
        {
            return unit.AllowedValues != null || GetValuesProviderFromChain(unit.Type) != null;
        }

        private class ValuesProviderFromChain
        {
            public Type TypeFor { get; }
            public Type TypeActual { get; }
            public CommandValuesProvider Provider { get; }

            public ValuesProviderFromChain(Type typeFor, Type typeActual, CommandValuesProvider provider)
            {
                TypeFor = typeFor;
                TypeActual = typeActual;
                Provider = provider;
            }
        }

        internal AllowedValuesResult GetAllowedValues(CommandUnitValue unit)
        {
            if (!CheckUnitAvailability(unit))
            {
                return null;
            }

            var type = unit.Type;
            var values = unit.AllowedValues?.Invoke();
            var currentValue = unit.GetValue();

            if (values == null)
            {
                var provider = GetValuesProviderFromChain(type);
                if (provider != null)
                {
                    values = provider.Provider.Values(provider.TypeActual);
                    currentValue = GetRepresentation(currentValue, currentValue?.GetType() ?? typeof(string), provider.TypeActual, out _);
                }
            }

            if (values == null)
            {
                return null;
            }

            var valuesObj = new List<object>();
            var hasNull = false;
            foreach (var val in values)
            {
                valuesObj.Add(val);
                if (val == null)
                {
                    hasNull = true;
                }
            }

            if (hasNull)
            {
                return new AllowedValuesResult(valuesObj, type, currentValue);
            }

            valuesObj.Insert(0, null);
            return new AllowedValuesResult(valuesObj, type, currentValue);
        }

        internal void TogglePinItem(Command command, bool value)
        {
            if (!CheckSettingsInitialized())
            {
                return;
            }

            var pinnedItem = new PinnedItem(command);
            PinnedItem existingPin = null;
            if (Settings.Value.PinnedItems != null)
            {
                foreach (var i in Settings.Value.PinnedItems)
                {
                    if (i.Same(pinnedItem))
                    {
                        existingPin = i;
                        break;
                    }
                }
            }

            if (value && (existingPin?.Match(command) ?? false))
            {
                Debug.LogWarning($"Same item '{command.Id}' was already pinned");
                return;
            }

            if (value)
            {
                Settings.Value.PinnedItems.Add(pinnedItem);
            }
            else
            {
                Settings.Value.PinnedItems.Remove(existingPin);
            }
            _pinnedCommands = null;
            Settings.ForceSave();

            RebuildTree();
        }

        internal bool IsGroupCollapsed(string groupId, string categoryId, bool defaultCollapsed)
        {
            if (!CheckSettingsInitialized(true))
            {
                return defaultCollapsed;
            }

            foreach (var item in Settings.Value.CollapsedGroups)
            {
                if (item.GroupId == groupId && item.CategoryId == categoryId)
                {
                    return item.Collapsed;
                }
            }
            return defaultCollapsed;
        }

        internal void ToggleGroupCollapse(string groupId, string categoryId, bool collapsed)
        {
            if (!CheckSettingsInitialized())
            {
                return;
            }

            CollapsedGroupItem existing = null;
            foreach (var item in Settings.Value.CollapsedGroups)
            {
                if (item.GroupId == groupId && item.CategoryId == categoryId)
                {
                    existing = item;
                    break;
                }
            }

            if (existing != null)
            {
                existing.Collapsed = collapsed;
            }
            else
            {
                Settings.Value.CollapsedGroups.Add(new CollapsedGroupItem(groupId, categoryId, collapsed));
            }
            Settings.ForceSave();
        }

        private OrderedSet<Command> _pinnedCommands;

        internal OrderedSet<Command> GetPinnedCommands(bool forceRefresh)
        {
            if (!CheckSettingsInitialized(true))
            {
                return DevSuiteUtils.EmptyOrderedSet<Command>();
            }

            if (_pinnedCommands == null || forceRefresh)
            {
                var list = new List<Command>();
                foreach (var kvp in Commands)
                {
                    if (kvp.Value.AlwaysPin)
                    {
                        list.Add(kvp.Value);
                    }
                }

                if (Settings.Value.PinnedItems != null)
                {
                    foreach (var i in Settings.Value.PinnedItems)
                    {
                        foreach (var kvp in Commands)
                        {
                            if (i.Match(kvp.Value))
                            {
                                list.Add(kvp.Value);
                                break;
                            }
                        }
                    }
                }

                list.Sort();
                _pinnedCommands = new OrderedSet<Command>(list);
            }
            return _pinnedCommands;
        }

        internal bool MetricsVisible
        {
            get => (Settings?.Ready ?? false) && Settings.Value.MetricsVisible;
            set => SetSettingsValue(() => Settings.Value.MetricsVisible, v => Settings.Value.MetricsVisible = v, value);
        }

        internal bool CommandsVisible
        {
            get => (Settings?.Ready ?? false) && Settings.Value.CommandsVisible;
            set => SetSettingsValue(() => Settings.Value.CommandsVisible, v => Settings.Value.CommandsVisible = v, value);
        }

        internal bool PinnedCommandsVisible
        {
            get => (Settings?.Ready ?? false) && Settings.Value.PinnedCommandsVisible;
            set => SetSettingsValue(() => Settings.Value.PinnedCommandsVisible, v => Settings.Value.PinnedCommandsVisible = v, value);
        }

        internal bool LogsVisible
        {
            get => (Settings?.Ready ?? false) && Settings.Value.LogsVisible;
            set => SetSettingsValue(() => Settings.Value.LogsVisible, v => Settings.Value.LogsVisible = v, value);
        }

        internal bool PanelExpanded
        {
            get => (Settings?.Ready ?? false) && Settings.Value.PanelExpanded;
            set => SetSettingsValue(() => Settings.Value.PanelExpanded, v => Settings.Value.PanelExpanded = v, value);
        }

        internal bool HierarchyVisible
        {
            get => (Settings?.Ready ?? false) && Settings.Value.HierarchyVisible;
            set => SetSettingsValue(() => Settings.Value.HierarchyVisible, v => Settings.Value.HierarchyVisible = v, value);
        }

        internal bool InspectorVisible
        {
            get => (Settings?.Ready ?? false) && Settings.Value.InspectorVisible;
            set => SetSettingsValue(() => Settings.Value.InspectorVisible, v => Settings.Value.InspectorVisible = v, value);
        }

        internal bool InspectorAutoRefresh
        {
            get => (Settings?.Ready ?? false) && Settings.Value.InspectorAutoRefresh;
            set => SetSettingsValue(() => Settings.Value.InspectorAutoRefresh, v => Settings.Value.InspectorAutoRefresh = v, value);
        }

        internal bool InspectorAutoPause
        {
            get => !(Settings?.Ready ?? false) || Settings.Value.InspectorAutoPause;
            set
            {
                SetSettingsValue(() => Settings.Value.InspectorAutoPause, v => Settings.Value.InspectorAutoPause = v, value);
                UpdateInspectorAutoPause();
            }
        }

        internal bool HierarchySearchRegex
        {
            get => (Settings?.Ready ?? false) && Settings.Value.HierarchySearchRegex;
            set => SetSettingsValue(() => Settings.Value.HierarchySearchRegex, v => Settings.Value.HierarchySearchRegex = v, value);
        }

        internal bool HierarchySearchByName
        {
            get => !(Settings?.Ready ?? false) || Settings.Value.HierarchySearchByName;
            set => SetSettingsValue(() => Settings.Value.HierarchySearchByName, v => Settings.Value.HierarchySearchByName = v, value);
        }

        internal bool HierarchySearchByType
        {
            get => !(Settings?.Ready ?? false) || Settings.Value.HierarchySearchByType;
            set => SetSettingsValue(() => Settings.Value.HierarchySearchByType, v => Settings.Value.HierarchySearchByType = v, value);
        }

        internal bool HierarchyKeepDimmed
        {
            get => !(Settings?.Ready ?? false) || Settings.Value.HierarchyKeepDimmed;
            set => SetSettingsValue(() => Settings.Value.HierarchyKeepDimmed, v => Settings.Value.HierarchyKeepDimmed = v, value);
        }

        internal string HierarchyPattern
        {
            get => Settings?.Ready ?? false ? Settings.Value.HierarchyPattern : "";
            set =>
                SetSettingsValue(
                    () => Settings.Value.HierarchyPattern,
                    v =>
                    {
                        if (Settings.Value.HierarchyPattern == v)
                        {
                            return;
                        }

                        Settings.Value.HierarchyPattern = v;
                        _onHierarchyPanelDispatcher.Dispatch();
                    },
                    value
                );
        }

        private void SetSettingsValue<T>(Func<T> getter, Action<T> setter, T value)
        {
            if (!CheckSettingsInitialized())
            {
                return;
            }

            var isStruct = value?.GetType().IsValueType ?? false;
            if (isStruct && value.Equals(getter()))
            {
                return;
            }

            setter(value);
            Settings.ForceSave();

            _onChangedDispatcher.Dispatch();
        }

        internal string FilterPattern
        {
            get => Settings?.Ready ?? false ? Settings.Value.FilterPattern : "";
            set =>
                SetSettingsValue(
                    () => Settings.Value.FilterPattern,
                    v =>
                    {
                        if (Settings.Value.FilterPattern == v)
                        {
                            return;
                        }

                        Settings.Value.FilterPattern = v;
                        RebuildTree();
                    },
                    value
                );
        }

        internal string LogsPattern
        {
            get => Settings?.Ready ?? false ? Settings.Value.LogsPattern : "";
            set
            {
                SetSettingsValue(
                    () => Settings.Value.LogsPattern,
                    v =>
                    {
                        if (Settings.Value.LogsPattern == v)
                        {
                            return;
                        }

                        Settings.Value.LogsPattern = v;
                        _onChangedDispatcher.Dispatch();
                    },
                    value
                );
                UpdateLogsFilterRegex();
                OnLogMessagesVisibilityChanged?.Invoke();
            }
        }

        internal bool LogsRegex
        {
            get => (Settings?.Ready ?? false) && Settings.Value.LogsRegex;
            set
            {
                SetSettingsValue(
                    () => Settings.Value.LogsRegex,
                    v => Settings.Value.LogsRegex = v,
                    value
                );
                UpdateLogsFilterRegex();
                OnLogMessagesVisibilityChanged?.Invoke();
            }
        }

        internal HashSet<GeneralizedLogSeverity> HiddenLogSeverity
        {
            get => (Settings?.Ready ?? false ? Settings.Value.HiddenLogSeverity : null) ?? new HashSet<GeneralizedLogSeverity>();
            set
            {
                SetSettingsValue(
                    () => Settings.Value.HiddenLogSeverity ?? new HashSet<GeneralizedLogSeverity>(),
                    v =>
                    {
                        Settings.Value.HiddenLogSeverity = v;
                        Settings.ForceSave();
                        _onChangedDispatcher.Dispatch();
                    },
                    value
                );
                OnLogMessagesVisibilityChanged?.Invoke();
            }
        }

        internal string SelectedCategory
        {
            get
            {
                var v = Settings?.Ready ?? false;
                return v ? Settings.Value.SelectedCategory : null;
            }
            set =>
                SetSettingsValue(
                    () => Settings.Value.SelectedCategory,
                    v => Settings.Value.SelectedCategory = v,
                    value
                );
        }

        private void UpdateLogsFilterRegex()
        {
            var pattern = LogsPattern;
            try
            {
                _logsFilterRegex = LogsRegex
                    ? new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase)
                    : DevSuiteUtils.GetSmartSearchRegex(pattern);
            }
            catch (Exception)
            {
                _logsFilterRegex = DevSuiteUtils.NeverMatch;
            }
        }

        private bool CheckSettingsInitialized(bool silent = false)
        {
            if (Settings == null && !silent)
            {
                Debug.LogWarning("Settings are not initialized");
            }
            return Settings != null;
        }

        private LazyCache<string, CommandGroup> _getGroupByCategory;

        private static readonly List<TreeCategory> EmptyTree = new();

        private void RebuildTree()
        {
            _getGroupByCategory ??= new LazyCache<string, CommandGroup>(categoryId => new CommandGroup(DefaultGroupId, categoryId, -1f, null));

            if (Commands.Count <= 0)
            {
                Tree = EmptyTree;
                return;
            }

            var categoriesDict = new Dictionary<CommandCategory, Dictionary<CommandGroup, Dictionary<object, List<Command>>>>();
            var nullInstance = new object();

            foreach (var kvp in Commands)
            {
                var c = kvp.Value;
                var categoryKey = new CategoryKey(c.CategoryId);
                var groupKey = new GroupKey(c.GroupId, c.CategoryId);
                Categories.TryGetValue(categoryKey, out var category);
                Groups.TryGetValue(groupKey, out var group);

                if (category == null)
                {
                    if (group != null)
                    {
                        Categories.TryGetValue(categoryKey, out category);
                    }
                    category ??= _defaultCategory;
                }
                group ??= _getGroupByCategory.Get(category.Id);

                if (!categoriesDict.TryGetValue(category, out var groupsDict))
                {
                    groupsDict = new Dictionary<CommandGroup, Dictionary<object, List<Command>>>();
                    categoriesDict[category] = groupsDict;
                }

                if (!groupsDict.TryGetValue(group, out var instancesDict))
                {
                    instancesDict = new Dictionary<object, List<Command>>();
                    groupsDict[group] = instancesDict;
                }

                var targetKey = c.TargetInstance ?? nullInstance;
                if (!instancesDict.TryGetValue(targetKey, out var commandsList))
                {
                    commandsList = new List<Command>();
                    instancesDict[targetKey] = commandsList;
                }

                commandsList.Add(c);
            }

            var categoryList = new List<CommandCategory>();
            foreach (var kvp in categoriesDict)
            {
                categoryList.Add(kvp.Key);
            }
            categoryList.Sort();

            var resultList = new List<TreeCategory>(categoryList.Count);
            foreach (var category in categoryList)
            {
                var groupsDict = categoriesDict[category];
                var groupList = new List<CommandGroup>();
                foreach (var kvp in groupsDict)
                {
                    groupList.Add(kvp.Key);
                }
                groupList.Sort();

                var treeGroups = new List<TreeGroup>(groupList.Count);
                foreach (var group in groupList)
                {
                    var instancesDict = groupsDict[group];
                    var instanceList = new List<object>();
                    foreach (var kvp in instancesDict)
                    {
                        instanceList.Add(kvp.Key);
                    }

                    var treeCommands = new List<TreeCommandByInstance>(instanceList.Count);
                    foreach (var instance in instanceList)
                    {
                        var commandsList = instancesDict[instance];
                        commandsList.Sort();

                        var targetInstance = instance == nullInstance ? null : instance;
                        treeCommands.Add(new TreeCommandByInstance(targetInstance, commandsList.ToReadOnlyList()));
                    }
                    treeGroups.Add(new TreeGroup(group, treeCommands.ToReadOnlyList()));
                }
                resultList.Add(new TreeCategory(category, treeGroups.ToReadOnlyList()));
            }

            var result = resultList.ToReadOnlyList();

            foreach (var categoryGroup in result)
            {
                foreach (var groupList in categoryGroup.Groups)
                {
                    groupList.Group.AssignedToCategory = categoryGroup.Category;
                    foreach (var commandsByTargetInstance in groupList.Commands)
                    {
                        foreach (var command in commandsByTargetInstance.Commands)
                        {
                            command.AssignedToGroup = groupList.Group;
                            command.Units.Sort();
                        }
                    }
                }
            }

            for (var i = result.Count - 1; i >= 0; i--)
            {
                var groupsByCategory = result[i];

                if (!CheckVisibilityBySearchPattern(groupsByCategory.Category, null))
                {
                    for (var j = groupsByCategory.Groups.Count - 1; j >= 0; j--)
                    {
                        var commandsByGroupsAndInstances = groupsByCategory.Groups[j];

                        for (var k = commandsByGroupsAndInstances.Commands.Count - 1; k >= 0; k--)
                        {
                            var commands = commandsByGroupsAndInstances.Commands[k];
                            if (!CheckVisibilityBySearchPattern(commandsByGroupsAndInstances.Group, commands.TargetInstance))
                            {
                                for (var l = commands.Commands.Count - 1; l >= 0; l--)
                                {
                                    var command = commands.Commands[l];
                                    if (!CheckVisibilityBySearchPattern(command, null))
                                    {
                                        commands.Commands.AsEditable().RemoveAt(l);
                                    }
                                }

                                if (commands.Commands.Count <= 0)
                                {
                                    commandsByGroupsAndInstances.Commands.AsEditable().RemoveAt(k);
                                }
                            }
                        }

                        if (commandsByGroupsAndInstances.Commands.Count <= 0)
                        {
                            groupsByCategory.Groups.AsEditable().RemoveAt(j);
                        }
                    }

                    if (groupsByCategory.Groups.Count <= 0)
                    {
                        result.AsEditable().RemoveAt(i);
                    }
                }
            }

            var pinnedCategoryKey = new CategoryKey(PinnedCategoryId);
            Categories.Remove(pinnedCategoryKey);
            _categoryPinned ??= new CommandCategory(PinnedCategoryId, float.MaxValue, null);
            _groupPinned ??= new CommandGroup(DefaultGroupId, _categoryPinned.Id, default, default);
            Categories.Add(pinnedCategoryKey, _categoryPinned);

            var visiblePinnedCommands = new List<Command>();
            foreach (var i in GetPinnedCommands(true))
            {
                if (CheckVisibilityBySearchPattern(i, i.TargetInstance))
                {
                    visiblePinnedCommands.Add(i);
                }
            }

            result.AsEditable().Insert(
                0,
                new TreeCategory(
                    _categoryPinned,
                    new[]
                    {
                        new TreeGroup(
                            _groupPinned,
                            new[]
                            {
                                new TreeCommandByInstance(null as object, visiblePinnedCommands.ToReadOnlyList()),
                            }.ToReadOnlyList()
                        ),
                    }.ToReadOnlyList()
                )
            );

            Tree = result;

            if (SelectedCategory == null)
            {
                var startingCategory = Tree[0].Category;
                foreach (var treeCategory in Tree)
                {
                    if (!treeCategory.IsEmpty)
                    {
                        startingCategory = treeCategory.Category;
                        break;
                    }
                }
                SelectedCategory = startingCategory.Id;
            }

            _onChangedDispatcher.Dispatch();
        }

        internal void ExecuteButton(CommandUnitButton button)
        {
            try
            {
                if (!CheckUnitAvailability(button))
                {
                    Debug.LogWarning($"Button '{button.AssignedToCommand?.Id}' is no longer available");
                    return;
                }

                button.Action?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Exception while executing the button: {e}");
                if (!button.SuppressExceptions)
                {
                    throw;
                }
            }
        }

        internal List<CliCommandData> GetActiveCliCommands()
        {
            var list = new List<CliCommandData>();
            var seen = new HashSet<CommandUnitButton>();

            foreach (var kvp in Commands)
            {
                var command = kvp.Value;
                if (!CheckVisibilityByVisibilityFunction(command, null))
                {
                    continue;
                }

                foreach (var unit in command.Units)
                {
                    if (unit is CommandUnitButton button && CheckUnitAvailability(button) && seen.Add(button))
                    {
                        var cliCmd = button.CliCommand;
                        if (string.IsNullOrEmpty(cliCmd))
                        {
                            continue;
                        }

                        var paramUnits = command.Units
                            .OfType<CommandUnitButtonParameter>()
                            .Where(p => p.OwnerButton == button)
                            .OrderBy(p => p.ParameterIndex)
                            .ToList();

                        var desc = !string.IsNullOrEmpty(button.Description) ? button.Description : command.Description;
                        list.Add(new CliCommandData(cliCmd, button.Text, desc, button, command, paramUnits));
                    }
                }
            }

            return list
                .OrderBy(c => c.CliCommand, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(c => c.Priority)
                .ToList();
        }

        internal bool TryConvertStringToType(string str, Type targetType, out object result)
        {
            if (targetType == typeof(string))
            {
                result = str;
                return true;
            }

            if (str == null || str == NullRepresentation || (string.IsNullOrEmpty(str) && (targetType.IsNullable() || !targetType.IsValueType)))
            {
                if (targetType.IsNullable() || !targetType.IsValueType)
                {
                    result = null;
                    return true;
                }
            }

            var underlyingType = Nullable.GetUnderlyingType(targetType);
            var nonNullableType = underlyingType ?? targetType;

            var chainResult = GetAdaptersChain(typeof(string), targetType, true);
            if (chainResult.Steps != null && chainResult.Steps.Count > 0)
            {
                try
                {
                    object val = str;
                    foreach (var step in chainResult.Steps)
                    {
                        val = step.Convert(val, null);
                    }
                    if (val != null || targetType.IsNullable() || !targetType.IsValueType)
                    {
                        result = val;
                        return true;
                    }
                }
                catch
                {
                }
            }

            if (underlyingType != null)
            {
                var nonNullableChain = GetAdaptersChain(typeof(string), nonNullableType, true);
                if (nonNullableChain.Steps != null && nonNullableChain.Steps.Count > 0)
                {
                    try
                    {
                        object val = str;
                        foreach (var step in nonNullableChain.Steps)
                        {
                            val = step.Convert(val, null);
                        }
                        if (val != null)
                        {
                            result = val;
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            try
            {
                if (nonNullableType.IsEnum)
                {
                    if (Enum.TryParse(nonNullableType, str, true, out var enumVal))
                    {
                        result = enumVal;
                        return true;
                    }
                    result = null;
                    return false;
                }

                if (nonNullableType == typeof(bool))
                {
                    if (bool.TryParse(str, out var bVal))
                    {
                        result = bVal;
                        return true;
                    }
                    if (str == "1") { result = true; return true; }
                    if (str == "0") { result = false; return true; }
                    result = null;
                    return false;
                }

                if (nonNullableType == typeof(int) && int.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var iVal)) { result = iVal; return true; }
                if (nonNullableType == typeof(float) && float.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var fVal)) { result = fVal; return true; }
                if (nonNullableType == typeof(double) && double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var dVal)) { result = dVal; return true; }
                if (nonNullableType == typeof(uint) && uint.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var uVal)) { result = uVal; return true; }
                if (nonNullableType == typeof(long) && long.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var lVal)) { result = lVal; return true; }
                if (nonNullableType == typeof(ulong) && ulong.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var ulVal)) { result = ulVal; return true; }
                if (nonNullableType == typeof(short) && short.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var sVal)) { result = sVal; return true; }
                if (nonNullableType == typeof(ushort) && ushort.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var usVal)) { result = usVal; return true; }
                if (nonNullableType == typeof(byte) && byte.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var byVal)) { result = byVal; return true; }
                if (nonNullableType == typeof(sbyte) && sbyte.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var sbVal)) { result = sbVal; return true; }
            }
            catch
            {
            }

            result = null;
            return false;
        }

        internal void ExecuteCliCommand(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }

            var tokens = DevSuiteUtils.TokenizeCommandLine(input);
            if (tokens.Count == 0)
            {
                return;
            }

            var commandName = tokens[0];
            var userArgs = tokens.Skip(1).ToList();

            var activeCommands = GetActiveCliCommands();
            var match = activeCommands.FirstOrDefault(c => string.Equals(c.CliCommand, commandName, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                Debug.LogWarning($"unknown command '{commandName}'");
                return;
            }

            var button = match.Button;
            var parameters = match.Parameters;

            if (parameters.Count == 0)
            {
                if (userArgs.Count > 0)
                {
                    Debug.LogWarning($"incorrect arguments of command '{match.CliCommand}'");
                    return;
                }

                try
                {
                    ExecuteButton(button);
                    Debug.Log($"executed command '{match.CliCommand}'");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Exception while executing command '{match.CliCommand}': {e}");
                    if (!button.SuppressExceptions)
                    {
                        throw;
                    }
                }
                return;
            }

            if (userArgs.Count > parameters.Count)
            {
                Debug.LogWarning($"incorrect arguments of command '{match.CliCommand}'");
                return;
            }

            var convertedArgs = new object[parameters.Count];
            for (var i = 0; i < parameters.Count; i++)
            {
                var param = parameters[i];
                var pType = param.Type;

                if (i < userArgs.Count)
                {
                    var strArg = userArgs[i];
                    if (!TryConvertStringToType(strArg, pType, out var convertedValue))
                    {
                        Debug.LogWarning($"incorrect arguments of command '{match.CliCommand}'");
                        return;
                    }
                    convertedArgs[i] = convertedValue;
                }
                else
                {
                    var currentVal = param.GetValue?.Invoke();
                    if (currentVal == null && pType.IsValueType && Nullable.GetUnderlyingType(pType) == null)
                    {
                        Debug.LogWarning($"incorrect arguments of command '{match.CliCommand}'");
                        return;
                    }
                    convertedArgs[i] = currentVal;
                }
            }

            for (var i = 0; i < parameters.Count; i++)
            {
                if (i < userArgs.Count)
                {
                    parameters[i].SaveValue?.Invoke(convertedArgs[i]);
                }
            }

            try
            {
                ExecuteButton(button);

                if (userArgs.Count > 0)
                {
                    var formattedArgs = string.Join(" ", userArgs.Select(a => $"'{a}'"));
                    Debug.Log($"executed command '{match.CliCommand}' with arguments: {formattedArgs}");
                }
                else
                {
                    Debug.Log($"executed command '{match.CliCommand}'");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Exception while executing command '{match.CliCommand}': {e}");
                if (!button.SuppressExceptions)
                {
                    throw;
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static TimeSpan GetUnityTime()
        {
            return TimeSpan.FromSeconds(Time.unscaledTime);
        }

        internal static TimeSpan GetCurrentTime()
        {
            try
            {
                return GetUnityTime();
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        internal bool CheckVisibilityByVisibilityFunction(BaseCommandItem item, TimeSpan? time, bool ignoreTime = false)
        {
            if (item == _groupPinned || item == _categoryPinned || item.Id == PinnedMockId)
            {
                return true;
            }

            time ??= GetCurrentTime();

            if (item is CommandGroup group && !CheckVisibilityByVisibilityFunction(group.AssignedToCategory, time, ignoreTime))
            {
                return false;
            }

            if (item is Command command && !CheckVisibilityByVisibilityFunction(command.AssignedToGroup, time, ignoreTime))
            {
                return false;
            }

            if (!ignoreTime && item.NextVisibilityCheckTime != null && time < item.NextVisibilityCheckTime.Value)
            {
                return item.LastVisibility ?? false;
            }

            try
            {
                var result = item.Visibility?.Invoke() ?? true;
                item.UpdateVisibilityCheck(result, time.Value);

                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Exception while checking the visibility: {e}");
                return false;
            }
        }

        private bool CheckVisibilityBySearchPattern(BaseCommandItem item, object targetInstance)
        {
            var result = true;
            if (Settings.Ready && !string.IsNullOrEmpty(Settings.Value.FilterPattern))
            {
                var id = item is CommandGroup group ? group.GetFullName(targetInstance) : item.DisplayName;
                if (!CheckSearchPattern(id, Settings.Value.FilterPattern))
                {
                    result = false;
                }

                //if (!result && item is Command command && Settings.Value.PinnedItems.Any(i => i.Match(command)))
                //    result = true;
            }
            return result;
        }

        private (string pattern, Regex regex)? _cachedRegexForSearch;

        private bool CheckSearchPattern(string id, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return true;
            }

            if (_cachedRegexForSearch == null || _cachedRegexForSearch.Value.pattern != pattern)
            {
                _cachedRegexForSearch = (pattern, DevSuiteUtils.GetSmartSearchRegex(pattern));
            }
            return _cachedRegexForSearch.Value.regex.IsMatch(id);
        }

        internal T GetRepresentation<T>(CommandUnitValue unit, out string error, bool silent = false)
        {
            if (!CheckUnitAvailability(unit))
            {
                error = ErrorNotAvailable;
                return default;
            }

            return GetRepresentation<T>(unit.GetValue(), unit.Type, out error, silent, unit.SuppressExceptions, unit.Format);
        }

        internal T GetRepresentation<T>(object valueFrom, Type typeFrom, out string error, bool silent = false, bool suppressException = true, string format = null)
        {
            return (T)GetRepresentation(valueFrom, typeFrom, typeof(T), out error, silent, suppressException, format);
        }

        private object GetRepresentation(object valueFrom, Type typeFrom, Type typeTo, out string error, bool silent = false, bool suppressException = true, string format = null)
        {
            var chainResult = GetAdaptersChain(typeFrom, typeTo, false);
            if (chainResult.Steps == null)
            {
                error = chainResult.Error;
                return null;
            }

            if (valueFrom == null)
            {
                error = null;
                return null;
            }

            try
            {
                var val = valueFrom;
                foreach (var step in chainResult.Steps)
                {
                    val = step.Convert(val, null, format);
                    if (val == null)
                    {
                        error = null;
                        return null;
                    }
                }

                error = null;
                return val;
            }
            catch (Exception e)
            {
                if (!silent)
                {
                    Debug.LogWarning($"Exception when getting the string representation for the type '{valueFrom.GetType().Name}': {e}");
                }

                error = ErrorException;
                if (!suppressException)
                {
                    throw;
                }

                return null;
            }
        }

        internal void SetByRepresentation(CommandUnitValue unit, object value, out string error, Type representationType = null, bool silent = false)
        {
            representationType ??= value?.GetType() ?? typeof(string);

            var chainResult = GetAdaptersChain(representationType, unit.Type, silent);
            if (chainResult.Steps == null)
            {
                error = chainResult.Error;
                return;
            }

            if (!CheckUnitAvailability(unit))
            {
                error = ErrorNotAvailable;
                return;
            }

            var isNull = value == null;
            try
            {
                var val = value;
                val = ClampValueIfPossible(val, unit.ValuesRange);
                for (var i = 0; i < chainResult.Steps.Count; i++)
                {
                    var step = chainResult.Steps[i];
                    val = step.Convert(
                        val,
                        i == chainResult.Steps.Count - 1 ? unit.GetValue() : null
                    );

                    if (isNull && !step.Adapter.ModifiesExistingObject)
                    {
                        val = null;
                    }

                    val = ClampValueIfPossible(val, unit.ValuesRange);
                }

                unit.SaveValue.Invoke(val);
                error = null;
            }
            catch (Exception e)
            {
                if (!silent)
                {
                    Debug.LogWarning($"Exception when setting a value for the type '{unit.Type}': {e}");
                }

                error = ErrorException;
                if (!unit.SuppressExceptions)
                {
                    throw;
                }
            }
        }

        private object ClampValueIfPossible(object val, NumberRange<float>? range)
        {
            if (val?.GetType().IsNumber() == true && range != null)
            {
                var numericVal = Convert.ToDouble(val);
                var clamped = Math.Clamp(numericVal, range.Value.Min, range.Value.Max);
                if (numericVal != clamped)
                {
                    val = Convert.ChangeType(clamped, val.GetType());
                }
            }
            return val;
        }

        internal string GetVirtualParameterKey(string commandId, string groupId, string categoryId, MethodInfo method, int paramIndex, string paramName)
        {
            var declType = method.DeclaringType?.FullName ?? "";
            var methodName = method.Name;
            return $"{categoryId ?? ""}__{groupId ?? ""}__{commandId ?? methodName}__{declType}__{methodName}__{paramIndex}__{paramName}";
        }

        internal object GetVirtualParameterValue(string key, Type targetType, object defaultValue)
        {
            if (CheckSettingsInitialized(true) && Settings.Value.VirtualButtonParameters.TryGetValue(key, out var savedStr))
            {
                if (savedStr == null || savedStr == NullRepresentation)
                {
                    if (targetType.IsNullable() || !targetType.IsValueType)
                    {
                        return null;
                    }
                }
                else
                {
                    var chainResult = GetAdaptersChain(typeof(string), targetType, true);
                    if (chainResult.Steps != null)
                    {
                        try
                        {
                            object val = savedStr;
                            foreach (var step in chainResult.Steps)
                            {
                                val = step.Convert(val, null);
                            }
                            return val;
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"Failed to convert virtual parameter '{key}' from string '{savedStr}' to '{targetType.Name}': {e}");
                        }
                    }
                }
            }

            if (defaultValue != null)
            {
                if (targetType.IsAssignableFrom(defaultValue.GetType()))
                {
                    return defaultValue;
                }

                var chainResult = GetAdaptersChain(defaultValue.GetType(), targetType, true);
                if (chainResult.Steps != null)
                {
                    try
                    {
                        object val = defaultValue;
                        foreach (var step in chainResult.Steps)
                        {
                            val = step.Convert(val, null);
                        }
                        return val;
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
            {
                return Activator.CreateInstance(targetType);
            }

            return null;
        }

        internal void SetVirtualParameterValue(string key, Type targetType, object value)
        {
            if (!CheckSettingsInitialized())
            {
                return;
            }

            string strVal;
            if (value == null)
            {
                strVal = NullRepresentation;
            }
            else
            {
                strVal = GetRepresentation<string>(value, value.GetType(), out _, true);
                strVal ??= value.ToString();
            }

            Settings.Value.VirtualButtonParameters[key] = strVal;
            Settings.ForceSave();
        }

        internal bool CanConvert(Type a, Type b)
        {
            return GetAdaptersChain(a, b, true).Error == null;
        }

        internal void InvalidateCache()
        {
            _getAdapterFromCache?.Clear();
            _adapterChainsCache?.Clear();
            _getValuesProviderCache?.Clear();
            _getReadableMemberFromTypeCache?.Clear();
            _getTryGetValueFromTargetsCache?.Clear();
            _pinnedCommands = null;
        }

        private LazyCache<Type, CommandValueAdapter> _getAdapterFromCache;

        private LazyCache<Type, ValuesProviderFromChain> _resolvedValuesProviders;

        private ValuesProviderFromChain GetValuesProviderFromChain(Type type)
        {
            _resolvedValuesProviders ??= new LazyCache<Type, ValuesProviderFromChain>(
                t =>
                {
                    var provider = GetValuesProviderDirect(t);
                    if (provider != null)
                    {
                        return new ValuesProviderFromChain(t, t, provider);
                    }

                    var chain = GetAdaptersChain(t, typeof(string), false);
                    if (chain?.Steps != null)
                    {
                        foreach (var step in chain.Steps)
                        {
                            provider = GetValuesProviderDirect(step.Transition.To);
                            if (provider != null)
                            {
                                return new ValuesProviderFromChain(t, step.Transition.To, provider);
                            }
                        }
                    }
                    return null;
                }
            );
            return _resolvedValuesProviders.Get(type);
        }

        private LazyCache<Type, CommandValuesProvider> _getValuesProviderCache;

        private CommandValuesProvider GetValuesProviderDirect(Type type)
        {
            _getValuesProviderCache ??= new LazyCache<Type, CommandValuesProvider>(
                type =>
                {
                    if (type == null)
                    {
                        return null;
                    }

                    var inheritedTypes = type.GetAllInheritedTypes();
                    foreach (var inheritedType in inheritedTypes)
                    {
                        ValuesProviders.TryGetValue(inheritedType, out var valuesProvider);
                        if (valuesProvider != null)
                        {
                            return valuesProvider;
                        }
                    }

                    return null;
                }
            );
            return _getValuesProviderCache.Get(type);
        }

        private readonly struct AdapterChainStepTransition : IEquatable<AdapterChainStepTransition>
        {
            public Type From { get; }
            public Type To { get; }

            public AdapterChainStepTransition(Type from, Type to)
            {
                From = from;
                To = to;
            }

            public bool Equals(AdapterChainStepTransition other)
            {
                return From == other.From && To == other.To;
            }

            public override bool Equals(object obj)
            {
                return obj is AdapterChainStepTransition other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(From, To);
            }
        }

        private readonly struct AdapterChainStep
        {
            public AdapterChainStepTransition Transition { get; }
            public CommandValueAdapter Adapter { get; }

            public AdapterChainStep(AdapterChainStepTransition transition, CommandValueAdapter adapter)
            {
                Transition = transition;
                Adapter = adapter;
            }

            public object Convert(object objSource, object objDestination, string format = null)
            {
                if (objDestination == null && Adapter.ModifiesExistingObject)
                {
                    Debug.LogError($"Adapter '{Adapter.GetType().Name}' requires {nameof(objDestination)} to be not null");
                    return null;
                }
                return Adapter.Convert(objSource, Transition.To, objDestination, format);
            }
        }

        private class AdapterChainResult
        {
            public List<AdapterChainStep> Steps { get; }
            public string Error { get; }
            public string ErrorDetails { get; }

            public AdapterChainResult(List<AdapterChainStep> steps, string error, string errorDetails)
            {
                Steps = steps;
                Error = error;
                ErrorDetails = errorDetails;
            }
        }

        private List<Type> GetGenericAndNestedTypes(Type type, List<Type> result)
        {
            if (type == null || result.Contains(type))
            {
                return result;
            }

            result.Add(type);

            if (type.IsGenericType)
            {
                foreach (var arg in type.GetGenericArguments())
                {
                    GetGenericAndNestedTypes(arg, result);
                }
            }

            if (type.IsNested)
            {
                GetGenericAndNestedTypes(type.DeclaringType, result);
            }
            return result;
        }

        private LazyCache<AdapterChainStepTransition, AdapterChainResult> _adapterChainsCache;

        private AdapterChainResult GetAdaptersChain(Type typeFrom, Type typeTo, bool silent)
        {
            _adapterChainsCache ??= new LazyCache<AdapterChainStepTransition, AdapterChainResult>(
                t =>
                {
                    var from = t.From;
                    var to = t.To;
                    if (from == to)
                    {
                        return new AdapterChainResult(
                            new List<AdapterChainStep>(),
                            null,
                            null
                        );
                    }

                    if (from == null || to == null)
                    {
                        throw new ArgumentException();
                    }

                    try
                    {
                        var hints = GetGenericAndNestedTypes(to, new List<Type>());

                        var queue = new Queue<Type>();
                        var visitedBy = new Dictionary<Type, AdapterChainStep>();
                        visitedBy[from] = new AdapterChainStep(); //don't try to return to the starting point

                        queue.Enqueue(from);

                        while (queue.Count > 0)
                        {
                            var current = queue.Dequeue();

                            foreach (var adapter in CommandValueAdapters)
                            {
                                var destinations = adapter.GetPossibleDestinations(current, hints);
                                if (destinations == null)
                                {
                                    continue;
                                }

                                foreach (var dest in destinations)
                                {
                                    if (dest == null || dest == from || visitedBy.ContainsKey(dest))
                                    {
                                        continue;
                                    }

                                    visitedBy[dest] = new AdapterChainStep(new AdapterChainStepTransition(current, dest), adapter);
                                    if (dest == to)
                                    {
                                        var path = new List<AdapterChainStep>();
                                        var backtrack = to;
                                        while (backtrack != from)
                                        {
                                            var step = visitedBy[backtrack];
                                            path.Add(step);
                                            backtrack = step.Transition.From;
                                        }
                                        path.Reverse();
                                        return new AdapterChainResult(path, null, null);
                                    }

                                    queue.Enqueue(dest);
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        return new AdapterChainResult(null, ErrorException, $"Exception when getting the string representation for the type '{from}'. {e}");
                    }

                    return new AdapterChainResult(null, ErrorNoAdapter, $"No adapter chain found from type '{from.Name}' to '{to.Name}'");
                }
            );

            var res = _adapterChainsCache.Get(new AdapterChainStepTransition(typeFrom, typeTo));
            if (!silent && res.ErrorDetails != null)
            {
                Debug.LogWarning(res.ErrorDetails);
            }
            return res;
        }

        internal struct UnderlyingTypeInfo
        {
            public Type Type { get; }
            public bool IsNullable { get; }

            public UnderlyingTypeInfo(Type type, bool isNullable)
            {
                Type = type;
                IsNullable = isNullable;
            }
        }

        internal UnderlyingTypeInfo? GetUnderlyingPrimitiveType(CommandUnitValue unit, bool silent)
        {
            if (IsPrimitive(unit.Type))
            {
                return new UnderlyingTypeInfo(unit.Type, unit.Type.IsNullable());
            }

            var chainResult = GetAdaptersChain(unit.Type, typeof(string), silent);
            if (!string.IsNullOrEmpty(chainResult.Error))
            {
                return null;
            }

            var isNullablePrevious = unit.Type.IsNullable();
            foreach (var step in chainResult.Steps)
            {
                if (IsPrimitive(step.Transition.To))
                {
                    return new UnderlyingTypeInfo(step.Transition.To, isNullablePrevious || step.Transition.To.IsNullable());
                }

                isNullablePrevious |= step.Transition.To.IsNullable();
            }

            return null;
        }

        internal bool HasUnderlyingNullableType(CommandUnitValue unit)
        {
            if (unit.Type.IsNullable())
            {
                return true;
            }

            var chainResult = GetAdaptersChain(unit.Type, typeof(string), true);
            if (!string.IsNullOrEmpty(chainResult.Error))
            {
                return false;
            }

            foreach (var adapter in chainResult.Steps)
            {
                if (adapter.Transition.From.IsNullable())
                {
                    return true;
                }

                if (adapter.Transition.From.IsValueType)
                {
                    return false;
                }
            }

            return true;
        }

        internal void ValidateCommandUnit(BaseCommandUnit unit)
        {
            switch (unit)
            {
                case CommandUnitValue dial:
                    var underlyingType = GetUnderlyingPrimitiveType(dial, true)?.Type;
                    if (dial.ValuesRange != null && !(underlyingType?.IsNumber() ?? false))
                    {
                        dial.ValuesRange = null;
                        Debug.LogWarning("ValuesRange is supported for anything but numbers");
                    }

                    if (dial.ValuesRange != null && dial.ValuesRange.Value.Min >= dial.ValuesRange.Value.Max)
                    {
                        dial.ValuesRange = null;
                        Debug.LogWarning($"Min value '{dial.ValuesRange.Value.Min}' must be lower than max value '{dial.ValuesRange.Value.Max}'");
                    }

                    if (dial.ScaleType != ScaleType.Linear && dial.ValuesRange == null)
                    {
                        dial.ScaleType = ScaleType.Linear;
                        Debug.LogWarning("ScaleType can not be used without specifying ValuesRange");
                    }

                    break;
            }
        }

        internal bool CheckUnitAvailability(BaseCommandUnit unit)
        {
            var command = unit.AssignedToCommand;
            return command == null || CheckVisibilityByVisibilityFunction(command, null);
        }

        private bool IsPrimitive(Type type)
        {
            return type.IsPrimitive || type.IsEnum || type == typeof(string);
        }

        private static readonly GeneralizedLogSeverity[] LogTypeToGeneralLogSeverity = new GeneralizedLogSeverity[(int)LogType.Exception + 1].With(
            l =>
            {
                l[(int)LogType.Error] = GeneralizedLogSeverity.Error;
                l[(int)LogType.Assert] = GeneralizedLogSeverity.Error;
                l[(int)LogType.Warning] = GeneralizedLogSeverity.Warning;
                l[(int)LogType.Log] = GeneralizedLogSeverity.Ordinary;
                l[(int)LogType.Exception] = GeneralizedLogSeverity.Error;
            }
        );

        private void HandleUnityLog(string message, string stackTrace, LogType type)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var severity = LogTypeToGeneralLogSeverity[(int)type];
            var msg = new LogMessageData(severity, message.Trim(), null, stackTrace.Trim(), DateTime.Now);
            lock (_allLogMessages)
            {
                _allLogMessages.Add(msg);
            }
            OnLogMessagesMessageAdded?.Invoke(msg);
        }

        public string GetAllLogsText()
        {
            lock (_allLogMessages)
            {
                return string.Join("\n", _allLogMessages.Select(m => m.MessageAndCallStack()));
            }
        }

        public void ClearLogs()
        {
            lock (_allLogMessages)
            {
                _allLogMessages.Clear();
            }
            OnLogMessagesChanged?.Invoke();
        }

        public void ClearSettings()
        {
            _savedPrefs.Clear();
            Settings.Value = new PersistentSettings();
            Settings.ForceSave();
            RebuildTree();
            foreach (var provider in _performancePanelProviders)
            {
                OnPerformanceGraphCollapsedChanged?.Invoke(provider, IsPerformanceGraphCollapsed(provider));
            }
            _onChangedDispatcher.Dispatch();
        }

        private void Subscribe()
        {
            Unsubscribe();

#if ENABLE_INPUT_SYSTEM
            InputSystem.onEvent += HandleNewInputSystemEvent;
#endif
            StartUpdateLoop();

            OnApiCalled += HandleApiCalled;
            try
            {
                Application.logMessageReceivedThreaded += HandleUnityLog;
            }
            catch
            {
            }
        }

        private void Unsubscribe()
        {
            OnApiCalled -= HandleApiCalled;
            try
            {
                Application.logMessageReceivedThreaded -= HandleUnityLog;
            }
            catch
            {
            }

            if (_updateCoroutine == null)
            {
                return;
            }
#if ENABLE_INPUT_SYSTEM
            InputSystem.onEvent -= HandleNewInputSystemEvent;
#endif
            if (_coroutineStarter != null)
            {
                _coroutineStarter.StopCoroutine(_updateCoroutine);
            }
            _updateCoroutine = null;
        }

#if ENABLE_INPUT_SYSTEM
        private void HandleNewInputSystemEvent(InputEventPtr eventPointer, InputDevice device)
        {
            if (device is Keyboard keyboard)
            {
                foreach (var control in keyboard.allControls)
                {
                    if (control is KeyControl key)
                    {
                        if (key.isPressed)
                        {
                            if (_holdKeys.Add(key.keyCode))
                            {
                                ExecuteButtonsByShortcutsIfNeeded();
                            }
                        }
                        else
                        {
                            _holdKeys.Remove(key.keyCode);
                        }
                    }
                }
            }
        }
#endif

        private void StartUpdateLoop()
        {
            if (_updateCoroutine != null)
            {
                return;
            }

            _updateCoroutine = _coroutineStarter.StartCoroutine(UpdateLoopCoroutine());
        }

        private IEnumerator UpdateLoopCoroutine()
        {
            while (true)
            {
                foreach (var provider in PerformancePanelProviders)
                {
                    if (IsPerformanceGraphCollapsed(provider))
                    {
                        continue;
                    }
                    provider.Process();
                }

                if (_selectedGameObjects.Count > 0)
                {
                    var countBefore = _selectedGameObjects.Count;
                    UpdateInspectorAutoPause();
                    if (_selectedGameObjects.Count != countBefore)
                    {
                        _onChangedDispatcher.Dispatch();
                    }
                }

                OnEveryFrameDispatcher.Dispatch();
#if !ENABLE_INPUT_SYSTEM
                if (Input.anyKeyDown)
                {
                    ExecuteButtonsByShortcutsIfNeeded();
                }
#endif
                yield return null;
            }
        }

        private void ExecuteButtonsByShortcutsIfNeeded()
        {
            var time = TimeSpan.FromSeconds(Time.unscaledTime);

            foreach (var command in Commands.Values)
            {
                if (!CheckVisibilityByVisibilityFunction(command, time))
                {
                    continue;
                }

                foreach (var unit in command.Units)
                {
                    if (unit is CommandUnitButton button)
                    {
                        if (button.Shortcut == null)
                        {
                            continue;
                        }

                        foreach (var key in button.Shortcut)
                        {
#if ENABLE_INPUT_SYSTEM
                            if (!_holdKeys.Contains(key))
                            {
                                goto nextUnit;
                            }
#else
                            if (!Input.GetKey(key))
                            {
                                goto nextUnit;
                            }
#endif
                        }

                        ExecuteButton(button);
                        nextUnit: ;
                    }
                }
            }
        }

        internal List<TreeCategory> GetPinnedList()
        {
            return new List<TreeCategory>
            {
                new(
                    new CommandCategory(PinnedMockId, 0, null),
                    new List<TreeGroup>
                    {
                        new(
                            new CommandGroup(PinnedMockId, PinnedMockId, 0, null),
                            new List<TreeCommandByInstance>
                            {
                                new(null, null),
                            }
                        ),
                    }
                ),
            };
        }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    [MessagePackObject(AllowPrivate = true)]
    [Serializable]
    [DataContract]
    internal partial class PersistentSettings
    {
        [DataMember][MemoryPackOrder(0)][Key(0)] public List<PinnedItem> PinnedItems { get; set; } = new();
        [DataMember][MemoryPackOrder(1)][Key(1)] public string FilterPattern { get; set; }
        [DataMember][MemoryPackOrder(2)][Key(2)] public string LogsPattern { get; set; }
        [DataMember][MemoryPackOrder(3)][Key(3)] public bool MetricsVisible { get; set; }
        [DataMember][MemoryPackOrder(4)][Key(4)] public bool CommandsVisible { get; set; }
        [DataMember][MemoryPackOrder(5)][Key(5)] public bool PinnedCommandsVisible { get; set; }
        [DataMember][MemoryPackOrder(6)][Key(6)] public bool LogsVisible { get; set; }
        [DataMember][MemoryPackOrder(7)][Key(7)] public bool PanelExpanded { get; set; }
        [DataMember][MemoryPackOrder(8)][Key(8)] public bool LogsRegex { get; set; }
        [DataMember][MemoryPackOrder(9)][Key(9)] public string SelectedCategory { get; set; }
        [DataMember][MemoryPackOrder(10)][Key(10)] public HashSet<GeneralizedLogSeverity> HiddenLogSeverity { get; set; } = new();
        [DataMember][MemoryPackOrder(11)][Key(11)] public List<CollapsedGroupItem> CollapsedGroups { get; set; } = new();
        [DataMember][MemoryPackOrder(12)][Key(12)] public bool HierarchyVisible { get; set; }
        [DataMember][MemoryPackOrder(13)][Key(13)] public bool InspectorVisible { get; set; }
        [DataMember][MemoryPackOrder(14)][Key(14)] public bool HierarchySearchRegex { get; set; }
        [DataMember][MemoryPackOrder(15)][Key(15)] public bool HierarchySearchByName { get; set; } = true;
        [DataMember][MemoryPackOrder(16)][Key(16)] public bool HierarchySearchByType { get; set; } = true;
        [DataMember][MemoryPackOrder(17)][Key(17)] public bool HierarchyKeepDimmed { get; set; } = true;
        [DataMember][MemoryPackOrder(18)][Key(18)] public Dictionary<string, bool> PerformanceGraphCollapsedState { get; set; } = new();
        [DataMember][MemoryPackOrder(19)][Key(19)] public bool InspectorAutoRefresh { get; set; }
        [DataMember][MemoryPackOrder(20)][Key(20)] public bool InspectorAutoPause { get; set; } = true;
        [DataMember][MemoryPackOrder(21)][Key(21)] public string HierarchyPattern { get; set; }
        [DataMember][MemoryPackOrder(22)][Key(22)] public Dictionary<string, string> VirtualButtonParameters { get; set; } = new();

        public void InitializeDefaultsIfNeeded()
        {
            PinnedItems ??= new();
            PerformanceGraphCollapsedState ??= new();
            CollapsedGroups ??= new();
            HiddenLogSeverity ??= new();
            VirtualButtonParameters ??= new();
        }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    [MessagePackObject(AllowPrivate = true)]
    [Serializable]
    [DataContract]
    internal partial class CollapsedGroupItem
    {
        [DataMember][MemoryPackOrder(0)][Key(0)] public string GroupId { get; set; }
        [DataMember][MemoryPackOrder(1)][Key(1)] public string CategoryId { get; set; }
        [DataMember][MemoryPackOrder(2)][Key(2)] public bool Collapsed { get; set; }

        public CollapsedGroupItem(string groupId, string categoryId, bool collapsed) : this()
        {
            GroupId = groupId;
            CategoryId = categoryId;
            Collapsed = collapsed;
        }

        [MemoryPackConstructor] public CollapsedGroupItem() { }

        public bool Same(CollapsedGroupItem other)
        {
            return other.GroupId == GroupId && other.CategoryId == CategoryId;
        }
    }

    [MemoryPackable(GenerateType.VersionTolerant)]
    [MessagePackObject(AllowPrivate = true)]
    [Serializable]
    [DataContract]
    internal partial class PinnedItem
    {
        [DataMember][MemoryPackOrder(0)][Key(0)] public string CommandId { get; set; }
        [DataMember][MemoryPackOrder(1)][Key(1)] public string GroupId { get; set; }
        [DataMember][MemoryPackOrder(2)][Key(2)] public string CategoryId { get; set; }

        public PinnedItem(Command command) : this()
        {
            CommandId = command.Id;
            GroupId = command.GroupId;
            CategoryId = command.CategoryId;
        }

        [MemoryPackConstructor] public PinnedItem() { }

        public bool Same(PinnedItem other)
        {
            return other.CommandId == CommandId &&
                   other.GroupId == GroupId &&
                   other.CategoryId == CategoryId;
        }

        public bool Match(Command other)
        {
            return other.Id == CommandId && other.GroupId == GroupId && other.CategoryId == CategoryId;
        }
    }

    internal struct CategoryKey : IEquatable<CategoryKey>
    {
        public string Id;

        public CategoryKey(string id)
        {
            Id = id;
        }

        public bool Equals(CategoryKey other)
        {
            return Id == other.Id;
        }

        public override bool Equals(object obj)
        {
            return obj is CategoryKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Id != null ? Id.GetHashCode() : 0;
        }
    }

    internal struct GroupKey : IEquatable<GroupKey>
    {
        public string Id;
        public string CategoryId;

        public GroupKey(string id, string categoryId)
        {
            Id = id;
            CategoryId = categoryId;
        }

        public bool Equals(GroupKey other)
        {
            return Id == other.Id && CategoryId == other.CategoryId;
        }

        public override bool Equals(object obj)
        {
            return obj is GroupKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, CategoryId);
        }
    }

    internal struct CommandKey : IEquatable<CommandKey>
    {
        public string Id;
        public string GroupId;
        public string CategoryId;
        public object Instance;

        public CommandKey(string id, string groupId, string categoryId, object instance)
        {
            Id = id;
            GroupId = groupId;
            CategoryId = categoryId;
            Instance = instance;
        }

        public bool Equals(CommandKey other)
        {
            return Id == other.Id && GroupId == other.GroupId && CategoryId == other.CategoryId && Equals(Instance, other.Instance);
        }

        public override bool Equals(object obj)
        {
            return obj is CommandKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, GroupId, CategoryId, Instance);
        }
    }

    internal struct TreeCategory
    {
        public CommandCategory Category { get; }
        public IReadOnlyList<TreeGroup> Groups { get; }
        public bool IsEmpty { get; }

        public TreeCategory(CommandCategory category, IReadOnlyList<TreeGroup> groups)
        {
            Category = category;
            Groups = groups;
            IsEmpty = true;
            foreach (var group in Groups)
            {
                if (!group.IsEmpty)
                {
                    IsEmpty = false;
                    break;
                }
            }
        }
    }

    internal struct TreeGroup
    {
        public CommandGroup Group { get; }
        public IReadOnlyList<TreeCommandByInstance> Commands { get; }
        public bool IsEmpty { get; }

        public TreeGroup(CommandGroup group, IReadOnlyList<TreeCommandByInstance> commands)
        {
            Group = group;
            Commands = commands;
            IsEmpty = true;
            foreach (var command in commands)
            {
                if (!command.IsEmpty)
                {
                    IsEmpty = false;
                    break;
                }
            }
        }
    }

    internal struct TreeCommandByInstance
    {
        public object TargetInstance { get; }
        public IReadOnlyList<Command> Commands { get; private set; }
        public bool IsEmpty { get; }

        public TreeCommandByInstance(object targetInstance, IReadOnlyList<Command> commands)
        {
            TargetInstance = targetInstance;
            Commands = commands;
            IsEmpty = !(commands?.Count > 0);
        }

        public void ChangeCommands(IReadOnlyList<Command> commands)
        {
            Commands = commands;
        }
    }

    internal class CliCommandData
    {
        public string CliCommand { get; }
        public string Title { get; }
        public string Description { get; }
        public CommandUnitButton Button { get; }
        public Command Command { get; }
        public IReadOnlyList<CommandUnitButtonParameter> Parameters { get; }
        public float Priority => Button?.Priority ?? 0f;
        public string CategoryName => Command?.AssignedToGroup?.AssignedToCategory?.DisplayName ?? (Command?.CategoryId != null ? DevSuiteUtils.TrimName(Command.CategoryId) : "Default");
        public string GroupName => Command?.AssignedToGroup?.DisplayName ?? (Command?.GroupId != null ? DevSuiteUtils.TrimName(Command.GroupId) : "Default");
        public string CommandId => !string.IsNullOrEmpty(Command?.DisplayName) ? Command.DisplayName : (!string.IsNullOrEmpty(Command?.Id) ? DevSuiteUtils.TrimName(Command.Id) : "Default");

        public CliCommandData(string cliCommand, string title, string description, CommandUnitButton button, Command command, IReadOnlyList<CommandUnitButtonParameter> parameters)
        {
            CliCommand = cliCommand;
            Title = title;
            Description = description;
            Button = button;
            Command = command;
            Parameters = parameters ?? Array.Empty<CommandUnitButtonParameter>();
        }
    }

    internal class LogMessageData
    {
        public GeneralizedLogSeverity Level { get; }
        public string Message { get; }
        public object Caller { get; }
        public string CallStack { get; }
        public DateTime Timestamp { get; }
        public bool Expanded { get; set; }

        public LogMessageData(GeneralizedLogSeverity level, string message, object caller, string callStack, DateTime timestamp)
        {
            Level = level;
            Message = message;
            Caller = caller;
            CallStack = callStack;
            Timestamp = timestamp;
        }

        public string MessageAndCallStack()
        {
            var cs = string.IsNullOrEmpty(CallStack) ? "" : $"\n{CallStack}";
            return $"[{Timestamp:HH:mm:ss.fff}] {Message}{cs}";
        }
    }

    internal enum GeneralizedLogSeverity
    {
        Ordinary,
        Warning,
        Error,
    }
}