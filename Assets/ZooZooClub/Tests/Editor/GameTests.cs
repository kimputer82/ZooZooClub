using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZooZooClub.Core;

namespace ZooZooClub.Tests
{
    public class GameTests
    {
        class FakeClock : IClock
        {
            public long Now { get; set; }
        }

        FakeClock clock;
        GameCatalog catalog;
        Game game;

        static GameCatalog BuildCatalog()
        {
            var c = new GameCatalog();
            c.items.Add(new ItemDef { id = "carrot", displayName = "Carrot", value = 4 });
            c.items.Add(new ItemDef { id = "wheat", displayName = "Wheat", value = 6 });
            c.items.Add(new ItemDef { id = "bread", displayName = "Bread", value = 20 });

            c.crops.Add(new CropDef { id = "carrot", harvestItemId = "carrot", harvestAmount = 2, growSeconds = 20, seedCost = 0, harvestXp = 1, unlockLevel = 1 });
            c.crops.Add(new CropDef { id = "wheat", harvestItemId = "wheat", harvestAmount = 2, growSeconds = 45, seedCost = 1, harvestXp = 2, unlockLevel = 1 });
            c.crops.Add(new CropDef { id = "locked", harvestItemId = "carrot", harvestAmount = 2, growSeconds = 10, seedCost = 0, harvestXp = 1, unlockLevel = 9 });

            c.recipes.Add(new RecipeDef
            {
                id = "bread",
                facilityId = "bakery",
                outputItemId = "bread",
                outputAmount = 1,
                inputs = new List<ItemAmount> { new ItemAmount("wheat", 2) },
                craftSeconds = 60,
                xp = 4,
                unlockLevel = 1
            });

            c.placeables.Add(new PlaceableDef { id = "plot", kind = PlaceableKind.Plot, width = 1, height = 1, cost = 20 });
            c.placeables.Add(new PlaceableDef { id = "bakery", kind = PlaceableKind.Facility, width = 2, height = 2, cost = 150, queueSlots = 2, maxCount = 1 });
            c.placeables.Add(new PlaceableDef { id = "tree", kind = PlaceableKind.Decor, width = 1, height = 1, cost = 40, charm = 4 });

            c.customers.Add(new CustomerDef { id = "dog", displayName = "Dog", species = "dog" });

            c.levels.Add(new LevelDef { xpToNext = 3, rewardCoins = 50, rewardGems = 1 });
            c.levels.Add(new LevelDef { xpToNext = 1000, rewardCoins = 0, rewardGems = 0 });

            c.balance.startCoins = 1000;
            c.balance.startStorage = 10;
            c.balance.startLandSize = 6;
            c.balance.maxLandSize = 8;
            c.balance.landExpandStep = 2;
            c.balance.landExpandBaseCost = 300;
            c.balance.landExpandFirstLevel = 2;
            c.balance.landExpandLevelStep = 2;
            c.balance.orderSlots = 3;
            c.balance.starterObjects.Add(new StarterObject { defId = "plot", x = 0, y = 0 });
            return c;
        }

        [SetUp]
        public void SetUp()
        {
            clock = new FakeClock { Now = 1000 };
            catalog = BuildCatalog();
            game = new Game(catalog, GameState.CreateNew(catalog, clock.Now), clock);
        }

        int StarterPlotId
        {
            get { return game.State.objects[0].id; }
        }

        int PlaceBakery()
        {
            int id;
            Assert.AreEqual(ActionResult.Ok, game.BuyAndPlace("bakery", 2, 2, out id));
            return id;
        }

        void GrowAndHarvest(string cropId)
        {
            Assert.AreEqual(ActionResult.Ok, game.Plant(StarterPlotId, cropId));
            clock.Now += catalog.GetCrop(cropId).growSeconds;
            Assert.AreEqual(ActionResult.Ok, game.Harvest(StarterPlotId));
        }

        [Test]
        public void Catalog_IsValid()
        {
            Assert.IsEmpty(catalog.Validate());
        }

