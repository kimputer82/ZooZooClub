using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using ZooZooClub.Core;

namespace ZooZooClub.Client
{
    /// <summary>방의 공개 정보(누구나 읽을 수 있다).</summary>
    public sealed class RoomInfo
    {
        public string code = "";
        public string name = "";
        public bool open = true;
        public long createdAt;
        /// <summary>선생님이 엑셀로 올린 이 방 문제의 개수. 0이면 게임에 들어 있는 기본 문제를 쓴다.</summary>
        public int quizCount;
    }

    /// <summary>
    /// 서버(Firebase 실시간 데이터베이스)와의 통신. 설정은 Resources/online.json(databaseUrl 등)에서 읽고,
    /// 주소가 비어 있으면 온라인 기능이 꺼진 것으로 본다. REST로만 쓰므로 실시간 연결(동시 접속 수)에 들어가지 않는다.
    ///
    /// 자료 모양(규칙은 Docs/firebase-rules.json):
    ///   secret/teacher                 선생님 비밀번호(아무도 읽지 못한다. 콘솔에서 직접 적는다)
    ///   rooms/&lt;코드&gt;/owner      {key, stamp}  선생님이 방을 만들거나 바꿀 때 함께 적는다. 읽지 못한다
    ///   rooms/&lt;코드&gt;/public     {name, createdAt, open}
    ///   rooms/&lt;코드&gt;/players/&lt;이름키&gt;  {name, level, xp, quizSolved, updatedAt}
    ///   saves/&lt;코드&gt;/&lt;이름키&gt;          학생의 저장 파일 내용(JSON 글 한 덩어리)
    /// </summary>
    public sealed class Online : MonoBehaviour
    {
        [Serializable]
        class Config
        {
            public string databaseUrl = "";
            public int rankUploadSeconds = 60;
            public int rankRefreshSeconds = 30;
            public int boardRefreshSeconds = 10;
        }

        const float Timeout = 10f;

        static Online instance;
        Config config = new Config();

        public static bool Enabled
        {
            get { return instance != null && !string.IsNullOrEmpty(instance.config.databaseUrl); }
        }

        public static int RankUploadSeconds { get { return instance != null ? Math.Max(10, instance.config.rankUploadSeconds) : 60; } }
        public static int RankRefreshSeconds { get { return instance != null ? Math.Max(5, instance.config.rankRefreshSeconds) : 30; } }
        public static int BoardRefreshSeconds { get { return instance != null ? Math.Max(3, instance.config.boardRefreshSeconds) : 10; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
        }

        public static Online Ensure(Transform parent)
        {
            if (instance != null) return instance;
            var go = new GameObject("Online");
            go.transform.SetParent(parent, false);
            instance = go.AddComponent<Online>();
            var asset = Resources.Load<TextAsset>("online");
            if (asset != null)
            {
                try
                {
                    instance.config = JsonUtility.FromJson<Config>(asset.text) ?? new Config();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Zoo] online.json을 읽지 못했습니다: " + e.Message);
                }
            }
            instance.config.databaseUrl = (instance.config.databaseUrl ?? "").Trim().TrimEnd('/');
            return instance;
        }

        static long Now
        {
            get { return DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }
        }

        // ---------- 방과 순위 ----------

        /// <summary>방이 있고 열려 있는지 본다. 안 되면 아이에게 보여 줄 이유를 준다.</summary>
        public static void CheckRoom(string code, Action<RoomInfo, string> done)
        {
            if (!Enabled) { done(null, "온라인 기능이 꺼져 있어요."); return; }
            instance.StartCoroutine(instance.Request("GET", "rooms/" + code + "/public", null, (status, text) =>
            {
                if (status != 200) { done(null, NetworkError(status)); return; }
                var info = ParseRoom(code, text);
                if (info == null) { done(null, "그런 방 코드가 없어요. 다시 확인해 주세요."); return; }
                if (!info.open) { done(null, "지금은 닫혀 있는 방이에요."); return; }
                done(info, null);
            }));
        }

        /// <summary>내 순위 정보를 방에 올린다.</summary>
        public static void PostPlayer(string code, RankEntry entry, Action<string> done)
        {
            if (!Enabled) { if (done != null) done("온라인 기능이 꺼져 있어요."); return; }
            string key = PlayerName.Key(entry.name);
            if (key == null) { if (done != null) done("이름이 이상해요."); return; }
            entry.updatedAt = Now;
            string body = MiniJson.Serialize(entry.ToJson());
            instance.StartCoroutine(instance.Request("PUT", "rooms/" + code + "/players/" + Uri.EscapeDataString(key), body, (status, text) =>
            {
                if (done != null) done(status == 200 ? null : NetworkError(status));
            }));
        }

