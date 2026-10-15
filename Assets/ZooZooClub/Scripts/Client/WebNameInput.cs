using System.Runtime.InteropServices;
using UnityEngine;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 브라우저(WebGL)에서 쓰는 이름 입력 칸. 유니티 입력 칸은 브라우저에서 한글 조합(IME)을 받지 못하므로,
    /// 같은 자리에 진짜 HTML 입력 칸을 얹어 두고(Plugins/WebGL/ZooWebInput.jslib) 그 값을 읽어 온다.
    /// 브라우저가 아닌 곳에서는 아무 일도 하지 않는다.
    /// </summary>
    public static class WebNameInput
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void ZooWebInputShow(float left, float top, float width, float height, string placeholder, int maxLength);
        [DllImport("__Internal")] static extern void ZooWebInputFocus();
        [DllImport("__Internal")] static extern void ZooWebInputHide();
        [DllImport("__Internal")] static extern void ZooWebInputClear();
        [DllImport("__Internal")] static extern string ZooWebInputGetValue();
        [DllImport("__Internal")] static extern int ZooWebInputTakeSubmit();

        public static bool Available { get { return true; } }
#else
        public static bool Available { get { return false; } }
#endif

        /// <summary>rect가 차지하는 화면 자리에 입력 칸을 보인다(없으면 만든다). 매 프레임 불러도 된다.</summary>
        public static void Show(RectTransform rect, string placeholder, int maxLength)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            // 화면 덮기 캔버스라 월드 좌표가 곧 화면 픽셀 좌표다(왼쪽 아래가 원점).
            float minX = corners[0].x, minY = corners[0].y, maxX = corners[2].x, maxY = corners[2].y;
            float w = Mathf.Max(1f, Screen.width), h = Mathf.Max(1f, Screen.height);
            ZooWebInputShow(minX / w, 1f - maxY / h, (maxX - minX) / w, (maxY - minY) / h, placeholder, maxLength);
            // 유니티가 키보드 입력을 독차지하면 HTML 입력 칸에 글자가 들어가지 않는다.
            WebGLInput.captureAllKeyboardInput = false;
#endif
        }

        public static void Focus()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ZooWebInputFocus();
#endif
        }

        public static void Hide()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ZooWebInputHide();
            WebGLInput.captureAllKeyboardInput = true;
#endif
        }

        public static void Clear()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ZooWebInputClear();
#endif
        }

        public static string Value
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return ZooWebInputGetValue() ?? "";
#else
                return "";
#endif
            }
        }

        /// <summary>입력 칸에서 Enter를 눌렀으면 true(한 번만).</summary>
        public static bool TakeSubmit()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ZooWebInputTakeSubmit() != 0;
#else
            return false;
#endif
        }
    }
}
