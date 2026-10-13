using System;
using System.Collections.Generic;
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
        const float PanelWidth = 820f;
        const float PanelBaseHeight = 548f;

        // 아래에 세워 둔 동물들 위쪽 공간의 한가운데에 창을 둔다(화면 높이는 늘 1080으로 본다).
        const float AnimalBottom = 36f;
        const float AnimalSize = 170f;
        const float PanelCenterY = (AnimalBottom + AnimalSize) / 2f;

        static readonly Color Grass = new Color(0.67f, 0.78f, 0.47f);

        Func<string, string> onLogin;
        RectTransform panel;
        TMP_InputField input;
        TextMeshProUGUI errorText;
        GameObject recentTitle;
        RectTransform recentGrid;

        /// <summary>
        /// onLogin은 이름을 받아 들어가기를 시도하고, 안 되면 아이에게 보여 줄 이유를, 되면 null을 돌려준다.
        /// animalArt는 화면 아래에 세워 둘 그림 이름들이다.
        /// </summary>
        public void Init(Func<string, string> onLogin, IList<string> animalArt)
        {
            this.onLogin = onLogin;

            var canvasGo = new GameObject("LoginCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // 세로 배치(이름 칸, 하던 이름 목록, 아래의 동물들)가 화면 비율에 따라 겹치지 않도록 높이에 맞춘다.
            scaler.matchWidthOrHeight = 1f;
            canvasGo.AddComponent<GraphicRaycaster>();
            Transform root = canvasGo.transform;

            // 바탕: 하늘과 풀밭. 뒤의 게임 화면을 가리고 클릭도 막는다.
            var sky = Ui.Panel("Sky", root, PlaceholderArt.Sky);
            Ui.Stretch(sky.rectTransform, 0f, 0f, 0f, 0f);
            var grass = Ui.Panel("Grass", root, Grass);
            grass.raycastTarget = false;
            grass.rectTransform.anchorMin = new Vector2(0f, 0f);
            grass.rectTransform.anchorMax = new Vector2(1f, 0f);
            grass.rectTransform.pivot = new Vector2(0.5f, 0f);
            grass.rectTransform.offsetMin = Vector2.zero;
            grass.rectTransform.offsetMax = new Vector2(0f, 150f);

            BuildAnimals(root, animalArt);
            BuildPanel(root);
        }

        void BuildAnimals(Transform root, IList<string> animalArt)
        {
            if (animalArt == null) return;

            var row = Ui.Rect("Animals", root);
            Ui.Put(row, 0.5f, 0f, 0f, AnimalBottom, 1400f, AnimalSize);
            var group = Ui.Horizontal(row.gameObject, 70f, 0);
            group.childAlignment = TextAnchor.LowerCenter;
            group.childForceExpandWidth = false;

            foreach (string art in animalArt)
            {
                Sprite sprite = ArtLibrary.Get(art);
                if (sprite != null) Ui.Icon(art, row, sprite, AnimalSize);
            }
        }

        void BuildPanel(Transform root)
        {
            var box = Ui.Box("LoginPanel", root, Ui.PanelSprite, Color.white);
            panel = box.rectTransform;
            Ui.Put(panel, 0.5f, 0.5f, 0f, PanelCenterY, PanelWidth, PanelBaseHeight);

            var content = Ui.Rect("Content", panel);
            Ui.Stretch(content, 70f, 56f, 70f, 50f);
            var group = Ui.Vertical(content.gameObject, 14f, 0);
            group.childAlignment = TextAnchor.UpperCenter;

            var title = Ui.Label("Title", content, "Zoo Zoo Club", 76f, Ui.Ink, TextAlignmentOptions.Center, true);
            Ui.Fixed(title.gameObject, 0f, 100f);
            var prompt = Ui.Label("Prompt", content, "이름을 적고 내 마을로 들어가요", 30f, Ui.Muted, TextAlignmentOptions.Center);
            Ui.Fixed(prompt.gameObject, 0f, 46f);

            input = BuildInput(content);

            errorText = Ui.Label("Error", content, "", 26f, Ui.Bad, TextAlignmentOptions.Center, true);
            Ui.Fixed(errorText.gameObject, 0f, 38f);

            Ui.Button("StartButton", content, "시작", () => Submit(input.text), 96f, 38f, Ui.Green);

            var recentLabel = Ui.Label("RecentTitle", content, "이 컴퓨터에서 하던 친구", 24f, Ui.Muted, TextAlignmentOptions.Center);
            Ui.Fixed(recentLabel.gameObject, 0f, 56f);
            recentLabel.alignment = TextAlignmentOptions.Bottom;
            recentTitle = recentLabel.gameObject;

            float cellWidth = (PanelWidth - 140f - 16f * (RecentColumns - 1)) / RecentColumns;
            recentGrid = Ui.Grid("Recent", content, cellWidth, 68f, 16f, RecentColumns);
        }

        TMP_InputField BuildInput(Transform parent)
        {
            var box = Ui.Box("NameInput", parent, Ui.CardSprite, Color.white);
            box.raycastTarget = true;
            Ui.Fixed(box.gameObject, 0f, 96f);

            // 입력 칸은 글자 칸과 안내 글이 다 연결된 뒤에 켜야 한다.
            box.gameObject.SetActive(false);

            var area = Ui.Rect("Text Area", box.transform);
            Ui.Stretch(area, 28f, 12f, 28f, 8f);
            area.gameObject.AddComponent<RectMask2D>();

            var placeholder = Ui.Label("Placeholder", area, "여기에 이름", 40f, new Color(0.59f, 0.46f, 0.35f, 0.55f),
                TextAlignmentOptions.Center);
            Ui.Stretch(placeholder.rectTransform, 0f, 0f, 0f, 0f);
            var text = Ui.Label("Text", area, "", 40f, Ui.Ink, TextAlignmentOptions.Center, true);
            Ui.Stretch(text.rectTransform, 0f, 0f, 0f, 0f);

            var field = box.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = box;
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = placeholder;
            if (Ui.Font != null) field.fontAsset = Ui.Font;
            field.pointSize = 40f;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = PlayerName.MaxLength;
            field.richText = false;
            field.caretColor = Ui.Ink;
            field.customCaretColor = true;
            field.selectionColor = new Color(1f, 0.84f, 0.35f, 0.6f);
            field.onSubmit.AddListener(Submit);
            field.onValueChanged.AddListener(_ => SetError(""));

            box.gameObject.SetActive(true);
            return field;
        }

        /// <summary>화면을 보여 준다. 하던 이름 목록을 새로 채우고 이름 칸을 비운다.</summary>
        public void Show()
        {
            gameObject.SetActive(true);
            input.text = "";
            SetError("");

            Ui.Clear(recentGrid);
            var names = SaveFiles.RecentNames(MaxRecentNames);
            foreach (string name in names)
            {
                string picked = name;
                Ui.Button("Recent " + name, recentGrid, name, () => Submit(picked), 68f, 28f, Ui.Sand);
            }

            bool any = names.Count > 0;
            recentTitle.SetActive(any);
            recentGrid.gameObject.SetActive(any);
            int rows = (names.Count + RecentColumns - 1) / RecentColumns;
            float extra = any ? 70f + rows * 68f + (rows - 1) * 16f + 14f : 0f;
            panel.sizeDelta = new Vector2(PanelWidth, PanelBaseHeight + extra);

            input.Select();
            input.ActivateInputField();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        void Submit(string name)
        {
            if (onLogin == null) return;
            string error = onLogin(name);
            if (error != null)
            {
                SetError(error);
                if (gameObject.activeInHierarchy) input.ActivateInputField();
            }
        }

        void SetError(string text)
        {
            if (errorText != null) errorText.text = text;
        }
    }
}
