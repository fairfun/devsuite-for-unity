#pragma warning disable CS0414
using Ff.DevSuite.Commands;
using Ff.DevSuite.Commands.Attributes;
using Ff.Prefs;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Key =
#if ENABLE_INPUT_SYSTEM
    UnityEngine.InputSystem.Key;
#else
    UnityEngine.KeyCode;
#endif
using RectInt = UnityEngine.RectInt;

namespace Ff.DevSuite
{
    /// <summary>
    /// For internal package testing only. Should not be used.
    /// </summary>
    internal static class DevSuiteCommandsTesting
    {
        public static void RegisterAll(IDevSuiteContext context)
        {
            context.CommandsApi.RegisterAdapter(new DelegateCommandValueAdapterToString<CultureInfo>(
                a => a?.Name,
                (b, _, _) => b == null ? null : CultureInfo.GetCultureInfo(b)
            ));
            context.CommandsApi.RegisterValuesProvider(new CommandValuesProvider(typeof(CultureInfo), _ => CultureInfo.GetCultures(CultureTypes.AllCultures).Take(50).ToArray()));

            var all = new[]
            {
                typeof(DevSuiteTestingCategoryNumbers),
                typeof(DevSuiteTestingCategoryOneLiner),
                typeof(DevSuiteTestingCategoryMembers),
                typeof(DevSuiteTestingCategorySavedPrefs),
                typeof(DevSuiteTestingCategoryNullables),
                typeof(DevSuiteTestingCategoryAttributes),
                typeof(DevSuiteTestingCategoryPossibleValues),
                typeof(DevSuiteTestingCategoryVisibilityChange),
                typeof(DevSuiteTestingCategoryForcedString),
                typeof(DevSuiteTestingCategoryUnity),
                typeof(DevSuiteTestingCategoryTime),
                typeof(DevSuiteTestingCategoryExotic),
                typeof(DevSuiteTestingCategoryTuples),
                typeof(DevSuiteTestingCategoryLongList),
                typeof(DevSuiteTestingCategoryCollections),
                typeof(DevSuiteTestingCategoryButtons),
                typeof(DevSuiteTestingCategoryScaleTypes),
                typeof(DevSuiteTestingCategoryFunctionsProvider),
                typeof(DevSuiteTestingExternalAdapter),
                typeof(DevSuiteTestingSameCommandId1),
                typeof(DevSuiteTestingSameCommandId2),
            };
            foreach (var type in all)
            {
                context.AttributesParser.RegisterStatic(type);
            }

            context.CommandsApi.RegisterTargetForFunctionsProvider(new CommandFunctionsSourceProvider(typeof(DevSuiteCommandsTesting)));
            var visibilityInstance = new VisibilityTestInstance();
            context.CommandsApi.RegisterTargetForFunctionsProvider(new CommandFunctionsSourceProvider(visibilityInstance, new HashSet<string>() { "IsVisiblePropertyFromInstance" } ));

            var inst1 = new DevSuiteTestingCategoryInstanceBased();
            context.AttributesParser.RegisterInstance(inst1);

            var inst2 = new DevSuiteTestingCategoryInstanceBased();
            context.AttributesParser.RegisterInstance(inst2);

            var inst3 = new DevSuiteTestingCategoryInstanceBased();
            context.AttributesParser.RegisterInstance(inst3);
            context.AttributesParser.UnregisterInstance(inst3);
        }

        private static bool IsVisibleMethod()
        {
            return TimeSpan.FromSeconds(Time.unscaledTime).Seconds % 15 < 5;
        }

        public class VisibilityTestInstance
        {
            private static bool IsVisiblePropertyFromInstance()
            {
                return TimeSpan.FromSeconds(Time.unscaledTime).Seconds % 15 < 10;
            }

            private static bool IsVisiblePropertyFromInstanceButExcluded()
            {
                return TimeSpan.FromSeconds(Time.unscaledTime).Seconds % 15 < 10;
            }
        }

        [CommandCategory("Numbers")]
        private static class DevSuiteTestingCategoryNumbers
        {
            [CommandValue(MinValue = 5, MaxValue = 88, Flex = 3f)]
            private static float? NumberNullableSlider = 67;

