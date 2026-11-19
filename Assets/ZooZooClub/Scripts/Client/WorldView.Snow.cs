using System.Collections.Generic;
using UnityEngine;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 눈 내리는 효과. backdrop.json의 snow 기간 안에만 마을 화면 위로 눈송이(snow_1~3 그림)가 내린다.
    /// 눈송이는 화면 기준으로 움직이므로 카메라를 옮기거나 확대해도 같은 크기·같은 빠르기로 보인다.
    /// 메뉴·버튼(화면 UI)은 늘 눈 위에 그려진다.
    /// </summary>
    public sealed partial class WorldView
    {
        const int SnowOrder = 9800;
        const int SnowCount = 55;
        static readonly string[] SnowArts = { "snow_1", "snow_2", "snow_3" };

        [System.Serializable]
        class SnowPeriod
        {
            public string from = "";
            public string until = "";
        }

        class Flake
        {
            public SpriteRenderer renderer;
            public float x, y;          // 화면 기준 위치: -1(왼쪽/아래) ~ 1(오른쪽/위)
            public float size;          // 화면 높이에 대한 비율
            public float fall;          // 초당 내려가는 양(화면 높이 비율)
            public float sway, swaySpeed, phase, spin;
        }

        readonly List<Flake> flakes = new List<Flake>();
        Transform snowRoot;
        bool snowChecked;
        bool snowing;
        float snowCheckAt;

        /// <summary>지금이 눈 오는 기간인지(backdrop.json의 snow 목록).</summary>
        bool IsSnowSeason()
        {
            var asset = Resources.Load<TextAsset>("backdrop");
            if (asset == null) return false;
            BackdropConfig config;
            try { config = JsonUtility.FromJson<BackdropConfig>(asset.text); }
            catch (System.Exception) { return false; }
            if (config == null || config.snow == null) return false;
            long now = game.Now;
            foreach (var period in config.snow)
            {
                if (period == null) continue;
                if (now >= ParseTime(period.from, long.MinValue) && now < ParseTime(period.until, long.MaxValue)) return true;
            }
            return false;
        }

        void UpdateSnow()
        {
            if (cam == null || game == null) return;

            // 기간은 1분마다 다시 본다(켜 둔 채로 날짜가 바뀌어도 맞게).
            if (!snowChecked || Time.unscaledTime >= snowCheckAt)
            {
                snowChecked = true;
                snowCheckAt = Time.unscaledTime + 60f;
                bool want = IsSnowSeason();
                if (want != snowing) SetSnowing(want);
            }
            if (!snowing) return;

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * Mathf.Max(0.1f, cam.aspect);
            Vector3 center = cam.transform.position;
            snowRoot.position = new Vector3(center.x, center.y, 0f);

            foreach (var flake in flakes)
            {
                flake.y -= flake.fall * 2f * dt;
                flake.phase += flake.swaySpeed * dt;
                flake.x += Mathf.Sin(flake.phase) * flake.sway * dt;
                if (flake.y < -1.1f) Respawn(flake, false);
                if (flake.x < -1.1f) flake.x += 2.2f;
                else if (flake.x > 1.1f) flake.x -= 2.2f;

                float worldSize = flake.size * halfHeight * 2f;
                var t = flake.renderer.transform;
                t.localPosition = new Vector3(flake.x * halfWidth, flake.y * halfHeight, 0f);
                float scale = worldSize / Mathf.Max(0.0001f, flake.renderer.sprite.bounds.size.y);
                t.localScale = new Vector3(scale, scale, 1f);
                t.Rotate(0f, 0f, flake.spin * dt);
            }
        }

        void SetSnowing(bool on)
        {
            snowing = on;
            if (!on)
            {
                if (snowRoot != null) snowRoot.gameObject.SetActive(false);
                return;
            }

            if (snowRoot == null)
            {
                var sprites = new List<Sprite>();
                foreach (string name in SnowArts)
                {
                    var sprite = ArtLibrary.Get(name);
                    if (sprite != null) sprites.Add(sprite);
                }
                if (sprites.Count == 0) { snowing = false; return; }

                snowRoot = NewChild("Snow");
                for (int i = 0; i < SnowCount; i++)
                {
                    var flake = new Flake { renderer = NewSprite("Flake", snowRoot, sprites[i % sprites.Count]) };
                    flake.renderer.sortingOrder = SnowOrder;
                    Respawn(flake, true);
                    flakes.Add(flake);
                }
            }
            snowRoot.gameObject.SetActive(true);
        }

        /// <summary>눈송이를 새로 띄운다. 처음에는 화면 곳곳에, 그 뒤로는 화면 위쪽 바깥에서 시작한다.</summary>
        static void Respawn(Flake flake, bool anywhere)
        {
            flake.x = Random.Range(-1.05f, 1.05f);
            flake.y = anywhere ? Random.Range(-1.05f, 1.1f) : Random.Range(1.05f, 1.25f);
            // 멀리 있는 눈(작고 느리고 흐림)과 가까운 눈(크고 빠르고 선명)을 섞는다.
            float depth = Random.value;
            flake.size = Mathf.Lerp(0.02f, 0.045f, depth);
            flake.fall = Mathf.Lerp(0.05f, 0.11f, depth) * Random.Range(0.85f, 1.15f);
            flake.sway = Random.Range(0.02f, 0.06f);
            flake.swaySpeed = Random.Range(0.8f, 1.8f);
            flake.phase = Random.Range(0f, Mathf.PI * 2f);
            flake.spin = Random.Range(-40f, 40f);
            flake.renderer.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.7f, 1f, depth));
            flake.renderer.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        }
    }
}
