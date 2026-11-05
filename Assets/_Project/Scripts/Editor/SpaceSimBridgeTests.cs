using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace SpaceSim.Editor
{
    /// <summary>
    /// Runs Unity Test Runner suites for <see cref="SpaceSimBridge"/> and writes a summary to
    /// Temp/SpaceSimBridge/tests.txt. Callbacks are registered on every domain load, so PlayMode runs
    /// (which reload the domain) still report.
    /// </summary>
    [InitializeOnLoad]
    static class SpaceSimBridgeTests
    {
        static readonly string ResultPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/SpaceSimBridge/tests.txt"));
        static TestRunnerApi api;

        static SpaceSimBridgeTests()
        {
            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks());
        }

        public static string Run(string mode)
        {
            TestMode m = mode == "playmode" ? TestMode.PlayMode : TestMode.EditMode;
            if (File.Exists(ResultPath)) File.Delete(ResultPath);
            api.Execute(new ExecutionSettings(new Filter { testMode = m }));
            return "running " + m + " tests → " + ResultPath;
        }

        sealed class Callbacks : ICallbacks
        {
            readonly List<string> failures = new List<string>();

            public void RunStarted(ITestAdaptor testsToRun) => failures.Clear();

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren && result.TestStatus == TestStatus.Failed)
                    failures.Add(result.FullName + "\n    " + (result.Message ?? "").Trim().Replace("\n", "\n    "));
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                var sb = new StringBuilder();
                sb.AppendLine("passed=" + result.PassCount + " failed=" + result.FailCount + " skipped=" + result.SkipCount +
                              " inconclusive=" + result.InconclusiveCount + " duration=" + result.Duration.ToString("0.0") + "s");
                foreach (string f in failures) sb.AppendLine("FAILED " + f);
                Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
                File.WriteAllText(ResultPath, sb.ToString(), Encoding.UTF8);
            }
        }
    }
}
