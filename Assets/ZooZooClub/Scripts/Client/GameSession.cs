using System;
using System.IO;
using UnityEngine;
using ZooZooClub.Core;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 게임 시작점. 씬에 아무것도 배치하지 않아도 플레이를 시작하면 스스로 만들어져서
    /// 데이터와 저장 파일을 읽고 월드와 UI를 세운다.
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        const float SaveDelaySeconds = 2f;

        public static GameSession Instance { get; private set; }

        public Game Game { get; private set; }

        string savePath;
        bool dirty;
        float saveAt;

        public static string SavePath
        {
            get { return Path.Combine(Application.persistentDataPath, "save.json"); }
        }

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

            var catalog = JsonUtility.FromJson<GameCatalog>(asset.text);
            foreach (var error in catalog.Validate())
            {
                Debug.LogError("[Zoo] catalog: " + error);
            }

            savePath = SavePath;
            var clock = new SystemClock();
            var state = LoadState() ?? GameState.CreateNew(catalog, clock.Now);

            Game = new Game(catalog, state, clock);
            Game.Changed += MarkDirty;

            var world = new GameObject("World").AddComponent<WorldView>();
            world.transform.SetParent(transform, false);
            world.Init(Game);

            var hud = new GameObject("Hud").AddComponent<GameHud>();
            hud.transform.SetParent(transform, false);
            hud.Init(Game, world);

            Debug.Log("[Zoo] started. level=" + state.level + " coins=" + state.coins
                + " objects=" + state.objects.Count + " orders=" + state.orders.Count);
        }

        GameState LoadState()
        {
            try
            {
                if (!File.Exists(savePath)) return null;
                var state = JsonUtility.FromJson<GameState>(File.ReadAllText(savePath));
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
            if (Game == null) return;
            dirty = false;
            try
            {
                File.WriteAllText(savePath, JsonUtility.ToJson(Game.State));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Zoo] 저장하지 못했습니다: " + e.Message);
            }
        }

        void Update()
        {
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
