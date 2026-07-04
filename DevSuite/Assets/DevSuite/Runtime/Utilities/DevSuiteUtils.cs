using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ff.DevSuite
{
    using System;
    using System.Collections.Generic;

    internal static class DevSuiteUtils
    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnEnterPlayMode]
        static void OnEnterPlayMode()
        {
            InvalidateCache();
        }
#endif

        public static Regex NewLineRegex { get; } = new Regex(@"[\r\n]+", RegexOptions.Compiled);

        public static IList<T> AsEditable<T>(this IReadOnlyList<T> list)
        {
            if (list is List<T> listT)
            {
                return listT;
            }
            if (list is T[] arrayT)
            {
                return arrayT;
            }
            throw new ArgumentException();
        }

        public static int BinaryLastIndex<T>(this IList<T> list, Func<T, bool> condition)
        {
            if (list.Count <= 0)
                return -1;

            var leftIndex = 0;
            var rightIndex = list.Count - 1;

            var lastTrueIndex = -1;

            while (leftIndex <= rightIndex)
            {
                var i = (rightIndex + leftIndex) / 2;
                var val = list[i];

                if (condition(val))
                {
                    lastTrueIndex = i;
                    leftIndex = i + 1;
                }
                else
                {
                    rightIndex = i - 1;
                }
            }

            return lastTrueIndex;
        }

        public static readonly HashSet<Type> IntegerTypes = new()
        {
            typeof(char),
            typeof(sbyte), typeof(byte),
            typeof(short), typeof(ushort),
            typeof(int), typeof(uint),
            typeof(long), typeof(ulong),
        };

        public static HashSet<Type> NumericTypes = IntegerTypes.Concat(
            new[] { typeof(float), typeof(double), typeof(decimal) }
        ).ToHashSet();

        public static HashSet<Type> CommonPrimitiveTypes = NumericTypes.Concat(
            new[] { typeof(bool), typeof(DateTime), typeof(TimeSpan) }
        ).ToHashSet();

        public static bool IsIntegerType(this Type type)
        {
            return IntegerTypes.Contains(type);
        }

        private static LazyCache<Type, IReadOnlyList<Type>> _getTypeImplementationsCache;

        public static IReadOnlyList<Type> GetImplementations(this Type type)
        {
            var assemblies = new Lazy<Type[]>(
                () => AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.DefinedTypes.Where(t => !t.IsAbstract && !t.IsInterface)).Cast<Type>().ToArray()
            );
            _getTypeImplementationsCache ??= new(
                v =>
                {
                    var values = assemblies.Value.Where(v.IsAssignableFrom).ToList();
                    return values;
                }
            );
            return _getTypeImplementationsCache[type];
        }

        private static LazyCache<Type, Type, bool> _isSubclassOfRawGeneric;

        public static bool IsSubclassOfRawGeneric(this Type generic, Type toCheck)
        {
            _isSubclassOfRawGeneric ??= new(
                (g, tc) =>
                {
                    var result = false;
                    while (tc != null && tc != typeof(object))
                    {
                        var cur = tc.IsGenericType ? tc.GetGenericTypeDefinition() : tc;
                        if (g == cur)
                        {
                            result = true;
                            break;
                        }
                        tc = tc.BaseType;
                    }
                    return result;
                }
            );
            return _isSubclassOfRawGeneric[generic, toCheck];
        }

        private static LazyCache<(Type type, string name), MemberInfo> _membersByName;

        private static void InitializeMembersByNameIfNeeded()
        {
            _membersByName ??= new LazyCache<(Type, string), MemberInfo>(
                t =>
                {
                    var (type, name) = t;
                    MethodInfo first = null;
                    foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                    {
                        var genericArgsCount = m.GetGenericArguments().Length;
                        if (m.Name == name && genericArgsCount == 0 || genericArgsCount == 1)
                        {
                            first = m;
                            break;
                        }
                    }
                    return type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static) as MemberInfo
                           ?? type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static) as MemberInfo
                           ?? first;
                }
            );
        }

        public static void SetValue(this Type type, object obj, string name, object value)
        {
            InitializeMembersByNameIfNeeded();
            var member = _membersByName.Get((type, name));
            SetValueByMember(member, obj, value);
        }

        public static object GetValue(this Type type, object obj, string name)
        {
            InitializeMembersByNameIfNeeded();
            var member = _membersByName.Get((type, name));
            return GetValueByMember(member, obj);
        }

        public static void SetValueByMember<T>(this T member, object obj, object value) where T : MemberInfo
        {
            switch (member)
            {
                case PropertyInfo prop:
                    prop.SetValue(obj, value);
                    return;

                case FieldInfo field:
                    field.SetValue(obj, value);
                    return;

                case MethodInfo method:
                    method.Invoke(obj, new[] { value });
                    return;
            }
            throw new Exception($"Unsupported member type '{member}'");
        }

        public static object GetValueByMember<T>(this T member, object obj) where T : MemberInfo
        {
            return member switch
            {
                PropertyInfo prop => prop.GetValue(obj),
                FieldInfo field => field.GetValue(obj),
                MethodInfo method => method.Invoke(obj, Array.Empty<object>()),
                _ => throw new Exception($"Unsupported member type '{member}'"),
            };
        }

        public static Type PropertyType<T>(this T member) where T : MemberInfo
        {
            return member switch
            {
                PropertyInfo prop => prop.PropertyType,
                FieldInfo field => field.FieldType,
                MethodInfo method => method.ReturnType,
                _ => throw new Exception($"Unsupported member type '{member}'"),
            };
        }

        private static LazyCache<Type, MemberInfo[]> _getFieldsAndPropertiesCache;

        public static IReadOnlyList<MemberInfo> GetFieldsAndProperties(this Type type)
        {
            _getFieldsAndPropertiesCache ??= new(
                t =>
                {
                    var res = t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy).Cast<MemberInfo>()
                        .Concat(t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy).Where(p => p.CanWrite))
                        .ToArray();
                    return res;
                }
            );

            return _getFieldsAndPropertiesCache[type];
        }

        private static LazyCache<Type, Type[]> _getAllInheritedClassesCache;

        public static IReadOnlyList<Type> GetAllInheritedTypes(this Type type)
        {
            _getAllInheritedClassesCache ??= new(
                t =>
                {
                    var result = new HashSet<Type>();

                    AddBaseClass(t, result);
                    foreach (var @interface in t.GetInterfaces())
                    {
                        AddBaseClass(@interface, result);
                    }

                    return result.ToArray();
                }
            );
            return _getAllInheritedClassesCache[type];
        }

        private static HashSet<Type> AddBaseClass(Type type, HashSet<Type> result)
        {
            if (type == null || result.Contains(type))
                return result;

            result.Add(type);
            if (type.IsGenericType)
                result.Add(type.GetGenericTypeDefinition());
            return AddBaseClass(type?.BaseType, result);
        }

        private static LazyCache<Type, Type, BindingFlags, List<(MemberInfo, Attribute)>> _getCustomAttributesCache;

        public static IReadOnlyList<(MemberInfo member, Attribute attribute)> GetCustomAttributes(this Type type, Type attributeType, BindingFlags flags)
        {
            _getCustomAttributesCache ??= new(
                (type, attributeType, flags) =>
                {
                    var classAttributes = type.GetCustomAttributes(attributeType).Select(a => (member: type as MemberInfo, attribute: a));
                    var members = type.GetMembers(flags).OrderBy(m => m.MetadataToken).ToArray();
                    var membersAttributes = members.SelectMany(m => m.GetCustomAttributes(attributeType).Select(a => (member: m, attribute: a)));
                    var allAttributes = classAttributes.Concat(membersAttributes);
                    return allAttributes.ToList();
                }
            );
            return _getCustomAttributesCache[(type, attributeType, flags)];
        }

        public static IReadOnlyList<(MemberInfo member, Attribute attribute)> GetCustomAttributes<T>(this Type type, BindingFlags flags)
        {
            return GetCustomAttributes(type, typeof(T), flags);
        }

        private static LazyCache<MemberInfo, Type> _getReturnTypeCache;

        public static Type GetReturnType(this MemberInfo member)
        {
            _getReturnTypeCache ??= new(
                member =>
                {
                    return member switch
                    {
                        FieldInfo field => field.FieldType,
                        PropertyInfo property => property.PropertyType,
                        MethodInfo method => method.ReturnType,
                        _ => throw new ArgumentException($"Unsupported member {member?.GetType()}"),
                    };
                }
            );
            return _getReturnTypeCache[member];
        }

        private static LazyCache<Type, string, PropertyInfo> _getTypePropertyCache;
        public static PropertyInfo GetTypeProperty(Type type, string name)
        {
            _getTypePropertyCache ??= new(
                (t, n) => t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            );
            return _getTypePropertyCache[type, name];
        }

        public static void InvalidateCache()
        {
            _getTypePropertyCache?.Clear();
            _getReturnTypeCache?.Clear();
            _getCustomAttributesCache?.Clear();
            _getAllInheritedClassesCache?.Clear();
            _getFieldsAndPropertiesCache?.Clear();
            _membersByName?.Clear();
            _isSubclassOfRawGeneric?.Clear();
            _getTypeImplementationsCache?.Clear();
        }

        public static bool IsInteger(this Type type)
        {
            return IntegerTypes.Contains(type);
        }

        public static bool IsNumber(this Type type)
        {
            return NumericTypes.Contains(type);
        }

        public static bool IsAssignableTo(this Type a, Type b)
        {
            return b.IsAssignableFrom(a);
        }

        public static Dictionary<string, Lazy<T>> GetMembersValues<T>(object obj) where T : class
        {
            var result = new Dictionary<string, Lazy<T>>();

            var properties = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var property in properties)
            {
                if (property.PropertyType.IsAssignableTo(typeof(T)))
                {
                    result.Add(property.Name, new Lazy<T>(() => property.GetValue(obj) as T));
                }
            }

            var fields = obj.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.FieldType.IsAssignableTo(typeof(T)))
                {
                    result.Add(field.Name, new Lazy<T>(() => field.GetValue(obj) as T));
                }
            }

            return result;
        }

        internal static bool IsNullable(this Type type)
        {
            return type.IsValueType && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
        }

        internal static IReadOnlyList<T> ToReadOnlyList<T>(this IEnumerable<T> xs)
        {
            if (xs is IReadOnlyList<T> ro)
            {
                return ro;
            }
            return new List<T>(xs);
        }

        internal static double TargetFps => Application.targetFrameRate > 0
            ? Application.targetFrameRate
            : DisplayFrameRate;

        private static double? _refreshRate; // cached to avoid allocations in Screen.mainWindowDisplayInfo
        internal static double DisplayFrameRate => _refreshRate ??=
