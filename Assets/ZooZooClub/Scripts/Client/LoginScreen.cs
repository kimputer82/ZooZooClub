using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZooZooClub.Core;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 게임을 켜면 처음 나오는 화면. 학생이 이름을 적으면 그 이름의 마을(저장 파일)로 들어간다.
    /// 이 컴퓨터에서 하던 이름은 아래에 버튼으로 나와서, 눌러서 바로 이어 할 수 있다.
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
        const float PadTop = 40f;
        const float PadBottom = 50f;
        const float Spacing = 10f;
        const float ContentWidth = PanelWidth - PadSide * 2f;
        const float PromptHeight = 40f;
        const float InputHeight = 88f;
        const float ErrorHeight = 30f;
        const float StartFallbackHeight = 96f;
        const float RecentTitleHeight = 36f;
        const float RecentHeight = 56f;
        const float RecentGap = 12f;

        Func<string, string> onLogin;
        RectTransform logo;
        RectTransform panel;
        TMP_InputField input;
        TextMeshProUGUI[] unityInputLabels;
        TextMeshProUGUI errorText;
        GameObject recentTitle;
        RectTransform recentGrid;
        float startHeight = StartFallbackHeight;

        /// <summary>
        /// onLogin은 이름을 받아 들어가기를 시도하고, 안 되면 아이에게 보여 줄 이유를, 되면 null을 돌려준다.
        /// </summary>
        public void Init(Func<string, string> onLogin)
        {
            this.onLogin = onLogin;

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

        void BuildPanel(Transform root)
        {
            var box = Ui.Box("LoginPanel", root, Ui.PanelSprite, Color.white);
            panel = box.rectTransform;

            var content = Ui.Rect("Content", panel);
            Ui.Stretch(content, PadSide, PadBottom, PadSide, PadTop);
            var group = Ui.Vertical(content.gameObject, Spacing, 0);
            group.childAlignment = TextAnchor.UpperCenter;

            var prompt = Ui.Label("Prompt", content, "이름을 적고 내 마을로 들어가요", 28f, Ui.Muted, TextAlignmentOptions.Center);
            Ui.Fixed(prompt.gameObject, 0f, PromptHeight);

            input = BuildInput(content);

            errorText = Ui.Label("Error", content, "", 24f, Ui.Bad, TextAlignmentOptions.Center, true);
            Ui.Fixed(errorText.gameObject, 0f, ErrorHeight);

            BuildStartButton(content);

            var recentLabel = Ui.Label("RecentTitle", content, "이 컴퓨터에서 하던 친구", 22f, Ui.Muted, TextAlignmentOptions.Center);
            Ui.Fixed(recentLabel.gameObject, 0f, RecentTitleHeight);
            recentLabel.alignment = TextAlignmentOptions.Bottom;
            recentTitle = recentLabel.gameObject;

            float cellWidth = (ContentWidth - RecentGap * (RecentColumns - 1)) / RecentColumns;
            recentGrid = Ui.Grid("Recent", content, cellWidth, RecentHeight, RecentGap, RecentColumns);
        }

        void BuildStartButton(Transform parent)
        {
            Sprite sprite = ArtLibrary.Get(StartArt);
            if (sprite == null)
            {
                Ui.Button("StartButton", parent, "시작", () => Submit(TypedName), StartFallbackHeight, 38f, Ui.Green);
                return;
            }

            // '시작' 글자가 그림에 들어 있으므로 그림을 통째로 버튼으로 쓴다.
            startHeight = ContentWidth * sprite.rect.height / sprite.rect.width;
            var button = Ui.ButtonBox("StartButton", parent, sprite, Color.white, () => Submit(TypedName));
            var image = (Image)button.targetGraphic;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            Ui.Fixed(button.gameObject, 0f, startHeight);
        }

        /// <summary>지금 이름 칸에 적힌 글. 브라우저에서는 HTML 입력 칸의 값이다.</summary>
        string TypedName
        {
            get { return WebNameInput.Available ? WebNameInput.Value : input.text; }
        }

        TMP_InputField BuildInput(Transform parent)
        {
            var box = Ui.Box("NameInput", parent, Ui.CardSprite, Color.white);
            box.raycastTarget = true;
            Ui.Fixed(box.gameObject, 0f, InputHeight);

            // 입력 칸은 글자 칸과 안내 글이 다 연결된 뒤에 켜야 한다.
            box.gameObject.SetActive(false);

            var area = Ui.Rect("Text Area", box.transform);
            Ui.Stretch(area, 28f, 10f, 28f, 6f);
            area.gameObject.AddComponent<RectMask2D>();

            var placeholder = Ui.Label("Placeholder", area, "여기에 이름", 38f, new Color(0.59f, 0.46f, 0.35f, 0.55f),
                TextAlignmentOptions.Center);
            Ui.Stretch(placeholder.rectTransform, 0f, 0f, 0f, 0f);
            var text = Ui.Label("Text", area, "", 38f, Ui.Ink, TextAlignmentOptions.Center, true);
            Ui.Stretch(text.rectTransform, 0f, 0f, 0f, 0f);

            var field = box.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = box;
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = placeholder;
            if (Ui.Font != null) field.fontAsset = Ui.Font;
            field.pointSize = 38f;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = PlayerName.MaxLength;
            field.richText = false;
            field.caretColor = Ui.Ink;
            field.customCaretColor = true;
            field.selectionColor = new Color(1f, 0.84f, 0.35f, 0.6f);
            field.onSubmit.AddListener(Submit);
            field.onValueChanged.AddListener(_ => SetError(""));

            box.gameObject.SetActive(true);

            // 브라우저에서는 한글 조합이 안 되므로 유니티 입력 칸 대신 같은 자리에 HTML 입력 칸을 얹는다(Update 참고).
            if (WebNameInput.Available)
            {
                field.enabled = false;
                unityInputLabels = new[] { placeholder, text };
                HideUnityInputLabels();
            }
            return field;
        }

        /// <summary>브라우저에서는 유니티 입력 칸의 글자(안내 글 포함)를 숨긴다. 입력 칸이 글을 바꿀 때 다시 켜지므로 거듭 부른다.</summary>
        void HideUnityInputLabels()
        {
            if (unityInputLabels == null) return;
            foreach (var label in unityInputLabels)
            {
                if (label != null && label.enabled) label.enabled = false;
            }
        }

        void Update()
        {
            if (!WebNameInput.Available || input == null) return;
            HideUnityInputLabels();
            WebNameInput.Show(input.GetComponent<RectTransform>(), "여기에 이름", PlayerName.MaxLength);
            if (WebNameInput.TakeSubmit()) Submit(TypedName);
        }

        /// <summary>화면을 보여 준다. 하던 이름 목록을 새로 채우고 이름 칸을 비운다.</summary>
        public void Show()
        {
            gameObject.SetActive(true);
            input.text = "";
            HideUnityInputLabels();
            WebNameInput.Clear();
            SetError("");

            Ui.Clear(recentGrid);
            var names = SaveFiles.RecentNames(MaxRecentNames);
            foreach (string name in names)
            {
                string picked = name;
                Ui.Button("Recent " + name, recentGrid, name, () => Submit(picked), RecentHeight, 26f, Ui.Sand);
            }

            bool any = names.Count > 0;
            recentTitle.SetActive(any);
            recentGrid.gameObject.SetActive(any);
            int rows = (names.Count + RecentColumns - 1) / RecentColumns;

            float height = PadTop + PromptHeight + InputHeight + ErrorHeight + startHeight + Spacing * 3f + PadBottom;
            if (any) height += Spacing * 2f + RecentTitleHeight + rows * RecentHeight + (rows - 1) * RecentGap;
            Arrange(height);

            if (WebNameInput.Available)
            {
                WebNameInput.Show(input.GetComponent<RectTransform>(), "여기에 이름", PlayerName.MaxLength);
                WebNameInput.Focus();
            }
            else
            {
                input.Select();
                input.ActivateInputField();
            }
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
            WebNameInput.Hide();
            gameObject.SetActive(false);
        }

        void Submit(string name)
        {
            if (onLogin == null) return;
            string error = onLogin(name);
            if (error != null)
            {
                SetError(error);
                if (!gameObject.activeInHierarchy) return;
                if (WebNameInput.Available) WebNameInput.Focus();
                else input.ActivateInputField();
            }
        }

        void SetError(string text)
        {
            if (errorText != null) errorText.text = text;
        }
    }
}