            [CommandValue(MinValue = 5, MaxValue = 88, Flex = 3f)]
            private static uint? UintNullableSlider = 67;

            [CommandValue, CommandGroup("2", Description = "Group 2 description", Priority = -2)]
            private static float FloatField = 67;

            [CommandValue, CommandGroup("2")]
            private static int IntField3 = 78;

            [CommandValue, CommandGroup("2")]
            private static bool boolField = true;
        }

        [CommandCategory("OneLiner")]
        private static class DevSuiteTestingCategoryOneLiner
        {
            [Command("Command")][CommandValue(MinValue = 5, MaxValue = 88)]
            private static float NumberFieldWithSlider = 67;

            [Command("Command"), CommandValue()]
            private static bool Checked = true;

#if ENABLE_INPUT_SYSTEM
            [Command("Command"), CommandButton(Shortcut = new[] { Key.A, Key.LeftCtrl })]
#else
            [Command("Command"), CommandButton(Shortcut = new[] { Key.A, Key.LeftControl })]
#endif
            private static bool Action()
            {
                Debug.LogWarning("Button click");
                return true;
            }
        }

        [CommandCategory("Members")]
        private static class DevSuiteTestingCategoryMembers
        {
            [CommandValue]
            private static string TextField = "TextField";

            [CommandValue]
            private static string TextProperty => "TextProperty";

            [CommandValue]
            private static string TextMethod() => "TextMethod";
        }

        [CommandCategory("SavedPrefs")]
        private static class DevSuiteTestingCategorySavedPrefs
        {
            [CommandValue, CommandGroup("2")]
            private static SavedPrefsProperty<float> _savedFloat = new(nameof(_savedFloat), 44.4f);

            [CommandValue, CommandGroup("2")]
            private static SavedPrefsProperty<DateTimeKind> _savedEnum = new(nameof(_savedEnum), DateTimeKind.Local);

            [CommandValue, CommandGroup("2")]
            private static SavedPrefsProperty<DateTimeKind?> _savedNullableEnum = new(nameof(_savedNullableEnum), DateTimeKind.Local);

            [CommandValue(MinValue = 3f, MaxValue = 25f), CommandGroup("2")]
            private static SavedPrefsProperty<float?> _savedNullableSlider = new(nameof(_savedNullableSlider), 15f);
        }

        [CommandCategory("Nullables")]
        private static class DevSuiteTestingCategoryNullables
        {
            [CommandValue, CommandGroup("2")]
            private static bool? _nullableBoolField = true;

            [CommandValue, CommandGroup("2")]
            private static DateTimeKind? _nullableDateTime = DateTimeKind.Local;
        }

        [CommandCategory("Attributes", Description = "Category description text")]
        private static class DevSuiteTestingCategoryAttributes
        {
            [CommandGroup("agroup3", Description = "Group description text")]
            [Command("Command", Description = "Command description text")]
            [CommandValue(Description = "Value description")]
            private static float NumberField = 67;

            [Command(AlwaysPin = true), CommandValue]
            private static float AlwaysPinned = 67;
        }

        [CommandCategory("Possible Values")]
        private static class DevSuiteTestingCategoryPossibleValues
        {
            [Command("Command")][CommandValue(PossibleValuesFunctionName = "PossibleFloatValues")]
            private static float NumberField = 67;

            [Command("Command")][CommandValue(PossibleValuesFunctionName = "PossibleFloatValues")]
            private static SavedPrefsProperty<float> SavedNumberField = new(nameof(SavedNumberField));

            [Command("Command")][CommandValue(PossibleValuesFunctionName = "PossibleStringValues")]
            private static string StringField = "";

            private static IList<float> PossibleFloatValues { get; } = new[] { 12f, 22.2f };
            private static IList<string> PossibleStringValues { get; } = new[] { "string1", "string2" };
        }

