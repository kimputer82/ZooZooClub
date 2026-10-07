using System.Collections.Generic;
using UnityEngine;

namespace ZooZooClub.Client
{
    /// <summary>
    /// Assets/ZooZooClub/Resources/Art 폴더의 그림을 이름으로 꺼내 쓴다.
    /// 그림을 바꾸려면 같은 이름의 PNG로 덮어쓰기만 하면 된다. 그림이 없으면 null을 돌려주고,
    /// 부르는 쪽에서 임시 도형으로 대신한다.
    ///
    /// 이름 규칙: 시설과 장식은 카탈로그의 id 그대로(bakery, tree ...),
    /// 작물은 crop_&lt;id&gt;, 새싹은 crop_sprout, 주민은 resident_&lt;id&gt;, 밭은 plot.
    /// </summary>
    public static class ArtLibrary
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Cache.Clear();
        }

        public static Sprite Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            Sprite sprite;
            if (Cache.TryGetValue(name, out sprite)) return sprite;

            sprite = Resources.Load<Sprite>("Art/" + name);
            Cache[name] = sprite;
            return sprite;
        }

        /// <summary>
        /// 그림의 바닥 가운데가 groundPoint(부모 기준 좌표)에 오고 가로 폭이 width가 되도록 놓는다.
        /// 그림 파일의 해상도나 기준점이 달라져도 화면에서의 크기와 위치는 그대로 유지된다.
        /// </summary>
        public static void Fit(SpriteRenderer renderer, Sprite sprite, Vector3 groundPoint, float width)
        {
            renderer.sprite = sprite;
            var bounds = sprite.bounds;
            float scale = width / Mathf.Max(0.0001f, bounds.size.x);
            renderer.transform.localScale = new Vector3(scale, scale, 1f);
            renderer.transform.localPosition = groundPoint
                + new Vector3(-bounds.center.x * scale, -bounds.min.y * scale, 0f);
        }

        /// <summary>Fit으로 놓았을 때 그림의 화면상 높이.</summary>
        public static float FittedHeight(Sprite sprite, float width)
        {
            var size = sprite.bounds.size;
            return size.y * width / Mathf.Max(0.0001f, size.x);
        }
    }
}
