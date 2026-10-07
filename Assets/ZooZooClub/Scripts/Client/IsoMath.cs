using UnityEngine;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 격자 칸과 화면 좌표 사이의 변환. 한 칸은 가로 1, 세로 0.5 크기의 마름모이고
    /// 칸 (x, y)의 아래쪽 꼭짓점이 CellToWorld(x, y)이다. x는 오른쪽 위로, y는 왼쪽 위로 뻗는다.
    /// </summary>
    public static class IsoMath
    {
        public const float HalfWidth = 0.5f;
        public const float HalfHeight = 0.25f;

        public static Vector3 CellToWorld(float x, float y)
        {
            return new Vector3((x - y) * HalfWidth, (x + y) * HalfHeight, 0f);
        }

        public static Vector3 CellCenter(int x, int y)
        {
            return CellToWorld(x + 0.5f, y + 0.5f);
        }

        public static Vector2Int WorldToCell(Vector3 world)
        {
            float a = world.x / HalfWidth;
            float b = world.y / HalfHeight;
            return new Vector2Int(Mathf.FloorToInt((a + b) * 0.5f), Mathf.FloorToInt((b - a) * 0.5f));
        }
    }
}
