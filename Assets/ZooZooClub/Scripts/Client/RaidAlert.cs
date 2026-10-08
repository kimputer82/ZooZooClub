using UnityEngine;
using ZooZooClub.Core;
using URawImage = UnityEngine.UI.RawImage;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 침략이 진행되는 동안 화면 전체에 깔리는 경고 효과.
    /// 침략이 시작되는 순간 화면이 한 번 붉게 번쩍이고, 끝날 때까지 화면 가장자리가 붉게 물들어 맥박처럼 뛴다.
    /// 클릭은 받지 않으며, 다른 화면 요소보다 아래(게임 화면 바로 위)에 그려진다.
    /// </summary>
    public class RaidAlert : MonoBehaviour
    {
        const int TextureSize = 128;

        // 가장자리 붉은 테두리
        const float EdgeStart = 0.64f;       // 화면 가운데에서 이만큼(0~1) 떨어진 곳부터 물들기 시작한다
        const float EdgeMaxAlpha = 0.65f;
        const float EdgeMinPulse = 0.4f;     // 맥박이 가장 약할 때의 세기 (가장 셀 때가 1)
        const float PulsesPerSecond = 0.9f;
        const float FadeSeconds = 0.45f;

        // 시작할 때의 번쩍임
        const float FlashAlpha = 0.3f;
        const float FlashSeconds = 0.7f;

        static readonly Color AlertColor = new Color(0.93f, 0.15f, 0.12f, 1f);

        Game game;
        URawImage edge;
        URawImage flash;
        Texture2D edgeTexture;
        float level;
        float flashLeft;

        public static RaidAlert Create(Transform parent, Game game)
        {
            var rect = Ui.Rect("RaidAlert", parent);
            Ui.Stretch(rect, 0f, 0f, 0f, 0f);
            var alert = rect.gameObject.AddComponent<RaidAlert>();
            alert.Init(game);
            return alert;
        }

        void Init(Game game)
        {
            this.game = game;

            edgeTexture = BuildEdgeTexture();
            edge = NewLayer("Edge", edgeTexture);
            flash = NewLayer("Flash", null);

            game.RaidStarted += OnRaidStarted;

            // 침략 도중에 화면이 만들어진 경우(다시 시작 등)에도 바로 보이게 한다.
            level = game.RaidActive ? 1f : 0f;
            Apply();
        }

        void OnDestroy()
        {
            if (game != null) game.RaidStarted -= OnRaidStarted;
            if (edgeTexture != null) Destroy(edgeTexture);
        }

        URawImage NewLayer(string name, Texture texture)
        {
            var rect = Ui.Rect(name, transform);
            Ui.Stretch(rect, 0f, 0f, 0f, 0f);
            var image = rect.gameObject.AddComponent<URawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            image.enabled = false;
            return image;
        }

        /// <summary>가운데는 투명하고 가장자리로 갈수록 진해지는 흰 그림. 화면 크기에 맞춰 늘여 쓴다.</summary>
        static Texture2D BuildEdgeTexture()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;

            var pixels = new Color32[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float u = Mathf.Abs((x + 0.5f) / TextureSize * 2f - 1f);
                    float v = Mathf.Abs((y + 0.5f) / TextureSize * 2f - 1f);
                    // 네모난 화면의 네 변을 고르게 물들이되 모서리는 둥글게 이어지도록 한다.
                    float distance = Mathf.Pow(Mathf.Pow(u, 4f) + Mathf.Pow(v, 4f), 0.25f);
                    float t = Mathf.Clamp01((distance - EdgeStart) / (1f - EdgeStart));
                    float alpha = t * t;
                    pixels[x + y * TextureSize] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        void OnRaidStarted(int wave)
        {
            flashLeft = FlashSeconds;
        }

        void Update()
        {
            if (game == null) return;

            float deltaTime = Time.unscaledDeltaTime;
            level = Mathf.MoveTowards(level, game.RaidActive ? 1f : 0f, deltaTime / FadeSeconds);
            if (flashLeft > 0f) flashLeft = Mathf.Max(0f, flashLeft - deltaTime);
            Apply();
        }

        void Apply()
        {
            bool showEdge = level > 0.001f;
            if (edge.enabled != showEdge) edge.enabled = showEdge;
            if (showEdge)
            {
                float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * PulsesPerSecond * Mathf.PI * 2f);
                float pulse = Mathf.Lerp(EdgeMinPulse, 1f, wave);
                edge.color = WithAlpha(AlertColor, EdgeMaxAlpha * pulse * level);
            }

            bool showFlash = flashLeft > 0f;
            if (flash.enabled != showFlash) flash.enabled = showFlash;
            if (showFlash)
            {
                float t = flashLeft / FlashSeconds;
                flash.color = WithAlpha(AlertColor, FlashAlpha * t * t);
            }
        }

        static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
