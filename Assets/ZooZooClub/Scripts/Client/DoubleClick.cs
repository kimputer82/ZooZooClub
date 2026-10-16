using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ZooZooClub.Client
{
    /// <summary>UI 조각을 두 번 연달아 눌렀을 때(더블클릭, 두 번 톡톡) 할 일을 붙인다. 그 조각의 그림이 눌림을 받아야 한다.</summary>
    public sealed class DoubleClick : MonoBehaviour, IPointerClickHandler
    {
        public Action Action;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.clickCount == 2 && Action != null) Action();
        }
    }
}