        /// <summary>방의 순위표를 받아 온다(정렬된 상태).</summary>
        public static void FetchPlayers(string code, Action<List<RankEntry>, string> done)
        {
            if (!Enabled) { done(null, "온라인 기능이 꺼져 있어요."); return; }
            instance.StartCoroutine(instance.Request("GET", "rooms/" + code + "/players", null, (status, text) =>
            {
                if (status != 200) { done(null, NetworkError(status)); return; }
                try
                {
                    done(RankBoard.FromPlayersJson(MiniJson.Parse(text)), null);
                }
                catch (Exception e)
                {
                    done(null, "순위표를 읽지 못했어요: " + e.Message);
                }
            }));
        }

        public static void FetchRoom(string code, Action<RoomInfo, string> done)
        {
            if (!Enabled) { done(null, "온라인 기능이 꺼져 있어요."); return; }
            instance.StartCoroutine(instance.Request("GET", "rooms/" + code + "/public", null, (status, text) =>
            {
                if (status != 200) { done(null, NetworkError(status)); return; }
                var info = ParseRoom(code, text);
                done(info, info == null ? "그런 방이 없어요." : null);
            }));
        }

        // ---------- 서버 저장 ----------

        /// <summary>방에 들어온 학생의 저장(JSON 글)을 받아 온다. 없으면 json이 null이고 error도 null이다.</summary>
        public static void FetchSave(string code, string name, Action<string, string> done)
        {
            if (!Enabled) { done(null, "온라인 기능이 꺼져 있어요."); return; }
            string key = PlayerName.Key(name);
            if (key == null) { done(null, "이름이 이상해요."); return; }
            instance.StartCoroutine(instance.Request("GET", "saves/" + code + "/" + Uri.EscapeDataString(key), null, (status, text) =>
            {
                if (status != 200) { done(null, NetworkError(status)); return; }
                try
                {
                    done(MiniJson.Parse(text) as string, null);
                }
                catch (Exception e)
                {
                    done(null, "저장을 읽지 못했어요: " + e.Message);
                }
            }));
        }

        /// <summary>학생의 저장(JSON 글)을 서버에 올린다.</summary>
        public static void PutSave(string code, string name, string json, Action<string> done)
        {
            if (!Enabled) { if (done != null) done("온라인 기능이 꺼져 있어요."); return; }
            string key = PlayerName.Key(name);
            if (key == null) { if (done != null) done("이름이 이상해요."); return; }
            instance.StartCoroutine(instance.Request("PUT", "saves/" + code + "/" + Uri.EscapeDataString(key), MiniJson.Serialize(json), (status, text) =>
            {
                if (done != null) done(status == 200 ? null : NetworkError(status));
            }));
        }

        // ---------- 선생님 ----------

        /// <summary>방을 만든다(같은 코드가 있으면 이름을 바꾸고 다시 연다). 비밀번호가 틀리면 그 이유를 준다.</summary>
        public static void OpenRoom(string code, string name, string teacherKey, string roomKey, Action<string> done)
        {
            var body = new Dictionary<string, object>
            {
                { "owner", new Dictionary<string, object> { { "key", teacherKey }, { "stamp", Now }, { "roomKey", roomKey } } },
                { "public", new Dictionary<string, object> { { "name", name }, { "createdAt", Now }, { "open", true } } }
            };
            TeacherPatch(code, body, done);
        }

        /// <summary>방을 닫거나 다시 연다. 닫힌 방에는 학생이 들어오거나 점수를 올릴 수 없다.</summary>
        public static void SetRoomOpen(string code, bool open, string teacherKey, Action<string> done)
        {
            var body = new Dictionary<string, object>
            {
                { "owner/key", teacherKey }, { "owner/stamp", Now }, { "public/open", open }
            };
            TeacherPatch(code, body, done);
        }

        /// <summary>방의 순위 기록을 모두 지운다.</summary>
        public static void ClearPlayers(string code, string teacherKey, Action<string> done)
        {
            var body = new Dictionary<string, object>
            {
                { "owner/key", teacherKey }, { "owner/stamp", Now }, { "players", null }
            };
            TeacherPatch(code, body, done);
        }

        // ---------- 방 문제 ----------

