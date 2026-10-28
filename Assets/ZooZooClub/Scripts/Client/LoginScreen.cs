using System.Collections.Generic;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZooZooClub.Core;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 게임을 켜면 처음 나오는 화면. 학생이 이름을 적으면 그 이름의 마을(저장 파일)로 들어간다.
    /// 선생님이 알려 준 방 코드를 함께 적으면 그 방의 친구들과 랭킹을 겨룬다(비워 두면 혼자 하기).
    /// 이 컴퓨터에서 하던 이름은 아래에 버튼으로 나와서, 눌러서 바로 이어 할 수 있다.
    /// 오른쪽 아래 작은 "선생님" 버튼으로 선생님 화면(방 만들기, 랭킹판)에 들어간다.
    /// </summary>
    public sealed class LoginScreen : MonoBehaviour
    {
        const int MaxRecentNames = 6;
        const int RecentColumns = 3;

        // 화면 높이는 늘 1080으로 본다(아래 Init의 matchWidthOrHeight 참고).
        const float ScreenHeight = 1080f;
        const float TopMargin = 24f;
        // 배경 그림 아래쪽에 서 있는 동물들의 머리 높이. 로고와 창은 이 위쪽 하늘에만 놓는다.
        const float AnimalTop = 300f;

        const string BackgroundArt = "login_background";
        const string LogoArt = "login_logo";
        const string StartArt = "login_start";

        const float LogoWidth = 860f;
        const float LogoFallbackHeight = 128f;
        const float LogoGap = 14f;

        const float PanelWidth = 780f;
        const float PadSide = 60f;
        const float PadTop = 36f;
        const float PadBottom = 40f;
        const float Spacing = 10f;
        const float ContentWidth = PanelWidth - PadSide * 2f;
        const float PromptHeight = 36f;
        const float InputHeight = 84f;
        const float CodeHeight = 64f;
        const float ErrorHeight = 28f;
        const float StartFallbackHeight = 96f;
        const float RecentTitleHeight = 32f;
        const float RecentHeight = 50f;
        const float RecentGap = 12f;

        /// <summary>이름, 방 코드(없으면 빈 문자열), 서버에 있던 저장(없으면 null)을 받아 들어가기를 시도하고, 안 되면 보여 줄 이유를, 되면 null을 돌려준다.</summary>
        Func<string, string, string, string> onLogin;
        Action onTeacher;
        RectTransform logo;
        RectTransform panel;
        TextEntry nameEntry;
        TextEntry codeEntry;
        TextEntry pinEntry;
        const float PinHeight = 64f;
        // 비밀번호를 여러 번 틀리면 잠시 막는다(아무 숫자나 계속 넣어 보지 못하게).
        const int MaxWrongPins = 5;
        const float WrongPinLockSeconds = 60f;
        int wrongPins;
        float pinLockedUntil;
        TextMeshProUGUI errorText;
        float startHeight = StartFallbackHeight;
        bool busy;

        public void Init(Func<string, string, string, string> onLogin, Action onTeacher)
        {
            this.onLogin = onLogin;
            this.onTeacher = onTeacher;

            var canvasGo = new GameObject("LoginCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, ScreenHeight);
            // 세로 배치(로고, 이름 칸, 하던 이름 목록, 배경의 동물들)가 화면 비율에 따라 겹치지 않도록 높이에 맞춘다.
            scaler.matchWidthOrHeight = 1f;
            canvasGo.AddComponent<GraphicRaycaster>();
            Transform root = canvasGo.transform;

            BuildBackdrop(root);
            BuildLogo(root);
            BuildPanel(root);
            BuildTeacherButton(root);
        }

        /// <summary>바탕. 뒤의 게임 화면을 가리고 클릭도 막는다.</summary>
        void BuildBackdrop(Transform root)
        {
            // 그림이 없거나 화면을 다 덮지 못할 때 보이는 하늘색.
            var sky = Ui.Panel("Sky", root, PlaceholderArt.Sky);
            Ui.Stretch(sky.rectTransform, 0f, 0f, 0f, 0f);

            Sprite sprite = ArtLibrary.Get(BackgroundArt);
            if (sprite == null) return;

            var picture = Ui.Panel("Background", root, Color.white);
            picture.sprite = sprite;
            picture.raycastTarget = false;
            // 화면 비율이 달라도 빈 곳 없이 덮는다. 아래쪽(동물들)을 기준으로 맞추고 넘치는 부분은 위나 옆에서 잘린다.
            picture.rectTransform.pivot = new Vector2(0.5f, 0f);
            var fitter = picture.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
        }

        void BuildLogo(Transform root)
        {
            Sprite sprite = ArtLibrary.Get(LogoArt);
            float height = LogoFallbackHeight;
            if (sprite != null)
            {
                height = LogoWidth * sprite.rect.height / sprite.rect.width;
                logo = Ui.Picture("Logo", root, sprite).rectTransform;
            }
            else
            {
                logo = Ui.Label("Logo", root, "Zoo Zoo Club", 96f, Ui.Ink, TextAlignmentOptions.Center, true).rectTransform;
            }
            logo.sizeDelta = new Vector2(LogoWidth, height);
        }

        // ---------- 창 ----------
        // 처음에는 두 가지 들어가는 방법을 고른다:
        //  · 이 컴퓨터에 저장된 이름 → 이름을 고르면 (방에 들어가던 이름이면) 비밀번호만 적는다
        //  · 이름과 방 코드 → 이름, 방 코드, 비밀번호를 적는다(방 코드를 비우면 혼자 하기)

        enum Mode { Choose, Saved, SavedPin, Form }
        Mode mode;
        RectTransform content;
        string selectedName = "";
        float panelHeight;

        void BuildPanel(Transform root)
        {
            var box = Ui.Box("LoginPanel", root, Ui.PanelSprite, Color.white);
            panel = box.rectTransform;
            content = Ui.Rect("Content", panel);
            Ui.Stretch(content, PadSide, PadBottom, PadSide, PadTop);
            var group = Ui.Vertical(content.gameObject, Spacing, 0);
            group.childAlignment = TextAnchor.UpperCenter;
        }

        /// <summary>창 안을 그 방법의 내용으로 다시 만든다.</summary>
        void Rebuild(Mode next, string message = "")
        {
            TextEntry.HideAll();
            mode = next;
            Ui.Clear(content);
            nameEntry = codeEntry = pinEntry = null;
            errorText = null;
            panelHeight = PadTop + PadBottom - Spacing;

            switch (mode)
            {
                case Mode.Choose:
                    Prompt("어떻게 들어갈까요?");
                    BigButton("SavedLoginButton", "이 컴퓨터에 저장된 이름으로 들어가기", Ui.Sand, () => Rebuild(Mode.Saved));
                    BigButton("FormLoginButton", "이름과 방 코드로 들어가기", Ui.Sand, () => Rebuild(Mode.Form));
                    Error();
                    break;

                case Mode.Saved:
                    Prompt("이 컴퓨터에서 하던 친구");
                    var names = SaveFiles.RecentNames(MaxRecentNames);
                    if (names.Count == 0)
                    {
                        var empty = Ui.Label("Empty", content, "아직 이 컴퓨터에 저장된 이름이 없어요.", 24f, Ui.Muted, TextAlignmentOptions.Center);
                        Fixed(empty.gameObject, 60f);
                    }
                    else RecentGrid(names);
                    Error();
                    BackButton();
                    break;

                case Mode.SavedPin:
                    Prompt(selectedName + WithParticle(selectedName) + " 들어가요");
                    pinEntry = Entry("Pin", "비밀번호 숫자 4자리", RoomLock.PinLength, InputHeight, 34f, true);
                    Error();
                    StartButton(() => Submit(selectedName, "", pinEntry.Value));
                    BackButton(() => Rebuild(Mode.Saved));
                    break;

                case Mode.Form:
                    Prompt("이름과 방 코드를 적어요");
                    nameEntry = Entry("Name", "이름", PlayerName.MaxLength, InputHeight, 36f, false);
                    codeEntry = Entry("Code", "방 코드 (없으면 혼자 하기)", 8, CodeHeight, 26f, false);
                    pinEntry = Entry("Pin", "비밀번호 숫자 4자리", RoomLock.PinLength, PinHeight, 26f, true);
                    Error();
                    StartButton(() => Submit(nameEntry.Value, codeEntry.Value, pinEntry.Value));
                    BackButton();
                    break;
            }

            Arrange(panelHeight);
            SetError(message);
            UpdateEntries();
            if (nameEntry != null) nameEntry.Focus();
            else if (pinEntry != null) pinEntry.Focus();
        }

        /// <summary>낱말 뒤의 "으로/로"(받침이 없거나 ㄹ 받침이면 "로").</summary>
        static string WithParticle(string word)
        {
            if (string.IsNullOrEmpty(word)) return "(으)로";
            char last = word[word.Length - 1];
            if (last < '\uAC00' || last > '\uD7A3') return "(으)로";
            int jong = (last - 0xAC00) % 28;
            return jong == 0 || jong == 8 ? "로" : "으로";
        }

        void Fixed(GameObject go, float height)
        {
            Ui.Fixed(go, 0f, height);
            panelHeight += height + Spacing;
        }

        void Prompt(string text)
        {
            var label = Ui.Label("Prompt", content, text, 28f, Ui.Muted, TextAlignmentOptions.Center);
            Fixed(label.gameObject, PromptHeight + 8f);
        }

        void Error()
        {
            errorText = Ui.Label("Error", content, "", 22f, Ui.Bad, TextAlignmentOptions.Center, true);
            Fixed(errorText.gameObject, ErrorHeight + 8f);
        }

        TextEntry Entry(string id, string placeholder, int maxLength, float height, float fontSize, bool password)
        {
            var entry = new TextEntry(id, content, placeholder, maxLength, height, fontSize, password, () => SetError(""));
            panelHeight += height + Spacing;
            return entry;
        }

        void BigButton(string name, string text, Color color, Action onClick)
        {
            var button = Ui.Button(name, content, text, onClick, 84f, 30f, color);
            Fixed(button.gameObject, 84f);
        }

        void BackButton(Action onClick = null)
        {
            var button = Ui.Button("BackButton", content, "돌아가기", onClick ?? (() => Rebuild(Mode.Choose)), 60f, 24f, Ui.Cream);
            Fixed(button.gameObject, 60f);
        }

        void StartButton(Action onClick)
        {
            Sprite sprite = ArtLibrary.Get(StartArt);
            if (sprite == null)
            {
                var plain = Ui.Button("StartButton", content, "시작", onClick, StartFallbackHeight, 38f, Ui.Green);
                Fixed(plain.gameObject, StartFallbackHeight);
                return;
            }
            // '시작' 글자가 그림에 들어 있으므로 그림을 통째로 버튼으로 쓴다.
            startHeight = ContentWidth * sprite.rect.height / sprite.rect.width;
            var button = Ui.ButtonBox("StartButton", content, sprite, Color.white, onClick);
            var image = (Image)button.targetGraphic;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            Fixed(button.gameObject, startHeight);
        }

        void RecentGrid(List<string> names)
        {
            float cellWidth = (ContentWidth - RecentGap * (RecentColumns - 1)) / RecentColumns;
            var grid = Ui.Grid("Recent", content, cellWidth, RecentHeight, RecentGap, RecentColumns);
            foreach (string name in names)
            {
                string picked = name;
                bool armed = removeArmed == name && Time.unscaledTime < removeArmedUntil;
                var button = Ui.Button("Recent " + name, grid, name, () => PickSaved(picked), RecentHeight, 24f,
                    armed ? Ui.Orange : Ui.Sand);
                // 잘못 만든 이름은 × 를 두 번 눌러 이 컴퓨터에서 지운다.
                var remove = Ui.Button("Forget " + name, button.transform, armed ? "지우기?" : "×", () => ForgetName(picked),
                    38f, 18f, Ui.Red);
                Ui.Put((RectTransform)remove.transform, 1f, 0.5f, -5f, 2f, armed ? 86f : 40f, 38f);
            }
            int rows = (names.Count + RecentColumns - 1) / RecentColumns;
            float height = rows * RecentHeight + (rows - 1) * RecentGap;
            Fixed(grid.gameObject, height);
        }

        /// <summary>저장된 이름을 골랐다. 방에 들어가던 이름이면 비밀번호를 묻고, 혼자 하던 이름이면 바로 들어간다.</summary>
        void PickSaved(string name)
        {
            if (busy) return;
            selectedName = name;
            if (string.IsNullOrEmpty(SaveFiles.RoomCodeOf(name)))
            {
                Submit(name, "", "");
                return;
            }
            Rebuild(Mode.SavedPin);
        }

        void BuildTeacherButton(Transform root)
        {
            var button = Ui.ButtonBox("TeacherButton", root, Ui.RoundSprite, new Color(0.2f, 0.13f, 0.08f, 0.55f), () =>
            {
                if (onTeacher != null) onTeacher();
            });
            Ui.Put((RectTransform)button.transform, 1f, 0f, -24f, 20f, 150f, 46f);
            var label = Ui.Label("Text", button.transform, "선생님", 20f, new Color(1f, 1f, 1f, 0.85f), TextAlignmentOptions.Center);
            Ui.Stretch(label.rectTransform, 0f, 0f, 0f, 0f);
        }

        void UpdateEntries()
        {
            if (nameEntry != null) nameEntry.Update();
            if (codeEntry != null) codeEntry.Update();
            if (pinEntry != null) pinEntry.Update();
        }

        void Update()
        {
            UpdateEntries();
            bool submit = (nameEntry != null && nameEntry.TakeSubmit()) | (codeEntry != null && codeEntry.TakeSubmit())
                | (pinEntry != null && pinEntry.TakeSubmit());
            if (!submit) return;
            if (mode == Mode.Form) Submit(nameEntry.Value, codeEntry.Value, pinEntry.Value);
            else if (mode == Mode.SavedPin) Submit(selectedName, "", pinEntry.Value);
        }

        /// <summary>화면을 보여 준다. 처음 고르는 화면부터 시작한다.</summary>
        public void Show()
        {
            gameObject.SetActive(true);
            busy = false;
            Rebuild(Mode.Choose);
        }

        /// <summary>로고와 창을 한 덩어리로 보고, 화면 위쪽과 동물들 머리 사이의 한가운데에 놓는다.</summary>
        void Arrange(float panelHeight)
        {
            float logoHeight = logo.sizeDelta.y;
            float total = logoHeight + LogoGap + panelHeight;
            float ceiling = ScreenHeight - TopMargin;
            float top = Mathf.Min(ceiling, (ceiling + AnimalTop) / 2f + total / 2f);

            PutBelow(logo, top, LogoWidth, logoHeight);
            PutBelow(panel, top - logoHeight - LogoGap, PanelWidth, panelHeight);
        }

        /// <summary>윗변이 화면 바닥에서 top만큼 올라온 곳에 오도록 가운데에 놓는다.</summary>
        static void PutBelow(RectTransform rect, float top, float width, float height)
        {
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, top);
            rect.sizeDelta = new Vector2(width, height);
        }

        public void Hide()
        {
            TextEntry.HideAll();
            gameObject.SetActive(false);
        }

        void Submit(string name, string codeText, string pinText)
        {
            if (onLogin == null || busy) return;

            // 이름이 이상하면 서버에 물어보기 전에 알려 준다.
            string nameError;
            string cleanName = PlayerName.Clean(name, out nameError);
            if (cleanName == null)
            {
                SetError(nameError);
                return;
            }

            string code;
            bool remembered = false;
            if (string.IsNullOrWhiteSpace(codeText))
            {
                // 코드를 안 적었으면 전에 들어갔던 방으로 자동으로 다시 들어간다.
                code = SaveFiles.RoomCodeOf(cleanName);
                remembered = !string.IsNullOrEmpty(code);
                if (!remembered)
                {
                    Finish(onLogin(cleanName, "", null));
                    return;
                }
            }
            else
            {
                code = RoomCode.Normalize(codeText);
                if (code == null)
                {
                    SetError("방 코드는 영문과 숫자 4~8자예요.");
                    return;
                }
            }

            if (Time.unscaledTime < pinLockedUntil)
            {
                SetError("비밀번호를 여러 번 틀렸어요. " + Mathf.CeilToInt(pinLockedUntil - Time.unscaledTime) + "초 뒤에 다시 해 보세요.");
                return;
            }
            string pin = RoomLock.CleanPin(pinText);
            if (pin == null)
            {
                SetError("방에 들어가려면 비밀번호 숫자 4자리를 적어 주세요.");
                if (pinEntry != null) pinEntry.Focus();
                return;
            }
            string roomLock = RoomLock.For(code, PlayerName.Key(cleanName), pin);

            busy = true;
            SetError("방을 확인하는 중…");
            Online.CheckRoom(code, (room, error) =>
            {
                if (!gameObject.activeInHierarchy) { busy = false; return; }
                if (error != null)
                {
                    busy = false;
                    if (!remembered) { Finish(error); return; }
                    // 전에 들어갔던 방이 닫혔거나 서버가 안 되면 혼자 하기로 들어간다(방 코드는 기억해 둔다).
                    // 이때도 이 컴퓨터에 적힌 비밀번호와 맞아야 들어간다.
                    string localLock = GameSession.LocalLockOf(cleanName);
                    if (localLock.Length > 0 && localLock != roomLock) { WrongPin(); return; }
                    var session = GameSession.Instance;
                    if (session != null) session.LoginNotice = "방 " + code + "에 들어가지 못해 혼자 하기로 들어왔어요. (" + error + ")";
                    Finish(onLogin(cleanName, "", null));
                    return;
                }
                SetError("저장을 불러오는 중…");
                // 서버에 이 이름의 저장이 있으면 가져와서, 이 컴퓨터의 저장과 견주어 더 새것을 쓴다.
                Online.FetchSave(code, cleanName, roomLock, (lookup, json, saveError) =>
                {
                    if (!gameObject.activeInHierarchy) { busy = false; return; }
                    if (lookup == Online.SaveLookup.Error) { busy = false; Finish(saveError); return; }
                    if (lookup == Online.SaveLookup.WrongPin) { busy = false; WrongPin(); return; }
                    wrongPins = 0;
                    var me = GameSession.Instance;
                    if (me != null)
                    {
                        me.PendingLock = roomLock;
                        me.PendingClaim = lookup != Online.SaveLookup.Found;
                        if (lookup != Online.SaveLookup.Found)
                            me.LoginNotice = "비밀번호를 정했어요. 다음부터 이 비밀번호로 들어와요. 잊지 마세요!";
                    }
                    // 선생님이 이 방에 올린 문제가 있으면 받아 둔다. 받지 못하면 기본 문제로 한다.
                    Online.FetchRoomQuiz(code, (quizJson, quizError) =>
                    {
                        busy = false;
                        if (!gameObject.activeInHierarchy) return;
                        var session = GameSession.Instance;
                        if (session != null) session.PendingRoomQuiz = quizError == null ? quizJson : null;
                        Finish(onLogin(cleanName, code, json));
                    });
                });
            });
        }

        string removeArmed;
        float removeArmedUntil;

        /// <summary>
        /// 이 컴퓨터에 있는 그 이름의 저장을 지운다(방 서버의 기록은 그대로, 지우려면 선생님이 지운다).
        /// 실수로 누르지 않도록 4초 안에 한 번 더 눌러야 지운다.
        /// </summary>
        void ForgetName(string name)
        {
            if (busy) return;
            if (removeArmed != name || Time.unscaledTime >= removeArmedUntil)
            {
                removeArmed = name;
                removeArmedUntil = Time.unscaledTime + 4f;
                Rebuild(Mode.Saved);
                SetError("한 번 더 누르면 '" + name + "'을(를) 이 컴퓨터에서 지워요. 되돌릴 수 없어요.");
                return;
            }
            removeArmed = null;
            bool removed = SaveFiles.DeleteProfile(name);
            Rebuild(Mode.Saved);
            SetError(removed ? "'" + name + "'을(를) 이 컴퓨터에서 지웠어요." : "지우지 못했어요.");
        }

        void WrongPin()
        {
            wrongPins++;
            if (wrongPins >= MaxWrongPins)
            {
                wrongPins = 0;
                pinLockedUntil = Time.unscaledTime + WrongPinLockSeconds;
                Finish("비밀번호를 여러 번 틀렸어요. 1분 뒤에 다시 해 보세요.");
                return;
            }
            Finish("비밀번호가 맞지 않아요. 잊었으면 선생님께 비밀번호 초기화를 부탁하세요.");
        }

        void Finish(string error)
        {
            if (error == null) return;
            SetError(error);
            if (!gameObject.activeInHierarchy) return;
            if (nameEntry != null) nameEntry.Focus();
            else if (pinEntry != null) pinEntry.Focus();
        }

        void SetError(string text)
        {
            if (errorText == null) return;
            errorText.text = text;
            errorText.color = text.EndsWith("중…") || text.StartsWith("'") && text.Contains("지웠어요") ? Ui.Muted : Ui.Bad;
        }
    }
}
