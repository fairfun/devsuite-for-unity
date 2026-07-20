using System;
using System.Collections;
using System.Collections.Generic;
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
        void RegisterPerformancePanelGraph(BaseGraphDataProvider provider);
        void SetPerformanceReferenceValue<T>(Func<double?> referenceValueProvider) where T : BaseGraphDataProvider;
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

        public CommandAttributesParser AttributesParser { get; private set; }
        public DevSuiteCommandsApi CommandsApi { get; private set; }

        public IDisposable SuspendEvents(object requestor)
        {
            return Block.SetAndTrack(true, 1, requestor);
        }

        internal static string PinnedMockId { get; set; } = "<Default$Pinned>";
        internal static string DefaultGroupId { get; set; } = "Default";
        internal static string PinnedCategoryId { get; set; } = "Pinned";
        internal static string NullRepresentation { get; set; } = "null";

        internal ValueStack<bool> Block { get; } = new();

        private event Action OnApiCalled;
        internal event Action OnChanged;
        internal event Action OnEveryFrame;
        internal event Action OnPerformancePanelChanged;
        internal event Action OnLogMessagesChanged;
        internal event Action<LogMessageData> OnLogMessagesMessageAdded;
        internal event Action OnLogMessagesVisibilityChanged;
        private readonly BlockableDispatcher _apiCalledDispatcher;
        private readonly BlockableDispatcher _onChangedDispatcher;
        private readonly BlockableDispatcher _onEveryFrameDispatcher;
        private readonly BlockableDispatcher _onPerformancePanelDispatcher;
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

        private readonly Dictionary<Type, Func<double?>> _performanceGraphReferenceValues = new();

        public Func<string> BuildVersionToDisplay { get; set; } = () => "v" + Application.version;

        private readonly List<GameObject> _selectedGameObjects = new();
        public IReadOnlyList<GameObject> SelectedGameObjects => _selectedGameObjects;

        public void SetSelectedGameObjects(IEnumerable<GameObject> gameObjects)
        {
            _selectedGameObjects.Clear();
            if (gameObjects != null)
            {
                _selectedGameObjects.AddRange(gameObjects);
            }
            _onChangedDispatcher.Dispatch();
        }

        public void ToggleSelectedGameObject(GameObject go)
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
                _onChangedDispatcher.Dispatch();
            }
        }

        private readonly CommandCategory _defaultCategory = new(DefaultGroupId, -1f, null);
        internal int RegistrationOrderCounter { get; set; }

        private ISavedPrefs _savedPrefs;

        internal IReadOnlyList<TreeCategory> Tree { get; private set; }

        private SavedPrefsProperty<PersistentSettings> Settings { get; set; }

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

            Reset();

            Application.logMessageReceivedThreaded += HandleUnityLog; // start collecting messages already, even though everything else awaits to be initialized yet
        }

        public bool Disposed { get; private set; }

        public void Dispose()
        {
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
            if (_initialized)
            {
                throw new Exception("Already initialized. If you need to reinitialize call Reset() before.");
            }

            _initialized = true;
            _coroutineStarter = coroutineStarter;
            _savedPrefs = savedPrefs ?? SavedPrefs.Factory.Invoke("DevSuiteContext.Default");
            Settings = new SavedPrefsProperty<PersistentSettings>("DevSuiteContext_Settings", new PersistentSettings(), true, _savedPrefs);
            _savedPrefs?.EnsureReady().Wait();

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

            RegisterPerformancePanelGraph(new FrameTimeGraphDataProvider());
            RegisterPerformancePanelGraph(new SystemRamGraphDataProvider());
            RegisterPerformancePanelGraph(new DrawCallsCountDataProvider());

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

        public void RegisterPerformancePanelGraph(BaseGraphDataProvider provider)
        {
            if (_performanceGraphReferenceValues.TryGetValue(provider.GetType(), out var refValue))
            {
                provider.ReferenceValueProvider = refValue;
            }
            _performancePanelProviders.Add(provider);
            _onPerformancePanelDispatcher.Dispatch();
        }

        public void SetPerformanceReferenceValue<T>(Func<double?> referenceValueProvider) where T : BaseGraphDataProvider
        {
            _performanceGraphReferenceValues[typeof(T)] = referenceValueProvider;
            foreach (var provider in _performancePanelProviders)
            {
                if (provider is T p)
                {
                    p.ReferenceValueProvider = referenceValueProvider;
                }
            }
        }

        internal double? GetPerformancePanelGraphReferenceValue<T>() where T : BaseGraphDataProvider
        {
            if (_performanceGraphReferenceValues.TryGetValue(typeof(T), out var refValue))
            {
                return refValue?.Invoke();
            }
            foreach (var provider in _performancePanelProviders)
            {
                if (provider is T p)
                {
                    return p.ReferenceValueProvider?.Invoke();
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

        internal bool CheckVisibilityByVisibilityFunction(BaseCommandItem item, TimeSpan? time, bool ignoreTime = false)
        {
            if (item == _groupPinned || item == _categoryPinned || item.Id == PinnedMockId)
            {
                return true;
            }

            time ??= TimeSpan.FromSeconds(Time.unscaledTime);

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

            return GetRepresentation<T>(unit.GetValue(), unit.Type, out error, silent, unit.SuppressExceptions);
        }

        internal T GetRepresentation<T>(object valueFrom, Type typeFrom, out string error, bool silent = false, bool suppressException = true)
        {
            return (T)GetRepresentation(valueFrom, typeFrom, typeof(T), out error, silent, suppressException);
        }

        private object GetRepresentation(object valueFrom, Type typeFrom, Type typeTo, out string error, bool silent = false, bool suppressException = true)
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
                    val = step.Convert(val, null);
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

            public object Convert(object objSource, object objDestination)
            {
                if (objDestination == null && Adapter.ModifiesExistingObject)
                {
                    Debug.LogError($"Adapter '{Adapter.GetType().Name}' requires {nameof(objDestination)} to be not null");
                    return null;
                }
                return Adapter.Convert(objSource, Transition.To, objDestination);
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
            Application.logMessageReceivedThreaded += HandleUnityLog;
        }

        private void Unsubscribe()
        {
            OnApiCalled -= HandleApiCalled;
            Application.logMessageReceivedThreaded -= HandleUnityLog;

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
                for (var i = 0; i < PerformancePanelProviders.Count; i++) // foreach here causes allocations
                {
                    PerformancePanelProviders[i].Process();
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