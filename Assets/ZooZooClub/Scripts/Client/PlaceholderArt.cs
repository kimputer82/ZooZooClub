using System.Collections.Generic;
using UnityEngine;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 그림이 준비되기 전까지 쓰는 임시 도형. 흰색 스프라이트를 코드로 만들고 색만 입혀 쓴다.
    /// 실제 아트가 생기면 이 클래스를 부르는 곳만 바꾸면 된다.
    /// </summary>
    public static class PlaceholderArt
    {
        static Sprite diamond;
        static Sprite circle;
        static Sprite box;

        static readonly Dictionary<string, Color> KnownColors = new Dictionary<string, Color>
        {
            { "carrot", new Color(0.96f, 0.55f, 0.16f) },
            { "wheat", new Color(0.93f, 0.82f, 0.38f) },
            { "strawberry", new Color(0.9f, 0.22f, 0.3f) },
            { "corn", new Color(0.98f, 0.86f, 0.2f) },
            { "pumpkin", new Color(0.88f, 0.45f, 0.1f) },
            { "bakery", new Color(0.85f, 0.62f, 0.4f) },
            { "juice_bar", new Color(0.98f, 0.6f, 0.45f) },
            { "jam_workshop", new Color(0.78f, 0.3f, 0.45f) },
            { "popcorn_cart", new Color(0.95f, 0.9f, 0.6f) },
            { "pizza_shop", new Color(0.93f, 0.55f, 0.4f) },
            { "fence", new Color(0.72f, 0.56f, 0.38f) },
            { "flower_bed", new Color(0.95f, 0.55f, 0.75f) },
            { "tree", new Color(0.2f, 0.55f, 0.28f) },
            { "bench", new Color(0.6f, 0.42f, 0.28f) },
            { "street_lamp", new Color(0.98f, 0.93f, 0.6f) },
            { "fountain", new Color(0.45f, 0.75f, 0.95f) },
            { "mushroom", new Color(0.85f, 0.3f, 0.25f) },
            { "flower_barrel", new Color(0.95f, 0.7f, 0.6f) },
            { "signpost", new Color(0.82f, 0.62f, 0.38f) },
            { "bird_bath", new Color(0.88f, 0.82f, 0.68f) },
            { "well", new Color(0.7f, 0.66f, 0.6f) },
            { "floor_wood", new Color(0.78f, 0.55f, 0.32f) },
            { "floor_gravel", new Color(0.86f, 0.8f, 0.68f) },
            { "floor_brick", new Color(0.8f, 0.42f, 0.3f) },
            { "floor_stone", new Color(0.7f, 0.68f, 0.62f) },
            { "floor_marble", new Color(0.94f, 0.9f, 0.84f) }
        };

        public static readonly Color GroundA = new Color(0.55f, 0.78f, 0.42f);
        public static readonly Color GroundB = new Color(0.5f, 0.74f, 0.39f);
        public static readonly Color GroundSeam = new Color(0.44f, 0.68f, 0.35f);
        public static readonly Color Grass = new Color(0.694f, 0.769f, 0.498f);
        public static readonly Color Soil = new Color(0.55f, 0.38f, 0.24f);
        public static readonly Color FacilityBase = new Color(0.78f, 0.74f, 0.66f);
        public static readonly Color DecorBase = new Color(0.62f, 0.82f, 0.5f);
        public static readonly Color Sky = new Color(0.62f, 0.84f, 0.92f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            diamond = null;
            circle = null;
            box = null;
        }

        /// <summary>가로 1, 세로 0.5 크기의 마름모(바닥 한 칸).</summary>
        public static Sprite Diamond
        {
            get
            {
                if (diamond == null) diamond = Build(128, 64, 128f, DiamondAlpha);
                return diamond;
            }
        }

        /// <summary>지름 1의 원.</summary>
        public static Sprite Circle
        {
            get
            {
                if (circle == null) circle = Build(64, 64, 64f, CircleAlpha);
                return circle;
            }
        }

        /// <summary>한 변이 1인 둥근 사각형.</summary>
        public static Sprite Box
        {
            get
            {
                if (box == null) box = Build(64, 64, 64f, BoxAlpha);
                return box;
            }
        }

        public static Color ColorFor(string id)
        {
            Color color;
            if (id != null && KnownColors.TryGetValue(id, out color)) return color;

            int hash = 17;
            if (id != null)
            {
                foreach (char c in id) hash = unchecked(hash * 31 + c);
            }
            float hue = (hash & 0x3ff) / 1023f;
            return Color.HSVToRGB(hue, 0.55f, 0.9f);
        }

        delegate float AlphaAt(float u, float v);

        static Sprite Build(int width, int height, float pixelsPerUnit, AlphaAt alphaAt)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.DontSave;

            var pixels = new Color32[width * height];
            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    // u, v는 중심이 0이고 가장자리가 ±1인 좌표.
                    float u = ((px + 0.5f) / width) * 2f - 1f;
                    float v = ((py + 0.5f) / height) * 2f - 1f;
                    float alpha = Mathf.Clamp01(alphaAt(u, v));
                    pixels[py * width + px] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        static float DiamondAlpha(float u, float v)
        {
            float distance = Mathf.Abs(u) + Mathf.Abs(v);
            return (0.97f - distance) * 30f;
        }

        static float CircleAlpha(float u, float v)
        {
            float distance = Mathf.Sqrt(u * u + v * v);
            return (0.97f - distance) * 20f;
        }

        static float BoxAlpha(float u, float v)
        {
            const float radius = 0.3f;
            float dx = Mathf.Max(Mathf.Abs(u) - (0.97f - radius), 0f);
            float dy = Mathf.Max(Mathf.Abs(v) - (0.97f - radius), 0f);
            float distance = Mathf.Sqrt(dx * dx + dy * dy);
            return (radius - distance) * 20f;
        }
    }
}
