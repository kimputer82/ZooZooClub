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
        GameObject worldObject;
        GameObject hudObject;
        bool dirty;
        float saveAt;
        bool logoutRequested;

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
            catalog.SetQuiz(bank);

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

            var animals = new List<string>();
            foreach (var resident in catalog.residents) animals.Add("resident_" + resident.id);

            login = new GameObject("Login").AddComponent<LoginScreen>();
            login.transform.SetParent(transform, false);
            login.Init(Login, animals);
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

            bool isNew = state == null;
            if (isNew) state = GameState.CreateNew(catalog, clock.Now);
            if (string.IsNullOrEmpty(state.playerName) || adopted) state.playerName = name;

            SavePath = path;
            Game = new Game(catalog, state, clock);
            Game.ResumeRaidClock();
            Game.Changed += MarkDirty;

            var world = new GameObject("World").AddComponent<WorldView>();
            world.transform.SetParent(transform, false);
            world.Init(Game);
            worldObject = world.gameObject;

            var hud = new GameObject("Hud").AddComponent<GameHud>();
            hud.transform.SetParent(transform, false);
            hud.Init(Game, world);
            hud.LogoutRequested += RequestLogout;
            hudObject = hud.gameObject;

            login.Hide();
            SaveFiles.LastName = name;

            // 새 마을이나 이어받은 마을은 바로 한 번 저장해서, 다음에 이름 목록에 나오게 한다.
            if (isNew || adopted) Save();

            Debug.Log("[Zoo] started. player=" + state.playerName + (isNew ? " (new)" : adopted ? " (adopted)" : "")
                + " level=" + state.level + " coins=" + state.coins
                + " objects=" + state.objects.Count + " orders=" + state.orders.Count
                + " quiz=" + Game.QuizCount);
            return null;
        }

        /// <summary>저장하고 로그인 화면으로 돌아간다.</summary>
        public void Logout()
        {
            logoutRequested = false;
            if (Game == null) return;

            Save();
            Game.Changed -= MarkDirty;
            if (hudObject != null) Destroy(hudObject);
            if (worldObject != null) Destroy(worldObject);
            hudObject = null;
            worldObject = null;
            Game = null;
            SavePath = null;
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
                var state = JsonUtility.FromJson<GameState>(File.ReadAllText(path));
                if (state == null || state.landWidth <= 0) return null;
                return state;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Zoo] 저장 파일을 읽지 못해 새 게임으로 시작합니다: " + e.Message);
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
                File.WriteAllText(temp, JsonUtility.ToJson(Game.State));
                if (!File.Exists(SavePath))
                {
                    File.Move(temp, SavePath);
                    return;
                }
                try
                {
                    File.Replace(temp, SavePath, null);
                }
                catch (Exception)
                {
                    // 바꿔치기를 지원하지 않는 폴더(일부 공유 폴더 등)에서는 덮어쓴다.
                    File.Copy(temp, SavePath, true);
                    File.Delete(temp);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Zoo] 저장하지 못했습니다: " + e.Message);
            }
        }

        void Update()
        {
            if (logoutRequested)
            {
                Logout();
                return;
            }
            if (Game != null) Game.TickRaid(Time.unscaledDeltaTime);
            if (dirty && Time.unscaledTime >= saveAt) Save();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && dirty) Save();
        }

        void OnApplicationQuit()
        {
            if (dirty) Save();
        }

        void OnDestroy()
        {
            if (dirty) Save();
            if (Instance == this) Instance = null;
        }
    }
}
