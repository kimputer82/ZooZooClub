using System;
using TMPro;
using UnityEngine;
using UButton = UnityEngine.UI.Button;
using UContentSizeFitter = UnityEngine.UI.ContentSizeFitter;
using UHorizontal = UnityEngine.UI.HorizontalLayoutGroup;
using UImage = UnityEngine.UI.Image;
using ULayoutElement = UnityEngine.UI.LayoutElement;
using UMask = UnityEngine.UI.Mask;
using UScrollRect = UnityEngine.UI.ScrollRect;
using UVertical = UnityEngine.UI.VerticalLayoutGroup;

namespace ZooZooClub.Client
{
    /// <summary>코드로 uGUI 요소를 만들 때 쓰는 작은 도우미 모음.</summary>
    public static class Ui
    {
        public static readonly Color PanelColor = new Color(0.12f, 0.15f, 0.2f, 0.94f);
        public static readonly Color BarColor = new Color(0.12f, 0.15f, 0.2f, 0.88f);
        public static readonly Color CardColor = new Color(0.2f, 0.25f, 0.32f, 1f);
        public static readonly Color ButtonColor = new Color(0.97f, 0.79f, 0.32f, 1f);
        public static readonly Color ButtonTextColor = new Color(0.16f, 0.12f, 0.05f, 1f);
        public static readonly Color TextColor = new Color(0.96f, 0.96f, 0.93f, 1f);
        public static readonly Color MutedColor = new Color(0.72f, 0.76f, 0.8f, 1f);

        static readonly string[] KoreanFontFamilies =
        {
            "Malgun Gothic",
            "Apple SD Gothic Neo",
            "Noto Sans CJK KR",
            "NanumGothic"
        };

        static TMP_FontAsset font;
        static bool fontResolved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            font = null;
            fontResolved = false;
        }

        /// <summary>
        /// 한글을 그릴 수 있는 글꼴. 지금은 운영체제에 설치된 글꼴을 빌려 쓴다.
        /// 찾지 못하면 null이고, 그때는 TextMeshPro 기본 글꼴이 쓰인다(한글은 깨져 보인다).
        /// </summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (fontResolved) return font;
                fontResolved = true;

                foreach (var family in KoreanFontFamilies)
                {
                    try
                    {
                        font = TMP_FontAsset.CreateFontAsset(family, "Regular");
                    }
                    catch (Exception)
                    {
                        font = null;
                    }
                    if (font != null) break;
                }

                if (font == null) Debug.LogWarning("[Zoo] 한글 글꼴을 찾지 못했습니다. 글자가 깨져 보일 수 있습니다.");
                return font;
            }
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static UImage Panel(string name, Transform parent, Color color)
        {
            var rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<UImage>();
            image.color = color;
            return image;
        }

        public static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            var rect = Rect(name, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (Font != null) label.font = Font;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        public static UButton Button(string name, Transform parent, string text, Action onClick,
            float height = 56f, float fontSize = 24f)
        {
            var image = Panel(name, parent, ButtonColor);
            var button = image.gameObject.AddComponent<UButton>();
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(() => onClick());

            var layout = image.gameObject.AddComponent<ULayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleWidth = 1f;

            var label = Label("Text", image.transform, text, fontSize, ButtonTextColor, TextAlignmentOptions.Center);
            Stretch(label.rectTransform, 8f, 4f, 8f, 4f);
            return button;
        }

        public static void SetButtonText(UButton button, string text)
        {
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = text;
        }

        public static UVertical Vertical(GameObject go, float spacing, int padding)
        {
            var group = go.AddComponent<UVertical>();
            group.spacing = spacing;
            group.padding = new RectOffset(padding, padding, padding, padding);
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            return group;
        }

        public static UHorizontal Horizontal(GameObject go, float spacing, int padding)
        {
            var group = go.AddComponent<UHorizontal>();
            group.spacing = spacing;
            group.padding = new RectOffset(padding, padding, padding, padding);
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            return group;
        }

        /// <summary>
        /// 세로로 스크롤되는 목록을 만들고, 항목을 넣을 Content를 돌려준다.
        /// 세로 배치 그룹 안에 넣으면 남는 높이를 모두 차지한다.
        /// </summary>
        public static RectTransform ScrollView(string name, Transform parent, float spacing)
        {
            var root = Rect(name, parent);
            var rootLayout = root.gameObject.AddComponent<ULayoutElement>();
            rootLayout.minHeight = 80f;
            rootLayout.flexibleHeight = 1f;
            rootLayout.flexibleWidth = 1f;
            var scroll = root.gameObject.AddComponent<UScrollRect>();

            // Mask는 Image가 있어야 동작한다. 그림 자체는 보이지 않게 한다.
            var viewport = Panel("Viewport", root, Color.white);
            Stretch(viewport.rectTransform, 0f, 0f, 0f, 0f);
            viewport.gameObject.AddComponent<UMask>().showMaskGraphic = false;

            var content = Rect("Content", viewport.transform);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            Vertical(content.gameObject, spacing, 0);

            var fitter = content.gameObject.AddComponent<UContentSizeFitter>();
            fitter.horizontalFit = UContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = UContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport.rectTransform;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = UScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            return content;
        }

        /// <summary>정해진 크기의 그림 한 칸. 가로로 늘어나지 않는다.</summary>
        public static UImage Icon(string name, Transform parent, Sprite sprite, float size)
        {
            var image = Panel(name, parent, Color.white);
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;

            var layout = image.gameObject.AddComponent<ULayoutElement>();
            layout.minWidth = size;
            layout.preferredWidth = size;
            layout.minHeight = size;
            layout.preferredHeight = size;
            layout.flexibleWidth = 0f;
            return image;
        }

        /// <summary>부모를 가득 채우되 네 변에서 주어진 만큼 안쪽으로 들인다.</summary>
        public static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>자식을 모두 치운다. 실제 삭제는 프레임 끝에 일어나므로 먼저 꺼서 배치 계산에서 뺀다.</summary>
        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }
    }
}
