using UnityEngine;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 마을 둘레의 배경 그림(숲 속 풀밭). 그림 가운데의 마름모꼴 풀밭이 마을 땅과 겹치도록 놓는다.
    /// 땅을 넓히면 풀밭이 넓어진 땅에 다시 맞도록 그림도 함께 커진다.
    /// 카메라는 그림 밖(그림이 끝나는 곳)이 보이지 않도록 그림 안에서만 움직이고 멀어진다.
    /// </summary>
    public sealed partial class WorldView
    {
        const string BackdropArt = "background";
        const int BackdropOrder = GroundOrder - 10;

        // 배경 그림에서 잰 풀밭 마름모(픽셀, 그림 왼쪽 위 기준). 그림을 바꾸면 이 값을 다시 잰다:
        // 풀밭 안에 들어가는 가장 큰 2:1 마름모의 한가운데와 가로 반지름.
        const float ClearingCenterX = 892f;
        const float ClearingCenterY = 452f;
        const float ClearingHalfWidth = 603f;

        // 땅이 풀밭 가장자리의 나무·덤불에 닿지 않도록, 풀밭보다 조금 작게 맞춘다.
        const float ClearingFill = 0.985f;

        // 그림 맨 위의 하늘색. 그림을 읽기 전 잠깐 비치는 바탕을 이 색으로 둔다.
        static readonly Color BackdropSky = new Color32(171, 218, 246, 255);

        SpriteRenderer backdrop;
        Rect backdropRect;
        bool hasBackdrop;

        /// <summary>배경 그림을 지금 땅 크기에 맞춘다. 그림이 없으면 아무것도 하지 않는다(하늘색 바탕 그대로).</summary>
        void FitBackdrop()
        {
            if (backdrop == null)
            {
                Sprite art = ArtLibrary.Get(BackdropArt);
                if (art == null) return;
                backdrop = NewSprite("Backdrop", groundRoot, art);
                backdrop.sortingOrder = BackdropOrder;
                if (cam != null) cam.backgroundColor = BackdropSky;
            }

            Sprite sprite = backdrop.sprite;
            float pixelWidth = sprite.rect.width;
            float pixelHeight = sprite.rect.height;
            int width = game.State.landWidth;
            int height = game.State.landHeight;

            // 땅 마름모의 가로 반지름은 (가로 칸 + 세로 칸) / 4. 이것이 그림 속 풀밭의 가로 반지름과 같아지는 축척.
            float unitsPerPixel = (width + height) * 0.25f / (ClearingHalfWidth * ClearingFill);
            float worldWidth = pixelWidth * unitsPerPixel;
            float worldHeight = pixelHeight * unitsPerPixel;
            float scale = worldWidth / Mathf.Max(0.0001f, sprite.bounds.size.x);
            backdrop.transform.localScale = new Vector3(scale, scale, 1f);

            // 풀밭 한가운데가 땅 한가운데에 오도록 그림을 놓는다(그림의 y는 아래로, 월드의 y는 위로 자란다).
            Vector3 landCenter = IsoMath.CellToWorld(width * 0.5f, height * 0.5f);
            Vector3 imageCenter = landCenter + new Vector3(
                (pixelWidth * 0.5f - ClearingCenterX) * unitsPerPixel,
                (ClearingCenterY - pixelHeight * 0.5f) * unitsPerPixel, 0f);
            backdrop.transform.localPosition = imageCenter - sprite.bounds.center * scale;

            backdropRect = new Rect(imageCenter.x - worldWidth * 0.5f, imageCenter.y - worldHeight * 0.5f,
                worldWidth, worldHeight);
            hasBackdrop = true;
        }

        /// <summary>카메라가 배경 그림 바깥을 비추지 않게 한다: 그림보다 넓게 멀어지지 않고, 그림 밖으로 나가지 않는다.</summary>
        void ClampCameraToBackdrop()
        {
            if (!hasBackdrop || cam == null) return;

            const float edge = 0.02f;   // 그림 맨 가장자리 줄이 비치지 않게 조금 안쪽까지만
            float usableWidth = backdropRect.width - edge * 2f;
            float usableHeight = backdropRect.height - edge * 2f;
            float aspect = Mathf.Max(0.1f, cam.aspect);

            float maxSize = Mathf.Min(usableHeight * 0.5f, usableWidth * 0.5f / aspect);
            if (cam.orthographicSize > maxSize) cam.orthographicSize = Mathf.Max(0.5f, maxSize);

            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * aspect;
            Vector3 position = cam.transform.position;
            position.x = ClampOrCenter(position.x, backdropRect.xMin + edge + halfWidth, backdropRect.xMax - edge - halfWidth);
            position.y = ClampOrCenter(position.y, backdropRect.yMin + edge + halfHeight, backdropRect.yMax - edge - halfHeight);
            cam.transform.position = position;
        }

        static float ClampOrCenter(float value, float min, float max)
        {
            return min > max ? (min + max) * 0.5f : Mathf.Clamp(value, min, max);
        }

        void LateUpdate()
        {
            ClampCameraToBackdrop();
        }
    }
}
