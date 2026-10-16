using System;
using TMPro;
using UnityEngine;
using ZooZooClub.Core;
using UCanvasScaler = UnityEngine.UI.CanvasScaler;
using UGraphicRaycaster = UnityEngine.UI.GraphicRaycaster;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 같은 방 친구의 마을을 구경하는 동안 보이는 화면. 누구의 마을인지와 돌아가기 버튼만 있다.
    /// 구경하는 마을은 보기만 하고 아무것도 바꿀 수 없다.
    /// </summary>
    public sealed class VisitHud : MonoBehaviour
    {
        public event Action BackRequested;

        public void Init(Game game)
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<UCanvasScaler>();
            scaler.uiScaleMode = UCanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<UGraphicRaycaster>();

            Transform root = canvasGo.transform;
            var state = game.State;

            var banner = Ui.Box("Banner", root, Ui.CapsuleSprite, Ui.Pill);
            Ui.Put(banner.rectTransform, 0.5f, 1f, 0f, -24f, 820f, 76f);
            var title = Ui.Label("Title", banner.transform, state.playerName + "의 마을에 놀러 왔어요", 34f, Color.white,
                TextAlignmentOptions.Center, true);
            Ui.Stretch(title.rectTransform, 24f, 0f, 24f, 0f);

            int residents = 0;
            foreach (var resident in game.Catalog.residents)
            {
                if (resident.unlockLevel <= state.level) residents++;
            }
            var info = Ui.Box("Info", root, Ui.CapsuleSprite, Ui.Pill);
            Ui.Put(info.rectTransform, 0.5f, 1f, 0f, -108f, 620f, 52f);
            var infoText = Ui.Label("Text", info.transform,
                "Lv " + state.level + "   ·   매력 " + game.TotalCharm + "   ·   주민 " + residents + "명", 24f, Color.white,
                TextAlignmentOptions.Center);
            Ui.Stretch(infoText.rectTransform, 16f, 0f, 16f, 0f);

            var back = Ui.Button("BackHome", root, "내 마을로 돌아가기", () =>
            {
                Sound.Play(Sound.Click);
                var handler = BackRequested;
                if (handler != null) handler();
            }, 80f, 28f, Ui.Green);
            Ui.Put((RectTransform)back.transform, 1f, 0f, -40f, 40f, 420f, 80f);
        }
    }
}
