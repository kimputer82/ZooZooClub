using System.Collections.Generic;
using NUnit.Framework;
using ZooZooClub.Core;

namespace ZooZooClub.Tests
{
    public class GridPathTests
    {
        // '#'은 막힌 칸. 첫 줄이 y = 0.
        static bool[] Parse(out int width, out int height, params string[] rows)
        {
            height = rows.Length;
            width = rows[0].Length;
            var blocked = new bool[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++) blocked[x + y * width] = rows[y][x] == '#';
            }
            return blocked;
        }

        [Test]
        public void Find_GoesAroundBlockedCells()
        {
            int w, h;
            var blocked = Parse(out w, out h,
                ".#.",
                ".#.",
                "...");
            var path = new List<int>();

            Assert.IsTrue(GridPath.Find(blocked, w, h, 0, 0, 2, 0, path));

            // 벽을 돌아가는 가장 짧은 길은 여섯 걸음이다.
            Assert.AreEqual(6, path.Count);
            Assert.AreEqual(2 + 0 * w, path[path.Count - 1]);

            int x = 0, y = 0;
            foreach (int index in path)
            {
                int nx = index % w, ny = index / w;
                Assert.IsFalse(blocked[index], "막힌 칸을 지나갔다");
                Assert.AreEqual(1, System.Math.Abs(nx - x) + System.Math.Abs(ny - y), "이웃 칸이 아닌 곳으로 건너뛰었다");
                x = nx;
                y = ny;
            }
        }

        [Test]
        public void Find_FailsWhenWalledOff()
        {
            int w, h;
            var blocked = Parse(out w, out h,
                ".#.",
                ".#.",
                ".#.");
            var path = new List<int>();

            Assert.IsFalse(GridPath.Find(blocked, w, h, 0, 0, 2, 0, path));
            Assert.AreEqual(0, path.Count);
        }

        [Test]
        public void Find_FailsWhenGoalIsBlocked()
        {
            int w, h;
            var blocked = Parse(out w, out h, "..#");
            var path = new List<int>();

            Assert.IsFalse(GridPath.Find(blocked, w, h, 0, 0, 2, 0, path));
        }

        [Test]
        public void Find_SameCellIsEmptyPath()
        {
            int w, h;
            var blocked = Parse(out w, out h, "...");
            var path = new List<int>();

            Assert.IsTrue(GridPath.Find(blocked, w, h, 1, 0, 1, 0, path));
            Assert.AreEqual(0, path.Count);
        }

        [Test]
        public void Reachable_ListsOnlyConnectedFreeCells()
        {
            int w, h;
            var blocked = Parse(out w, out h,
                "..#.",
                "..#.",
                "###.");
            var cells = new List<int>();

            GridPath.Reachable(blocked, w, h, 0, 0, 99, cells);

            CollectionAssert.AreEquivalent(new[] { 1, 4, 5 }, cells);
        }

        [Test]
        public void Reachable_RespectsStepLimit()
        {
            int w, h;
            var blocked = Parse(out w, out h, ".....");
            var cells = new List<int>();

            GridPath.Reachable(blocked, w, h, 0, 0, 2, cells);

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, cells);
        }

        [Test]
        public void Reachable_WorksFromABlockedStart()
        {
            int w, h;
            var blocked = Parse(out w, out h, "#..");
            var cells = new List<int>();

            GridPath.Reachable(blocked, w, h, 0, 0, 99, cells);

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, cells);
        }

        [Test]
        public void NearestFree_PicksClosestFreeCell()
        {
            int w, h;
            var blocked = Parse(out w, out h,
                "###.",
                "###.",
                ".##.");

            Assert.AreEqual(0 + 2 * w, GridPath.NearestFree(blocked, w, h, 0, 1));
            Assert.AreEqual(3 + 0 * w, GridPath.NearestFree(blocked, w, h, 2, 0));
        }

        [Test]
        public void NearestFree_ReturnsMinusOneWhenEverythingIsBlocked()
        {
            int w, h;
            var blocked = Parse(out w, out h, "##", "##");

            Assert.AreEqual(-1, GridPath.NearestFree(blocked, w, h, 0, 0));
        }
    }
}