        [CommandCategory("VisibilityChange", VisibilityFunctionName = "CheckCategoryVisibility")]
        private static class DevSuiteTestingCategoryVisibilityChange
        {
            [Command("Command")][CommandValue(MinValue = 5, MaxValue = 88, Flex = 3f)]
            private static float NumberFieldWithSlider = 67;

            [Command("CommandVisibility", VisibilityFunctionName = "CheckCommandVisibility")][CommandValue]
            private static float NumberSlider1 = 67;

            [CommandGroup(VisibilityFunctionName = "CheckGroupVisibility")][CommandValue]
            private static float NumberSlider2 = 67;

            private static bool CheckCategoryVisibility()
            {
                return DateTime.UtcNow.Second < 30;
            }

            private static bool CheckCommandVisibility => Sec % 4 == 0 || Sec % 4 == 1;

            private static bool CheckGroupVisibility()
            {
                return Sec % 4 == 0 || Sec % 4 == 1;
            }

            private static int Sec => DateTime.UtcNow.Second;
        }

        [CommandCategory("ForcedString")]
        private static class DevSuiteTestingCategoryForcedString
        {
            [Command("Command")][CommandValue(MinValue = 5, MaxValue = 88, Flex = 3f, ForceStringRepresentation = true)]
            private static float NumberFieldWithSlider = 67;

            [Command("Command")][CommandValue(ForceStringRepresentation = true)]
            private static bool BoolFieldWithSlider = true;
        }

        [CommandCategory("Unity")]
        private static class DevSuiteTestingCategoryUnity
        {
            [CommandValue]
            private static Vector2? Vector2NullableField = new Vector2(1, 2);

            [CommandValue]
            private static Vector2Int Vector2IntField = new(3, 4);

            [CommandValue]
            private static SavedPrefsProperty<Vector3> Vector3SavedField = new(nameof(Vector3SavedField), new Vector3(5, 6, 7));

            [CommandValue]
            private static Vector3Int? Vector3IntNullableField = new Vector3Int(8, 9, 10);

            [CommandValue]
            private static Vector4 Vector4Field = new(11, 12, 13, 14);

            [CommandValue]
            private static SavedPrefsProperty<Rect> RectSavedField = new(nameof(RectSavedField), new Rect(1, 2, 3, 4));

            [CommandValue]
            private static RectInt? RectIntNullableField = new RectInt(5, 6, 7, 8);

            [CommandValue]
            private static SavedPrefsProperty<Color> ColorSavedField = new(nameof(ColorSavedField), Color.red);

            [CommandValue]
            private static Quaternion QuaternionField = Quaternion.identity;
        }

        [CommandCategory("Time")]
        private static class DevSuiteTestingCategoryTime
        {
            [CommandValue]
            private static DateTime DateTimeField = DateTime.Now;

            [CommandValue]
            private static DateTime? DateTimeNullableField = DateTime.Now;

            [CommandValue]
            private static SavedPrefsProperty<DateTime> DateTimeSavedField = new(nameof(DateTimeSavedField), DateTime.Now);

            [CommandValue]
            private static TimeSpan TimeSpanField = TimeSpan.FromHours(1);

            [CommandValue]
            private static TimeSpan? TimeSpanNullableField = TimeSpan.FromMinutes(30);

            [CommandValue]
            private static SavedPrefsProperty<TimeSpan> TimeSpanSavedField = new(nameof(TimeSpanSavedField), TimeSpan.FromSeconds(15));
        }

        [CommandCategory("Exotic")]
        private static class DevSuiteTestingCategoryExotic
        {
            [CommandValue]
            private static Func<(int, float)> FuncTuple = () => (10, 20.5f);

            [CommandValue]
            private static Func<float> FuncFloat = () => 10.55f;

            [Command(HeightMultiplier = 1.01f), CommandValue]
            private static string LongTextX1_01 = "Text Line 1\nText Line 2\nText Line 3\nText Line 4\nText Line 5\nText Line 6\nText Line 7\nText Line 8\nText Line 9\nText Line 10";

            [Command(HeightMultiplier = 2f), CommandValue]
            private static string LongTextX2 = "Text Line 1\nText Line 2\nText Line 3\nText Line 4\nText Line 5\nText Line 6\nText Line 7\nText Line 8\nText Line 9\nText Line 10";

