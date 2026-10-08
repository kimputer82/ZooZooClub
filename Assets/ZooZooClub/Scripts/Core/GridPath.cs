using System.Collections.Generic;

namespace ZooZooClub.Core
{
    /// <summary>
    /// 격자 위에서 막힌 칸을 피해 가는 길 찾기. 상하좌우 네 방향으로만 움직인다.
    /// 칸은 x + y * width 인 번호 하나로 나타내고, blocked[번호]가 true면 지나갈 수 없다.
    /// </summary>
    public static class GridPath
    {
        static readonly int[] StepX = { 1, -1, 0, 0 };
        static readonly int[] StepY = { 0, 0, 1, -1 };

        /// <summary>
        /// 출발 칸에서 maxSteps 걸음 안에 걸어서 닿을 수 있는 빈칸을 cells에 모은다(출발 칸은 빼고).
        /// 출발 칸 자체가 막혀 있어도 거기서부터 찾는다.
        /// </summary>
        public static void Reachable(bool[] blocked, int width, int height, int startX, int startY, int maxSteps, List<int> cells)
        {
            cells.Clear();
            if (!Inside(width, height, startX, startY)) return;

            var steps = Flood(blocked, width, height, startX, startY, maxSteps, -1, null);
            int start = startX + startY * width;
            for (int i = 0; i < steps.Length; i++)
            {
                if (i != start && steps[i] >= 0) cells.Add(i);
            }
        }

        /// <summary>
        /// 출발 칸에서 도착 칸까지 가장 짧은 길을 path에 담는다(출발 칸은 빼고 도착 칸은 넣는다).
        /// 길이 없으면 false.
        /// </summary>
        public static bool Find(bool[] blocked, int width, int height, int startX, int startY, int goalX, int goalY, List<int> path)
        {
            path.Clear();
            if (!Inside(width, height, startX, startY) || !Inside(width, height, goalX, goalY)) return false;

            int start = startX + startY * width;
            int goal = goalX + goalY * width;
            if (start == goal) return true;
            if (blocked[goal]) return false;

            var parent = new int[width * height];
            var steps = Flood(blocked, width, height, startX, startY, int.MaxValue, goal, parent);
            if (steps[goal] < 0) return false;

            for (int at = goal; at != start; at = parent[at]) path.Add(at);
            path.Reverse();
            return true;
        }

        /// <summary>
        /// 출발 칸에서 가장 가까운 빈칸의 번호. 막힌 칸도 건너뛰며 찾으므로, 물체 밑에 깔린 주민이
        /// 비켜설 자리를 찾을 때 쓴다. 빈칸이 하나도 없으면 -1.
        /// </summary>
        public static int NearestFree(bool[] blocked, int width, int height, int startX, int startY)
        {
            if (!Inside(width, height, startX, startY)) return -1;

            int best = -1;
            int bestDistance = int.MaxValue;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = x + y * width;
                    if (blocked[index]) continue;
                    int dx = x - startX;
                    int dy = y - startY;
                    int distance = dx * dx + dy * dy;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = index;
                    }
                }
            }
            return best;
        }

        static bool Inside(int width, int height, int x, int y)
        {
            return x >= 0 && y >= 0 && x < width && y < height;
        }

        /// <summary>출발 칸에서 퍼져 나가며 칸마다 걸음 수를 적는다. 닿지 못한 칸은 -1.</summary>
        static int[] Flood(bool[] blocked, int width, int height, int startX, int startY, int maxSteps, int stopAt, int[] parent)
        {
            int count = width * height;
            var steps = new int[count];
            for (int i = 0; i < count; i++) steps[i] = -1;

            var queue = new Queue<int>();
            int start = startX + startY * width;
            steps[start] = 0;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                if (at == stopAt) break;
                if (steps[at] >= maxSteps) continue;

                int ax = at % width;
                int ay = at / width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = ax + StepX[d];
                    int ny = ay + StepY[d];
                    if (!Inside(width, height, nx, ny)) continue;

                    int next = nx + ny * width;
                    if (blocked[next] || steps[next] >= 0) continue;

                    steps[next] = steps[at] + 1;
                    if (parent != null) parent[next] = at;
                    queue.Enqueue(next);
                }
            }
            return steps;
        }
    }
}
