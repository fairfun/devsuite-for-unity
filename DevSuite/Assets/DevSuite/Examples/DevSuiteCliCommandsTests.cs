using System;
using System.Collections.Generic;
using System.Linq;
using Ff.DevSuite.Commands;
using Ff.DevSuite.Commands.Attributes;
using Ff.Prefs;
using UnityEngine;

namespace Ff.DevSuite
{
    public static class DevSuiteCliCommandsTests
    {
        public static Action<string> ExternalLogSink;

        [CommandCategory("CliTests")]
        public static class TestCommandsContainer
        {
            public static bool ParameterlessCalled;
            public static string LastStringA;
            public static string LastStringB;
            public static int LastIntVal;
            public static DayOfWeek LastDay;
            public static bool LastBoolVal;
            public static float? LastFloatVal;

            [CommandButton]
            public static void TestParameterless()
            {
                ParameterlessCalled = true;
            }

            [CommandButton(Title = "Multi Word Button")]
            public static void TestMultiWordTitle()
            {
            }

            [CommandButton(CliCommand = "explicit_cli")]
            public static void TestExplicitCli()
            {
            }

            [CommandButton]
            public static void TestStrings(string a, string b = "defaultB")
            {
                LastStringA = a;
                LastStringB = b;
            }

            [CommandButton(Description = "Test various typed parameters")]
            public static void TestVarious(int count, DayOfWeek day = DayOfWeek.Monday, bool flag = false, float? num = null)
            {
                LastIntVal = count;
                LastDay = day;
                LastBoolVal = flag;
                LastFloatVal = num;
            }

            [CommandButton(Description = "Line 1 of description\nLine 2 of description")]
            public static void TestMultilineDesc()
            {
            }

            [CommandButton(CliEnabled = false)]
            public static void TestDisabledCli()
            {
            }
        }

        private class TestMemorySavedPrefs : ISavedPrefs
        {
            private readonly Dictionary<string, object> _data = new();
            public void SetBool(string key, bool? value) => _data[key] = value;
            public void SetInt(string key, int? value) => _data[key] = value;
            public void SetFloat(string key, float? value) => _data[key] = value;
            public void SetString(string key, string value) => _data[key] = value;
            public void SetObject<T>(string key, T value) => _data[key] = value;
            public bool? GetBool(string key, bool? defaultValue = default) => _data.TryGetValue(key, out var v) && v is bool b ? b : defaultValue;
            public int? GetInt(string key, int? defaultValue = default) => _data.TryGetValue(key, out var v) && v is int i ? i : defaultValue;
            public float? GetFloat(string key, float? defaultValue = default) => _data.TryGetValue(key, out var v) && v is float f ? f : defaultValue;
            public string GetString(string key, string defaultValue = default) => _data.TryGetValue(key, out var v) && v is string s ? s : defaultValue;
            public T GetObject<T>(string key, T defaultValue = default) => _data.TryGetValue(key, out var v) && v is T obj ? obj : defaultValue;
            public void DeleteKey(string key) => _data.Remove(key);
            public void Flush() { }
            public void Clear() => _data.Clear();
            public System.Threading.Tasks.Task EnsureReady() => System.Threading.Tasks.Task.CompletedTask;
            public bool Ready => true;
            public void SetSerializer(SerializeFunction serialize, DeserializeFunction deserialize) { }
            public string SessionId { get; } = Guid.NewGuid().ToString();
            public bool Disposed => false;
        }

