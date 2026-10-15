using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UButton = UnityEngine.UI.Button;
using UColorBlock = UnityEngine.UI.ColorBlock;
using UContentSizeFitter = UnityEngine.UI.ContentSizeFitter;
using UGrid = UnityEngine.UI.GridLayoutGroup;
using UHorizontal = UnityEngine.UI.HorizontalLayoutGroup;
using UImage = UnityEngine.UI.Image;
using ULayoutElement = UnityEngine.UI.LayoutElement;
using UMask = UnityEngine.UI.Mask;
using UScrollRect = UnityEngine.UI.ScrollRect;
using UVertical = UnityEngine.UI.VerticalLayoutGroup;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 코드로 uGUI 요소를 만들 때 쓰는 도우미 모음. 색, 글꼴, 창·버튼 바탕 그림을 여기서 정한다.
    /// 바탕 그림은 Resources/Art 의 ui_*.png 이고, 없으면 단색 사각형으로 대신한다.
    /// </summary>
    public static class Ui
    {
        // 글자색
        public static readonly Color Ink = Rgb(92, 58, 33);
        public static readonly Color Muted = Rgb(150, 118, 88);
        public static readonly Color Good = Rgb(64, 146, 58);
        public static readonly Color Bad = Rgb(212, 68, 58);

        // 버튼과 바탕색
        public static readonly Color Cream = Rgb(255, 247, 226);
        public static readonly Color Sand = Rgb(246, 232, 200);
        public static readonly Color Green = Rgb(132, 206, 84);
        public static readonly Color Orange = Rgb(255, 170, 66);
        public static readonly Color Yellow = Rgb(255, 214, 90);
        public static readonly Color Blue = Rgb(110, 194, 244);
        public static readonly Color Red = Rgb(240, 104, 92);
        public static readonly Color Purple = Rgb(184, 160, 240);
        public static readonly Color Gray = Rgb(204, 196, 184);
        public static readonly Color Pill = new Color(0.2f, 0.13f, 0.08f, 0.8f);
        public static readonly Color Dim = new Color(0.08f, 0.05f, 0.03f, 0.55f);

        static readonly string[] KoreanFontFamilies =
        {
            "Malgun Gothic",
            "Apple SD Gothic Neo",
            "Noto Sans CJK KR",
            "NanumGothic"
        };

        static TMP_FontAsset font;
        static bool fontResolved;
        static readonly Dictionary<string, Sprite> SlicedCache = new Dictionary<string, Sprite>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            font = null;
            fontResolved = false;
            SlicedCache.Clear();
        }

        static Color Rgb(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        /// <summary>글 속에서 일부만 색을 바꿀 때 쓰는 태그. 닫을 때는 &lt;/color&gt;.</summary>
        public static string ColorTag(Color color)
        {
            return "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">";
        }

        /// <summary>게임에 넣어 둔 한글 글꼴(Resources/Fonts). 브라우저처럼 운영체제 글꼴이 없는 곳에서도 쓰인다.</summary>
        const string BundledFont = "Fonts/NotoSansKR-Zoo";

        /// <summary>
        /// 한글을 그릴 수 있는 글꼴. 게임에 넣어 둔 글꼴을 먼저 쓰고, 없으면 운영체제에 설치된 글꼴을 빌려 쓴다.
        /// 둘 다 없으면 null이고, 그때는 TextMeshPro 기본 글꼴이 쓰인다(한글은 깨져 보인다).
        /// </summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (fontResolved) return font;
                fontResolved = true;

                var bundled = Resources.Load<UnityEngine.Font>(BundledFont);
                if (bundled != null)
                {
                    try
                    {
                        font = TMP_FontAsset.CreateFontAsset(bundled, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                            AtlasPopulationMode.Dynamic, true);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[Zoo] 넣어 둔 글꼴을 쓰지 못했습니다: " + e.Message);
                        font = null;
                    }
                }
                if (font != null) return font;

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

        // ---------- 바탕 그림 ----------

        // 늘여도 모서리가 찌그러지지 않게 그림마다 모서리 크기(왼쪽, 아래, 오른쪽, 위 픽셀)를 정해 둔다.
        // 그림을 바꿀 때는 Tools/make_ui.py 가 만든 것과 같은 크기로 그려야 이 값이 맞는다.
        public static Sprite PanelSprite { get { return Sliced("ui_panel", 40f, 44f, 40f, 40f); } }
        public static Sprite CardSprite { get { return Sliced("ui_card", 24f, 28f, 24f, 24f); } }
        public static Sprite ButtonSprite { get { return Sliced("ui_button", 26f, 32f, 26f, 26f); } }
        public static Sprite CapsuleSprite { get { return Sliced("ui_capsule", 27f, 27f, 27f, 27f); } }

        static Sprite Sliced(string name, float left, float bottom, float right, float top)
        {
            Sprite sprite;
            if (SlicedCache.TryGetValue(name, out sprite)) return sprite;

            Sprite source = ArtLibrary.Get(name);
            if (source != null)
            {
                var texture = source.texture;
                sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                    new Vector4(left, bottom, right, top));
                sprite.hideFlags = HideFlags.DontSave;
            }
            SlicedCache[name] = sprite;
            return sprite;
        }

        // ---------- 기본 요소 ----------

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>단색 사각형.</summary>
        public static UImage Panel(string name, Transform parent, Color color)
        {
            var rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<UImage>();
            image.color = color;
            return image;
        }

        /// <summary>모서리를 지키며 늘어나는 바탕 그림. 클릭은 받지 않는다(필요하면 raycastTarget을 켠다).</summary>
        public static UImage Box(string name, Transform parent, Sprite sprite, Color color)
        {
            var image = Panel(name, parent, color);
            image.raycastTarget = false;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = UImage.Type.Sliced;
            }
            return image;
        }

        /// <summary>그림 한 장. 비율을 지키며 칸 안에 맞춰 그린다. 클릭은 받지 않는다.</summary>
        public static UImage Picture(string name, Transform parent, Sprite sprite)
        {
            var image = Panel(name, parent, Color.white);
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = sprite != null;
            return image;
        }

        public static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left, bool bold = false)
        {
            var rect = Rect(name, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (Font != null) label.font = Font;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            if (bold) label.fontStyle = FontStyles.Bold;
            label.text = text;
            return label;
        }

        /// <summary>바탕색 위에 얹을 글자색. 밝은 바탕에는 갈색, 어두운 바탕에는 흰색.</summary>
        public static Color TextOn(Color background)
        {
            float brightness = background.r * 0.299f + background.g * 0.587f + background.b * 0.114f;
            return brightness > 0.6f ? Ink : Color.white;
        }

        static void StyleButton(UButton button)
        {
            UColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.96f, 0.96f, 0.96f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.72f, 0.72f, 0.72f, 0.8f);
            colors.fadeDuration = 0.06f;
            button.colors = colors;
        }

        /// <summary>
        /// 글자가 들어간 버튼. 세로 배치 그룹 안에서는 가로로 늘어나고, 가로 배치 그룹 안에서는 나눠 가진다.
        /// </summary>
        public static UButton Button(string name, Transform parent, string text, Action onClick,
            float height, float fontSize, Color color)
        {
            var image = Box(name, parent, ButtonSprite, color);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<UButton>();
            button.targetGraphic = image;
            StyleButton(button);
            button.onClick.AddListener(() => Sound.Play(Sound.Click));
            if (onClick != null) button.onClick.AddListener(() => onClick());

            var layout = image.gameObject.AddComponent<ULayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleWidth = 1f;

            var label = Label("Text", image.transform, text, fontSize, TextOn(color), TextAlignmentOptions.Center, true);
            // 버튼 그림 아래쪽에는 두께가 있어서 글자를 조금 위로 올린다.
            Stretch(label.rectTransform, 10f, 10f, 10f, 3f);
            return button;
        }

        /// <summary>바탕 그림이 통째로 눌리는 버튼(카드, 원형 버튼). 안의 내용은 부르는 쪽에서 채운다.</summary>
        public static UButton ButtonBox(string name, Transform parent, Sprite sprite, Color color, Action onClick)
        {
            var image = Box(name, parent, sprite, color);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<UButton>();
            button.targetGraphic = image;
            StyleButton(button);
            button.onClick.AddListener(() => Sound.Play(Sound.Click));
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        public static void SetButtonText(UButton button, string text)
        {
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = text;
        }

        /// <summary>정해진 크기의 그림 한 칸(배치 그룹 안에서 쓴다). 가로로 늘어나지 않는다.</summary>
        public static UImage Icon(string name, Transform parent, Sprite sprite, float size)
        {
            var image = Picture(name, parent, sprite);
            Fixed(image.gameObject, size, size);
            return image;
        }

        /// <summary>
        /// 알약 모양 바탕에 왼쪽 그림과 글자가 들어간 작은 표시(가격, 시간 등). 크기는 부르는 쪽에서 정한다.
        /// </summary>
        public static TextMeshProUGUI Chip(string name, Transform parent, Sprite icon, string text, Color color,
            float height, float fontSize)
        {
            var box = Box(name, parent, CapsuleSprite, color);
            float iconSize = height - 8f;
            float left = 14f;
            if (icon != null)
            {
                var picture = Picture("Icon", box.transform, icon);
                Put(picture.rectTransform, 0f, 0.5f, 6f, 0f, iconSize, iconSize);
                left = iconSize + 8f;
            }

            Color textColor = color.a < 0.95f ? Color.white : TextOn(color);
            var label = Label("Text", box.transform, text, fontSize, textColor, TextAlignmentOptions.Center, true);
            Stretch(label.rectTransform, left, 0f, 14f, 0f);
            return label;
        }

        // ---------- 배치 ----------

        /// <summary>배치 그룹 안에서 크기를 고정한다. 0 이하를 주면 그 방향은 건드리지 않는다.</summary>
        public static ULayoutElement Fixed(GameObject go, float width, float height)
        {
            var layout = go.GetComponent<ULayoutElement>();
            if (layout == null) layout = go.AddComponent<ULayoutElement>();
            if (width > 0f)
            {
                layout.minWidth = width;
                layout.preferredWidth = width;
                layout.flexibleWidth = 0f;
            }
            if (height > 0f)
            {
                layout.minHeight = height;
                layout.preferredHeight = height;
                layout.flexibleHeight = 0f;
            }
            return layout;
        }

        /// <summary>배치 그룹 안에서 남는 공간을 차지하는 빈칸.</summary>
        public static RectTransform Spacer(Transform parent, float flexibleWidth, float flexibleHeight)
        {
            var rect = Rect("Spacer", parent);
            var layout = rect.gameObject.AddComponent<ULayoutElement>();
            layout.flexibleWidth = flexibleWidth;
            layout.flexibleHeight = flexibleHeight;
            return rect;
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

        /// <summary>같은 크기의 칸을 정해진 열 수로 늘어놓는 격자.</summary>
        public static RectTransform Grid(string name, Transform parent, float cellWidth, float cellHeight,
            float spacing, int columns)
        {
            var rect = Rect(name, parent);
            var grid = rect.gameObject.AddComponent<UGrid>();
            grid.cellSize = new Vector2(cellWidth, cellHeight);
            grid.spacing = new Vector2(spacing, spacing);
            grid.startCorner = UGrid.Corner.UpperLeft;
            grid.startAxis = UGrid.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = UGrid.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            return rect;
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
            scroll.scrollSensitivity = 40f;
            return content;
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

        /// <summary>
        /// 부모의 한 점(anchorX, anchorY: 0~1)에 같은 쪽 모서리를 맞춰 놓는다.
        /// 예: (1, 1)이면 오른쪽 위 모서리 기준이고 x, y는 그 점에서 떨어진 거리다.
        /// </summary>
        public static void Put(RectTransform rect, float anchorX, float anchorY, float x, float y, float width, float height)
        {
            var anchor = new Vector2(anchorX, anchorY);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
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
