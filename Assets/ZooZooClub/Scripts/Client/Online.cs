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

        static long lastNow;

        /// <summary>지금 시각(초). 선생님 기록의 stamp는 늘 커져야 하므로, 같은 초 안에서 여러 번 불러도 1씩 커지게 한다.</summary>
        static long Now
        {
            get
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                lastNow = now > lastNow ? now : lastNow + 1;
                return lastNow;
            }
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

        static long NowMs
        {
            get { return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }
        }

        /// <summary>
        /// 내 순위 정보를 방에 올린다. 같은 요청에 "증명"(내 저장 열쇠와 새 시각)을 함께 써서,
        /// 비밀번호를 모르는 사람은 남의 순위를 고칠 수 없게 한다(서버 규칙이 확인한다).
        /// </summary>
        public static void PostPlayer(string code, RankEntry entry, string roomLock, Action<string> done)
        {
            if (!Enabled) { if (done != null) done("온라인 기능이 꺼져 있어요."); return; }
            string key = PlayerName.Key(entry.name);
            if (key == null) { if (done != null) done("이름이 이상해요."); return; }
            entry.updatedAt = Now;
            var body = new Dictionary<string, object>
            {
                { "players/" + key, entry.ToJson() },
                { "proof/" + key, Proof(roomLock) }
            };
            instance.StartCoroutine(instance.Request("PATCH", "rooms/" + code, MiniJson.Serialize(body), (status, text) =>
            {
                if (done != null) done(status == 200 ? null : NetworkError(status));
            }));
        }

        static Dictionary<string, object> Proof(string roomLock)
        {
            return new Dictionary<string, object> { { "lock", roomLock ?? "" }, { "stamp", NowMs } };
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

        /// <summary>서버에서 저장을 찾아본 결과.</summary>
        public enum SaveLookup
        {
            /// <summary>이 비밀번호의 저장이 있다.</summary>
            Found,
            /// <summary>비밀번호를 쓰기 전에 올린 예전 저장이 있다. 이번 비밀번호로 새로 잠근다.</summary>
            Legacy,
            /// <summary>이 이름으로 처음 들어온다. 이번 비밀번호로 잠근다.</summary>
            New,
            /// <summary>이 이름은 다른 비밀번호로 잠겨 있다.</summary>
            WrongPin,
            Error
        }

        /// <summary>
        /// 방에 들어온 학생의 저장(JSON 글)을 비밀번호 열쇠로 받아 온다.
        /// done(결과, 저장 글(없으면 null), 오류 글).
        /// </summary>
        public static void FetchSave(string code, string name, string roomLock, Action<SaveLookup, string, string> done)
        {
            if (!Enabled) { done(SaveLookup.Error, null, "온라인 기능이 꺼져 있어요."); return; }
            string key = PlayerName.Key(name);
            if (key == null) { done(SaveLookup.Error, null, "이름이 이상해요."); return; }
            string path = "saves/" + code + "/" + Uri.EscapeDataString(key);
            instance.StartCoroutine(instance.Request("GET", path + "/" + roomLock, null, (status, text) =>
            {
                if (status != 200) { done(SaveLookup.Error, null, NetworkError(status)); return; }
                string json = ParseString(text);
                if (json != null) { done(SaveLookup.Found, json, null); return; }
                // 이 열쇠의 저장이 없다. 이름 자리를 보면: 비어 있으면 처음, 글이면 예전 저장, 읽을 수 없으면 다른 비밀번호.
                instance.StartCoroutine(instance.Request("GET", path, null, (status2, text2) =>
                {
                    if (status2 == 401 || status2 == 403) { done(SaveLookup.WrongPin, null, null); return; }
                    if (status2 != 200) { done(SaveLookup.Error, null, NetworkError(status2)); return; }
                    string legacy = ParseString(text2);
                    if (legacy != null) done(SaveLookup.Legacy, legacy, null);
                    else done(SaveLookup.New, null, null);
                }));
            }));
        }

        static string ParseString(string text)
        {
            try { return MiniJson.Parse(text) as string; }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// 학생의 저장(JSON 글)을 서버의 내 열쇠 자리에 올리고, 친구들이 구경할 수 있는 사본도 방에 올린다.
        /// </summary>
        public static void PutSave(string code, string name, string roomLock, string json, Action<string> done)
        {
            if (!Enabled) { if (done != null) done("온라인 기능이 꺼져 있어요."); return; }
            string key = PlayerName.Key(name);
            if (key == null || string.IsNullOrEmpty(roomLock)) { if (done != null) done("이름이 이상해요."); return; }
            string path = "saves/" + code + "/" + Uri.EscapeDataString(key) + "/" + roomLock;
            instance.StartCoroutine(instance.Request("PUT", path, MiniJson.Serialize(json), (status, text) =>
            {
                if (status != 200) { if (done != null) done(NetworkError(status)); return; }
                var body = new Dictionary<string, object>
                {
                    { "visits/" + key, json },
                    { "proof/" + key, Proof(roomLock) }
                };
                instance.StartCoroutine(instance.Request("PATCH", "rooms/" + code, MiniJson.Serialize(body), (status2, text2) =>
                {
                    if (done != null) done(status2 == 200 ? null : NetworkError(status2));
                }));
            }));
        }

        /// <summary>친구 마을 구경용 사본을 받아 온다. 없으면(아직 안 올렸으면) 예전 방식 저장을 찾아본다.</summary>
        public static void FetchVisit(string code, string name, Action<string, string> done)
        {
            if (!Enabled) { done(null, "온라인 기능이 꺼져 있어요."); return; }
            string key = PlayerName.Key(name);
            if (key == null) { done(null, "이름이 이상해요."); return; }
            string escaped = Uri.EscapeDataString(key);
            instance.StartCoroutine(instance.Request("GET", "rooms/" + code + "/visits/" + escaped, null, (status, text) =>
            {
                if (status != 200) { done(null, NetworkError(status)); return; }
                string json = ParseString(text);
                if (json != null) { done(json, null); return; }
                instance.StartCoroutine(instance.Request("GET", "saves/" + code + "/" + escaped, null, (status2, text2) =>
                {
                    done(status2 == 200 ? ParseString(text2) : null, null);
                }));
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

        /// <summary>이 방 관리 키(방 비밀번호)가 맞는지 서버에 확인한다. 맞으면 error가 null.</summary>
        public static void VerifyRoomKey(string code, string teacherKey, string roomKey, Action<string> done)
        {
            var body = new Dictionary<string, object>
            {
                { "owner/key", teacherKey }, { "owner/stamp", Now }, { "owner/proof", roomKey }, { "verify", NowMs }
            };
            TeacherPatch(code, body, done, "선생님 비밀번호나 방 비밀번호가 맞지 않아요.");
        }

        /// <summary>
        /// 이 방에 방 비밀번호(방 관리 키)가 정해져 있는지 알아본다. done(정해져 있음, 오류).
        /// 선생님 비밀번호를 먼저 확인하고, 관리 키가 없을 때만 써지는 자리(probe)에 써 봐서 판단한다.
        /// </summary>
        public static void CheckRoomKey(string code, string teacherKey, Action<bool, string> done)
        {
            var who = new Dictionary<string, object> { { "owner/key", teacherKey }, { "owner/stamp", Now } };
            TeacherPatch(code, who, teacherError =>
            {
                if (teacherError != null) { done(false, "선생님 비밀번호가 틀렸어요."); return; }
                var probe = new Dictionary<string, object> { { "owner/key", teacherKey }, { "owner/stamp", Now }, { "probe", NowMs } };
                TeacherPatch(code, probe, probeError => done(probeError != null, null), "");
            }, "선생님 비밀번호가 틀렸어요.");
        }

        /// <summary>방 관리 키를 바꾼다(지금 키를 알아야 한다). 방 비밀번호를 정하거나 바꿀 때 쓴다.</summary>
        public static void SetRoomKey(string code, string teacherKey, string oldKey, string newKey, Action<string> done)
        {
            var body = new Dictionary<string, object>
            {
                { "owner/key", teacherKey }, { "owner/stamp", Now }, { "owner/proof", oldKey }, { "owner/roomKey", newKey }
            };
            TeacherPatch(code, body, done, "선생님 비밀번호가 틀렸거나, 이 컴퓨터에 적힌 방 비밀번호가 맞지 않아요.");
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

        const string RoomQuizDenied = "선생님 비밀번호나 방 비밀번호가 맞지 않아요. 방 문제는 방 비밀번호를 아는 선생님만 바꿀 수 있어요.";

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

        /// <summary>
        /// 방에서 학생 한 명을 지운다(저장, 순위, 구경용 사본 모두). 방을 만든 컴퓨터(방 관리 키)에서만 된다.
        /// </summary>
        public static void DeleteStudent(string code, string name, string teacherKey, string roomKey, Action<string> done)
        {
            StudentPatch(code, name, teacherKey, roomKey, null, true, done);
        }

        /// <summary>
        /// 학생의 비밀번호를 지운다. 마을은 구경용 사본으로 되살려 두어, 학생이 다음에 들어올 때 새 비밀번호를 정하고 이어 한다.
        /// </summary>
        public static void ResetStudentPin(string code, string name, string teacherKey, string roomKey, Action<string> done)
        {
            if (!Enabled) { done("온라인 기능이 꺼져 있어요."); return; }
            string key = PlayerName.Key(name);
            if (key == null) { done("이름이 이상해요."); return; }
            instance.StartCoroutine(instance.Request("GET", "rooms/" + code + "/visits/" + Uri.EscapeDataString(key), null, (status, text) =>
            {
                if (status != 200) { done(NetworkError(status)); return; }
                StudentPatch(code, name, teacherKey, roomKey, ParseString(text), false, done);
            }));
        }

        static void StudentPatch(string code, string name, string teacherKey, string roomKey, string keepSave, bool removeAll,
            Action<string> done)
        {
            if (!Enabled) { done("온라인 기능이 꺼져 있어요."); return; }
            string key = PlayerName.Key(name);
            if (key == null) { done("이름이 이상해요."); return; }
            var body = new Dictionary<string, object>
            {
                { "rooms/" + code + "/owner/key", teacherKey },
                { "rooms/" + code + "/owner/stamp", Now },
                { "rooms/" + code + "/owner/proof", roomKey },
                { "saves/" + code + "/" + key, keepSave },
                { "rooms/" + code + "/proof/" + key, null }
            };
            if (removeAll)
            {
                body["rooms/" + code + "/players/" + key] = null;
                body["rooms/" + code + "/visits/" + key] = null;
            }
            instance.StartCoroutine(instance.Request("PATCH", "", MiniJson.Serialize(body), (status, text) =>
            {
                if (status == 200) done(null);
                else if (status == 401 || status == 403) done(RoomQuizDenied.Replace("방 문제는", "학생 관리는"));
                else done(NetworkError(status));
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
