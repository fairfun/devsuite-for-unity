using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Linq;
using UnityEngine;

namespace Ff.DevSuite.Commands.Attributes
{
    public class CommandAttributesParser
    {
        private readonly DevSuiteContext _context;

        private readonly Dictionary<MemberInfo, CommandCategoryAttribute> _categoriesForMembers = new();
        private readonly Dictionary<MemberInfo, CommandGroupAttribute> _groupsForMembers = new();
        private readonly Dictionary<(MemberInfo member, object targetInstance), CommandAttribute> _commandsForMembers = new();
        private readonly Dictionary<CommandUnitAttribute, CommandAttribute> _commandsForUnits = new();
        private readonly HashSet<object> _registeredItems = new();

        private readonly Dictionary<string, CommandCategoryAttribute> _categoriesByIds = new();
        private readonly Dictionary<(string categodyId, string groupId), CommandGroupAttribute> _groupsByIds = new();
        private readonly Dictionary<object, CommandAttribute> _commandsByRef = new();

        public bool SuppressWarnings { get; set; } = false;

        public List<Regex> ExcludeClasses = new()
        {
            //new Regex(@"^System\.", RegexOptions.Compiled),
        };

        public CommandAttributesParser(DevSuiteContext context)
        {
            _context = context;
        }

        /// <summary>Registers all attributes form the specified assemblies. Consider running it after registering your custom adapters and providers for better experience.</summary>
        /// <param name="assemblies">It's recommended specifying your game assembly (i.e. Assembly.GetAssembly(typeof(SomeClassOfYours)). Otherwise, a broader set of assemblies will be checked, and that is slow.</param>
        public void RegisterStatic(IList<Assembly> assemblies = null)
        {
            if (assemblies == null)
            {
                //fallback to all non-system assemblies
                var filterOutCommonSystemAssemblies = new Regex(@"\.Editor\b|(^(System|netstandard|mscorlib|Unity|UnityEngine|UnityEditor|System|Mono|Microsoft|Ff|VContainer|UniTask|MemoryPack|MessagePack|nunit|Grpc|Newtonsoft|NuGetForUnity|Bee|JetBrains|Anonymously Hosted DynamicMethods Assembly|ExCSS|DOTween|LitMotion|Cysharp|Coffee|TriInspector|MagicOnion|DevSuite|DevSuiteScenes)[,\.])");
                assemblies = AppDomain.CurrentDomain.GetAssemblies();
                assemblies = assemblies.Where(a => !filterOutCommonSystemAssemblies.IsMatch(a.FullName)).ToArray();
            }

            using var _ = _context.Block.SetAndTrack(true, 1, this);

            DoForEveryAttributeType(
                t =>
                {
                    foreach (var assembly in assemblies)
                    {
                        try
                        {
                            foreach (var type in assembly.DefinedTypes)
                            {
                                try
                                {
                                    Register(type, t);
                                }
                                catch (Exception e)
                                {
                                    Debug.LogWarning($"{nameof(RegisterStatic)}: Exception while registering type '{type?.GetType().Name}':\n{e}");
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"{nameof(RegisterStatic)}: Exception while registering assembly '{assembly?.FullName}'\n{e}");
                        }
                    }
                }
            );
        }

        /// <summary></summary>
        /// <param name="assemblies">Recommended specifying your game assembly (i.e. Assembly.GetAssembly(typeof(SomeClassOfYours)). Otherwise, every single assembly will be checked, and that is slow.</param>
        public void UnregisterStatic(IList<Assembly> assemblies = null)
        {
            assemblies ??= AppDomain.CurrentDomain.GetAssemblies();

            using var _ = _context.Block.SetAndTrack(true, 1, this);

            DoForEveryAttributeType(
                t =>
                {
                    foreach (var assembly in assemblies)
                    foreach (var type in assembly.DefinedTypes)
                        Unregister(type, t);
                }
            );
        }

        public void RegisterStatic(Type type)
        {
            using var _ = _context.Block.SetAndTrack(true, 1, this);

            DoForEveryAttributeType(t => Register(type, t, null, false));
        }

        private readonly object _unregisterStaticTypeLock = new();

        public void UnregisterStatic(Type type)
        {
            using var _ = _context.Block.SetAndTrack(true, 1, this);

            DoForEveryAttributeType(t => Unregister(type, t));
        }

        public void RegisterInstance(object instance)
        {
            using var _ = _context.Block.SetAndTrack(true, 1, this);

            DoForEveryAttributeType(t => Register(instance.GetType(), t, instance, false));
        }

        public void UnregisterInstance(object instance)
        {
            using var _ = _context.Block.SetAndTrack(true, 1, this);

            DoForEveryAttributeType(t => Unregister(instance.GetType(), t, instance));
        }

        private readonly object _registerBlock = new();

        private void Register(Type type, Type attributeType, object instance = null, bool useExcludeRules = true)
        {
            if (type.IsInterface)
                return;

            if (useExcludeRules)
            {
                foreach (var excludeRegex in ExcludeClasses)
                {
                    if (excludeRegex.IsMatch(type.FullName))
                    {
                        return;
                    }
                }
            }

            using var _ = _context.Block.SetAndTrack(true, 1, _registerBlock);

            var attributes = GetAttributesFor(type, attributeType, instance == null);
            if (attributes.Count <= 0)
                return;

            var members = new HashSet<MemberInfo>();
            if (attributeType == typeof(CommandCategoryAttribute))
            {
                members.Clear();
                foreach (var (member, attribute) in attributes)
                {
                    var attributeCategory = attribute as CommandCategoryAttribute;
                    attributeCategory.Id ??= member.Name;

                    Merge(_categoriesByIds, attributeCategory, a => a.Id);
                    _categoriesForMembers[member] = _categoriesByIds[attributeCategory.Id];

                    members.Add(member);
                }

                foreach (var member in members)
                {
                    var category = _categoriesForMembers[member];
                    if (_registeredItems.Contains(category))
                        continue;
                    _registeredItems.Add(category);

                    _context.AddCategory(
                        new CommandCategory(
                            category.Id,
                            category.Priority,
                            GetValueFunction<bool>(type, category.VisibilityFunctionName, instance)
                        ).WithLineNumber(category.LineNumber)
                        .WithDescription(category.Description)
                        .WithDisplayName(category.DisplayName)
                        .WithColor(ParseColor(category.Color)),
                        SuppressWarnings
                    );
                }
            }

            if (attributeType == typeof(CommandGroupAttribute))
            {
                members.Clear();
                foreach (var (member, attribute) in attributes)
                {
                    var attributeGroup = attribute as CommandGroupAttribute;
                    attributeGroup.Id ??= member.Name;

                    if (string.IsNullOrEmpty(attributeGroup.CategoryId))
                    {
                        var possibleCategory = _categoriesForMembers.GetValueOrDefault(member) ?? _categoriesForMembers.GetValueOrDefault(type);
                        attributeGroup.CategoryId = possibleCategory?.Id;
                    }

                    Merge(_groupsByIds, attributeGroup, a => (a.CategoryId, a.Id));
                    _groupsForMembers[member] = _groupsByIds[(attributeGroup.CategoryId, attributeGroup.Id)];

                    members.Add(member);
                }

                foreach (var member in members)
                {
                    var group = _groupsForMembers[member];
                    if (_registeredItems.Contains(group))
                        continue;
                    _registeredItems.Add(group);

                    _context.AddGroup(
                        new CommandGroup(
                            group.Id,
                            group.CategoryId,
                            group.Priority,
                            GetValueFunction<bool>(type, group.VisibilityFunctionName, instance)
                        ).WithLineNumber(group.LineNumber)
                        .WithDescription(group.Description)
                        .WithDisplayName(group.DisplayName)
                        .WithColor(ParseColor(group.Color))
                        .WithCollapsed(group.Collapsed),
                        SuppressWarnings
                    );
                }
            }

            if (attributeType == typeof(CommandAttribute))
            {
                members.Clear();
                foreach (var (member, attribute) in attributes)
                {
                    var attributeCommand = attribute as CommandAttribute;
                    ServeCommandAttribute(member, attributeCommand);

                    members.Add(member);
                }

                foreach (var member in members)
                {
                    var command = _commandsForMembers[(member, instance)];
                    if (_registeredItems.Contains(command))
                        continue;
                    _registeredItems.Add(command);

                    AddCommandToContext(command);
                }
            }

            if (attributeType == typeof(CommandUnitAttribute))
            {
                foreach (var (member, attribute) in attributes)
                {
                    var attributeCommandUnit = attribute as CommandUnitAttribute;

                    var associatedCommand = _commandsByRef.GetValueOrDefault(LocalizeCommandId(attributeCommandUnit.CommandId, type, member, instance));
                    if (associatedCommand == null)
                    {
                        _commandsForMembers.TryGetValue((member, instance), out associatedCommand);
                    }
                    if (associatedCommand == null)
                    {
                        associatedCommand = new CommandAttribute(attributeCommandUnit.CommandId ?? member.Name, attributeCommandUnit.CommandId, attributeCommandUnit.LineNumber);
                        ServeCommandAttribute(member, associatedCommand);
                        AddCommandToContext(associatedCommand);
                    }

                    BaseCommandUnit unit = null;
                    switch (attributeCommandUnit)
                    {
                        case CommandValueAttribute value:
                            unit = new CommandUnitValue(
                                member.GetReturnType(),
                                GetMemberValueFunction(member, instance),
                                value.ReadOnly ? null : SetMemberValueFunction(member, instance),
                                GetValueFunction<IEnumerable>(type, value.PossibleValuesFunctionName, instance),
                                value.ValuesRange == null ? null : (value.ValuesRange.Value.Min, value.ValuesRange.Value.Max),
                                value.Priority,
                                value.ForceStringRepresentation,
                                value.Description,
                                value.ScaleType,
                                value.SuppressExceptions,
                                value.Flex,
                                ParseColor(value.Color),
                                value.FontResource
                            ).WithLineNumber(value.LineNumber);
                            break;

                        case CommandButtonAttribute button:
                            unit = new CommandUnitButton(
                                !string.IsNullOrEmpty(button.Title) ? button.Title : member.Name,
                                GetActionFunction(member, instance),
                                button.Priority,
                                button.Shortcut,
                                button.Description,
                                button.SuppressExceptions,
                                button.Flex,
                                ParseColor(button.Color),
                                button.FontResource
                            ).WithLineNumber(button.LineNumber);
                            break;
                    }

                    _commandsForUnits[attributeCommandUnit] = associatedCommand;
                    _context.AttachCommandUnit(
                        new CommandKey(associatedCommand.Id, associatedCommand.GroupId, associatedCommand.CategoryId, instance),
                        unit,
                        SuppressWarnings
                    );
                }
            }
            return;

            void ServeCommandAttribute(MemberInfo member, CommandAttribute attributeCommand)
            {
                attributeCommand.Id ??= member.Name;

                var localizedCommandId = LocalizeCommandId(attributeCommand.CommandId ?? attributeCommand.Id, type, member, instance);

                if (string.IsNullOrEmpty(attributeCommand.GroupId))
                {
                    var possibleGroup = _groupsForMembers.GetValueOrDefault(member) ?? _groupsForMembers.GetValueOrDefault(type);
                    attributeCommand.GroupId = possibleGroup?.Id;
                }

                if (string.IsNullOrEmpty(attributeCommand.CategoryId))
                {
                    var possibleCategory = _categoriesForMembers.GetValueOrDefault(member) ?? _categoriesForMembers.GetValueOrDefault(type);
                    attributeCommand.CategoryId = possibleCategory?.Id;
                }

                Merge(_commandsByRef, attributeCommand, a => localizedCommandId);
                _commandsForMembers[(member, instance)] = _commandsByRef[localizedCommandId];
            }

            void AddCommandToContext(CommandAttribute command)
            {
                _context.AddCommand(
                    new Command(
                        command.Id,
                        command.GroupId,
                        command.CategoryId,
                        command.Priority,
                        GetValueFunction<bool>(type, command.VisibilityFunctionName, instance),
                        instance,
                        null,
                        command.HeightMultiplier,
                        command.AlwaysPin
                    ).WithLineNumber(command.LineNumber)
                    .WithDescription(command.Description)
                    .WithDisplayName(command.DisplayName)
                    .WithColor(ParseColor(command.Color)),
                    SuppressWarnings
                );
            }
        }

        private Func<T> GetValueFunction<T>(Type type, string functionName, object instance = null)
        {
            var visibilityFunction = !string.IsNullOrEmpty(functionName)
                ? new Func<T>(
                    () =>
                    {
                        if (_context.TryGetValueFromTargets<T>(type, functionName, instance, out var value, out var error))
                            return value;
                        return default;
                    }
                )
                : null;
            return visibilityFunction;
        }

        private object[][] _defaultArguments = new []
        {
            new object[] {},
            new object[] { null },
            new object[] { null, null },
            new object[] { null, null, null },
            new object[] { null, null, null, null },
            new object[] { null, null, null, null, null },
        };

        private Action GetActionFunction(MemberInfo member, object instance = null)
        {
            switch (member)
            {
                case MethodInfo method:
                    return () => method.Invoke(instance, _defaultArguments[method.GetParameters().Length]);

                case EventInfo action:
                    var backingField = member.DeclaringType.GetField(
                        member.Name,
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                    );
                    var delegateInstance = (Action)backingField.GetValue(instance);
                    return () => delegateInstance?.Invoke();

                default:
                    Debug.LogError($"The associated member is not a function");
                    break;
            }
            return null;
        }

        private static Func<object> GetMemberValueFunction(MemberInfo member, object instance = null)
        {
            return () => member.GetValueByMember(instance);
        }

        private static Action<object> SetMemberValueFunction(MemberInfo member, object instance = null)
        {
            if (member is PropertyInfo property && property.SetMethod == null ||
                member is MethodInfo)
                return null;

            return o => member.SetValueByMember(instance, o);
        }

        private static string LocalizeCommandId(string commandGlobalId, Type type, MemberInfo member, object instance)
        {
            commandGlobalId ??= member.Name;

            return $"{commandGlobalId}__{type.GetHashCode()}__{instance?.GetHashCode()}";
        }

        private IList<Type> ExecutionOrder { get; } = new[]
        {
            typeof(CommandCategoryAttribute),
            typeof(CommandGroupAttribute),
            typeof(CommandAttribute),
            typeof(BaseCommandUnit),
            typeof(CommandUnitAttribute),
        };

        private void DoForEveryAttributeType(Action<Type> action)
        {
            foreach (var attributeType in ExecutionOrder)
            {
                action(attributeType);
            }
        }

        private void Merge<T, TKey>(Dictionary<TKey, T> keep, T use, Func<T, TKey> getKey) where T : BaseCommandAttribute
        {
            if (!keep.TryGetValue(getKey(use), out var existing))
            {
                keep[getKey(use)] = use;
                return;
            }
            Merge(existing, use);
        }

        private void Merge<T>(T keep, T use) where T : BaseCommandAttribute
        {
            if (keep.Id != use.Id)
                return;

            if (!string.IsNullOrEmpty(use.Description))
            {
                if (!string.IsNullOrEmpty(keep.Description) && keep.Description != use.Description)
                    LogConflict(nameof(keep.Description), keep.Id);
                keep.Description = use.Description;
            }
            if (!string.IsNullOrEmpty(use.DisplayName))
            {
                if (!string.IsNullOrEmpty(keep.DisplayName) && keep.DisplayName != use.DisplayName)
                    LogConflict(nameof(keep.DisplayName), keep.Id);
                keep.DisplayName = use.DisplayName;
            }
            if (!string.IsNullOrEmpty(use.VisibilityFunctionName))
            {
                if (!string.IsNullOrEmpty(keep.VisibilityFunctionName) && keep.VisibilityFunctionName != use.VisibilityFunctionName)
                    LogConflict(nameof(keep.VisibilityFunctionName), keep.Id);
                keep.VisibilityFunctionName = use.VisibilityFunctionName;
            }
            if (use.Priority != 0)
            {
                if (keep.Priority != 0 && keep.Priority != use.Priority)
                    LogConflict(nameof(keep.Priority), keep.Id);
                keep.Priority = use.Priority;
            }
            if (!string.IsNullOrEmpty(use.Color))
            {
                if (!string.IsNullOrEmpty(keep.Color) && keep.Color != use.Color)
                    LogConflict(nameof(keep.Color), keep.Id);
                keep.Color = use.Color;
            }

            if (keep is CommandGroupAttribute keepGroup && use is CommandGroupAttribute useGroup)
            {
                if (!string.IsNullOrEmpty(useGroup.CategoryId))
                {
                    if (!string.IsNullOrEmpty(keepGroup.CategoryId) && keepGroup.CategoryId != useGroup.CategoryId)
                        LogConflict(nameof(keepGroup.CategoryId), keepGroup.Id);
                    keepGroup.CategoryId = useGroup.CategoryId;
                }

                if (useGroup.Collapsed)
                {
                    keepGroup.Collapsed = true;
                }
            }

            if (keep is CommandAttribute keepCommand && use is CommandAttribute useCommand)
            {
                if (!string.IsNullOrEmpty(useCommand.CategoryId))
                {
                    if (!string.IsNullOrEmpty(keepCommand.CategoryId) && keepCommand.CategoryId != useCommand.CategoryId)
                        LogConflict(nameof(keepCommand.CategoryId), keepCommand.Id);
                    keepCommand.CategoryId = useCommand.CategoryId;
                }

                if (!string.IsNullOrEmpty(useCommand.GroupId))
                {
                    if (!string.IsNullOrEmpty(keepCommand.GroupId) && keepCommand.GroupId != useCommand.GroupId)
                        LogConflict(nameof(keepCommand.GroupId), keepCommand.Id);
                    keepCommand.GroupId = useCommand.GroupId;
                }

                if (useCommand.CommandId != null)
                {
                    if (keepCommand.CommandId != null && keepCommand.CommandId != useCommand.CommandId)
                        LogConflict(nameof(keepCommand.CommandId), keepCommand.Id);
                    keepCommand.CommandId = useCommand.CommandId;
                }

                if (useCommand.HeightMultiplier > 0)
                {
                    if (keepCommand.HeightMultiplier > 0 && keepCommand.HeightMultiplier != useCommand.HeightMultiplier)
                        LogConflict(nameof(keepCommand.HeightMultiplier), keepCommand.Id);
                    keepCommand.HeightMultiplier = useCommand.HeightMultiplier;
                }

                if (useCommand.AlwaysPin)
                {
                    keepCommand.AlwaysPin = useCommand.AlwaysPin;
                }


                keepCommand.AlwaysPin |= useCommand.AlwaysPin;
            }
        }

        private void LogConflict(string fieldName, string id)
        {
            Debug.LogWarning($"Conflicting '{fieldName}' in attribute '{id}'");
        }

        private readonly object _unregisterBlock = new();

        private void Unregister(Type type, Type attributeType, object instance = null)
        {
            if (attributeType != typeof(CommandAttribute) && !attributeType.IsAssignableTo(typeof(CommandUnitAttribute)))
                return;

            using var _ = _context.Block.SetAndTrack(true, 1, _unregisterBlock);

            var attributes = GetAttributesFor(type, attributeType, instance == null);

            if (attributeType == typeof(CommandAttribute))
            {
                foreach (var (member, attribute) in attributes)
                {
                    if (_commandsForMembers.TryGetValue((member, instance), out var command))
                    {
                        _context.RemoveCommand(command.Id, command.GroupId, command.CategoryId, instance);
                    }
                }
            }

            if (attributeType.IsAssignableTo(typeof(CommandUnitAttribute)))
            {
                foreach (var (member, attribute) in attributes)
                {
                    var commandUnit = attribute as CommandUnitAttribute;
                    if (_commandsForUnits.TryGetValue(commandUnit, out var command))
                    {
                        _context.RemoveCommand(command.Id, command.GroupId, command.CategoryId, instance);
                    }
                }
            }
        }

        private static LazyCache<(Type type, Type attribute, bool isStatic), IReadOnlyList<(MemberInfo, Attribute)>> _getAttributesForCache;
        private static IReadOnlyList<(MemberInfo, Attribute)> GetAttributesFor(Type type, Type attributeType, bool isStatic)
        {
            _getAttributesForCache ??= new(
                t =>
                {
                    (Type type, Type attributeType, bool isStatic) = t;
                    var flags = BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
                    var all = type.GetCustomAttributes<BaseDevSuiteAttribute>(flags);
                    var res = new List<(MemberInfo, Attribute)>();
                    foreach (var e in all)
                    {
                        if (e.attribute.GetType().IsAssignableTo(attributeType))
                        {
                            res.Add(e);
                        }
                    }
                    return res;
                }
            );
            return _getAttributesForCache[(type, attributeType, isStatic)];
        }

        public void InvalidateCache()
        {
            _categoriesForMembers.Clear();
            _groupsForMembers.Clear();
            _commandsForMembers.Clear();

            _categoriesByIds.Clear();
            _groupsByIds.Clear();
            _commandsByRef.Clear();
        }

        private static Color? ParseColor(string colorHex)
        {
            if (string.IsNullOrEmpty(colorHex))
                return null;
            colorHex = colorHex.Trim().TrimStart('#');
            if (ColorUtility.TryParseHtmlString(colorHex, out var color))
                return color;
            if (ColorUtility.TryParseHtmlString($"#{colorHex}", out color))
                return color;
            return null;
        }
    }
}