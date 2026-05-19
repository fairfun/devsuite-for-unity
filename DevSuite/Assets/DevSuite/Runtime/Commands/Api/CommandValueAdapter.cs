using Ff.Prefs;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Ff.DevSuite.Commands
{
    internal class AdapterValues
    {
        public static List<Type> None = new();
        public static List<Type> ListString = new() { typeof(string) };
    }

    public abstract class CommandValueAdapter
    {
        public virtual float Priority => 0f;
        public virtual bool ModifiesExistingObject => false;
        public abstract List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null);
        //objDestination for changing existing instance instead of creating a new one
        public abstract object Convert(object objSource, Type typeDestination, object objDestination);
    }

    public class DelegateCommandValueAdapterToString<T> : CommandValueAdapter
    {
        private readonly ConvertToString _convertToString;
        private readonly ConvertFromString _convertFromString;
        private static List<Type> _listT = null;

        public DelegateCommandValueAdapterToString(ConvertToString convertToString, ConvertFromString convertFromString)
        {
            _convertToString = convertToString;
            _convertFromString = convertFromString;
            _listT ??= typeof(T).GetImplementations().ToList();
        }

        public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
        {
            if (typeSource == typeof(string) && _convertFromString != null)
            {
                return _listT;
            }
            if (typeof(T).IsAssignableFrom(typeSource) && _convertToString != null)
            {
                return AdapterValues.ListString;
            }
            return AdapterValues.None;
        }

        public override object Convert(object objSource, Type typeDestination, object objDestination)
        {
            if (typeDestination == typeof(string))
            {
                return _convertToString((T)objSource);
            }
            if (typeof(T).IsAssignableFrom(typeDestination) && _convertToString != null)
            {
                return _convertFromString((string)objSource, typeDestination);
            }
            throw new ArgumentException();
        }

        public delegate string ConvertToString(T obj);
        /// <summary>
        /// typeDestination is for cases when adapter is defined for a base class and it may be needed to know an actual class into which to convert.
        /// targetDestination is for cases when need to modify an exiting instance instead of creating a new one.
        /// </summary>
        public delegate T ConvertFromString(string str, Type typeDestination, T targetDestination = default);
    }

    internal static class DefaultCommandValueAdapters
    {
        private static readonly Regex RegexVector2 = new(@"\((\d+\.?\d*), *(\d+\.?\d*)\)", RegexOptions.Compiled);
        private static readonly Regex RegexVector2Int = new(@"\((\d+), *(\d+)\)", RegexOptions.Compiled);
        private static readonly Regex RegexVector3 = new(@"\((\d+\.?\d*), *(\d+\.?\d*), *(\d+\.?\d*)\)", RegexOptions.Compiled);
        private static readonly Regex RegexVector3Int = new(@"\((\d+), *(\d+), *(\d+)\)", RegexOptions.Compiled);
        private static readonly Regex RegexVector4 = new(@"\((\d+\.?\d*), *(\d+\.?\d*), *(\d+\.?\d*), *(\d+\.?\d*)\)", RegexOptions.Compiled);
        private static readonly Regex RegexRect = new(@"\(x:(\d+\.?\d*), *y:(\d+\.?\d*), *width:(\d+\.?\d*), *height:(\d+\.?\d*)\)", RegexOptions.Compiled);
        private static readonly Regex RegexRectInt = new(@"\(x:(\d+), *y:(\d+), *width:(\d+), *height:(\d+)\)", RegexOptions.Compiled);
        private static readonly string DateTimeFormat = "yyyy.MM.dd HH:mm:ss";
        private static readonly string TimeSpanFormat = "g";

        public static CommandValueAdapter[] Get()
        {
            return new CommandValueAdapter[]
            {
                new CommandValueAdapterPrimitiveToNullable(),
                new CommandValueAdapterPrimitiveFromNullable(),
                new CommandValueAdapterEnumToNullable(),
                new CommandValueAdapterEnumFromNullable(),
                new CommandValueAdapterPrimitiveToString(),
                new CommandValueAdapterPrimitiveFromString(),
                new CommandValueAdapterEnumToString(),
                new CommandValueAdapterEnumFromString(),
                new CommandValueAdapterRegexToString(),
                new CommandValueAdapterRegexFromString(),
                new CommandValueAdapterUnityVectorsToString(),
                new CommandValueAdapterUnityVectorsFromString(),
                new CommandValueAdapterUnityRectanglesToString(),
                new CommandValueAdapterUnityRectanglesFromString(),
                new CommandValueAdapterUnityColorToString(),
                new CommandValueAdapterUnityColorFromString(),
                new CommandValueAdapterUnityQuaternionToString(),
                new CommandValueAdapterUnityQuaternionFromString(),
                new CommandValueAdapterSavedPrefsPropertyToValue(),
                new CommandValueAdapterSavedPrefsPropertyFromValue(),
                new CommandValueAdapterTupleToString(),
                new CommandValueAdapterDictionaryToString(),
                new CommandValueAdapterEnumerableToString(),
                new CommandValueAdapterBetweenNumbers(),
                new CommandValueAdapterFuncToValue(),
                new CommandValueAdapterLazyToValue(),
            };
        }

        internal class CommandValueAdapterPrimitiveToNullable : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource.IsValueType && Nullable.GetUnderlyingType(typeSource) == null)
                {
                    return new List<Type>
                    {
                        typeof(Nullable<>).MakeGenericType(typeSource),
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                return objSource;
            }
        }

        internal class CommandValueAdapterPrimitiveFromNullable : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                var underlying = Nullable.GetUnderlyingType(typeSource);
                if (underlying != null)
                {
                    return new List<Type>
                    {
                        underlying,
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                if (objSource == null)
                {
                    return Activator.CreateInstance(typeDestination);
                }
                return objSource;
            }
        }

        internal class CommandValueAdapterEnumToNullable : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource.IsEnum)
                {
                    return new List<Type>
                    {
                        typeof(Nullable<>).MakeGenericType(typeSource),
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                return objSource;
            }
        }

        internal class CommandValueAdapterEnumFromNullable : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                var underlying = Nullable.GetUnderlyingType(typeSource);
                if (underlying is { IsEnum: true })
                {
                    return new List<Type>
                    {
                        underlying,
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                if (objSource == null)
                {
                    return Activator.CreateInstance(typeDestination);
                }
                return objSource;
            }
        }

        internal class CommandValueAdapterPrimitiveToString : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource.IsPrimitive || typeSource == typeof(decimal) || typeSource == typeof(DateTime) || typeSource == typeof(TimeSpan))
                {
                    return new List<Type>
                    {
                        typeof(string),
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                if (objSource == null)
                {
                    return null;
                }
                if (objSource is DateTime dt)
                {
                    return dt.ToUniversalTime().ToString(DateTimeFormat, CultureInfo.InvariantCulture);
                }
                if (objSource is TimeSpan ts)
                {
                    return ts.ToString(TimeSpanFormat, CultureInfo.InvariantCulture);
                }
                if (objSource is IFormattable formattable)
                {
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                }
                return objSource.ToString();
            }
        }

        private static List<Type> PrimitiveTypes = DevSuiteUtils.CommonPrimitiveTypes.ToList();
        internal class CommandValueAdapterPrimitiveFromString : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource == typeof(string))
                {
                    return PrimitiveTypes;
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                if (objSource == null)
                {
                    return null;
                }
                var str = (string)objSource;
                if (string.IsNullOrEmpty(str))
                {
                    return Activator.CreateInstance(typeDestination);
                }
                if (typeDestination == typeof(DateTime))
                {
                    return DateTime.SpecifyKind(DateTime.ParseExact(str, DateTimeFormat, CultureInfo.InvariantCulture), DateTimeKind.Utc);
                }
                if (typeDestination == typeof(TimeSpan))
                {
                    return TimeSpan.ParseExact(str, TimeSpanFormat, CultureInfo.InvariantCulture);
                }
                return System.Convert.ChangeType(str, typeDestination, CultureInfo.InvariantCulture);
            }
        }

        internal class CommandValueAdapterEnumToString : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource.IsEnum || typeSource == typeof(Enum))
                {
                    return new List<Type>
                    {
                        typeof(string),
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                return objSource?.ToString();
            }
        }

        internal class CommandValueAdapterEnumFromString : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource == typeof(string))
                {
                    var result = new List<Type>
                    {
                        typeof(Enum),
                    };
                    if (hintDestinations != null)
                    {
                        foreach (var hint in hintDestinations)
                        {
                            if (hint.IsEnum && !result.Contains(hint))
                            {
                                result.Add(hint);
                            }
                        }
                    }
                    return result;
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                if (objSource == null)
                {
                    return null;
                }
                var str = (string)objSource;
                if (string.IsNullOrEmpty(str))
                {
                    return Enum.Parse(typeDestination, "0");
                }
                return Enum.Parse(typeDestination, str.Trim(), true);
            }
        }

        internal class CommandValueAdapterRegexToString : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource == typeof(Regex))
                {
                    return new List<Type>
                    {
                        typeof(string),
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                return objSource?.ToString();
            }
        }

        internal class CommandValueAdapterRegexFromString : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource == typeof(string))
                {
                    return new List<Type>
                    {
                        typeof(Regex),
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                if (objSource == null)
                {
                    return null;
                }
                return new Regex((string)objSource);
            }
        }

        internal class CommandValueAdapterUnityVectorsToString : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource == typeof(Vector2) ||
                    typeSource == typeof(Vector2Int) ||
                    typeSource == typeof(Vector3) ||
                    typeSource == typeof(Vector3Int) ||
                    typeSource == typeof(Vector4))
                {
                    return new List<Type>
                    {
                        typeof(string),
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                return objSource?.ToString();
            }
        }

        internal class CommandValueAdapterUnityVectorsFromString : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource == typeof(string))
                {
                    return new List<Type>
                    {
                        typeof(Vector2),
                        typeof(Vector2Int),
                        typeof(Vector3),
                        typeof(Vector3Int),
                        typeof(Vector4),
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                if (objSource == null)
                {
                    return null;
                }
                var str = (string)objSource;
                if (string.IsNullOrEmpty(str))
                {
                    return Activator.CreateInstance(typeDestination);
                }

                if (typeDestination == typeof(Vector2))
                {
                    var g = RegexVector2.Match(str).Groups;
                    return new Vector2(float.Parse(g[1].Value), float.Parse(g[2].Value));
                }
                if (typeDestination == typeof(Vector2Int))
                {
                    var g = RegexVector2Int.Match(str).Groups;
                    return new Vector2Int(int.Parse(g[1].Value), int.Parse(g[2].Value));
                }
                if (typeDestination == typeof(Vector3))
                {
                    var g = RegexVector3.Match(str).Groups;
                    return new Vector3(float.Parse(g[1].Value), float.Parse(g[2].Value), float.Parse(g[3].Value));
                }
                if (typeDestination == typeof(Vector3Int))
                {
                    var g = RegexVector3Int.Match(str).Groups;
                    return new Vector3Int(int.Parse(g[1].Value), int.Parse(g[2].Value), int.Parse(g[3].Value));
                }
                if (typeDestination == typeof(Vector4))
                {
                    var g = RegexVector4.Match(str).Groups;
                    return new Vector4(float.Parse(g[1].Value), float.Parse(g[2].Value), float.Parse(g[3].Value), float.Parse(g[4].Value));
                }
                return null;
            }
        }

        internal class CommandValueAdapterUnityRectanglesToString : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource == typeof(Rect) || typeSource == typeof(RectInt))
                {
                    return new List<Type>
                    {
                        typeof(string),
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                return objSource?.ToString();
            }
        }

        internal class CommandValueAdapterUnityRectanglesFromString : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource == typeof(string))
                {
                    return new List<Type>
                    {
                        typeof(Rect),
                        typeof(RectInt),
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                if (objSource == null)
                {
                    return null;
                }
                var str = (string)objSource;
                if (string.IsNullOrEmpty(str))
                {
                    return Activator.CreateInstance(typeDestination);
                }

                if (typeDestination == typeof(Rect))
                {
                    var g = RegexRect.Match(str).Groups;
                    return new Rect(float.Parse(g[1].Value), float.Parse(g[2].Value), float.Parse(g[3].Value), float.Parse(g[4].Value));
                }
                if (typeDestination == typeof(RectInt))
                {
                    var g = RegexRectInt.Match(str).Groups;
                    return new RectInt(int.Parse(g[1].Value), int.Parse(g[2].Value), int.Parse(g[3].Value), int.Parse(g[4].Value));
                }
                return null;
            }
        }

        internal class CommandValueAdapterUnityColorToString : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource == typeof(Color))
                {
                    return new List<Type>
                    {
                        typeof(string),
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                if (objSource == null)
                {
                    return null;
                }
                return $"#{ColorUtility.ToHtmlStringRGBA((Color)objSource)}";
            }
        }

        internal class CommandValueAdapterUnityColorFromString : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource == typeof(string))
                {
                    return new List<Type>
                    {
                        typeof(Color),
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                if (objSource == null)
                {
                    return null;
                }
                var str = (string)objSource;
                if (string.IsNullOrEmpty(str))
                {
                    return default(Color);
                }
                if (ColorUtility.TryParseHtmlString(str, out var c))
                {
                    return c;
                }
                return default(Color);
            }
        }

        internal class CommandValueAdapterUnityQuaternionToString : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource == typeof(Quaternion))
                {
                    return new List<Type>
                    {
                        typeof(string),
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                return objSource?.ToString();
            }
        }

        internal class CommandValueAdapterUnityQuaternionFromString : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource == typeof(string))
                {
                    return new List<Type>
                    {
                        typeof(Quaternion),
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                if (objSource == null)
                {
                    return null;
                }
                var str = (string)objSource;
                if (string.IsNullOrEmpty(str))
                {
                    return Quaternion.identity;
                }
                var g = RegexVector4.Match(str).Groups;
                return new Quaternion(float.Parse(g[1].Value), float.Parse(g[2].Value), float.Parse(g[3].Value), float.Parse(g[4].Value));
            }
        }

        internal class CommandValueAdapterSavedPrefsPropertyToValue : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource.IsGenericType && typeSource.GetGenericTypeDefinition() == typeof(SavedPrefsProperty<>))
                {
                    return new List<Type>
                    {
                        typeSource.GetGenericArguments()[0],
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                if (objSource == null)
                {
                    return null;
                }
                return objSource.GetType().GetProperty(nameof(SavedPrefsProperty<int>.Value)).GetValue(objSource);
            }
        }

        internal class CommandValueAdapterSavedPrefsPropertyFromValue : CommandValueAdapter
        {
            public override bool ModifiesExistingObject => true;

            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource.IsGenericType && typeSource.GetGenericTypeDefinition() == typeof(SavedPrefsProperty<>))
                {
                    return AdapterValues.None;
                }
                return new List<Type>
                {
                    typeof(SavedPrefsProperty<>).MakeGenericType(typeSource),
                };
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                objDestination.GetType().GetProperty(nameof(SavedPrefsProperty<int>.Value)).SetValue(objDestination, objSource);
                return objDestination;
            }
        }

        internal class CommandValueAdapterTupleToString : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource.Name.StartsWith("Tuple`") || typeSource.Name.StartsWith("ValueTuple`"))
                {
                    return new List<Type>
                    {
                        typeof(string),
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                return objSource?.ToString();
            }
        }

        internal class CommandValueAdapterEnumerableToString : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeof(IEnumerable).IsAssignableFrom(typeSource) && typeSource != typeof(string))
                {
                    return new List<Type>
                    {
                        typeof(string),
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                if (objSource == null)
                {
                    return null;
                }
                var a = (IEnumerable)objSource;
                var result = new StringBuilder();
                result.Append('[');
                var first = true;
                var enumerator = a.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    if (!first)
                    {
                        result.Append(", ");
                    }
                    var item = enumerator.Current;
                    result.Append(item?.ToString() ?? DevSuiteContext.NullRepresentation);
                    first = false;
                }
                result.Append(']');
                return result.ToString();
            }
        }

        internal class CommandValueAdapterDictionaryToString : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeof(IDictionary).IsAssignableFrom(typeSource))
                {
                    return new List<Type>
                    {
                        typeof(string),
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                if (objSource == null)
                {
                    return null;
                }
                var dict = (IDictionary)objSource;
                var result = new StringBuilder();
                result.Append('{');
                var first = true;
                var enumerator = dict.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    if (!first)
                    {
                        result.Append(", ");
                    }
                    result.Append(enumerator.Key?.ToString() ?? "null");
                    result.Append(": ");
                    result.Append(enumerator.Value?.ToString() ?? DevSuiteContext.NullRepresentation);
                    first = false;
                }
                result.Append('}');
                return result.ToString();
            }
        }

        internal class CommandValueAdapterBetweenNumbers : CommandValueAdapter
        {
            private static List<Type> NumericTypesAsList = DevSuiteUtils.NumericTypes.ToList();

            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (DevSuiteUtils.NumericTypes.Contains(typeSource))
                {
                    return NumericTypesAsList;
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                if (objSource == null)
                {
                    return null;
                }

                if (DevSuiteUtils.IntegerTypes.Contains(typeDestination))
                {
                    var value = System.Convert.ToDouble(objSource, CultureInfo.InvariantCulture);
                    return System.Convert.ChangeType(Math.Round(value), typeDestination, CultureInfo.InvariantCulture);
                }

                return System.Convert.ChangeType(objSource, typeDestination, CultureInfo.InvariantCulture);
            }
        }

        internal class CommandValueAdapterFuncToValue : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource.IsGenericType && typeSource.GetGenericTypeDefinition() == typeof(Func<>))
                {
                    return new List<Type>
                    {
                        typeSource.GetGenericArguments()[0],
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                if (objSource == null)
                {
                    return null;
                }
                return ((Delegate)objSource).DynamicInvoke();
            }
        }

        internal class CommandValueAdapterLazyToValue : CommandValueAdapter
        {
            public override List<Type> GetPossibleDestinations(Type typeSource, List<Type> hintDestinations = null)
            {
                if (typeSource.IsGenericType && typeSource.GetGenericTypeDefinition() == typeof(Lazy<>))
                {
                    return new List<Type>
                    {
                        typeSource.GetGenericArguments()[0],
                    };
                }
                return AdapterValues.None;
            }

            public override object Convert(object objSource, Type typeDestination, object objDestination)
            {
                if (objSource == null)
                {
                    return null;
                }
                return objSource.GetType().GetProperty("Value").GetValue(objSource);
            }
        }
    }
}