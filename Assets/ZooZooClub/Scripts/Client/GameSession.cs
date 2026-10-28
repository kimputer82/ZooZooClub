using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using ZooZooClub.Core;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 게임 시작점. 씬에 아무것도 배치하지 않아도 플레이를 시작하면 스스로 만들어져서 데이터를 읽고
    /// 로그인 화면을 띄운다. 학생이 이름을 적으면 그 이름의 저장 파일을 읽어 월드와 UI를 세우고,
    /// 나가면 저장한 뒤 다시 로그인 화면으로 돌아간다.
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        const float SaveDelaySeconds = 2f;

        public static GameSession Instance { get; private set; }

        /// <summary>지금 들어와 있는 학생의 게임. 로그인 화면에 있을 때는 null.</summary>
        public Game Game { get; private set; }

        /// <summary>지금 쓰고 있는 저장 파일. 로그인 화면에 있을 때는 null.</summary>
        public string SavePath { get; private set; }

        GameCatalog catalog;
        LoginScreen login;
        TeacherScreen teacher;
        GameObject worldObject;
        GameObject hudObject;
        bool dirty;
        float saveAt;
        bool logoutRequested;

        // 친구 마을 구경: 내 월드와 화면은 숨겨 두고, 친구의 저장으로 보기 전용 월드를 따로 띄운다.
        GameObject visitWorldObject;
        GameObject visitHudObject;
        bool endVisitRequested;
        // 구경하는 동안 내 마을의 다음 침략까지 남은 시간(초). 돌아오면 이어서 센다.
        long raidSecondsLeft;

        /// <summary>지금 친구 마을을 구경하는 중인지.</summary>
        public bool Visiting { get { return visitWorldObject != null; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("ZooZooClub");
            DontDestroyOnLoad(go);
            go.AddComponent<GameSession>();
        }

        void Awake()
        {
            Instance = this;
            Application.runInBackground = true;

            var asset = Resources.Load<TextAsset>("catalog");
            if (asset == null)
            {
                Debug.LogError("[Zoo] Resources/catalog.json 을 찾을 수 없습니다.");
                enabled = false;
                return;
            }

            catalog = JsonUtility.FromJson<GameCatalog>(asset.text);

            // 문제 은행은 따로 고치기 쉽게 quiz.json에 둔다. 없으면 문제 풀기만 비어 있게 된다.
            var quizAsset = Resources.Load<TextAsset>("quiz");
            QuizBank bank = null;
            if (quizAsset != null)
            {
                try
                {
                    bank = JsonUtility.FromJson<QuizBank>(quizAsset.text);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Zoo] quiz.json 을 읽지 못했습니다: " + e.Message);
                }
            }
            DefaultQuiz = bank ?? new QuizBank();
            catalog.SetQuiz(DefaultQuiz);

            foreach (var error in catalog.Validate())
            {
                Debug.LogError("[Zoo] catalog: " + error);
            }

            // 로그인 화면과 게임 화면이 함께 쓰는 입력 처리기. 화면이 바뀌어도 남아 있도록 여기에 둔다.
            if (EventSystem.current == null)
            {
                var eventSystem = new GameObject("EventSystem");
                eventSystem.transform.SetParent(transform, false);
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<InputSystemUIInputModule>();
            }

            Sound.Ensure(transform);
            Online.Ensure(transform);

            teacher = new GameObject("Teacher").AddComponent<TeacherScreen>();
            teacher.transform.SetParent(transform, false);
            teacher.Init(() =>
            {
                teacher.Hide();
                login.Show();
            });
            teacher.Hide();

            login = new GameObject("Login").AddComponent<LoginScreen>();
            login.transform.SetParent(transform, false);
            login.Init(Login, () =>
            {
                login.Hide();
                teacher.Show();
            });
            login.Show();

            Debug.Log("[Zoo] ready. quiz=" + (catalog.quiz != null ? catalog.quiz.questions.Count : 0)
                + " saves=" + SaveFiles.Folder);
        }

        /// <summary>
        /// 그 이름으로 들어간다. 저장 파일이 있으면 이어서, 없으면 새 마을로 시작한다.
        /// 들어가지 못하면 아이에게 보여 줄 이유를, 들어갔으면 null을 돌려준다.
        /// </summary>
        public string Login(string rawName)
        {
            return Login(rawName, "", null);
        }

        public string Login(string rawName, string roomCode)
        {
            return Login(rawName, roomCode, null);
        }

        /// <summary>게임에 들어 있는 기본 문제 은행(quiz.json). 방 문제가 없을 때 쓴다.</summary>
        public QuizBank DefaultQuiz { get; private set; }

        /// <summary>
        /// 들어가려는 방에 선생님이 올린 문제(문제 은행 JSON 글). 로그인 화면이 방을 확인하면서 받아 두고,
        /// Login이 그 방으로 들어갈 때 한 번 쓰고 비운다. 비어 있으면 기본 문제를 쓴다.
        /// </summary>
        public string PendingRoomQuiz { get; set; }

        /// <summary>들어가려는 방에서 쓸 저장 열쇠(비밀번호로 만든 값). 로그인 화면이 정해 두고 Login이 가져간다.</summary>
        public string PendingLock { get; set; }

        /// <summary>이번에 이 비밀번호로 이름을 처음 잠그는가(새 이름이거나 예전 저장). 그러면 들어가자마자 서버에 올린다.</summary>
        public bool PendingClaim { get; set; }

        /// <summary>이번 접속의 저장 열쇠. 방에 들어오지 않았으면 빈 문자열.</summary>
        public string CurrentLock { get; private set; }

        /// <summary>이 컴퓨터에 있는 그 이름의 저장에 적힌 열쇠(없으면 빈 문자열). 서버가 안 될 때 비밀번호 확인에 쓴다.</summary>
        public static string LocalLockOf(string name)
        {
            var state = LoadState(SaveFiles.PathFor(name));
            return state != null ? state.roomLock ?? "" : "";
        }

        /// <summary>이번 접속에서 실제로 연결된 방 코드. 빈 문자열이면 혼자 하는 중(저장에 적힌 방은 기억만 한다).</summary>
        public string ActiveRoom { get; private set; }

        /// <summary>들어간 직후 화면에 잠깐 보여 줄 말(방이 닫혀 있어 혼자 하기로 들어갔을 때 등).</summary>
        public string LoginNotice { get; set; }

        /// <summary>
        /// roomCode는 정리된 방 코드(없으면 빈 문자열). 방이 있는지는 부르는 쪽에서 미리 확인한다.
        /// cloudJson은 서버에 있던 이 이름의 저장(없으면 null)이며, 이 컴퓨터의 저장보다 새것이면 그것으로 시작한다.
        /// 방 코드가 비어 있으면 혼자 하지만, 저장에 적혀 있던 방 코드는 지우지 않고 기억해 둔다(다음에 자동으로 다시 들어간다).
        /// </summary>
        static QuizBank ParseRoomQuiz(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var bank = JsonUtility.FromJson<QuizBank>(json);
                if (bank == null || bank.questions == null || bank.questions.Count == 0) return null;
                return bank;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Zoo] 방 문제를 읽지 못해 기본 문제를 씁니다: " + e.Message);
                return null;
            }
        }

        public string Login(string rawName, string roomCode, string cloudJson)
        {
            if (catalog == null) return "게임 데이터를 읽지 못했어요.";
            if (Game != null) return "이미 들어와 있어요.";

            string error;
            string name = PlayerName.Clean(rawName, out error);
            if (name == null) return error;

            string path = SaveFiles.PathFor(name);
            var clock = new SystemClock();
            bool existed = File.Exists(path);
            var state = LoadState(path);

            // 이름별 저장이 생기기 전에 하던 저장 파일이 있으면, 이 컴퓨터의 첫 이름이 그 마을을 이어받는다.
            bool adopted = false;
            if (state == null && !existed && !SaveFiles.AnyProfileExists())
            {
                state = LoadState(SaveFiles.LegacyPath);
                adopted = state != null;
            }

            // 서버 저장이 있으면 더 새것을 고른다(같으면 서버 것).
            bool fromCloud = false;
            var cloud = ParseState(cloudJson);
            if (cloud != null && (state == null || cloud.savedAt >= state.savedAt))
            {
                state = cloud;
                fromCloud = true;
            }

            bool isNew = state == null;
            if (isNew) state = GameState.CreateNew(catalog, clock.Now);
            if (string.IsNullOrEmpty(state.playerName) || adopted) state.playerName = name;
            ActiveRoom = roomCode ?? "";
            bool roomChanged = ActiveRoom.Length > 0 && state.roomCode != ActiveRoom;
            if (ActiveRoom.Length > 0) state.roomCode = ActiveRoom;
            CurrentLock = ActiveRoom.Length > 0 ? PendingLock ?? "" : "";
            bool claim = ActiveRoom.Length > 0 && PendingClaim;
            bool lockChanged = ActiveRoom.Length > 0 && state.roomLock != CurrentLock;
            if (ActiveRoom.Length > 0) state.roomLock = CurrentLock;
            PendingLock = null;
            PendingClaim = false;

            // 방에 선생님이 올린 문제가 있으면 그 문제로, 아니면 기본 문제로 푼다.
            QuizBank roomQuiz = ActiveRoom.Length > 0 ? ParseRoomQuiz(PendingRoomQuiz) : null;
            PendingRoomQuiz = null;
            catalog.SetQuiz(roomQuiz ?? DefaultQuiz);

            SavePath = path;
            Game = new Game(catalog, state, clock);
            Game.ResumeRaidClock();
            Game.Changed += MarkDirty;

            var world = new GameObject("World").AddComponent<WorldView>();
            world.transform.SetParent(transform, false);
            world.Init(Game);
            worldObject = world.gameObject;

            // 브라우저는 클릭 뒤에야 소리를 낼 수 있으므로 배경음은 여기(시작 버튼을 누른 뒤)서 튼다.
            Sound.PlayMusic();

            var hud = new GameObject("Hud").AddComponent<GameHud>();
            hud.transform.SetParent(transform, false);
            hud.Init(Game, world);
            hud.LogoutRequested += RequestLogout;
            hudObject = hud.gameObject;

            login.Hide();
            TextEntry.HideAll();
            SaveFiles.LastName = name;

            // 방에 들어왔으면 내 순위 정보를 올리기 시작한다.
            if (ActiveRoom.Length > 0)
            {
                var reporter = hud.gameObject.AddComponent<RankReporter>();
                reporter.Init(Game);
            }
            if (!string.IsNullOrEmpty(LoginNotice))
            {
                hud.Toast(LoginNotice);
                LoginNotice = null;
            }

            // 새 마을이나 이어받은 마을은 바로 한 번 저장해서, 다음에 이름 목록에 나오게 한다.
            if (isNew || adopted || roomChanged || fromCloud || claim || lockChanged) Save();
            // 이 비밀번호로 이름을 처음 잠글 때는 서버에 바로 올려서, 다른 사람이 그 이름을 먼저 차지하지 못하게 한다.
            if (claim) cloudAt = 0f;

            Debug.Log("[Zoo] started. player=" + state.playerName + (isNew ? " (new)" : adopted ? " (adopted)" : "")
                + (ActiveRoom.Length > 0 ? " room=" + ActiveRoom : "") + (fromCloud ? " (cloud)" : "")
                + " level=" + state.level + " coins=" + state.coins
                + " objects=" + state.objects.Count + " orders=" + state.orders.Count
                + " quiz=" + Game.QuizCount);
            return null;
        }

        /// <summary>
        /// 친구의 저장(JSON)으로 그 마을을 구경하러 간다. 내 마을은 그대로 숨겨 두고, 침략 시계는 멈춘다.
        /// 갈 수 없으면 이유를, 갔으면 null을 돌려준다.
        /// </summary>
        public string Visit(string json)
        {
            if (Game == null) return "먼저 마을에 들어와야 해요.";
            if (Visiting) return "이미 구경하는 중이에요.";
            if (Game.RaidActive) return "침략을 막는 중에는 구경 갈 수 없어요.";
            var state = ParseState(json);
            if (state == null) return "친구 마을을 읽지 못했어요.";

            raidSecondsLeft = NonNegative(Game.State.nextRaidAt - Game.Now);
            if (dirty) Save();

            var visitGame = new Game(catalog, state, new SystemClock());
            worldObject.SetActive(false);
            hudObject.SetActive(false);

            var world = new GameObject("VisitWorld").AddComponent<WorldView>();
            world.transform.SetParent(transform, false);
            world.ReadOnly = true;
            world.Init(visitGame);
            visitWorldObject = world.gameObject;

            var hud = new GameObject("VisitHud").AddComponent<VisitHud>();
            hud.transform.SetParent(transform, false);
            hud.Init(visitGame);
            hud.BackRequested += () => endVisitRequested = true;
            visitHudObject = hud.gameObject;

            Debug.Log("[Zoo] visiting " + state.playerName);
            return null;
        }

        static long NonNegative(long value)
        {
            return Math.Max(0L, value);
        }

        /// <summary>구경을 마치고 내 마을로 돌아온다.</summary>
        public void EndVisit()
        {
            endVisitRequested = false;
            if (!Visiting) return;

            Destroy(visitWorldObject);
            if (visitHudObject != null) Destroy(visitHudObject);
            visitWorldObject = null;
            visitHudObject = null;

            if (worldObject != null)
            {
                worldObject.SetActive(true);
                worldObject.GetComponent<WorldView>().FocusCamera();
            }
            if (hudObject != null) hudObject.SetActive(true);

            // 구경하는 동안 멈춰 둔 침략 시계를 이어서 센다. 돌아오자마자 쳐들어오지 않도록 경고 시간만큼은 남긴다.
            if (Game != null && Game.RaidsUnlocked)
            {
                long warning = Math.Max(0, catalog.balance.raidWarningSeconds);
                Game.State.nextRaidAt = Game.Now + Math.Max(raidSecondsLeft, warning);
                MarkDirty();
            }
        }

        /// <summary>저장하고 로그인 화면으로 돌아간다.</summary>
        public void Logout()
        {
            logoutRequested = false;
            if (Game == null) return;
            EndVisit();

            Save();
            FlushCloudSave();
            Game.Changed -= MarkDirty;
            if (hudObject != null) Destroy(hudObject);
            if (worldObject != null) Destroy(worldObject);
            hudObject = null;
            worldObject = null;
            Game = null;
            SavePath = null;
            ActiveRoom = "";
            CurrentLock = "";
            dirty = false;

            login.Show();
            Debug.Log("[Zoo] logged out.");
        }

        // 버튼을 누른 그 순간에 화면을 없애지 않고, 다음 프레임에 나간다.
        void RequestLogout()
        {
            logoutRequested = true;
        }

        static GameState LoadState(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                return ParseState(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Zoo] 저장 파일을 읽지 못해 새 게임으로 시작합니다: " + e.Message);
                return null;
            }
        }

        static GameState ParseState(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var state = JsonUtility.FromJson<GameState>(json);
                if (state == null || state.landWidth <= 0) return null;
                return state;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Zoo] 저장을 읽지 못했습니다: " + e.Message);
                return null;
            }
        }

        void MarkDirty()
        {
            if (dirty) return;
            dirty = true;
            saveAt = Time.unscaledTime + SaveDelaySeconds;
        }

        public void Save()
        {
            if (Game == null || SavePath == null) return;
            dirty = false;
            try
            {
                // 쓰는 도중에 컴퓨터가 꺼져도 원래 파일이 깨지지 않도록, 옆에 다 쓴 뒤 바꿔치기한다.
                string temp = SavePath + ".tmp";
                Game.State.savedAt = Game.Now;
                string json = JsonUtility.ToJson(Game.State);
                File.WriteAllText(temp, json);
                QueueCloudSave(json);
                if (!File.Exists(SavePath))
                {
                    File.Move(temp, SavePath);
                }
                else
                {
                    try
                    {
                        File.Replace(temp, SavePath, null);
                    }
                    catch (Exception)
                    {
                        // 바꿔치기를 지원하지 않는 폴더(일부 공유 폴더, 브라우저)에서는 덮어쓴다.
                        File.Copy(temp, SavePath, true);
                        File.Delete(temp);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Zoo] 저장하지 못했습니다: " + e.Message);
            }
            WebSave.Flush();
        }

        /// <summary>바뀐 것이 있으면 지금 저장한다. 브라우저에서는 탭을 닫거나 숨길 때 페이지가 부른다.</summary>
        public void SaveNow()
        {
            if (dirty) Save();
            FlushCloudSave();
        }

        // ---------- 서버 저장 ----------
        // 방에 들어온 학생의 저장은 서버에도 올린다. 저장할 때마다 올리지 않고 조금 모아서(CloudDelaySeconds) 올리며,
        // 나가거나 탭을 닫을 때는 바로 올린다. 실패하면 다음 저장 때 다시 올린다.

        const float CloudDelaySeconds = 20f;
        string cloudPending;
        float cloudAt;
        bool cloudUploading;

        void QueueCloudSave(string json)
        {
            if (Game == null || string.IsNullOrEmpty(ActiveRoom) || !Online.Enabled) return;
            if (cloudPending == null) cloudAt = Time.unscaledTime + CloudDelaySeconds;
            cloudPending = json;
        }

        void FlushCloudSave()
        {
            if (cloudPending == null || cloudUploading) return;
            string json = cloudPending;
            string code = ActiveRoom;
            string name = Game != null ? Game.State.playerName : null;
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name)) { cloudPending = null; return; }
            cloudPending = null;
            cloudUploading = true;
            Online.PutSave(code, name, CurrentLock, json, error =>
            {
                cloudUploading = false;
                if (error != null)
                {
                    Debug.LogWarning("[Zoo] 서버 저장 실패: " + error);
                    // 다음 기회에 다시 올린다(새 저장이 그 사이에 없었을 때만).
                    if (cloudPending == null) { cloudPending = json; cloudAt = Time.unscaledTime + 30f; }
                }
            });
        }

        void Update()
        {
            if (logoutRequested)
            {
                Logout();
                return;
            }
            if (endVisitRequested) EndVisit();
            // 친구 마을을 구경하는 동안에는 내 마을의 침략 시계를 멈춘다.
            if (Game != null && !Visiting) Game.TickRaid(Time.unscaledDeltaTime);
            if (dirty && Time.unscaledTime >= saveAt) Save();
            if (cloudPending != null && Time.unscaledTime >= cloudAt) FlushCloudSave();
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            if (dirty) Save();
            FlushCloudSave();
        }

        void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            if (dirty) Save();
            FlushCloudSave();
        }

        void OnApplicationQuit()
        {
            if (dirty) Save();
            FlushCloudSave();
        }

        void OnDestroy()
        {
            if (dirty) Save();
            if (Instance == this) Instance = null;
        }
    }
}
