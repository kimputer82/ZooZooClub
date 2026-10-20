using System;
using System.Globalization;
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
    /// 명령: ping, refresh, test, build(윈도우 빌드), buildweb(웹 빌드), play, stop, shot, tmp, delsave, backup, restore, stash, unstash, boost, reimport, clear, quiz(문제 엑셀 다시 읽기)
    /// 플레이 중 점검용: login <이름>[|방코드], logout, ui <버튼 이름>, cell <x> <y>, dump, state, grade <물체 번호> <등급 id>, place <id> <x> <y> [방향], give <아이템|gems|coins|solved> <수>, visit <이름>, home
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

                case "raid":
                    WriteOut(id + " raid: " + Raid(argument));
                    break;

                case "login":
                    LogIn(id, argument);
                    break;

                case "logout":
                    WriteOut(id + " logout: " + LogOut());
                    break;

                case "room":
                    Room(id, argument);
                    break;

                case "quiz":
                    WriteOut(id + " quiz: " + QuizExcelImporter.ImportNow());
                    break;

                case "grade":
                    WriteOut(id + " grade: " + SetGrade(argument));
                    break;

                case "place":
                    WriteOut(id + " place: " + Place(argument));
                    break;

                case "visit":
                    WriteOut(id + " visit: " + VisitLocal(argument));
                    break;

                case "home":
                    if (GameSession.Instance != null) GameSession.Instance.EndVisit();
                    WriteOut(id + " home");
                    break;

                case "give":
                    WriteOut(id + " give: " + Give(argument));
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

                case "build":
                    WriteOut(id + " build: " + BuildWindows.Build());
                    break;

                case "buildweb":
                    WriteOut(id + " buildweb: " + BuildWeb.Build());
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
                    AudioImportSettings.ReimportAudio();
                    WriteOut(id + " art and audio reimported");
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

                case "stash":
                    WriteOut(id + " stash: " + StashSave());
                    break;

                case "unstash":
                    WriteOut(id + " unstash: " + UnstashSave());
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
        static void LogIn(string id, string name)
        {
            if (!EditorApplication.isPlaying) { WriteOut(id + " login: skipped, not playing"); return; }
            var session = GameSession.Instance;
            if (session == null) { WriteOut(id + " login: no session"); return; }
            // "이름|방코드" 꼴이면 그 방으로 들어간다(방이 있는지는 확인하지 않고, 서버 저장은 가져온다).
            string code = "";
            int bar = name.IndexOf('|');
            if (bar >= 0)
            {
                code = ZooZooClub.Core.RoomCode.Normalize(name.Substring(bar + 1)) ?? "";
                name = name.Substring(0, bar);
            }
            string cleanName = name;
            if (code.Length == 0)
            {
                string error = session.Login(cleanName, code, null);
                WriteOut(id + " login: " + (error ?? ("ok, save=" + Path.GetFileName(session.SavePath))));
                return;
            }
            Online.FetchSave(code, cleanName, (json, fetchError) =>
            {
                if (fetchError != null) { WriteOut(id + " login: " + fetchError); return; }
                string error = session.Login(cleanName, code, json);
                WriteOut(id + " login: " + (error ?? ("ok, save=" + Path.GetFileName(session.SavePath) + " room=" + code
                    + (json != null ? " cloud=" + json.Length + "B" : " cloud=none"))));
            });
        }

        /// <summary>room &lt;비밀번호&gt; &lt;방 이름&gt; : 서버에 방을 만들고 코드를 적는다. room close|open|clear &lt;코드&gt; &lt;비밀번호&gt; 도 된다.</summary>
        static void Room(string id, string argument)
        {
            if (!EditorApplication.isPlaying) { WriteOut(id + " room: skipped, not playing"); return; }
            var parts = argument.Split(new[] { ' ' }, 3);
            if (parts.Length >= 3 && (parts[0] == "close" || parts[0] == "open" || parts[0] == "clear"))
            {
                string code = ZooZooClub.Core.RoomCode.Normalize(parts[1]) ?? parts[1];
                System.Action<string> done = error => WriteOut(id + " room " + parts[0] + ": " + (error ?? "ok"));
                if (parts[0] == "clear") Online.ClearPlayers(code, parts[2], done);
                else Online.SetRoomOpen(code, parts[0] == "open", parts[2], done);
                return;
            }
            if (parts.Length < 2) { WriteOut(id + " room: usage room <password> <name>"); return; }
            string newCode = ZooZooClub.Core.RoomCode.Generate(new System.Random());
            Online.OpenRoom(newCode, parts.Length >= 3 ? parts[1] + " " + parts[2] : parts[1], parts[0],
                error => WriteOut(id + " room: " + (error ?? ("ok code=" + newCode))));
        }

        static string LogOut()
        {
            if (!EditorApplication.isPlaying) return "skipped, not playing";
            var session = GameSession.Instance;
            if (session == null) return "no session";
            if (session.Game == null) return "not logged in";
            session.Logout();
            return "ok";
        }

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
            // 버튼이 아니라 두 번 눌러야 하는 조각(창고의 보따리 등)이면 두 번 누른 것으로 친다.
            foreach (var target in Resources.FindObjectsOfTypeAll<DoubleClick>())
            {
                var go = target.gameObject;
                if (go.name != name || !go.scene.IsValid() || !go.activeInHierarchy) continue;
                if (target.Action != null) target.Action();
                return name + " double-clicked";
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

        /// <summary>
        /// "raid start"는 침략을 지금 시작하고, "raid"만 쓰면 지금 상황을 한 줄로 돌려준다:
        /// 진행 여부, 몇 번째인지, 다음 침략까지 남은 시간, 침략자마다 "종류:x,y:체력".
        /// </summary>
        /// <summary>
        /// 점검용: 놓인 방어 시설의 등급을 정해 준다. 그림은 플레이를 다시 시작하면 바뀐다.
        /// 진행 중인 저장을 건드리지 않도록 stash로 치워 둔 뒤에만 쓴다.
        /// </summary>
        static string SetGrade(string argument)
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var session = GameSession.Instance;
            if (session == null || session.Game == null) return "no game";

            var parts = argument.Split(' ');
            int objectId;
            if (parts.Length != 2 || !int.TryParse(parts[0], out objectId)) return "usage: grade <objectId> <gradeId>";
            var obj = session.Game.FindObject(objectId);
            if (obj == null) return "no object " + objectId;
            if (session.Game.Catalog.GetGrade(parts[1]) == null) return "no grade " + parts[1];

            obj.gradeId = parts[1];
            session.Save();
            return obj.defId + " " + objectId + " is now " + parts[1];
        }

        /// <summary>
        /// 점검용: place &lt;defId&gt; &lt;x&gt; &lt;y&gt; [방향] 으로 배치물을 사서 놓는다.
        /// 레벨, 코인, 맞힌 문제 수가 모자라면 채워 주므로, 점검용 이름으로 들어온 뒤에만 쓴다.
        /// </summary>
        static string Place(string argument)
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var session = GameSession.Instance;
            if (session == null || session.Game == null) return "no game";

            var parts = argument.Split(' ');
            int x, y, rotation = 0;
            if (parts.Length < 3 || !int.TryParse(parts[1], out x) || !int.TryParse(parts[2], out y)
                || (parts.Length > 3 && !int.TryParse(parts[3], out rotation)))
                return "usage: place <defId> <x> <y> [rotation]";

            var game = session.Game;
            game.State.level = Math.Max(game.State.level, 10);
            game.State.coins = Math.Max(game.State.coins, 100000);
            game.State.quizSolved = Math.Max(game.State.quizSolved, 1000);
            int objectId;
            var result = game.BuyAndPlace(parts[0], x, y, out objectId, rotation);
            session.Save();
            return parts[0] + " at " + x + "," + y + " rot=" + rotation + " -> " + result + " id=" + objectId;
        }

        /// <summary>점검용: give &lt;아이템 id|gems|coins&gt; &lt;수&gt; 로 지금 게임에 넣어 준다. 점검용 이름으로 들어온 뒤에만 쓴다.</summary>
        static string Give(string argument)
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var session = GameSession.Instance;
            if (session == null || session.Game == null) return "no game";

            var parts = argument.Split(' ');
            int amount;
            if (parts.Length != 2 || !int.TryParse(parts[1], out amount)) return "usage: give <itemId|gems|coins> <amount>";

            var state = session.Game.State;
            if (parts[0] == "gems") state.gems = Math.Max(0, state.gems + amount);
            else if (parts[0] == "coins") state.coins = Math.Max(0, state.coins + amount);
            else if (parts[0] == "solved") state.quizSolved = Math.Max(0, state.quizSolved + amount);
            else
            {
                var entry = state.inventory.Find(e => e.itemId == parts[0]);
                if (entry != null) entry.amount += amount;
                else state.inventory.Add(new ItemAmount(parts[0], amount));
            }
            session.Save();
            return parts[0] + " +" + amount + " gems=" + state.gems;
        }

        /// <summary>점검용: visit &lt;이름&gt; 은 이 컴퓨터에 있는 그 이름의 저장으로 마을 구경을 해 본다(서버 없이).</summary>
        static string VisitLocal(string name)
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var session = GameSession.Instance;
            if (session == null || session.Game == null) return "no game";
            string path = SaveFiles.PathFor(name);
            if (path == null || !File.Exists(path)) return "no save for " + name;
            return session.Visit(File.ReadAllText(path)) ?? "ok";
        }

        static string Raid(string argument)
        {
            if (!EditorApplication.isPlaying) return "not playing";
            var session = GameSession.Instance;
            if (session == null || session.Game == null) return "no game";
            var game = session.Game;

            if (argument == "start") game.StartRaidNow();

            var text = new StringBuilder();
            text.Append("active=").Append(game.RaidActive)
                .Append(" wave=").Append(game.RaidWave)
                .Append(" next=").Append(game.SecondsUntilRaid)
                .Append(" coins=").Append(game.State.coins);
            foreach (var invader in game.Invaders)
            {
                text.Append(' ').Append(invader.def.id).Append(':')
                    .Append(invader.entered ? "" : "wait:")
                    .Append(invader.x.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                    .Append(invader.y.ToString("0.0", CultureInfo.InvariantCulture)).Append(':')
                    .Append(Mathf.CeilToInt(invader.hp));
            }
            return text.ToString();
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

        /// <summary>
        /// 개발 도구가 다루는 저장 파일: 마지막으로 들어온 이름의 저장 파일.
        /// 아직 아무도 들어온 적이 없으면 이름별 저장이 생기기 전의 옛 파일.
        /// </summary>
        static string SavePath
        {
            get
            {
                string path = SaveFiles.PathFor(SaveFiles.LastName);
                return path ?? SaveFiles.LegacyPath;
            }
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

        /// <summary>
        /// 점검하는 동안 진행 중인 저장 파일을 잠시 치워 둔다. 점검이 끝나면 unstash로 그대로 되돌린다.
        /// 이미 치워 둔 것이 있으면 덮어쓰지 않는다.
        /// </summary>
        static string StashSave()
        {
            if (EditorApplication.isPlaying) return "skipped, stop play mode first";
            string stash = SavePath + ".stash";
            if (File.Exists(stash)) return "skipped, a stash already exists";
            if (!File.Exists(SavePath)) return "skipped, nothing to copy";
            File.Copy(SavePath, stash, false);
            return "done";
        }

        static string UnstashSave()
        {
            if (EditorApplication.isPlaying) return "skipped, stop play mode first";
            string stash = SavePath + ".stash";
            if (!File.Exists(stash)) return "skipped, no stash";
            File.Copy(stash, SavePath, true);
            File.Delete(stash);
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
