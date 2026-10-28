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

        enum Step { Password, Rooms, Board, RoomPassword, Create, OpenCode, Saved }

        const int MinRoomPassword = 4;
        const int MaxRoomPassword = 20;

        Action onBack;
        Transform root;
        RectTransform panel;
        RectTransform content;
        Step step;

        string teacherKey = "";
        TextEntry passwordEntry;
        TextEntry roomNameEntry;
        TextEntry roomPasswordEntry;
        TextEntry openCodeEntry;
        TextEntry newRoomPasswordEntry;
        string removeArmed;
        float removeArmedUntil;
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
        RankKind boardKind = RankKind.Level;
        RectTransform boardTabs;
        bool quizBusy;
        // 지금 연 방에 방 비밀번호가 정해져 있는가(-1 모름, 0 없음, 1 있음).
        int boardKeyState = -1;
        UnityEngine.UI.Button roomPasswordButton;
        TextEntry currentRoomPasswordEntry;
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
            else if (step == Step.Create)
            {
                roomNameEntry.Update();
                roomPasswordEntry.Update();
                if (roomNameEntry.TakeSubmit() || roomPasswordEntry.TakeSubmit()) CreateRoom();
            }
            else if (step == Step.OpenCode)
            {
                openCodeEntry.Update();
                roomPasswordEntry.Update();
                if (openCodeEntry.TakeSubmit() || roomPasswordEntry.TakeSubmit()) OpenByCode();
            }
            else if (step == Step.RoomPassword)
            {
                if (currentRoomPasswordEntry != null) currentRoomPasswordEntry.Update();
                newRoomPasswordEntry.Update();
                if (newRoomPasswordEntry.TakeSubmit()) SaveRoomPassword();
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
            panel.sizeDelta = new Vector2(step == Step.Board ? 1300f : 1100f,
                step == Step.Password ? 420f : step == Step.Rooms ? 640f : step == Step.Create ? 600f
                : step == Step.OpenCode ? 520f : step == Step.Saved ? 760f : step == Step.RoomPassword ? 560f : 1000f);
            Ui.Clear(content);
            passwordEntry = null;
            roomNameEntry = null;
            roomPasswordEntry = null;
            openCodeEntry = null;
            newRoomPasswordEntry = null;
            currentRoomPasswordEntry = null;
            roomPasswordButton = null;
            status = null;

            switch (step)
            {
                case Step.Password: BuildPassword(); break;
                case Step.Rooms: BuildRooms(); break;
                case Step.Board: BuildBoard(); break;
                case Step.RoomPassword: BuildRoomPassword(); break;
                case Step.Create: BuildCreate(); break;
                case Step.OpenCode: BuildOpenCode(); break;
                case Step.Saved: BuildSaved(); break;
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
            Title("선생님");
            Note("무엇을 할까요?");
            Ui.Spacer(content, 0f, 8f);
            MenuButton("MenuCreate", "방 만들기", Ui.Green, () => ShowStep(Step.Create));
            MenuButton("MenuOpenCode", "방 코드로 들어가기", Ui.Blue, () => ShowStep(Step.OpenCode));
            MenuButton("MenuSaved", "이 컴퓨터에 저장된 방", Ui.Sand, () => ShowStep(Step.Saved));
            Status();
            Ui.Spacer(content, 0f, 1f);
            var row = ButtonRow(70f);
            Ui.Button("BackButton", row, "처음 화면으로", () => { if (onBack != null) onBack(); }, 70f, 24f, Ui.Cream);
        }

        void MenuButton(string name, string text, Color color, Action onClick)
        {
            var button = Ui.Button(name, content, text, onClick, 90f, 32f, color);
            Ui.Fixed(button.gameObject, 0f, 90f);
        }

        void BackToMenuRow(string actionName, string actionText, Action action)
        {
            var row = ButtonRow(76f);
            Ui.Button("BackButton", row, "돌아가기", () => ShowStep(Step.Rooms), 76f, 26f, Ui.Sand);
            if (action != null) Ui.Button(actionName, row, actionText, action, 76f, 26f, Ui.Green);
        }

        void BuildCreate()
        {
            Title("방 만들기");
            Note("만들면 6자리 방 코드가 나와요. 학생들은 그 코드로 들어와요.");
            roomNameEntry = new TextEntry("RoomName", content, "방 이름 (예: 6학년 3반)", 20, 76f, 28f, false, () => SetStatus("", true));
            roomPasswordEntry = new TextEntry("RoomPassword", content, "방 비밀번호 (4자 이상)", MaxRoomPassword, 76f, 28f, true,
                () => SetStatus("", true));
            Note("방 비밀번호는 문제 올리기와 학생 관리에 써요. 다른 컴퓨터에서도 이 비밀번호로 방을 열어요.");
            Status();
            BackToMenuRow("CreateButton", "방 만들기", CreateRoom);
            roomNameEntry.Update();
            roomPasswordEntry.Update();
            roomNameEntry.Focus();
        }

        void BuildOpenCode()
        {
            Title("방 코드로 들어가기");
            Note("이미 만든 방의 방 코드와 방 비밀번호를 적어요.");
            openCodeEntry = new TextEntry("OpenCode", content, "방 코드", 8, 76f, 28f, false, () => SetStatus("", true));
            roomPasswordEntry = new TextEntry("RoomPassword", content, "방 비밀번호", MaxRoomPassword, 76f, 28f, true,
                () => SetStatus("", true));
            Status();
            BackToMenuRow("OpenByCode", "들어가기", OpenByCode);
            openCodeEntry.Update();
            roomPasswordEntry.Update();
            openCodeEntry.Focus();
        }

        void BuildSaved()
        {
            Title("이 컴퓨터에 저장된 방");
            var rooms = SavedRooms();
            if (rooms.Count == 0) Note("아직 이 컴퓨터에 저장된 방이 없어요.");
            else
            {
                Note("방을 누르면 들어가요. × 를 두 번 누르면 이 컴퓨터의 목록에서만 지워요.");
                var grid = Ui.Grid("Saved", content, 480f, 64f, 12f, 2);
                foreach (var pair in rooms)
                {
                    string code = pair.Key;
                    bool armed = removeArmed == code && Time.unscaledTime < removeArmedUntil;
                    var roomButton = Ui.Button("Room " + code, grid, code + "  ·  " + pair.Value, () => OpenBoard(code), 64f, 24f,
                        armed ? Ui.Orange : Ui.Sand);
                    var remove = Ui.Button("Remove " + code, roomButton.transform, armed ? "지우기?" : "×",
                        () => RemoveSavedRoom(code), 46f, 20f, Ui.Red);
                    Ui.Put((RectTransform)remove.transform, 1f, 0.5f, -8f, 2f, armed ? 100f : 48f, 46f);
                }
            }
            Status();
            Ui.Spacer(content, 0f, 1f);
            BackToMenuRow(null, null, null);
        }

        /// <summary>이 컴퓨터의 방 목록에서 지운다(서버의 방은 그대로). 4초 안에 한 번 더 눌러야 지운다.</summary>
        void RemoveSavedRoom(string code)
        {
            if (removeArmed != code || Time.unscaledTime >= removeArmedUntil)
            {
                removeArmed = code;
                removeArmedUntil = Time.unscaledTime + 4f;
                ShowStep(Step.Saved);
                SetStatus("한 번 더 누르면 방 " + code + "을(를) 이 컴퓨터의 목록에서 지워요. 다시 열려면 방 코드와 방 비밀번호가 필요해요.", true);
                return;
            }
            removeArmed = null;
            var list = SavedRooms();
            list.RemoveAll(pair => pair.Key == code);
            WriteRooms(list);
            ForgetKey(code);
            ShowStep(Step.Saved);
            SetStatus("방 " + code + "을(를) 목록에서 지웠어요.", false);
        }

        string ReadRoomPassword(TextEntry entry, out string error)
        {
            string password = (entry != null ? entry.Value ?? "" : "").Trim();
            error = null;
            if (password.Length < MinRoomPassword) { error = "방 비밀번호를 " + MinRoomPassword + "자 이상 적어 주세요."; return null; }
            return password;
        }

        /// <summary>방 비밀번호로 만든 방 관리 키. 같은 방 코드와 비밀번호면 어느 컴퓨터에서나 같다.</summary>
        public static string RoomKeyFrom(string code, string password)
        {
            return RoomLock.Sha256Hex("room|" + code + "|" + password).Substring(0, 24);
        }

        void CreateRoom()
        {
            string name = (roomNameEntry.Value ?? "").Trim();
            if (name.Length == 0) { SetStatus("방 이름을 적어 주세요.", true); return; }
            if (name.Length > 40) name = name.Substring(0, 40);
            string pwError;
            string password = ReadRoomPassword(roomPasswordEntry, out pwError);
            if (password == null) { SetStatus(pwError, true); roomPasswordEntry.Focus(); return; }
            string code = RoomCode.Generate(new System.Random());
            string roomKey = RoomKeyFrom(code, password);
            SetStatus("방을 만드는 중…", false);
            Online.OpenRoom(code, name, teacherKey, roomKey, error =>
            {
                if (!gameObject.activeInHierarchy || step != Step.Create) return;
                if (error != null)
                {
                    SetStatus(error, true);
                    if (error.Contains("비밀번호")) teacherKey = "";
                    return;
                }
                SaveKey(code, roomKey);
                RememberRoom(code, name);
                OpenBoard(code);
            });
        }

        /// <summary>방 코드와 방 비밀번호로 방을 연다(다른 컴퓨터에서 만들었거나 목록에서 지운 방).</summary>
        void OpenByCode()
        {
            string code = RoomCode.Normalize(openCodeEntry.Value ?? "");
            if (code == null) { SetStatus("방 코드는 영문과 숫자 4~8자예요.", true); openCodeEntry.Focus(); return; }
            string pwError;
            string password = ReadRoomPassword(roomPasswordEntry, out pwError);
            if (password == null) { SetStatus(pwError + "", true); roomPasswordEntry.Focus(); return; }
            string roomKey = RoomKeyFrom(code, password);
            SetStatus("방을 확인하는 중…", false);
            Online.FetchRoom(code, (room, roomError) =>
            {
                if (!gameObject.activeInHierarchy || step != Step.OpenCode) return;
                if (room == null) { SetStatus(roomError ?? "그런 방이 없어요.", true); return; }
                Online.VerifyRoomKey(code, teacherKey, roomKey, error =>
                {
                    if (!gameObject.activeInHierarchy || step != Step.OpenCode) return;
                    if (error == null) { Opened(code, roomKey, room.name); return; }
                    // 방 관리 키가 아직 없는 예전 방이면 이 비밀번호로 처음 정한다.
                    Online.ClaimRoom(code, teacherKey, roomKey, claimError =>
                    {
                        if (!gameObject.activeInHierarchy || step != Step.OpenCode) return;
                        if (claimError != null) { SetStatus("선생님 비밀번호나 방 비밀번호가 맞지 않아요.", true); return; }
                        Opened(code, roomKey, room.name);
                    });
                });
            });
        }

        void Opened(string code, string roomKey, string name)
        {
            SaveKey(code, roomKey);
            RememberRoom(code, name);
            OpenBoard(code);
        }

        // ---------- 방 비밀번호 정하기(예전에 만든 방이나 바꿀 때) ----------

        void BuildRoomPassword()
        {
            bool change = boardKeyState == 1;
            bool askCurrent = change && SavedKey(boardCode) == null;
            Title(change ? "방 비밀번호 바꾸기" : "방 비밀번호 정하기");
            Note(change
                ? "방 " + boardCode + "의 방 비밀번호를 새로 바꿔요." + (askCurrent ? " 지금 방 비밀번호도 적어 주세요." : "")
                : "방 " + boardCode + "의 방 비밀번호를 정해요. 다른 컴퓨터에서도 이 비밀번호로 방을 관리할 수 있어요.");
            currentRoomPasswordEntry = askCurrent
                ? new TextEntry("CurrentRoomPassword", content, "지금 방 비밀번호", MaxRoomPassword, 64f, 24f, true, () => SetStatus("", true))
                : null;
            newRoomPasswordEntry = new TextEntry("NewRoomPassword", content, "새 방 비밀번호 (4자 이상)", MaxRoomPassword, 70f, 26f, true,
                () => SetStatus("", true));
            Status();
            var row = ButtonRow(76f);
            Ui.Button("BackButton", row, "돌아가기", () => OpenBoard(boardCode), 76f, 26f, Ui.Sand);
            Ui.Button("SaveRoomPassword", row, "저장", SaveRoomPassword, 76f, 26f, Ui.Green);
            if (currentRoomPasswordEntry != null) currentRoomPasswordEntry.Update();
            newRoomPasswordEntry.Update();
            if (currentRoomPasswordEntry != null) currentRoomPasswordEntry.Focus();
            else newRoomPasswordEntry.Focus();
        }

        void SaveRoomPassword()
        {
            string pwError;
            string password = ReadRoomPassword(newRoomPasswordEntry, out pwError);
            if (password == null) { SetStatus(pwError, true); return; }
            string code = boardCode;
            string newKey = RoomKeyFrom(code, password);
            SetStatus("저장하는 중…", false);

            if (boardKeyState != 1)
            {
                // 아직 방 비밀번호가 없는 방: 처음 정한다.
                Online.ClaimRoom(code, teacherKey, newKey, error =>
                {
                    if (!gameObject.activeInHierarchy || step != Step.RoomPassword) return;
                    if (error != null) { SetStatus("정하지 못했어요. 선생님 비밀번호를 확인해 주세요.", true); return; }
                    Saved(code, newKey);
                });
                return;
            }

            // 이미 있는 방: 지금 방 비밀번호(또는 이 컴퓨터에 적힌 키)로 증명하고 새 비밀번호로 바꾼다.
            string oldKey = SavedKey(code);
            if (oldKey == null)
            {
                string current = ReadRoomPassword(currentRoomPasswordEntry, out pwError);
                if (current == null) { SetStatus("지금 방 비밀번호를 적어 주세요.", true); return; }
                oldKey = RoomKeyFrom(code, current);
            }
            Online.SetRoomKey(code, teacherKey, oldKey, newKey, error =>
            {
                if (!gameObject.activeInHierarchy || step != Step.RoomPassword) return;
                if (error != null)
                {
                    SetStatus(currentRoomPasswordEntry != null
                        ? "지금 방 비밀번호가 맞지 않아요."
                        : "이 컴퓨터에 적힌 방 정보가 맞지 않아요. 방 만들기 화면에서 방 코드와 지금 방 비밀번호로 다시 열어 주세요.", true);
                    if (currentRoomPasswordEntry == null) ForgetKey(code);
                    return;
                }
                Saved(code, newKey);
            });
        }

        void Saved(string code, string newKey)
        {
            SaveKey(code, newKey);
            OpenBoard(code);
            boardStatus.text = "방 비밀번호를 저장했어요. 꼭 기억해 두세요.";
            boardStatusHoldUntil = Time.unscaledTime + 6f;
        }

        void OpenBoard(string code)
        {
            boardCode = code;
            boardRoom = null;
            boardEntries = new List<RankEntry>();
            boardKeyState = -1;
            ShowStep(Step.Board);
            RefreshBoard();
            CheckBoardKey();
        }

        /// <summary>방 비밀번호가 정해져 있는지 알아보고 버튼 글자와 안내를 바꾼다.</summary>
        void CheckBoardKey()
        {
            string code = boardCode;
            Online.CheckRoomKey(code, teacherKey, (hasKey, error) =>
            {
                if (!gameObject.activeInHierarchy || boardCode != code) return;
                if (error != null) { boardKeyState = -1; if (step == Step.Board) SetNote(error, Ui.Bad); return; }
                boardKeyState = hasKey ? 1 : 0;
                if (step != Step.Board) return;
                if (roomPasswordButton != null)
                    Ui.SetButtonText(roomPasswordButton, hasKey ? "방 비밀번호 바꾸기" : "방 비밀번호 정하기");
                if (!hasKey)
                    SetNote("이 방은 아직 방 비밀번호가 없어요. '방 비밀번호 정하기'로 정하면 문제 올리기와 학생 관리를 할 수 있어요.", Ui.Orange);
                else if (SavedKey(code) == null)
                    SetNote("이 방은 방 비밀번호가 있어요. 문제 올리기나 학생 관리를 하려면 방 만들기 화면에서 방 코드와 방 비밀번호로 열어 주세요.", Ui.Orange);
            });
        }

        void BuildBoard()
        {
            boardTitle = Ui.Label("BoardTitle", content, "방 코드  " + boardCode, 56f, Ui.Ink, TextAlignmentOptions.Center, true);
            Ui.Fixed(boardTitle.gameObject, 0f, 76f);
            boardStatus = Ui.Label("BoardStatus", content, "불러오는 중…", 24f, Ui.Muted, TextAlignmentOptions.Center);
            Ui.Fixed(boardStatus.gameObject, 0f, 36f);

            boardTabs = ButtonRow(60f);
            BuildBoardTabs();

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
            roomPasswordButton = Ui.Button("RoomPasswordButton", quizRow,
                boardKeyState == 1 ? "방 비밀번호 바꾸기" : boardKeyState == 0 ? "방 비밀번호 정하기" : "방 비밀번호",
                () => ShowStep(Step.RoomPassword), 72f, 26f, Ui.Yellow);
            boardNote = Ui.Label("BoardNote", content, "엑셀(.xlsx)로 이 방 문제를 바꿀 수 있어요. 첫 시트의 문제·보기1~4·정답 열을 읽어요.",
                20f, Ui.Muted, TextAlignmentOptions.Center);
            Ui.Fixed(boardNote.gameObject, 0f, 84f);
            quizBusy = false;
            resetArmedUntil = -1f;
        }

        void BuildBoardTabs()
        {
            Ui.Clear(boardTabs);
            foreach (var kind in new[] { RankKind.Level, RankKind.Charm, RankKind.Quiz })
            {
                var k = kind;
                string text = kind == RankKind.Charm ? "매력 순위" : kind == RankKind.Quiz ? "문제풀이 순위" : "레벨 순위";
                Ui.Button("BoardTab " + kind, boardTabs, text, () =>
                {
                    boardKind = k;
                    BuildBoardTabs();
                    RenderBoard();
                }, 60f, 24f, boardKind == kind ? Ui.Orange : Ui.Sand);
            }
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
            if (Time.unscaledTime >= boardStatusHoldUntil)
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

            var sorted = RankBoard.Sort(boardEntries, boardKind);
            var places = RankBoard.Places(sorted, boardKind);
            for (int i = 0; i < sorted.Count; i++)
            {
                var entry = sorted[i];
                var rowBox = Ui.Box("Row " + i, boardList, Ui.CardSprite, places[i] <= 3 ? Ui.Sand : Color.white);
                Ui.Fixed(rowBox.gameObject, 0f, 58f);
                var place = Ui.Label("Place", rowBox.transform, places[i] + "위", 30f, places[i] == 1 ? Ui.Orange : Ui.Ink,
                    TextAlignmentOptions.Center, true);
                Ui.Put(place.rectTransform, 0f, 0.5f, 10f, 0f, 110f, 50f);
                var name = Ui.Label("Name", rowBox.transform, entry.name, 30f, Ui.Ink, TextAlignmentOptions.Left, true);
                Ui.Put(name.rectTransform, 0f, 0.5f, 130f, 0f, 290f, 50f);
                string main = boardKind == RankKind.Charm ? "매력 " + entry.charm
                    : boardKind == RankKind.Quiz ? "문제 " + entry.quizSolved + "개" : "Lv " + entry.level;
                string rest = boardKind == RankKind.Charm ? "Lv " + entry.level + "  ·  문제 " + entry.quizSolved + "개"
                    : boardKind == RankKind.Quiz ? "Lv " + entry.level + "  ·  매력 " + entry.charm
                    : "매력 " + entry.charm + "  ·  문제 " + entry.quizSolved + "개";
                var level = Ui.Label("Level", rowBox.transform, main, 30f, Ui.Good, TextAlignmentOptions.Center, true);
                Ui.Put(level.rectTransform, 1f, 0.5f, -590f, 0f, 170f, 50f);
                var detail = Ui.Label("Detail", rowBox.transform, rest, 20f, Ui.Muted, TextAlignmentOptions.Right);
                Ui.Put(detail.rectTransform, 1f, 0.5f, -300f, 0f, 280f, 50f);

                // 학생 관리: 비밀번호를 잊은 학생은 초기화, 잘못 만든 이름은 삭제(둘 다 한 번 더 눌러야 실행).
                string student = entry.name;
                var reset = Ui.Button("ResetPin " + i, rowBox.transform, Armed(student, false) ? "정말 초기화?" : "비번 초기화",
                    () => StudentAction(student, false), 44f, 20f, Armed(student, false) ? Ui.Orange : Ui.Sand);
                Ui.Put((RectTransform)reset.transform, 1f, 0.5f, -150f, 0f, 140f, 44f);
                var remove = Ui.Button("Delete " + i, rowBox.transform, Armed(student, true) ? "정말 삭제?" : "삭제",
                    () => StudentAction(student, true), 44f, 20f, Ui.Red);
                Ui.Put((RectTransform)remove.transform, 1f, 0.5f, -10f, 0f, 130f, 44f);
            }
        }

        // ---------- 학생 관리 ----------

        string armedStudent;
        bool armedDelete;
        float armedUntil;
        bool studentBusy;

        bool Armed(string student, bool delete)
        {
            return armedStudent == student && armedDelete == delete && Time.unscaledTime < armedUntil;
        }

        /// <summary>
        /// 학생 한 명의 비밀번호를 초기화하거나 지운다. 실수로 누르지 않도록 4초 안에 한 번 더 눌러야 실행한다.
        /// 방을 만든 이 컴퓨터(방 관리 키)에서만 된다.
        /// </summary>
        void StudentAction(string student, bool delete)
        {
            if (studentBusy) return;
            if (!Armed(student, delete))
            {
                armedStudent = student;
                armedDelete = delete;
                armedUntil = Time.unscaledTime + 4f;
                boardStatus.text = delete
                    ? "한 번 더 누르면 '" + student + "'의 마을과 기록을 모두 지워요. 되돌릴 수 없어요."
                    : "한 번 더 누르면 '" + student + "'의 비밀번호를 지워요. 다음에 들어올 때 새로 정해요.";
                RenderRowsSoon();
                return;
            }
            armedStudent = null;
            studentBusy = true;
            string code = boardCode;
            boardStatus.text = delete ? "'" + student + "' 지우는 중…" : "'" + student + "' 비밀번호 초기화 중…";
            WithRoomKey(code, (roomKey, keyError) =>
            {
                if (keyError != null) { studentBusy = false; ShowStudentResult(code, keyError); return; }
                Action<string> done = error =>
                {
                    studentBusy = false;
                    ShowStudentResult(code, error ?? (delete
                        ? "'" + student + "'을(를) 지웠어요."
                        : "'" + student + "'의 비밀번호를 지웠어요. 학생이 다음에 들어올 때 새 비밀번호를 정하면 마을을 이어서 해요."));
                    nextBoardRefresh = 0f;
                };
                if (delete) Online.DeleteStudent(code, student, teacherKey, roomKey, done);
                else Online.ResetStudentPin(code, student, teacherKey, roomKey, done);
            });
        }

        void RenderRowsSoon()
        {
            string keep = boardStatus.text;
            RenderBoard();
            boardStatus.text = keep;
        }

        void ShowStudentResult(string code, string text)
        {
            if (!gameObject.activeInHierarchy || step != Step.Board || boardCode != code) return;
            boardStatus.text = text;
            boardStatusHoldUntil = Time.unscaledTime + 6f;
        }

        float boardStatusHoldUntil;

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
            done(null, "이 컴퓨터에 이 방의 방 비밀번호가 없어요. '방 비밀번호' 버튼으로 정하거나, 방 만들기 화면에서 방 코드와 방 비밀번호로 다시 열어 주세요.");
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

        static void SaveKey(string code, string key)
        {
            var map = SavedKeys();
            map[code] = key;
            WriteKeys(map);
        }

        static void WriteRooms(List<KeyValuePair<string, string>> list)
        {
            var parts = new List<string>();
            foreach (var pair in list) parts.Add(pair.Key + ":" + pair.Value);
            PlayerPrefs.SetString(RoomsKey, string.Join("|", parts.ToArray()));
            PlayerPrefs.Save();
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
