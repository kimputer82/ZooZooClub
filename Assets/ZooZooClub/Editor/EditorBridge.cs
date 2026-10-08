using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using ZooZooClub.Client;
using ZooZooClub.Core;

namespace ZooZooClub.EditorTools
{
    /// <summary>
    /// 에디터 밖에서 개발 작업을 시킬 수 있게 해 주는 작은 도구.
    /// 프로젝트의 Logs/ZooBridge/cmd.txt 에 "아이디 명령" 한 줄을 쓰면 실행하고,
    /// 결과를 같은 폴더의 out.txt 에 덧붙인다. 빌드에는 포함되지 않는다.
    ///
    /// 명령: ping, refresh, test, play, stop, shot, tmp, delsave, backup, restore, boost, reimport, clear
    /// 플레이 중 점검용: ui <버튼 이름>, cell <x> <y>, dump, state
    /// </summary>
    [InitializeOnLoad]
    public static class EditorBridge
    {
        static readonly string Dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "ZooBridge");
        static readonly string CmdPath = Path.Combine(Dir, "cmd.txt");
        static readonly string OutPath = Path.Combine(Dir, "out.txt");
        static readonly string CompilePath = Path.Combine(Dir, "compile.txt");
        static readonly string ConsolePath = Path.Combine(Dir, "console.txt");
        static readonly string TestsPath = Path.Combine(Dir, "tests.txt");

        static double nextPoll;
        static TestRunnerApi testApi;
        static TestCallbacks callbacks;

        static EditorBridge()
        {
            try
            {
                Directory.CreateDirectory(Dir);
            }
            catch (Exception)
            {
                return;
            }

            EditorApplication.update += Poll;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            Application.logMessageReceived += OnLog;
            WriteOut("- loaded " + State());
        }

        static string State()
        {
            return "compiling=" + EditorApplication.isCompiling
                + " updating=" + EditorApplication.isUpdating
                + " playing=" + EditorApplication.isPlaying;
        }

        static string Stamp()
        {
            return DateTime.Now.ToString("HH:mm:ss");
        }

        static void Append(string path, string text)
        {
            try
            {
                File.AppendAllText(path, text + "\n", Encoding.UTF8);
            }
            catch (Exception)
            {
                // 파일이 잠겨 있으면 이번 기록은 건너뛴다.
            }
        }

