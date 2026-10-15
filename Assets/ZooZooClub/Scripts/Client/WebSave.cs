using System.Runtime.InteropServices;
using UnityEngine;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 브라우저(WebGL)에서의 저장. 브라우저에서는 Application.persistentDataPath 아래에 쓴 파일이
    /// 브라우저 저장소(IndexedDB)로 옮겨져야 남는다. 페이지 설정(autoSyncPersistentDataPath)으로 자동으로도 옮겨지지만,
    /// 탭을 닫기 직전의 저장까지 확실히 남도록 저장할 때마다 Flush로 바로 옮긴다.
    /// 같은 PC의 같은 브라우저에서는 이어 할 수 있고, 브라우저 데이터를 지우면 함께 지워진다.
    /// </summary>
    public static class WebSave
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void ZooWebSyncSaves();
#endif

        public static bool IsWeb
        {
            get { return Application.platform == RuntimePlatform.WebGLPlayer; }
        }

        public static void Flush()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ZooWebSyncSaves();
#endif
        }
    }
}