        [Test]
        public void ShippedCatalog_LoadsAndIsValid()
        {
            var asset = Resources.Load<TextAsset>("catalog");
            Assert.IsNotNull(asset, "Resources/catalog.json is missing");

            var shipped = JsonUtility.FromJson<GameCatalog>(asset.text);
            Assert.IsEmpty(shipped.Validate());
            Assert.Greater(shipped.crops.Count, 0);
            Assert.Greater(shipped.customers.Count, 0);
        }

        [Test]
        public void Plant_ThenHarvestAfterGrowTime_AddsItemsAndFreesPlot()
        {
            Assert.AreEqual(ActionResult.Ok, game.Plant(StarterPlotId, "carrot"));
            Assert.AreEqual(ActionResult.Busy, game.Plant(StarterPlotId, "carrot"));
            Assert.AreEqual(ActionResult.NotReady, game.Harvest(StarterPlotId));

            clock.Now += 10;
            Assert.AreEqual(0.5f, game.GrowthProgress(StarterPlotId), 0.001f);

            clock.Now += 10;
            Assert.IsTrue(game.IsRipe(StarterPlotId));
            Assert.AreEqual(ActionResult.Ok, game.Harvest(StarterPlotId));
            Assert.AreEqual(2, game.CountOf("carrot"));
            Assert.AreEqual(ActionResult.Ok, game.Plant(StarterPlotId, "carrot"));
        }

        [Test]
        public void Plant_ChargesSeedCost_AndRespectsLevelLock()
        {
            long before = game.State.coins;
            Assert.AreEqual(ActionResult.Ok, game.Plant(StarterPlotId, "wheat"));
            Assert.AreEqual(before - 1, game.State.coins);

            clock.Now += 45;
            game.Harvest(StarterPlotId);
            Assert.AreEqual(ActionResult.LockedByLevel, game.Plant(StarterPlotId, "locked"));
        }

        [Test]
        public void Harvest_FailsWhenStorageIsFull_AndCropStaysOnPlot()
        {
            game.State.storageCapacity = 1;
            game.Plant(StarterPlotId, "carrot");
            clock.Now += 20;

            Assert.AreEqual(ActionResult.StorageFull, game.Harvest(StarterPlotId));
            Assert.IsTrue(game.IsRipe(StarterPlotId));

            game.State.storageCapacity = 10;
            Assert.AreEqual(ActionResult.Ok, game.Harvest(StarterPlotId));
        }

        [Test]
        public void Crops_KeepGrowingWhileTheGameIsClosed()
        {
            game.Plant(StarterPlotId, "wheat");

            // 저장 후 한참 뒤에 다시 연 상황.
            string json = JsonUtility.ToJson(game.State);
            clock.Now += 3600;
            var reloaded = new Game(catalog, JsonUtility.FromJson<GameState>(json), clock);

            Assert.IsTrue(reloaded.IsRipe(StarterPlotId));
        }

        [Test]
        public void Craft_ConsumesInputs_RunsJobsInSequence_AndCollectsWhenDone()
        {
            GrowAndHarvest("wheat");
            GrowAndHarvest("wheat");
            int bakery = PlaceBakery();

            Assert.AreEqual(ActionResult.Ok, game.StartCraft(bakery, "bread"));
            Assert.AreEqual(ActionResult.Ok, game.StartCraft(bakery, "bread"));
            Assert.AreEqual(0, game.CountOf("wheat"));
            Assert.AreEqual(ActionResult.NotReady, game.CollectCrafted(bakery));

            clock.Now += 60;
            Assert.AreEqual(1, game.ReadyJobCount(bakery));
            Assert.AreEqual(ActionResult.Ok, game.CollectCrafted(bakery));
            Assert.AreEqual(1, game.CountOf("bread"));

            clock.Now += 60;
            Assert.AreEqual(ActionResult.Ok, game.CollectCrafted(bakery));
            Assert.AreEqual(2, game.CountOf("bread"));
        }

