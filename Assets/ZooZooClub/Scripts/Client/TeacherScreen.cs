using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZooZooClub.Core;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 선생님 화면. 비밀번호를 적고 들어오면 방을 만들고, 방의 랭킹판을 볼 수 있다(교실 화면에 띄워 두기 좋게 크게).
    /// 비밀번호는 서버(Firebase 규칙)가 검사하므로 여기서는 기억만 해 둔다. 틀리면 방 만들기에서 알려 준다.
    /// 이 컴퓨터에서 만든 방 목록은 PlayerPrefs에 남겨 다음에 바로 열 수 있다.
    /// </summary>
    public sealed class TeacherScreen : MonoBehaviour
    {
        const string RoomsKey = "zoo.teacherRooms";
        /// <summary>방마다 만든 방 관리 키(방 문제를 바꿀 때 쓴다). 이 컴퓨터에만 남는다.</summary>
        const string RoomKeysKey = "zoo.roomKeys";
        /// <summary>문제 엑셀 양식. WebGL 템플릿 폴더에 들어 있어 페이지와 같은 자리에 올라간다.</summary>
        const string TemplateFile = "quiz_template.xlsx";
        const int MaxSavedRooms = 8;

        enum Step { Password, Rooms, Board }

        Action onBack;
        Transform root;
        RectTransform panel;
        RectTransform content;
        Step step;

        string teacherKey = "";
        TextEntry passwordEntry;
        TextEntry roomNameEntry;
        TextMeshProUGUI status;

        // 랭킹판
        string boardCode = "";
        RoomInfo boardRoom;
        List<RankEntry> boardEntries = new List<RankEntry>();
        float nextBoardRefresh;
        bool boardLoading;
        RectTransform boardList;
        TextMeshProUGUI boardTitle;
        TextMeshProUGUI boardStatus;
        TextMeshProUGUI openButtonLabel;
        TextMeshProUGUI boardNote;
        bool quizBusy;
        float resetArmedUntil = -1f;

        public void Init(Action onBack)
        {
            this.onBack = onBack;

            var canvasGo = new GameObject("TeacherCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 110;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            canvasGo.AddComponent<GraphicRaycaster>();
            root = canvasGo.transform;

            var sky = Ui.Panel("Sky", root, PlaceholderArt.Sky);
            Ui.Stretch(sky.rectTransform, 0f, 0f, 0f, 0f);
            Sprite background = ArtLibrary.Get("login_background");
            if (background != null)
            {
                var picture = Ui.Panel("Background", root, new Color(1f, 1f, 1f, 0.6f));
                picture.sprite = background;
                picture.raycastTarget = false;
                picture.rectTransform.pivot = new Vector2(0.5f, 0f);
                var fitter = picture.gameObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = background.rect.width / background.rect.height;
            }

            var box = Ui.Box("TeacherPanel", root, Ui.PanelSprite, Color.white);
            panel = box.rectTransform;
            Ui.Put(panel, 0.5f, 0.5f, 0f, 20f, 1100f, 860f);
            content = Ui.Rect("Content", panel);
            Ui.Stretch(content, 56f, 44f, 56f, 36f);
            var group = Ui.Vertical(content.gameObject, 12f, 0);
            group.childAlignment = TextAnchor.UpperCenter;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            ShowStep(teacherKey.Length > 0 ? Step.Rooms : Step.Password);
        }

        /// <summary>테스트용: 비밀번호를 적고 그 방의 랭킹판으로 바로 간다.</summary>
        public void ShowBoardForTest(string password, string code)
        {
            teacherKey = password ?? "";
            gameObject.SetActive(true);
            OpenBoard(code);
        }

        public void Hide()
        {
            TextEntry.HideAll();
            gameObject.SetActive(false);
        }

        void Update()
        {
            if (step == Step.Password)
            {
                passwordEntry.Update();
                if (passwordEntry.TakeSubmit()) EnterPassword();
            }
            else if (step == Step.Rooms)
            {
                roomNameEntry.Update();
                if (roomNameEntry.TakeSubmit()) CreateRoom();
            }
            else if (step == Step.Board)
            {
                if (!boardLoading && Time.unscaledTime >= nextBoardRefresh) RefreshBoard();
                if (QuizFilePicker.Waiting)
                {
                    var file = QuizFilePicker.Take();
                    if (file != null) UploadQuizFile(file);
                }
            }
        }

        // ---------- 화면들 ----------

        void ShowStep(Step next)
        {
            TextEntry.HideAll();
            step = next;
            panel.sizeDelta = new Vector2(1100f, step == Step.Password ? 420f : step == Step.Rooms ? 760f : 1000f);
            Ui.Clear(content);
            passwordEntry = null;
            roomNameEntry = null;
            status = null;

            switch (step)
            {
                case Step.Password: BuildPassword(); break;
                case Step.Rooms: BuildRooms(); break;
                case Step.Board: BuildBoard(); break;
            }
        }

        void Title(string text)
        {
            var label = Ui.Label("Title", content, text, 44f, Ui.Ink, TextAlignmentOptions.Center, true);
            Ui.Fixed(label.gameObject, 0f, 64f);
        }

        void Note(string text)
        {
            var label = Ui.Label("Note", content, text, 22f, Ui.Muted, TextAlignmentOptions.Center);
            Ui.Fixed(label.gameObject, 0f, 34f);
        }

        void Status()
        {
            status = Ui.Label("Status", content, "", 24f, Ui.Bad, TextAlignmentOptions.Center, true);
            Ui.Fixed(status.gameObject, 0f, 36f);
        }

        void SetStatus(string text, bool bad)
        {
            if (status == null) return;
            status.text = text ?? "";
            status.color = bad ? Ui.Bad : Ui.Good;
        }

        RectTransform ButtonRow(float height)
        {
            var row = Ui.Rect("Buttons", content);
            Ui.Fixed(row.gameObject, 0f, height);
            Ui.Horizontal(row.gameObject, 16f, 0);
            return row;
        }

        void BuildPassword()
        {
            Title("선생님");
            Note("선생님 비밀번호를 적어 주세요.");
            passwordEntry = new TextEntry("TeacherPassword", content, "비밀번호", 30, 84f, 34f, true, () => SetStatus("", true));
            Status();
            var row = ButtonRow(80f);
            Ui.Button("BackButton", row, "돌아가기", () => { if (onBack != null) onBack(); }, 80f, 28f, Ui.Sand);
            Ui.Button("EnterButton", row, "들어가기", EnterPassword, 80f, 28f, Ui.Green);
            Ui.Spacer(content, 0f, 1f);
            if (!Online.Enabled) Note("온라인 기능이 꺼져 있어요(online.json의 databaseUrl이 비어 있음).");
            passwordEntry.Update();
            passwordEntry.Focus();
        }

        void EnterPassword()
        {
            string key = (passwordEntry.Value ?? "").Trim();
            if (key.Length == 0) { SetStatus("비밀번호를 적어 주세요.", true); return; }
            teacherKey = key;
            ShowStep(Step.Rooms);
        }

        void BuildRooms()
        {
            Title("방 만들기");
            Note("방 이름을 적고 만들면 6자리 방 코드가 나와요. 학생들은 그 코드로 들어와요.");
            roomNameEntry = new TextEntry("RoomName", content, "방 이름 (예: 6학년 3반)", 20, 76f, 30f, false, () => SetStatus("", true));
            Status();
            var row = ButtonRow(80f);
            Ui.Button("BackButton", row, "돌아가기", () => { if (onBack != null) onBack(); }, 80f, 28f, Ui.Sand);
            Ui.Button("CreateButton", row, "방 만들기", CreateRoom, 80f, 28f, Ui.Green);

            var rooms = SavedRooms();
            if (rooms.Count > 0)
            {
                var label = Ui.Label("SavedTitle", content, "이 컴퓨터에서 만든 방", 24f, Ui.Muted, TextAlignmentOptions.Center);
                Ui.Fixed(label.gameObject, 0f, 44f);
                label.alignment = TextAlignmentOptions.Bottom;
                var grid = Ui.Grid("Saved", content, 480f, 64f, 12f, 2);
                foreach (var pair in rooms)
                {
                    string code = pair.Key;
                    Ui.Button("Room " + code, grid, code + "  ·  " + pair.Value, () => OpenBoard(code), 64f, 26f, Ui.Sand);
                }
            }
            Ui.Spacer(content, 0f, 1f);
            Note("다른 컴퓨터에서 만든 방은 코드를 알면 학생 화면처럼 볼 수 있어요. 비밀번호가 틀리면 방 만들기에서 알려 줘요.");
            roomNameEntry.Update();
            roomNameEntry.Focus();
        }

        void CreateRoom()
        {
            string name = (roomNameEntry.Value ?? "").Trim();
            if (name.Length == 0) { SetStatus("방 이름을 적어 주세요.", true); return; }
            if (name.Length > 40) name = name.Substring(0, 40);
            string code = RoomCode.Generate(new System.Random());
            SetStatus("방을 만드는 중…", false);
            Online.OpenRoom(code, name, teacherKey, KeyFor(code), error =>
            {
                if (!gameObject.activeInHierarchy || step != Step.Rooms) return;
                if (error != null)
                {
                    SetStatus(error, true);
                    if (error.Contains("비밀번호")) teacherKey = "";
                    return;
                }
                RememberRoom(code, name);
                OpenBoard(code);
            });
        }

        void OpenBoard(string code)
        {
            boardCode = code;
            boardRoom = null;
            boardEntries = new List<RankEntry>();
            ShowStep(Step.Board);
            RefreshBoard();
        }

        void BuildBoard()
        {
            boardTitle = Ui.Label("BoardTitle", content, "방 코드  " + boardCode, 56f, Ui.Ink, TextAlignmentOptions.Center, true);
            Ui.Fixed(boardTitle.gameObject, 0f, 76f);
            boardStatus = Ui.Label("BoardStatus", content, "불러오는 중…", 24f, Ui.Muted, TextAlignmentOptions.Center);
            Ui.Fixed(boardStatus.gameObject, 0f, 36f);

            boardList = Ui.ScrollView("List", content, 6f);

            var row = ButtonRow(72f);
            Ui.Button("BackButton", row, "돌아가기", () => ShowStep(Step.Rooms), 72f, 26f, Ui.Sand);
            Ui.Button("RefreshButton", row, "새로고침", RefreshBoard, 72f, 26f, Ui.Blue);
            var openButton = Ui.Button("ToggleOpenButton", row, "방 닫기", ToggleOpen, 72f, 26f, Ui.Yellow);
            openButtonLabel = openButton.GetComponentInChildren<TextMeshProUGUI>();
            Ui.Button("ClearButton", row, "기록 지우기", ClearBoard, 72f, 26f, Ui.Red);

            var quizRow = ButtonRow(72f);
            Ui.Button("UploadQuizButton", quizRow, "문제 올리기", PickQuizFile, 72f, 26f, Ui.Purple);
            Ui.Button("ResetQuizButton", quizRow, "기본 문제로", ResetQuiz, 72f, 26f, Ui.Sand);
            Ui.Button("TemplateButton", quizRow, "엑셀 양식 받기", () => Application.OpenURL(TemplateFile), 72f, 26f, Ui.Blue);
            boardNote = Ui.Label("BoardNote", content, "엑셀(.xlsx)로 이 방 문제를 바꿀 수 있어요. 첫 시트의 문제·보기1~4·정답 열을 읽어요.",
                20f, Ui.Muted, TextAlignmentOptions.Center);
            Ui.Fixed(boardNote.gameObject, 0f, 84f);
            quizBusy = false;
            resetArmedUntil = -1f;
        }

        void RefreshBoard()
        {
            if (boardLoading) return;
            boardLoading = true;
            nextBoardRefresh = Time.unscaledTime + Online.BoardRefreshSeconds;
            Online.FetchRoom(boardCode, (room, roomError) =>
            {
                if (!gameObject.activeInHierarchy || step != Step.Board) { boardLoading = false; return; }
                boardRoom = room;
                if (room == null)
                {
                    boardLoading = false;
                    boardStatus.text = roomError;
                    return;
                }
                Online.FetchPlayers(boardCode, (entries, error) =>
                {
                    boardLoading = false;
                    if (!gameObject.activeInHierarchy || step != Step.Board) return;
                    if (error != null) { boardStatus.text = error; return; }
                    boardEntries = entries;
                    RenderBoard();
                });
            });
        }

        void RenderBoard()
        {
            boardTitle.text = (boardRoom != null && boardRoom.name.Length > 0 ? boardRoom.name + "   " : "") + "방 코드  " + boardCode;
            bool open = boardRoom == null || boardRoom.open;
            int quizCount = boardRoom != null ? boardRoom.quizCount : 0;
            boardStatus.text = (open ? "열린 방" : "닫힌 방") + "  ·  " + boardEntries.Count + "명  ·  "
                + (quizCount > 0 ? "올린 문제 " + quizCount + "개" : "기본 문제") + "  ·  "
                + DateTime.Now.ToString("HH:mm:ss") + " 기준 (" + Online.BoardRefreshSeconds + "초마다 새로고침)";
            if (openButtonLabel != null) openButtonLabel.text = open ? "방 닫기" : "방 열기";

            Ui.Clear(boardList);
            if (boardEntries.Count == 0)
            {
                var empty = Ui.Label("Empty", boardList, "아직 들어온 학생이 없어요.", 28f, Ui.Muted, TextAlignmentOptions.Center);
                Ui.Fixed(empty.gameObject, 0f, 80f);
                return;
            }

            var places = RankBoard.Places(boardEntries);
            for (int i = 0; i < boardEntries.Count; i++)
            {
                var entry = boardEntries[i];
                var rowBox = Ui.Box("Row " + i, boardList, Ui.CardSprite, places[i] <= 3 ? Ui.Sand : Color.white);
                Ui.Fixed(rowBox.gameObject, 0f, 58f);
                var place = Ui.Label("Place", rowBox.transform, places[i] + "위", 30f, places[i] == 1 ? Ui.Orange : Ui.Ink,
                    TextAlignmentOptions.Center, true);
                Ui.Put(place.rectTransform, 0f, 0.5f, 10f, 0f, 110f, 50f);
                var name = Ui.Label("Name", rowBox.transform, entry.name, 30f, Ui.Ink, TextAlignmentOptions.Left, true);
                Ui.Put(name.rectTransform, 0f, 0.5f, 130f, 0f, 440f, 50f);
                var level = Ui.Label("Level", rowBox.transform, "Lv " + entry.level, 30f, Ui.Good, TextAlignmentOptions.Center, true);
                Ui.Put(level.rectTransform, 1f, 0.5f, -300f, 0f, 150f, 50f);
                var detail = Ui.Label("Detail", rowBox.transform, "경험치 " + entry.xp + "  ·  문제 " + entry.quizSolved + "개",
                    22f, Ui.Muted, TextAlignmentOptions.Right);
                Ui.Put(detail.rectTransform, 1f, 0.5f, -20f, 0f, 280f, 50f);
            }
        }

        void ToggleOpen()
        {
            bool open = boardRoom == null || boardRoom.open;
            boardStatus.text = open ? "방을 닫는 중…" : "방을 여는 중…";
            Online.SetRoomOpen(boardCode, !open, teacherKey, error =>
            {
                if (!gameObject.activeInHierarchy || step != Step.Board) return;
                if (error != null) { boardStatus.text = error; return; }
                nextBoardRefresh = 0f;
            });
        }

        void ClearBoard()
        {
            boardStatus.text = "기록을 지우는 중…";
            Online.ClearPlayers(boardCode, teacherKey, error =>
            {
                if (!gameObject.activeInHierarchy || step != Step.Board) return;
                if (error != null) { boardStatus.text = error; return; }
                nextBoardRefresh = 0f;
            });
        }

        // ---------- 방 문제 ----------

        void SetNote(string text, Color color)
        {
            if (boardNote == null) return;
            boardNote.text = text;
            boardNote.color = color;
        }

        void PickQuizFile()
        {
            if (quizBusy || QuizFilePicker.Waiting) return;
            SetNote("엑셀 파일을 골라 주세요…", Ui.Muted);
            QuizFilePicker.Pick();
        }

        void UploadQuizFile(QuizFileResult file)
        {
            if (file.cancelled) { SetNote("파일을 고르지 않았어요.", Ui.Muted); return; }
            if (file.error != null) { SetNote("파일을 읽지 못했어요: " + file.error, Ui.Bad); return; }

            var sheet = QuizSheet.Read(file.rows);
            if (sheet.questions.Count == 0)
            {
                SetNote("올릴 수 있는 문제가 없어요." + ProblemSummary(sheet), Ui.Bad);
                return;
            }
            var session = GameSession.Instance;
            var bank = QuizSheet.ToBank(sheet.questions, session != null ? session.DefaultQuiz : null);
            int skipped = sheet.problems.Count + Math.Max(0, sheet.questions.Count - QuizSheet.MaxRoomQuestions);
            string json = JsonUtility.ToJson(bank);
            string code = boardCode;

            quizBusy = true;
            SetNote("문제 " + bank.questions.Count + "개를 올리는 중…", Ui.Muted);
            WithRoomKey(code, (roomKey, keyError) =>
            {
                if (keyError != null) { quizBusy = false; ShowQuizResult(code, keyError, Ui.Bad); return; }
                Online.PutRoomQuiz(code, teacherKey, roomKey, json, bank.questions.Count, error =>
                {
                    quizBusy = false;
                    if (error != null) { ShowQuizResult(code, error, Ui.Bad); return; }
                    string text = "'" + file.fileName + "'에서 문제 " + bank.questions.Count + "개를 올렸어요. 이제 이 방 학생은 이 문제를 풀어요.";
                    if (skipped > 0) text += "\n올리지 못한 줄 " + skipped + "개:" + ProblemSummary(sheet);
                    ShowQuizResult(code, text, skipped > 0 ? Ui.Orange : Ui.Good);
                    nextBoardRefresh = 0f;
                });
            });
        }

        void ResetQuiz()
        {
            if (quizBusy) return;
            if (Time.unscaledTime > resetArmedUntil)
            {
                // 실수로 누르지 않도록 한 번 더 눌러야 바뀐다.
                resetArmedUntil = Time.unscaledTime + 4f;
                SetNote("한 번 더 누르면 올린 문제를 지우고 기본 문제로 돌아가요.", Ui.Orange);
                return;
            }
            resetArmedUntil = -1f;
            string code = boardCode;
            quizBusy = true;
            SetNote("기본 문제로 바꾸는 중…", Ui.Muted);
            WithRoomKey(code, (roomKey, keyError) =>
            {
                if (keyError != null) { quizBusy = false; ShowQuizResult(code, keyError, Ui.Bad); return; }
                Online.PutRoomQuiz(code, teacherKey, roomKey, null, 0, error =>
                {
                    quizBusy = false;
                    ShowQuizResult(code, error ?? "기본 문제로 돌아갔어요.", error != null ? Ui.Bad : Ui.Good);
                    nextBoardRefresh = 0f;
                });
            });
        }

        void ShowQuizResult(string code, string text, Color color)
        {
            if (!gameObject.activeInHierarchy || step != Step.Board || boardCode != code) return;
            SetNote(text, color);
        }

        /// <summary>
        /// 방 관리 키를 준비한다. 키를 만들기 전에 만든 방이면, 이 컴퓨터의 새 키를 서버에 처음 적어 둔다
        /// (이미 다른 컴퓨터가 적어 두었으면 서버가 거절한다).
        /// </summary>
        void WithRoomKey(string code, Action<string, string> done)
        {
            string key = SavedKey(code);
            if (key != null) { done(key, null); return; }
            key = KeyFor(code);
            Online.ClaimRoom(code, teacherKey, key, error =>
            {
                if (error != null) ForgetKey(code);
                done(error == null ? key : null, error);
            });
        }

        static string ProblemSummary(QuizSheetResult sheet)
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < sheet.problems.Count && i < 2; i++) text.Append("\n· ").Append(sheet.problems[i]);
            if (sheet.problems.Count > 2) text.Append("\n· 그 밖에 ").Append(sheet.problems.Count - 2).Append("줄");
            return text.ToString();
        }

        // ---------- 방 관리 키 ----------

        static Dictionary<string, string> SavedKeys()
        {
            var map = new Dictionary<string, string>();
            foreach (string item in PlayerPrefs.GetString(RoomKeysKey, "").Split('|'))
            {
                int colon = item.IndexOf(':');
                if (colon > 0) map[item.Substring(0, colon)] = item.Substring(colon + 1);
            }
            return map;
        }

        static void WriteKeys(Dictionary<string, string> map)
        {
            var parts = new List<string>();
            foreach (var pair in map) parts.Add(pair.Key + ":" + pair.Value);
            PlayerPrefs.SetString(RoomKeysKey, string.Join("|", parts.ToArray()));
            PlayerPrefs.Save();
        }

        static string SavedKey(string code)
        {
            string key;
            return SavedKeys().TryGetValue(code, out key) ? key : null;
        }

        /// <summary>이 방의 관리 키. 없으면 새로 만들어 이 컴퓨터에 적어 둔다.</summary>
        public static string KeyFor(string code)
        {
            var map = SavedKeys();
            string key;
            if (map.TryGetValue(code, out key)) return key;
            const string letters = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var random = new System.Random(Guid.NewGuid().GetHashCode());
            var chars = new char[24];
            for (int i = 0; i < chars.Length; i++) chars[i] = letters[random.Next(letters.Length)];
            key = new string(chars);
            map[code] = key;
            WriteKeys(map);
            return key;
        }

        static void ForgetKey(string code)
        {
            var map = SavedKeys();
            if (map.Remove(code)) WriteKeys(map);
        }

        // ---------- 이 컴퓨터에서 만든 방 ----------

        static List<KeyValuePair<string, string>> SavedRooms()
        {
            var list = new List<KeyValuePair<string, string>>();
            string raw = PlayerPrefs.GetString(RoomsKey, "");
            foreach (string item in raw.Split('|'))
            {
                int colon = item.IndexOf(':');
                if (colon <= 0) continue;
                list.Add(new KeyValuePair<string, string>(item.Substring(0, colon), item.Substring(colon + 1)));
            }
            return list;
        }

        static void RememberRoom(string code, string name)
        {
            var list = SavedRooms();
            list.RemoveAll(pair => pair.Key == code);
            list.Insert(0, new KeyValuePair<string, string>(code, name.Replace('|', ' ').Replace(':', ' ')));
            while (list.Count > MaxSavedRooms) list.RemoveAt(list.Count - 1);
            var parts = new List<string>();
            foreach (var pair in list) parts.Add(pair.Key + ":" + pair.Value);
            PlayerPrefs.SetString(RoomsKey, string.Join("|", parts.ToArray()));
            PlayerPrefs.Save();
        }
    }
}