        /// <summary>
        /// 방 관리 키를 처음 적어 둔다(키가 생기기 전에 만든 방). 이미 다른 키가 적혀 있으면 서버가 거절한다.
        /// 방 관리 키는 방을 만든 컴퓨터에만 남아 있어서, 그 컴퓨터에서만 방 문제를 바꿀 수 있다.
        /// </summary>
        public static void ClaimRoom(string code, string teacherKey, string roomKey, Action<string> done)
        {
            var body = new Dictionary<string, object>
            {
                { "owner/key", teacherKey }, { "owner/stamp", Now }, { "owner/roomKey", roomKey }
            };
            TeacherPatch(code, body, done, RoomQuizDenied);
        }

        /// <summary>방 문제(문제 은행 JSON 글)를 올린다. quizJson이 null이면 지워서 기본 문제로 돌아간다.</summary>
        public static void PutRoomQuiz(string code, string teacherKey, string roomKey, string quizJson, int count, Action<string> done)
        {
            var body = new Dictionary<string, object>
            {
                { "owner/key", teacherKey }, { "owner/stamp", Now }, { "owner/proof", roomKey },
                { "quiz", quizJson }, { "public/quizCount", quizJson == null ? 0 : count }
            };
            TeacherPatch(code, body, done, RoomQuizDenied);
        }

        const string RoomQuizDenied = "비밀번호가 틀렸거나, 이 방을 만든 컴퓨터가 아니에요. 방 문제는 방을 만든 컴퓨터에서만 바꿀 수 있어요.";

        /// <summary>방 문제를 받아 온다. 올린 문제가 없으면 json이 null이고 error도 null이다.</summary>
        public static void FetchRoomQuiz(string code, Action<string, string> done)
        {
            if (!Enabled) { done(null, "온라인 기능이 꺼져 있어요."); return; }
            instance.StartCoroutine(instance.Request("GET", "rooms/" + code + "/quiz", null, (status, text) =>
            {
                if (status != 200) { done(null, NetworkError(status)); return; }
                try
                {
                    done(MiniJson.Parse(text) as string, null);
                }
                catch (Exception e)
                {
                    done(null, "방 문제를 읽지 못했어요: " + e.Message);
                }
            }));
        }

        static void TeacherPatch(string code, Dictionary<string, object> body, Action<string> done, string deniedMessage)
        {
            if (!Enabled) { done("온라인 기능이 꺼져 있어요."); return; }
            instance.StartCoroutine(instance.Request("PATCH", "rooms/" + code, MiniJson.Serialize(body), (status, text) =>
            {
                if (status == 200) done(null);
                else if (status == 401 || status == 403) done(deniedMessage);
                else done(NetworkError(status));
            }));
        }

        static void TeacherPatch(string code, Dictionary<string, object> body, Action<string> done)
        {
            TeacherPatch(code, body, done, "선생님 비밀번호가 틀렸어요.");
        }

        // ---------- 바닥 ----------

        static RoomInfo ParseRoom(string code, string text)
        {
            try
            {
                var map = MiniJson.AsObject(MiniJson.Parse(text));
                if (map == null) return null;
                return new RoomInfo
                {
                    code = code,
                    name = MiniJson.AsString(MiniJson.Get(map, "name")),
                    open = MiniJson.AsBool(MiniJson.Get(map, "open"), true),
                    createdAt = (long)MiniJson.AsNumber(MiniJson.Get(map, "createdAt")),
                    quizCount = (int)MiniJson.AsNumber(MiniJson.Get(map, "quizCount"))
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        static string NetworkError(long status)
        {
            if (status == 0) return "서버에 연결하지 못했어요. 인터넷을 확인해 주세요.";
            if (status == 401 || status == 403) return "서버가 허락하지 않았어요(권한).";
            return "서버 오류 (" + status + ")";
        }

        /// <summary>path는 'rooms/ABC' 같은 자리. 끝에 .json을 붙여 보낸다. status 0은 연결 실패.</summary>
        IEnumerator Request(string method, string path, string body, Action<long, string> done)
        {
            string url = config.databaseUrl + "/" + path + ".json";
            using (var request = new UnityWebRequest(url, method))
            {
                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = (int)Timeout;
                yield return request.SendWebRequest();

                long status = request.responseCode;
                string text = request.downloadHandler != null ? request.downloadHandler.text : "";
                if (request.result == UnityWebRequest.Result.ConnectionError) status = 0;
                if (status != 200) Debug.LogWarning("[Zoo] online " + method + " " + path + " -> " + status + " " + Shorten(text));
                done(status, text);
            }
        }

        static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length > 120 ? text.Substring(0, 120) + "…" : text;
        }
    }
}