            [Command(HeightMultiplier = 5.5f), CommandValue]
            private static string LongTextX5 = "Text Line 1\nText Line 2\nText Line 3\nText Line 4\nText Line 5\nText Line 6\nText Line 7\nText Line 8\nText Line 9\nText Line 10";

            [Command(HeightMultiplier = 5f), CommandValue]
            private static string DifferentElementsX5 = "Text";
            [CommandValue(nameof(DifferentElementsX5), MinValue = 0f, MaxValue = 5f)]
            private static float DifferentElementsX5_Slider = 1f;
            [CommandValue(nameof(DifferentElementsX5))]
            private static bool DifferentElementsX5_Toggle = true;
            [CommandValue(nameof(DifferentElementsX5))]
            private static double DifferentElementsX5_Double = 1f;
        }

        [CommandCategory("Tuples")]
        private static class DevSuiteTestingCategoryTuples
        {
            [CommandValue]
            private static (int, float) Tuple2D => (10, 20.5f);

            [CommandValue]
            private static (int, bool, float) Tuple3D => (42, true, 1.23f);

            [CommandValue]
            private static (float, double, int, bool) Tuple4D => (1.1f, 2.2, 33, false);
        }

        [CommandCategory("Long List")]
        private static class DevSuiteTestingCategoryLongList
        {
            [CommandValue] private static int IntField1 = 1;
            [CommandValue] private static int IntField2 = 2;
            [CommandValue] private static int IntField3 = 3;
            [CommandValue] private static int IntField4 = 4;
            [CommandValue] private static int IntField5 = 5;
            [CommandValue] private static int IntField6 = 6;
            [CommandValue] private static int IntField7 = 7;
            [CommandValue] private static int IntField8 = 8;
            [CommandValue] private static int IntField9 = 9;
            [CommandValue] private static int IntField10 = 10;
            [CommandValue] private static int IntField11 = 11;
            [CommandValue] private static int IntField12 = 12;
            [CommandValue] private static int IntField13 = 13;
            [CommandValue] private static int IntField14 = 14;
            [CommandValue] private static int IntField15 = 15;
            [CommandValue] private static int IntField16 = 16;
            [CommandValue] private static int IntField17 = 17;
            [CommandValue] private static int IntField18 = 18;
            [CommandValue] private static int IntField19 = 19;
            [CommandValue] private static int IntField20 = 20;
            [CommandValue] private static int IntField21 = 21;
            [CommandValue] private static int IntField22 = 22;
            [CommandValue] private static int IntField23 = 23;
            [CommandValue] private static int IntField24 = 24;
            [CommandValue] private static int IntField25 = 25;
            [CommandValue] private static int IntField26 = 26;
            [CommandValue] private static int IntField27 = 27;
            [CommandValue] private static int IntField28 = 28;
            [CommandValue] private static int IntField29 = 29;
            [CommandValue] private static int IntField30 = 30;
            [CommandValue] private static int IntField31 = 31;
            [CommandValue] private static int IntField32 = 32;
            [CommandValue] private static int IntField33 = 33;
            [CommandValue] private static int IntField34 = 34;
            [CommandValue] private static int IntField35 = 35;
            [CommandValue] private static int IntField36 = 36;
            [CommandValue] private static int IntField37 = 37;
            [CommandValue] private static int IntField38 = 38;
            [CommandValue] private static int IntField39 = 39;
            [CommandValue] private static int IntField40 = 40;
        }

        [CommandCategory("Collections")]
        private static class DevSuiteTestingCategoryCollections
        {
            [CommandValue]
            private static float[] ArrayField = { 1.1f, 2.2f, 3.3f };

            [CommandValue]
            private static List<int> ListField = new() { 10, 20, 30 };

            [CommandValue]
            private static Dictionary<string, float> DictionaryField = new()
            {
                { "Key1", 1.1f },
                { "Key2", 2.2f }
            };

            [CommandValue]
            private static List<int> ListNull = null;
        }

