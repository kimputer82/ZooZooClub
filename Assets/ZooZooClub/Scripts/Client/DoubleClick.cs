using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ZooZooClub.Client
{
    /// <summary>UI 조각을 두 번 연달아 눌렀을 때(더블클릭, 두 번 톡톡) 할 일을 붙인다. 그 조각의 그림이 눌림을 받아야 한다.</summary>
    public sealed class DoubleClick : MonoBehaviour, IPointerClickHandler
    {
        public Action Action;

        /// <summary>두 번째 누름이 이 시간(초) 안에 오면 두 번 누른 것으로 본다.</summary>
        const float Window = 0.45f;
        float lastClick = -10f;

        public void OnPointerClick(PointerEventData eventData)
        {
            // 터치에서는 clickCount가 늘지 않는 브라우저가 있어서, 누른 시각으로도 직접 센다.
            float now = Time.unscaledTime;
            bool twice = eventData.clickCount == 2 || now - lastClick <= Window;
            lastClick = twice ? -10f : now;
            if (twice && Action != null) Action();
        }
    }
}