        [Test]
        public void Craft_RejectsMissingInputs_AndFullQueue()
        {
            int bakery = PlaceBakery();
            Assert.AreEqual(ActionResult.NotEnoughItems, game.StartCraft(bakery, "bread"));

            game.State.inventory.Add(new ItemAmount("wheat", 6));
            Assert.AreEqual(ActionResult.Ok, game.StartCraft(bakery, "bread"));
            Assert.AreEqual(ActionResult.Ok, game.StartCraft(bakery, "bread"));
            Assert.AreEqual(ActionResult.QueueFull, game.StartCraft(bakery, "bread"));
            Assert.AreEqual(2, game.CountOf("wheat"));
        }

        [Test]
        public void Orders_AreFilledAtStart_WithObtainableItemsOnly()
        {
            Assert.AreEqual(3, game.State.orders.Count);
            foreach (var order in game.State.orders)
            {
                Assert.Greater(order.items.Count, 0);
                Assert.Greater(order.rewardCoins, 0);
                foreach (var item in order.items)
                {
                    Assert.IsNotNull(catalog.GetItem(item.itemId));
                    Assert.Greater(item.amount, 0);
                }
            }
        }

        [Test]
        public void DeliverOrder_TakesItems_PaysReward_AndReplacesTheOrderAfterCooldown()
        {
            var order = game.State.orders[0];
            Assert.AreEqual(ActionResult.NotEnoughItems, game.DeliverOrder(order.id));

            foreach (var item in order.items)
            {
                game.State.inventory.Add(new ItemAmount(item.itemId, item.amount));
            }

            long coinsBefore = game.State.coins;
            int reward = order.rewardCoins;
            Assert.AreEqual(ActionResult.Ok, game.DeliverOrder(order.id));

            // 레벨 업 보상이 함께 들어올 수 있으므로 최소 보상만큼 늘었는지 확인한다.
            Assert.GreaterOrEqual(game.State.coins, coinsBefore + reward);
            Assert.AreEqual(0, game.StoredTotal);
            Assert.AreEqual(3, game.State.orders.Count);
            Assert.IsNull(game.FindOrder(order.id));

            var replacement = game.State.orders[0];
            Assert.IsFalse(game.IsOrderAvailable(replacement));
            clock.Now += catalog.balance.orderCooldownSeconds;
            Assert.IsTrue(game.IsOrderAvailable(replacement));
        }

        [Test]
        public void DiscardOrder_ReplacesItWithALongerCooldown()
        {
            var order = game.State.orders[1];
            Assert.AreEqual(ActionResult.Ok, game.DiscardOrder(order.id));

            var replacement = game.State.orders[1];
            Assert.AreNotEqual(order.id, replacement.id);
            Assert.AreEqual(clock.Now + catalog.balance.orderDiscardCooldownSeconds, replacement.availableAt);
        }

        [Test]
        public void Xp_LevelsUp_AndGrantsLevelReward()
        {
            int leveledTo = 0;
            game.LeveledUp += level => leveledTo = level;
            long coinsBefore = game.State.coins;

            GrowAndHarvest("carrot");
            GrowAndHarvest("carrot");
            Assert.AreEqual(1, game.State.level);

            GrowAndHarvest("carrot");
            Assert.AreEqual(2, game.State.level);
            Assert.AreEqual(2, leveledTo);
            Assert.AreEqual(0, game.State.xp);
            Assert.AreEqual(coinsBefore + 50, game.State.coins);
        }

        [Test]
        public void Placement_RejectsOverlapAndOutOfBounds()
        {
            Assert.IsFalse(game.CanPlace("tree", 0, 0), "starter plot is at 0,0");
            Assert.IsFalse(game.CanPlace("tree", -1, 0));
            Assert.IsFalse(game.CanPlace("bakery", 5, 5), "2x2 does not fit at the edge of a 6x6 land");
            Assert.IsTrue(game.CanPlace("bakery", 4, 4));

            int id;
            Assert.AreEqual(ActionResult.InvalidPlacement, game.BuyAndPlace("tree", 0, 0, out id));
            Assert.AreEqual(ActionResult.Ok, game.BuyAndPlace("tree", 1, 0, out id));
            Assert.AreEqual(4, game.TotalCharm);
        }