        [CommandCategory("Buttons")]
        private static class DevSuiteTestingCategoryButtons
        {
            [CommandButton]
            private static void ParameterlessMethod() => Debug.LogWarning(nameof(ParameterlessMethod));

            [CommandButton]
            private static event Action ActionWithoutParameters;
            static DevSuiteTestingCategoryButtons()
            {
                ActionWithoutParameters += () => Debug.LogWarning(nameof(ActionWithoutParameters));
            }

            [CommandButton]
            private static string MethodWithParameters(string a = null, string b = null)
            {
                var result = $"{nameof(MethodWithParameters)}: {nameof(a)}={a}, {nameof(b)}={b}";
                Debug.LogWarning(result);
                return result;
            }
        }

        [CommandCategory("Scale Types")]
        private static class DevSuiteTestingCategoryScaleTypes
        {
            [CommandValue(MinValue = 1, MaxValue = 10000, ScaleType = ScaleType.Linear)]
            [CommandValue(ReadOnly = true)]
            private static int ScaleLinearInteger;

            [CommandValue(MinValue = 1, MaxValue = 10000, ScaleType = ScaleType.Linear)]
            [CommandValue(ReadOnly = true)]
            private static float ScaleLinear;

            [CommandValue(MinValue = 1, MaxValue = 10000, ScaleType = ScaleType.Logarithmic)]
            [CommandValue(ReadOnly = true)]
            private static double ScaleLogarithmicDouble;

            [CommandValue(MinValue = 0, MaxValue = 10000, ScaleType = ScaleType.Logarithmic)]
            [CommandValue(ReadOnly = true)]
            private static float ScaleLogarithmic;

            [CommandValue(MinValue = 0, MaxValue = 10000, ScaleType = ScaleType.Logarithmic)]
            [CommandValue]
            private static float ScaleLogarithmicEditable;

            [CommandValue(MinValue = 0, MaxValue = 10000, ScaleType = ScaleType.Logarithmic, ReadOnly = true)]
            [CommandValue]
            private static float ScaleLogarithmicReadonlyScale;

            [CommandValue(MinValue = -10000, MaxValue = 10000, ScaleType = ScaleType.Logarithmic)]
            [CommandValue(ReadOnly = true)]
            private static float ScaleLogarithmicWithNegative;

            [CommandValue(MinValue = 0, MaxValue = 10000, ScaleType = ScaleType.Logarithmic)]
            [CommandValue(ReadOnly = true)]
            private static SavedPrefsProperty<float> SavedScaleLogarithmic = new SavedPrefsProperty<float>(nameof(SavedScaleLogarithmic));
        }

        [CommandCategory("FunctionsProviders")]
        private static class DevSuiteTestingCategoryFunctionsProvider
        {
            [Command(VisibilityFunctionName = "IsVisibleMethod"), CommandValue]
            private static string SomeString = "some string";

            [Command(VisibilityFunctionName = "IsVisiblePropertyFromInstance"), CommandValue]
            private static string SomeStringValue { get; set; } = "some value";
        }

        [CommandCategory("InstanceBased")]
        private class DevSuiteTestingCategoryInstanceBased
        {
            [CommandButton]
            public void ParameterlessMethod() => Debug.LogWarning($"{nameof(ParameterlessMethod)} {GetHashCode()}");
            [CommandValue] public string SomeStringValue { get; set; } = "some value";
        }

        [CommandCategory("ExternalAdapter")]
        private static class DevSuiteTestingExternalAdapter
        {
            [CommandValue] private static CultureInfo Culture { get; set; }
            [CommandValue] private static CultureInfo CultureGetter => Culture;
            [CommandValue] private static Func<CultureInfo> CultureFunc = () => Culture;
        }

        [CommandCategory("SameCommandId1")]
        private static class DevSuiteTestingSameCommandId1
        {
            [CommandValue] private static string Value { get; set; }
        }

        [CommandCategory("SameCommandId2")]
        private static class DevSuiteTestingSameCommandId2
        {
            [CommandValue] private static string Value { get; set; }
        }
    }
}
#pragma warning restore CS0414