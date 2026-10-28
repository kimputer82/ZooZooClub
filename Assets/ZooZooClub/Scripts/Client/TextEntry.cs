using System;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZooZooClub.Core;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 글자를 적는 칸 하나. 보통은 유니티(TextMeshPro) 입력 칸이지만, 브라우저(WebGL)에서는 한글 조합(IME)이
    /// 안 되므로 같은 자리에 진짜 HTML 입력 칸을 얹어 쓴다(Plugins/WebGL/ZooWebInput.jslib).
    /// 만든 쪽에서 매 프레임 Update()를 불러 주고, Enter는 TakeSubmit()으로 알아낸다.
    /// </summary>
    public sealed class TextEntry
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void ZooWebInputShow(string id, float left, float top, float width, float height, string placeholder, int maxLength, int password);
        [DllImport("__Internal")] static extern void ZooWebInputFocus(string id);
        [DllImport("__Internal")] static extern void ZooWebInputHide(string id);
        [DllImport("__Internal")] static extern void ZooWebInputHideAll();
        [DllImport("__Internal")] static extern void ZooWebInputClear(string id);
        [DllImport("__Internal")] static extern string ZooWebInputGetValue(string id);
        [DllImport("__Internal")] static extern int ZooWebInputTakeSubmit();
        public const bool Web = true;
#else
        static void ZooWebInputShow(string id, float left, float top, float width, float height, string placeholder, int maxLength, int password) { }
        static void ZooWebInputFocus(string id) { }
        static void ZooWebInputHide(string id) { }
        static void ZooWebInputHideAll() { }
        static void ZooWebInputClear(string id) { }
        static string ZooWebInputGetValue(string id) { return ""; }
        static int ZooWebInputTakeSubmit() { return 0; }
        public const bool Web = false;
#endif

        readonly string id;
        readonly string placeholderText;
        readonly int maxLength;
        readonly bool password;
        readonly TMP_InputField field;
        readonly TextMeshProUGUI placeholder;
        readonly TextMeshProUGUI text;
        bool submitted;

        public RectTransform Rect { get; private set; }

        /// <summary>
        /// parent(세로 배치 그룹) 안에 높이 height의 입력 칸을 만든다. onChanged는 글자가 바뀔 때(유니티 칸에서만) 불린다.
        /// </summary>
        public TextEntry(string id, Transform parent, string placeholderText, int maxLength, float height, float fontSize,
            bool password = false, Action onChanged = null)
        {
            this.id = id;
            this.placeholderText = placeholderText;
            this.maxLength = maxLength;
            this.password = password;

            var box = Ui.Box(id + "Input", parent, Ui.CardSprite, Color.white);
            box.raycastTarget = true;
            Ui.Fixed(box.gameObject, 0f, height);
            Rect = box.rectTransform;

            // 입력 칸은 글자 칸과 안내 글이 다 연결된 뒤에 켜야 한다.
            box.gameObject.SetActive(false);

            var area = Ui.Rect("Text Area", box.transform);
            Ui.Stretch(area, 28f, 10f, 28f, 6f);
            area.gameObject.AddComponent<RectMask2D>();

            placeholder = Ui.Label("Placeholder", area, placeholderText, fontSize, new Color(0.59f, 0.46f, 0.35f, 0.55f),
                TextAlignmentOptions.Center);
            Ui.Stretch(placeholder.rectTransform, 0f, 0f, 0f, 0f);
            // 안내 글이 칸보다 길면 글자 크기를 줄여 한 줄에 다 보이게 한다.
            placeholder.textWrappingMode = TextWrappingModes.NoWrap;
            placeholder.enableAutoSizing = true;
            placeholder.fontSizeMax = fontSize;
            placeholder.fontSizeMin = Mathf.Min(fontSize, 12f);
            text = Ui.Label("Text", area, "", fontSize, Ui.Ink, TextAlignmentOptions.Center, true);
            Ui.Stretch(text.rectTransform, 0f, 0f, 0f, 0f);

            field = box.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = box;
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = placeholder;
            if (Ui.Font != null) field.fontAsset = Ui.Font;
            field.pointSize = fontSize;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = maxLength;
            field.richText = false;
            field.caretColor = Ui.Ink;
            field.customCaretColor = true;
            field.selectionColor = new Color(1f, 0.84f, 0.35f, 0.6f);
            if (password) field.contentType = TMP_InputField.ContentType.Password;
            field.onSubmit.AddListener(_ => submitted = true);
            if (onChanged != null) field.onValueChanged.AddListener(_ => onChanged());

            box.gameObject.SetActive(true);

            if (Web)
            {
                field.enabled = false;
                HideUnityLabels();
            }
        }

        public string Value
        {
            get { return Web ? (ZooWebInputGetValue(id) ?? "") : field.text; }
        }

        public void Clear()
        {
            field.text = "";
            HideUnityLabels();
            ZooWebInputClear(id);
            submitted = false;
        }

        public void Focus()
        {
            if (Web) ZooWebInputFocus(id);
            else
            {
                field.Select();
                field.ActivateInputField();
            }
        }

        /// <summary>브라우저의 HTML 칸을 유니티 칸 자리에 맞춘다. 화면이 보이는 동안 매 프레임 부른다.</summary>
        public void Update()
        {
            if (!Web) return;
            HideUnityLabels();
            // 유니티 시작 화면이 끝나기 전에는 입력 칸이 그 위에 떠 보이므로 기다린다.
            if (!UnityEngine.Rendering.SplashScreen.isFinished) return;
            if (!Rect.gameObject.activeInHierarchy) { ZooWebInputHide(id); return; }

            var corners = new Vector3[4];
            Rect.GetWorldCorners(corners);
            // 화면 덮기 캔버스라 월드 좌표가 곧 화면 픽셀 좌표다(왼쪽 아래가 원점).
            float minX = corners[0].x, minY = corners[0].y, maxX = corners[2].x, maxY = corners[2].y;
            float w = Mathf.Max(1f, Screen.width), h = Mathf.Max(1f, Screen.height);
            ZooWebInputShow(id, minX / w, 1f - maxY / h, (maxX - minX) / w, (maxY - minY) / h, placeholderText, maxLength, password ? 1 : 0);
#if UNITY_WEBGL && !UNITY_EDITOR
            // 유니티가 키보드 입력을 독차지하면 HTML 입력 칸에 글자가 들어가지 않는다.
            WebGLInput.captureAllKeyboardInput = false;
#endif
        }

        /// <summary>이 칸을 숨긴다(화면을 닫을 때).</summary>
        public void Hide()
        {
            ZooWebInputHide(id);
        }

        /// <summary>모든 HTML 입력 칸을 숨기고 키보드를 유니티에 돌려준다. 게임 화면으로 갈 때 부른다.</summary>
        public static void HideAll()
        {
            ZooWebInputHideAll();
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLInput.captureAllKeyboardInput = true;
#endif
        }

        /// <summary>이 칸에서 Enter를 눌렀으면 true(한 번만). 브라우저에서는 어느 칸이든 Enter면 true.</summary>
        public bool TakeSubmit()
        {
            if (Web) return ZooWebInputTakeSubmit() != 0;
            bool was = submitted;
            submitted = false;
            return was;
        }

        /// <summary>브라우저에서는 유니티 칸의 글자(안내 글 포함)를 숨긴다. 칸이 글을 바꿀 때 다시 켜지므로 거듭 부른다.</summary>
        void HideUnityLabels()
        {
            if (!Web) return;
            if (placeholder != null && placeholder.enabled) placeholder.enabled = false;
            if (text != null && text.enabled) text.enabled = false;
        }
    }
}
