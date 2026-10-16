using NUnit.Framework;
using ZooZooClub.Core;

namespace ZooZooClub.Tests
{
    public class RotationTests
    {
        class FakeClock : IClock
        {
            public long Now { get; set; }
        }

        const int Land = 6;

        GameCatalog catalog;
        Game game;
        PlaceableDef bench;
        PlaceableDef sign;
        PlaceableDef fence;

        [SetUp]
        public void SetUp()
        {
            catalog = new GameCatalog();
            bench = new PlaceableDef { id = "bench", kind = PlaceableKind.Decor, width = 2, height = 1, cost = 0, rotations = 4 };
            sign = new PlaceableDef { id = "sign", kind = PlaceableKind.Decor, width = 2, height = 1, cost = 0, rotations = 2 };
            fence = new PlaceableDef { id = "fence", kind = PlaceableKind.Decor, width = 1, height = 1, cost = 0 };
            catalog.placeables.Add(bench);
            catalog.placeables.Add(sign);
            catalog.placeables.Add(fence);
            catalog.levels.Add(new LevelDef { xpToNext = 1000 });
            catalog.balance.startCoins = 1000;
            catalog.balance.startLandSize = Land;
            catalog.balance.maxLandSize = Land;
            catalog.balance.raidUnlockLevel = 99;

            var clock = new FakeClock { Now = 1000 };
            game = new Game(catalog, GameState.CreateNew(catalog, clock.Now), clock);
        }

        PlacedObject Place(string defId, int x, int y, int rotation = 0)
        {
            int id;
            Assert.AreEqual(ActionResult.Ok, game.BuyAndPlace(defId, x, y, out id, rotation));
            return game.FindObject(id);
        }

        void AssertFootprint(PlacedObject obj, int expectedWidth, int expectedHeight)
        {
            int width, height;
            game.GetFootprint(obj, out width, out height);
            Assert.AreEqual(expectedWidth, width);
            Assert.AreEqual(expectedHeight, height);
        }

        [Test]
        public void Next_CyclesThroughTheAllowedDirections()
        {
            Assert.AreEqual(1, Rotation.Next(bench, 0));
            Assert.AreEqual(2, Rotation.Next(bench, 1));
            Assert.AreEqual(3, Rotation.Next(bench, 2));
            Assert.AreEqual(0, Rotation.Next(bench, 3));

            // 방향이 둘뿐이면 앞과 뒤만 오간다.
            Assert.AreEqual(2, Rotation.Next(sign, 0));
            Assert.AreEqual(0, Rotation.Next(sign, 2));

            Assert.AreEqual(0, Rotation.Next(fence, 0));
        }

        [Test]
        public void Normalize_RejectsDirectionsTheObjectCannotFace()
        {
            Assert.AreEqual(1, Rotation.Normalize(bench, 5));
            Assert.AreEqual(3, Rotation.Normalize(bench, -1));
            Assert.AreEqual(0, Rotation.Normalize(sign, 1));
            Assert.AreEqual(2, Rotation.Normalize(sign, 2));
            Assert.AreEqual(0, Rotation.Normalize(fence, 2));
        }

        [Test]
        public void SidewaysDirections_SwapTheFootprint()
        {
            AssertFootprint(Place("bench", 0, 0, 0), 2, 1);
            AssertFootprint(Place("bench", 0, 1, 2), 2, 1);
            AssertFootprint(Place("bench", 3, 0, 1), 1, 2);
            AssertFootprint(Place("bench", 4, 0, 3), 1, 2);
        }

        [Test]
        public void RotatedObject_BlocksTheCellsItReallyCovers()
        {
            // 옆으로 돌린 벤치는 (0,0)과 (0,1)을 차지한다.
            Place("bench", 0, 0, 1);

            Assert.IsTrue(game.CanPlace("fence", 1, 0));
            Assert.IsFalse(game.CanPlace("fence", 0, 0));
            Assert.IsFalse(game.CanPlace("fence", 0, 1));
            Assert.IsTrue(game.CanPlace("fence", 0, 2));
        }