        [Test]
        public void BuyAndPlace_ChargesCoins_AndEnforcesMaxCount()
        {
            long before = game.State.coins;
            PlaceBakery();
            Assert.AreEqual(before - 150, game.State.coins);

            int id;
            Assert.AreEqual(ActionResult.MaxReached, game.BuyAndPlace("bakery", 4, 4, out id));
        }

        [Test]
        public void Move_AllowsOverlapWithItself_ButNotWithOthers()
        {
            int bakery = PlaceBakery();
            Assert.AreEqual(ActionResult.Ok, game.Move(bakery, 3, 3));
            Assert.AreEqual(ActionResult.InvalidPlacement, game.Move(bakery, 0, 0));

            var obj = game.FindObject(bakery);
            Assert.AreEqual(3, obj.x);
            Assert.AreEqual(3, obj.y);
        }

        [Test]
        public void Remove_RefundsPartOfTheCost_AndRefusesBusyObjects()
        {
            game.Plant(StarterPlotId, "carrot");
            Assert.AreEqual(ActionResult.Busy, game.Remove(StarterPlotId));

            int id;
            game.BuyAndPlace("tree", 3, 3, out id);
            long before = game.State.coins;
            Assert.AreEqual(ActionResult.Ok, game.Remove(id));
            Assert.AreEqual(before + 20, game.State.coins);
            Assert.IsNull(game.FindObject(id));
        }

        [Test]
        public void ExpandLand_NeedsLevel_ChargesCoins_AndStopsAtMaxSize()
        {
            Assert.AreEqual(ActionResult.LockedByLevel, game.ExpandLand());

            game.State.level = 2;
            long before = game.State.coins;
            Assert.AreEqual(ActionResult.Ok, game.ExpandLand());
            Assert.AreEqual(8, game.State.landWidth);
            Assert.AreEqual(8, game.State.landHeight);
            Assert.AreEqual(before - 300, game.State.coins);
            Assert.AreEqual(ActionResult.MaxReached, game.ExpandLand());
        }

        [Test]
        public void Storage_SellAndUpgrade()
        {
            game.State.inventory.Add(new ItemAmount("bread", 2));
            long before = game.State.coins;
            Assert.AreEqual(ActionResult.Ok, game.SellItem("bread", 2));
            Assert.AreEqual(before + 40, game.State.coins);
            Assert.AreEqual(ActionResult.NotEnoughItems, game.SellItem("bread", 1));

            int cost = game.StorageUpgradeCost;
            Assert.AreEqual(ActionResult.Ok, game.UpgradeStorage());
            Assert.AreEqual(10 + catalog.balance.storageUpgradeStep, game.State.storageCapacity);
            Assert.AreEqual(cost * 2, game.StorageUpgradeCost);
        }

        [Test]
        public void State_SurvivesJsonRoundTrip()
        {
            GrowAndHarvest("wheat");
            int bakery = PlaceBakery();
            game.StartCraft(bakery, "bread");

            string json = JsonUtility.ToJson(game.State);
            var restored = JsonUtility.FromJson<GameState>(json);
            var reloaded = new Game(catalog, restored, clock);

            Assert.AreEqual(game.State.coins, reloaded.State.coins);
            Assert.AreEqual(game.State.objects.Count, reloaded.State.objects.Count);
            Assert.AreEqual(1, reloaded.FindObject(bakery).queue.Count);
            Assert.AreEqual(game.State.orders.Count, reloaded.State.orders.Count);

            clock.Now += 60;
            Assert.AreEqual(ActionResult.Ok, reloaded.CollectCrafted(bakery));
            Assert.AreEqual(1, reloaded.CountOf("bread"));
        }
    }
}