        public static bool RunAllTests()
        {
            var passed = true;
            Debug.Log("[DevSuite Tests] Starting CLI Commands Tests...");

            void Assert(bool condition, string testName)
            {
                if (condition)
                {
                    Debug.Log($"[DevSuite Tests] PASS: {testName}");
                }
                else
                {
                    Debug.LogError($"[DevSuite Tests] FAIL: {testName}");
                    passed = false;
                }
            }

            // Test 1: SanitizeCliCommand
            Assert(DevSuiteUtils.SanitizeCliCommand("SingleWord", false) == "SingleWord", "Sanitize single word unchanged");
            Assert(DevSuiteUtils.SanitizeCliCommand("  SingleWord  ", false) == "SingleWord", "Sanitize trimmed single word");
            Assert(DevSuiteUtils.SanitizeCliCommand("Multi Word Title", false) == "MultiWordTitle", "Sanitize multi-word removes whitespace");
            Assert(DevSuiteUtils.SanitizeCliCommand("give  gold   100", false) == "givegold100", "Sanitize multi-whitespace");
            Assert(DevSuiteUtils.SanitizeCliCommand(null, false) == string.Empty, "Sanitize null returns empty");

            // Test 2: TokenizeCommandLine
            var tokens1 = DevSuiteUtils.TokenizeCommandLine("cmd arg1 arg2");
            Assert(tokens1.Count == 3 && tokens1[0] == "cmd" && tokens1[1] == "arg1" && tokens1[2] == "arg2", "Tokenize space separated");

            var tokens2 = DevSuiteUtils.TokenizeCommandLine("cmd \"hello world\" 'single quoted'");
            Assert(tokens2.Count == 3 && tokens2[0] == "cmd" && tokens2[1] == "hello world" && tokens2[2] == "single quoted", "Tokenize double and single quotes");

            var tokens3 = DevSuiteUtils.TokenizeCommandLine("   ");
            Assert(tokens3.Count == 0, "Tokenize empty/whitespace");

            // Test 3: DevSuiteContext CLI registration & execution
            var context = new DevSuiteContext();
            var prefs = new TestMemorySavedPrefs();
            context.Settings = new SavedPrefsProperty<PersistentSettings>("DevSuiteContext_Settings", new PersistentSettings(), true, prefs);
            context.Settings.Value.InitializeDefaultsIfNeeded();
            context.CommandsApi = new DevSuiteCommandsApi(context);
            context.AttributesParser = new CommandAttributesParser(context);
            DevSuiteContext.Default = context;
            foreach (var defaultAdapter in DefaultCommandValueAdapters.Get())
            {
                context.CommandsApi.RegisterAdapter(defaultAdapter, true);
            }
            context.AttributesParser.RegisterStatic(typeof(TestCommandsContainer));

            var activeCommands = context.GetActiveCliCommands();
            Assert(activeCommands.Any(c => c.CliCommand == "TestParameterless"), "Found TestParameterless CLI command");
            Assert(activeCommands.Any(c => c.CliCommand == "MultiWordButton"), "Found MultiWordButton converted from Title");
            Assert(activeCommands.Any(c => c.CliCommand == "explicit_cli"), "Found explicit_cli command");
            Assert(activeCommands.Any(c => c.CliCommand == "TestStrings"), "Found TestStrings command");
            Assert(activeCommands.Any(c => c.CliCommand == "TestVarious"), "Found TestVarious command");
            Assert(!activeCommands.Any(c => c.CliCommand == "TestDisabledCli"), "CliEnabled = false is excluded from active CLI commands");

            // Collect log messages
            var logs = new List<string>();
            ExternalLogSink = msg => logs.Add(msg);
            Application.LogCallback logCallback = (msg, stack, type) => logs.Add(msg);
            try
            {
                Application.logMessageReceived += logCallback;
            }
            catch
            {
            }

            try
            {
                // Test 4: Unknown command
                logs.Clear();
                context.ExecuteCliCommand("NonExistentCommand");
                Assert(logs.Any(l => l.Contains("unknown command 'NonExistentCommand'")), "Log message for unknown command");

                // Test 5: Parameterless execution
                logs.Clear();
                TestCommandsContainer.ParameterlessCalled = false;
                context.ExecuteCliCommand("TestParameterless");
                Assert(TestCommandsContainer.ParameterlessCalled, "Parameterless method was invoked");
                Assert(logs.Any(l => l.Contains("executed command 'TestParameterless'")), "Log message for executed parameterless command");

                // Test 6: Parameterless with unexpected args
                logs.Clear();
                context.ExecuteCliCommand("TestParameterless extraArg");
                Assert(logs.Any(l => l.Contains("incorrect arguments of command 'TestParameterless'")), "Log message for parameterless with extra args");

                // Test 7: Parametered execution with arguments
                logs.Clear();
                context.ExecuteCliCommand("TestStrings \"hello world\" \"foo bar\"");
                Assert(TestCommandsContainer.LastStringA == "hello world" && TestCommandsContainer.LastStringB == "foo bar", "TestStrings arguments set correctly");
                Assert(logs.Any(l => l.Contains("executed command 'TestStrings' with arguments: 'hello world' 'foo bar'")), "Log message for TestStrings with arguments");

                // Test 8: Parametered execution with typed arguments (int, enum, bool, float?)
                logs.Clear();
                context.ExecuteCliCommand("TestVarious 42 Friday true 3.14");
                Assert(TestCommandsContainer.LastIntVal == 42 && TestCommandsContainer.LastDay == DayOfWeek.Friday && TestCommandsContainer.LastBoolVal == true && Math.Abs((TestCommandsContainer.LastFloatVal ?? 0) - 3.14f) < 0.001f, "TestVarious typed arguments set correctly");
                Assert(logs.Any(l => l.Contains("executed command 'TestVarious' with arguments: '42' 'Friday' 'true' '3.14'")), "Log message for TestVarious with arguments");

                // Test 9: Parametered execution with invalid type argument
                logs.Clear();
                context.ExecuteCliCommand("TestVarious notAnInt");
                Assert(logs.Any(l => l.Contains("incorrect arguments of command 'TestVarious'")), "Log message for invalid integer argument");

                // Test 10: Parametered execution with too many arguments
                logs.Clear();
                context.ExecuteCliCommand("TestVarious 1 2 3 4 5 6 7");
                Assert(logs.Any(l => l.Contains("incorrect arguments of command 'TestVarious'")), "Log message for too many arguments");

                // Test 11: CLI Command History (persistent data & max 20)
                var history = context.GetCliCommandHistory();
                Assert(history.Contains("TestParameterless"), "History contains TestParameterless");
                Assert(history.Contains("TestStrings \"hello world\" \"foo bar\""), "History contains TestStrings with args");
                Assert(history.Contains("TestVarious 42 Friday true 3.14"), "History contains TestVarious with args");

                for (var i = 1; i <= 25; i++)
                {
                    context.AddCliCommandToHistory($"cmd_{i} arg");
                }
                var updatedHistory = context.GetCliCommandHistory();
                Assert(updatedHistory.Count == 20, "History capped at max 20 entries");
                Assert(updatedHistory[updatedHistory.Count - 1] == "cmd_25 arg", "Latest command is at end of history");
                Assert(updatedHistory[0] == "cmd_6 arg", "Oldest entries properly evicted");

                // Test duplicate move to latest
                context.AddCliCommandToHistory("cmd_10 arg");
                var reorderedHistory = context.GetCliCommandHistory();
                Assert(reorderedHistory.Count == 20, "History count remains 20 after duplicate add");
                Assert(reorderedHistory[reorderedHistory.Count - 1] == "cmd_10 arg", "Duplicate command moved to most recent position");

                // Test 12: Help command
                context.AttributesParser.RegisterStatic(typeof(CommonCommands));
                logs.Clear();
                context.ExecuteCliCommand("help");
                var helpLog = logs.FirstOrDefault(l => l.Contains("Available CLI Commands"));
                Assert(helpLog != null, "Help command logs available CLI commands list");
                Assert(helpLog.Contains("    CliTests/Default/TestMultilineDesc/TestMultilineDesc\n        Line 1 of description\n        Line 2 of description"), "Command and parameters on line 1, descriptions on subsequent lines");

                // Test 13: Tab completion
                var allCliCmds = context.GetActiveCliCommands();
                var tab1 = DevSuiteUtils.TryGetCliTabCompletion("TestP", allCliCmds, out var comp1);
                Assert(tab1 && comp1 == "TestParameterless ", "Tab completes command name from prefix");

                var tab2 = DevSuiteUtils.TryGetCliTabCompletion("Multi", allCliCmds, out var comp2);
                Assert(tab2 && comp2 == "MultiWordButton ", "Tab completes multi-word title command");

                var tab3 = DevSuiteUtils.TryGetCliTabCompletion("exp", allCliCmds, out var comp3);
                Assert(tab3 && comp3 == "explicit_cli ", "Tab completes explicit_cli command");

                var tab4 = DevSuiteUtils.TryGetCliTabCompletion("TestStrings hello ", allCliCmds, out var comp4);
                Assert(tab4 && comp4 == "TestStrings hello \"foo bar\" ", "Tab completes current value for second parameter");

                var tab5 = DevSuiteUtils.TryGetCliTabCompletion("TestVarious 42 Fri", allCliCmds, out var comp5);
                Assert(tab5 && comp5 == "TestVarious 42 Friday ", "Tab completes Enum value from partial text");

                var tab6 = DevSuiteUtils.TryGetCliTabCompletion("TestVarious 42 Friday t", allCliCmds, out var comp6);
                Assert(tab6 && comp6 == "TestVarious 42 Friday true ", "Tab completes bool value from partial text");

                var tab7 = DevSuiteUtils.TryGetCliTabCompletion("unknown_cmd", allCliCmds, out _);
                Assert(!tab7, "Tab does nothing on unknown command prefix");

                var tab8 = DevSuiteUtils.TryGetCliTabCompletion("", allCliCmds, out _);
                Assert(!tab8, "Tab does nothing on empty input");
            }
            finally
            {
                DevSuiteContext.Default = null;
                ExternalLogSink = null;
                try
                {
                    Application.logMessageReceived -= logCallback;
                }
                catch
                {
                }
                context.Dispose();
            }

            Debug.Log($"[DevSuite Tests] CLI Commands Tests Completed. Passed: {passed}");
            return passed;
        }
    }
}