#if UNITY_EDITOR || UNITY_STANDALONE
            Screen.mainWindowDisplayInfo.refreshRate.value;
#else
            Screen.currentResolution.refreshRateRatio.value;
#endif

        private static readonly char[] TrimChars =
        {
            ' ',
            '_',
        };

        private static readonly List<Regex> TrimNamePatterns = new()
        {
            new Regex(
                @"(?i)(?:^(?:cheats?|commands?|debug|category|group|command))|(?:(?:cheats?|commands?|debug|category|group|command)$)",
                RegexOptions.Compiled
            ),
        };

        public static string TrimName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            name = name.Trim(TrimChars);
            foreach (var pattern in TrimNamePatterns)
            {
                var newName = pattern.Replace(name, "");
                if (newName.Length > 0)
                    name = newName;
            }
            return name;
        }

        public static void ShowIconButtonClickedFeedback(Button button)
        {
            if (button.userData is string)
                return; // already showing feedback

            var originalIcon = button.text;
            button.userData = originalIcon;
            button.text = "\uf00c";
            button.schedule.Execute(() =>
            {
                button.text = originalIcon;
                button.userData = null;
            }).StartingIn(500);
        }

        private static readonly Regex AllUppercaseLetters = new(@"([A-Z]|[0-9]+)|( +)", RegexOptions.Compiled);
        private const string AllUppercaseLettersReplacement = @"[a-z_\- ]*$1";

        public static readonly Regex AlwaysMatch = new Regex(@".*", RegexOptions.Compiled);
        public static readonly Regex NeverMatch = new Regex(@"\A(?!x)x", RegexOptions.Compiled);

        public static Regex GetSmartSearchRegex(string text)
        {
            if (string.IsNullOrEmpty(text))
                return AlwaysMatch;

            text = Regex.Escape(text);
            var regexExpression = AllUppercaseLetters.Replace(text, AllUppercaseLettersReplacement);
            regexExpression = $"(?i){regexExpression}";
            return new Regex(regexExpression, RegexOptions.Compiled);
        }

        public static double Length(this NumberRange<double> range)
        {
            return range.Max - range.Min;
        }

        public static float Length(this NumberRange<float> range)
        {
            return range.Max - range.Min;
        }

        private static readonly Dictionary<Type, object> _lists = new();

        public static List<T> EmptyList<T>()
        {
            var key = typeof(T);
            if (!_lists.TryGetValue(key, out var result))
            {
                result = new List<T>();
                _lists.Add(key, result);
            }
            return result as List<T>;
        }

        private static readonly Dictionary<Type, object> _orderedSets = new();

        public static OrderedSet<T> EmptyOrderedSet<T>()
        {
            var key = typeof(T);
            if (!_orderedSets.TryGetValue(key, out var result))
            {
                result = new OrderedSet<T>();
                _orderedSets.Add(key, result);
            }
            return result as OrderedSet<T>;
        }

        private static readonly Dictionary<Type, object> _hashSets = new();

        public static HashSet<T> EmptyHashSet<T>()
        {
            var key = typeof(T);
            if (!_hashSets.TryGetValue(key, out var result))
            {
                result = new HashSet<T>();
                _hashSets.Add(key, result);
            }
            return result as HashSet<T>;
        }

        private static readonly Dictionary<(Type, Type), object> _dictionaries = new();

        public static Dictionary<TKey, TValue> EmptyDictionary<TKey, TValue>()
        {
            var key = (typeof(TKey), typeof(TValue));
            if (!_dictionaries.TryGetValue(key, out var result))
            {
                result = new Dictionary<TKey, TValue>();
                _dictionaries.Add(key, result);
            }
            return result as Dictionary<TKey, TValue>;
        }

        public static void CopyToClipboard(string text)
        {
            if (IsWebGl)
            {
                CopyToClipboardWebGL(text);
            }
            else
            {
                GUIUtility.systemCopyBuffer = text;
            }
        }

        public static bool IsVisible(this VisualElement e)
        {
            return e.visible && e.style.display != DisplayStyle.None &&
                   (e.parent == null || IsVisible(e.parent));
        }

        public static void SetupInputFieldFocus(TextField textField)
        {
            textField.focusable = false;
            textField.tabIndex = -1;

            void ConfigureTextInput(VisualElement input)
            {
                if (input != null)
                {
                    input.tabIndex = -1;
                    input.focusable = false;
                }
            }

            var textInput = textField.Q("unity-text-input");
            if (textInput != null)
            {
                ConfigureTextInput(textInput);
            }
            else
            {
                textField.RegisterCallback<AttachToPanelEvent>(evt =>
                {
                    ConfigureTextInput(textField.Q("unity-text-input"));
                });
            }

            textField.RegisterCallback<PointerDownEvent>(evt =>
            {
                textField.focusable = true;
                var input = textField.Q("unity-text-input");
                input.focusable = true;
            }, TrickleDown.TrickleDown);

            textField.RegisterCallback<FocusOutEvent>(evt =>
            {
                textField.focusable = false;
                var input = textField.Q("unity-text-input");
                input.focusable = false;
            });
        }


        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void CopyToClipboardWebGL(string text);

        private static bool IsWebGl =>
#if UNITY_WEBGL && !UNITY_EDITOR
            true;
#else
            false;
#endif
    }
}