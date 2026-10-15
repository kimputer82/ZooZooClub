using UnityEngine;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 브라우저(WebGL)에서 저장이 어떻게 되는지에 대한 자리.
    /// 브라우저에서는 Application.persistentDataPath 아래에 쓴 파일이 브라우저 저장소(IndexedDB)에
    /// 자동으로 옮겨진다(Unity 6). 그래서 같은 PC의 같은 브라우저에서는 이어 할 수 있고,
    /// 브라우저 데이터를 지우면 함께 지워진다.
    /// </summary>
    public static class WebSave
    {
        public static bool IsWeb
        {
            get { return Application.platform == RuntimePlatform.WebGLPlayer; }
        }
    }
}
