using System;
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
    /// 방어 시설은 등급마다 그림이 다르다: &lt;id&gt;_&lt;등급 id&gt; (예: guard_rare). 없으면 &lt;id&gt; 그림을 쓴다.
    /// 돌릴 수 있는 배치물은 뒷모습 &lt;id&gt;_back, 옆 방향 &lt;id&gt;_side 그림을 둘 수 있다.
    /// _side 그림이 없으면 옆 방향은 앞모습을 좌우로 뒤집어 보여 준다.
    /// </summary>
    public static class ArtLibrary
    {
        /// <summary>
        /// 그림 한 장을 화면에 놓는 방법. Resources/art_layout.json 에서 읽는다.
        /// width는 화면에서의 가로 폭(격자 한 칸의 폭이 1), lift는 그림의 맨 아래가
        /// 차지하는 땅의 앞쪽 꼭짓점보다 얼마나 위에 오는지, offsetX는 좌우로 미는 정도다.
        /// </summary>
        [Serializable]
        public class LayoutEntry
        {
            public string id;
            public float width;
            public float lift;
            public float offsetX;
        }

        [Serializable]
        public class LayoutFile
        {
            public List<LayoutEntry> entries = new List<LayoutEntry>();
        }

        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        static Dictionary<string, LayoutEntry> layouts;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Cache.Clear();
            layouts = null;
        }

        /// <summary>그림의 배치 값. 파일에 적혀 있지 않으면 null.</summary>
        public static LayoutEntry Layout(string id)
        {
            if (layouts == null)
            {
                layouts = new Dictionary<string, LayoutEntry>();
                var asset = Resources.Load<TextAsset>("art_layout");
                if (asset != null)
                {
                    var file = JsonUtility.FromJson<LayoutFile>(asset.text);
                    if (file != null && file.entries != null)
                    {
                        foreach (var entry in file.entries)
                        {
                            if (entry != null && !string.IsNullOrEmpty(entry.id)) layouts[entry.id] = entry;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(id)) return null;
            LayoutEntry found;
            return layouts.TryGetValue(id, out found) ? found : null;
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
        /// 변형 그림의 이름. baseName_variant 그림이 있으면 그 이름을, 없으면 baseName을 돌려준다.
        /// 방어 시설의 등급별 그림을 고를 때 쓴다.
        /// </summary>
        public static string Variant(string baseName, string variant)
        {
            if (string.IsNullOrEmpty(variant)) return baseName;
            string name = baseName + "_" + variant;
            return Get(name) != null ? name : baseName;
        }

        /// <summary>그림의 배치 값. 변형 그림에 따로 적힌 값이 없으면 기본 그림의 값을 쓴다.</summary>
        public static LayoutEntry LayoutFor(string name, string baseName)
        {
            return Layout(name) ?? Layout(baseName);
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