        [Test]
        public void Rotation_DecidesWhatFitsAtTheEdge()
        {
            // 땅의 오른쪽 끝 칸: 가로로 긴 모양은 삐져나가고, 세로로 긴 모양은 들어간다.
            Assert.IsFalse(game.CanPlace("bench", Land - 1, 0, 0, 0));
            Assert.IsTrue(game.CanPlace("bench", Land - 1, 0, 0, 1));
            Assert.IsFalse(game.CanPlace("bench", 0, Land - 1, 0, 1));
            Assert.IsTrue(game.CanPlace("bench", 0, Land - 1, 0, 2));
        }

        [Test]
        public void Rotate_TurnsInPlaceWhenThereIsRoom()
        {
            var obj = Place("bench", 0, 0);
            var blocker = Place("fence", 0, 1);

            // 돌리면 (0,1)이 필요한데 울타리가 있다.
            Assert.AreEqual(ActionResult.InvalidPlacement, game.Rotate(obj.id));
            Assert.AreEqual(0, obj.rotation);

            Assert.AreEqual(ActionResult.Ok, game.Remove(blocker.id));
            Assert.AreEqual(ActionResult.Ok, game.Rotate(obj.id));
            Assert.AreEqual(1, obj.rotation);
            AssertFootprint(obj, 1, 2);
            Assert.AreEqual(0, obj.x);
            Assert.AreEqual(0, obj.y);

            Assert.AreEqual(ActionResult.Ok, game.Rotate(obj.id));
            Assert.AreEqual(2, obj.rotation);
            AssertFootprint(obj, 2, 1);
        }

        [Test]
        public void Rotate_FixedObjectsCannotTurn()
        {
            var obj = Place("fence", 0, 0);

            Assert.AreEqual(ActionResult.NotReady, game.Rotate(obj.id));
            Assert.AreEqual(0, obj.rotation);

            // 돌릴 수 없는 것에 방향을 줘도 앞모습으로 놓인다.
            Assert.AreEqual(0, Place("fence", 1, 0, 3).rotation);
        }

        [Test]
        public void Move_CanChangeDirectionOrKeepIt()
        {
            var obj = Place("bench", 0, 0);

            Assert.AreEqual(ActionResult.Ok, game.Move(obj.id, 3, 3, 3));
            Assert.AreEqual(3, obj.rotation);
            AssertFootprint(obj, 1, 2);

            Assert.AreEqual(ActionResult.Ok, game.Move(obj.id, 2, 2));
            Assert.AreEqual(3, obj.rotation);

            // 옮길 자리에 그 방향으로 들어가지 않으면 옮기지 못하고 그대로 남는다.
            Assert.AreEqual(ActionResult.InvalidPlacement, game.Move(obj.id, Land - 1, 0, 0));
            Assert.AreEqual(2, obj.x);
            Assert.AreEqual(3, obj.rotation);
        }

        [Test]
        public void RaidGoal_AvoidsCellsCoveredByARotatedObject()
        {
            // 옆으로 돌린 벤치가 한가운데 칸과 그 위 칸을 덮는다.
            Place("bench", Land / 2, Land / 2, 1);

            int goalX, goalY;
            game.GetRaidGoal(out goalX, out goalY);

            Assert.IsFalse(goalX == Land / 2 && (goalY == Land / 2 || goalY == Land / 2 + 1));
        }
    
        [Test]
        public void Mirror_TogglesBetweenFrontAndFlipped()
        {
            var fence = new PlaceableDef { id = "fence", width = 1, height = 1, rotations = 2, mirror = true };
            Assert.AreEqual(1, Rotation.Next(fence, 0));
            Assert.AreEqual(0, Rotation.Next(fence, 1));
            Assert.AreEqual(1, Rotation.Normalize(fence, 3));
            Assert.AreEqual(0, Rotation.Normalize(fence, 2));
            Assert.IsTrue(Rotation.Mirrored(1));
            Assert.IsFalse(Rotation.ShowsBack(1));
            Assert.AreEqual(1, Rotation.Width(fence, 1));
        }
}
}