        static void WriteOut(string line)
        {
            Append(OutPath, Stamp() + " " + line);
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 0.5;
            if (!File.Exists(CmdPath)) return;

            string text;
            try
            {
                text = File.ReadAllText(CmdPath);
                File.Delete(CmdPath);
            }
            catch (Exception)
            {
                return;
            }

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;

                int space = line.IndexOf(' ');
                string id = space < 0 ? "?" : line.Substring(0, space);
                string command = space < 0 ? line : line.Substring(space + 1).Trim();

                try
                {
                    Execute(id, command);
                }
                catch (Exception e)
                {
                    WriteOut(id + " " + command + " failed: " + e.Message);
                }
            }
        }

        static void Execute(string id, string line)
        {
            int space = line.IndexOf(' ');
            string command = space < 0 ? line : line.Substring(0, space);
            string argument = space < 0 ? "" : line.Substring(space + 1).Trim();

            switch (command)
            {
                case "ui":
                    WriteOut(id + " ui: " + ClickButton(argument));
                    break;

                case "cell":
                    WriteOut(id + " cell: " + ClickCell(argument));
                    break;

                case "dump":
                    WriteOut(id + " dump: " + DumpUi());
                    break;

                case "residents":
                    WriteOut(id + " residents: " + DescribeResidents());
                    break;

                case "state":
                    WriteOut(id + " state: " + DumpState());
                    break;

                case "ping":
                    WriteOut(id + " pong " + State());
                    break;

                case "refresh":
                    AssetDatabase.Refresh();
                    WriteOut(id + " refresh requested " + State());
                    break;

                case "test":
                    RunTests(id);
                    break;

                case "play":
                    EditorApplication.isPlaying = true;
                    WriteOut(id + " play requested");
                    break;

                case "stop":
                    EditorApplication.isPlaying = false;
                    WriteOut(id + " stop requested");
                    break;

                case "shot":
                    if (!EditorApplication.isPlaying)
                    {
                        WriteOut(id + " shot skipped: not playing");
                        break;
                    }
                    int scale;
                    if (!int.TryParse(argument, out scale) || scale < 1) scale = 1;
                    ScreenCapture.CaptureScreenshot(Path.Combine(Dir, "shot-" + id + ".png"), Math.Min(scale, 4));
                    WriteOut(id + " shot requested");
                    break;

                case "tmp":
                    TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);
                    WriteOut(id + " tmp essentials import requested");
                    break;

                case "delsave":
                    DeleteSave();
                    WriteOut(id + " save deleted");
                    break;

                case "reimport":
                    ArtImportSettings.ReimportArt();
                    WriteOut(id + " art reimported");
                    break;

                case "backup":
                    WriteOut(id + " backup: " + CopySave(SavePath, SavePath + ".backup"));
                    break;

                case "restore":
                    WriteOut(id + " restore: " + CopySave(SavePath + ".backup", SavePath));
                    break;

                case "boost":
                    WriteOut(id + " boost " + BoostSave());
                    break;

                case "clear":
                    File.WriteAllText(ConsolePath, "");
                    WriteOut(id + " console cleared");
                    break;

                default:
                    WriteOut(id + " unknown command: " + command);
                    break;
            }
        }

        // ---------- 플레이 화면 점검 ----------

        static readonly string UiDumpPath = Path.Combine(Dir, "ui.txt");
        static readonly string StateDumpPath = Path.Combine(Dir, "state.json");

        /// <summary>이름이 같은 켜져 있는 버튼을 찾아 누른 것처럼 동작시킨다.</summary>
        static string ClickButton(string name)
        {
            if (!EditorApplication.isPlaying) return "skipped, not playing";

            foreach (var button in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>())
            {
                var go = button.gameObject;
                if (go.name != name || !go.scene.IsValid() || !go.activeInHierarchy) continue;
                if (!button.interactable) return name + " is disabled";
                button.onClick.Invoke();
                return name + " clicked";
            }
            return name + " not found";
        }

        static string ClickCell(string argument)
        {
            if (!EditorApplication.isPlaying) return "skipped, not playing";

            var parts = argument.Split(' ');
            int x, y;
            if (parts.Length != 2 || !int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y))
                return "usage: cell <x> <y>";

            foreach (var world in Resources.FindObjectsOfTypeAll<WorldView>())
            {
                if (!world.gameObject.scene.IsValid() || !world.gameObject.activeInHierarchy) continue;
                world.SimulateClick(x, y);
                return "clicked " + x + "," + y;
            }
            return "world not found";
        }

        /// <summary>주민들의 위치와 상태를 한 줄로 돌려준다.</summary>
        static string DescribeResidents()
        {
            if (!EditorApplication.isPlaying) return "not playing";
            foreach (var world in Resources.FindObjectsOfTypeAll<WorldView>())
            {
                if (world.gameObject.scene.IsValid()) return world.DescribeResidents();
            }
            return "no world";
        }

        /// <summary>화면에 켜져 있는 UI의 글자와 버튼 상태를 ui.txt 에 적는다.</summary>
        static string DumpUi()
        {
            if (!EditorApplication.isPlaying) return "skipped, not playing";

            var builder = new StringBuilder();
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                var go = canvas.gameObject;
                if (!go.scene.IsValid() || !go.activeInHierarchy) continue;
                DumpNode(go.transform, 0, builder);
            }
            File.WriteAllText(UiDumpPath, builder.ToString(), Encoding.UTF8);
            return "written";
        }

        static void DumpNode(Transform node, int depth, StringBuilder builder)
        {
            if (!node.gameObject.activeSelf) return;

            var button = node.GetComponent<UnityEngine.UI.Button>();
            var text = node.GetComponent<TMPro.TMP_Text>();
            if (button != null || text != null || depth <= 2)
            {
                builder.Append(' ', depth * 2).Append(node.name);
                if (button != null) builder.Append(button.interactable ? " [button]" : " [button disabled]");
                if (text != null) builder.Append(" : ").Append(text.text.Replace("\n", " / "));
                builder.Append('\n');
            }

            for (int i = 0; i < node.childCount; i++) DumpNode(node.GetChild(i), depth + 1, builder);
        }

        static string DumpState()
        {
            if (!EditorApplication.isPlaying) return "skipped, not playing";
            var session = GameSession.Instance;
            if (session == null || session.Game == null) return "no session";

            File.WriteAllText(StateDumpPath, JsonUtility.ToJson(session.Game.State, true), Encoding.UTF8);
            return "written";
        }

        // ---------- 저장 파일 (테스트용) ----------

        static string SavePath
        {
            get { return Path.Combine(Application.persistentDataPath, "save.json"); }
        }

        [MenuItem("ZooZooClub/저장 파일 삭제 (새 게임)")]
        static void DeleteSave()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Zoo] 플레이를 멈춘 뒤에 저장 파일을 삭제하세요.");
                return;
            }
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }

        [MenuItem("ZooZooClub/테스트용: 레벨 6과 코인 5000 받기")]
        static void BoostSaveMenu()
        {
            Debug.Log("[Zoo] boost: " + BoostSave());
        }

        static string CopySave(string from, string to)
        {
            if (EditorApplication.isPlaying) return "skipped, stop play mode first";
            if (!File.Exists(from)) return "skipped, nothing to copy";
            File.Copy(from, to, true);
            return "done";
        }

        /// <summary>후반 콘텐츠를 바로 시험해 볼 수 있게 저장 파일의 레벨, 코인, 재료를 올린다.</summary>
        static string BoostSave()
        {
            if (EditorApplication.isPlaying) return "skipped: stop play mode first";
            GameState state;
            if (File.Exists(SavePath))
            {
                state = JsonUtility.FromJson<GameState>(File.ReadAllText(SavePath));
            }
            else
            {
                // 저장 파일이 아직 없으면 새 게임에서 시작한다.
                var asset = Resources.Load<TextAsset>("catalog");
                if (asset == null) return "skipped: catalog not found";
                var catalog = JsonUtility.FromJson<GameCatalog>(asset.text);
                state = GameState.CreateNew(catalog, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }

            state.Normalize();
            state.level = Math.Max(state.level, 6);
            state.coins = Math.Max(state.coins, 5000);
            state.inventory.Clear();
            state.inventory.Add(new ItemAmount("carrot", 6));
            state.inventory.Add(new ItemAmount("wheat", 8));
            state.inventory.Add(new ItemAmount("strawberry", 6));
            state.inventory.Add(new ItemAmount("corn", 4));
            state.inventory.Add(new ItemAmount("pumpkin", 4));
            File.WriteAllText(SavePath, JsonUtility.ToJson(state));
            return "done";
        }

        // ---------- 컴파일 결과 ----------

        static void OnCompilationStarted(object context)
        {
            try
            {
                File.WriteAllText(CompilePath, Stamp() + " compilation started\n");
            }
            catch (Exception)
            {
            }
        }

        static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            foreach (var message in messages)
            {
                if (message.type != CompilerMessageType.Error) continue;
                Append(CompilePath, "ERROR " + message.file + "(" + message.line + "): " + message.message);
            }
        }

        static void OnCompilationFinished(object context)
        {
            Append(CompilePath, Stamp() + " compilation finished");
            WriteOut("- compilation finished");
        }

        // ---------- 콘솔 오류 ----------

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            bool tagged = condition != null && condition.StartsWith("[Zoo]");
            if (type == LogType.Log && !tagged) return;
            if (type == LogType.Warning && !tagged) return;

            var builder = new StringBuilder();
            builder.Append(Stamp()).Append(' ').Append(type).Append(": ").Append(condition);
            if (type != LogType.Log && !string.IsNullOrEmpty(stackTrace))
            {
                var lines = stackTrace.Split('\n');
                for (int i = 0; i < lines.Length && i < 6; i++)
                {
                    if (lines[i].Length > 0) builder.Append("\n    ").Append(lines[i]);
                }
            }
            Append(ConsolePath, builder.ToString());
        }

        // ---------- 테스트 ----------

        static void RunTests(string id)
        {
            if (testApi == null) testApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            if (callbacks != null) testApi.UnregisterCallbacks(callbacks);

            callbacks = new TestCallbacks(id);
            testApi.RegisterCallbacks(callbacks);
            testApi.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
            WriteOut(id + " tests started");
        }

        class TestCallbacks : ICallbacks
        {
            readonly string id;

            public TestCallbacks(string id)
            {
                this.id = id;
            }

            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                var builder = new StringBuilder();
                builder.AppendLine("passed=" + result.PassCount
                    + " failed=" + result.FailCount
                    + " skipped=" + result.SkipCount
                    + " inconclusive=" + result.InconclusiveCount);
                Collect(result, builder);

                try
                {
                    File.WriteAllText(TestsPath, builder.ToString(), Encoding.UTF8);
                }
                catch (Exception)
                {
                }

                WriteOut(id + " tests finished passed=" + result.PassCount + " failed=" + result.FailCount);
            }

            static void Collect(ITestResultAdaptor result, StringBuilder builder)
            {
                if (result.HasChildren)
                {
                    foreach (var child in result.Children) Collect(child, builder);
                    return;
                }

                builder.AppendLine(result.TestStatus + " " + result.Test.FullName);
                if (result.TestStatus == TestStatus.Failed)
                {
                    builder.AppendLine("    " + result.Message);
                    builder.AppendLine("    " + result.StackTrace);
                }
            }
        }
    }
}
