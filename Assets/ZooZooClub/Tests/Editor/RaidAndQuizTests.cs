using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZooZooClub.Core;

namespace ZooZooClub.Tests
{
    public class RaidAndQuizTests
    {
        class FakeClock : IClock
        {
            public long Now { get; set; }
        }

        const int Land = 6;

        FakeClock clock;
        GameCatalog catalog;
        Game game;

        static GameCatalog BuildCatalog()
        {
            var c = new GameCatalog();

            c.placeables.Add(new PlaceableDef { id = "fence", kind = PlaceableKind.Decor, width = 1, height = 1, cost = 0 });
            // 침략자의 길을 막는 것(장식과 바닥은 넘어 다니므로 막지 못한다).
            c.placeables.Add(new PlaceableDef { id = "wall", kind = PlaceableKind.Facility, width = 1, height = 1, cost = 0, queueSlots = 1 });
            c.placeables.Add(new PlaceableDef
            {
                id = "tower", kind = PlaceableKind.Defense, width = 1, height = 1, cost = 0,
                attackRange = 20f, attackDamage = 100, attackInterval = 0.1f
            });
            c.placeables.Add(new PlaceableDef
            {
                id = "weak", kind = PlaceableKind.Defense, width = 1, height = 1, cost = 0,
                attackRange = 1.2f, attackDamage = 1, attackInterval = 5f
            });

            // 수확 문제 점검용 밭과 작물.
            c.items.Add(new ItemDef { id = "carrot", displayName = "Carrot", value = 3 });
            c.crops.Add(new CropDef { id = "carrot", harvestItemId = "carrot", harvestAmount = 1, growSeconds = 10, unlockLevel = 1 });
            c.placeables.Add(new PlaceableDef { id = "plot", kind = PlaceableKind.Plot, width = 1, height = 1, cost = 0 });

            c.invaders.Add(new InvaderDef { id = "thief", hp = 10, speed = 2f, stealCoins = 30, bounty = 5, xp = 0, firstLevel = 1 });

            c.levels.Add(new LevelDef { xpToNext = 1000 });

            c.balance.startCoins = 1000;
            c.balance.startLandSize = Land;
            c.balance.maxLandSize = Land + 2;
            c.balance.landExpandStep = 2;
            c.balance.landExpandBaseCost = 10;
            c.balance.landExpandFirstLevel = 1;
            c.balance.raidUnlockLevel = 1;
            c.balance.raidIntervalSeconds = 100;
            c.balance.raidWarningSeconds = 10;
            c.balance.raidBaseCount = 3;
            c.balance.raidLevelsPerExtra = 100;
            c.balance.raidMaxStealPercent = 100;
            c.balance.raidMaxCount = 10;
            c.balance.raidHpGrowthPercent = 0;
            c.balance.raidSpawnGapSeconds = 0.5f;
            c.balance.raidStealSeconds = 0f;

            var bank = new QuizBank { rewardCoins = 30, wrongCooldownSeconds = 5 };
            bank.questions.Add(new QuizDef { question = "1 + 1", choices = new List<string> { "1", "2", "3" }, answer = 2 });
            bank.questions.Add(new QuizDef { question = "2 + 2", choices = new List<string> { "4", "5" }, answer = 1, reward = 70 });
            bank.questions.Add(new QuizDef { question = "3 + 3", choices = new List<string> { "5", "6", "7", "8" }, answer = 2 });
            c.SetQuiz(bank);
            return c;
        }

        [SetUp]
        public void SetUp()
        {
            clock = new FakeClock { Now = 1000 };
            catalog = BuildCatalog();
            game = new Game(catalog, GameState.CreateNew(catalog, clock.Now), clock);
        }

        int Place(string defId, int x, int y)
        {
            int id;
            Assert.AreEqual(ActionResult.Ok, game.BuyAndPlace(defId, x, y, out id));
            return id;
        }

        /// <summary>침략을 지금 시작해서 끝날 때까지 돌리고 결과를 돌려준다. step마다 한 번씩 check를 부른다.</summary>
        RaidReport RunRaid(Action check = null)
        {
            RaidReport report = null;
            Action<RaidReport> onEnded = r => report = r;
            game.RaidEnded += onEnded;

            game.StartRaidNow();
            Assert.IsTrue(game.RaidActive);

            for (int step = 0; step < 5000 && game.RaidActive; step++)
            {
                game.TickRaid(0.05f);
                if (check != null) check();
            }

            game.RaidEnded -= onEnded;
            Assert.IsFalse(game.RaidActive, "침략이 끝나지 않았다");
            Assert.IsNotNull(report);
            return report;
        }

        // ---------- 침략 ----------

        [Test]
        public void Raid_IsLockedBelowUnlockLevel()
        {
            catalog.balance.raidUnlockLevel = 5;

            game.TickRaid(0.1f);

            Assert.IsFalse(game.RaidsUnlocked);
            Assert.AreEqual(-1, game.SecondsUntilRaid);
            Assert.AreEqual(0, game.State.nextRaidAt);
        }

        [Test]
        public void Raid_StartsWhenTheTimerRunsOut()
        {
            int startedWave = 0;
            game.RaidStarted += wave => startedWave = wave;

            game.TickRaid(0.1f);
            Assert.AreEqual(100, game.SecondsUntilRaid);
            Assert.IsFalse(game.RaidImminent);

            clock.Now += 95;
            game.TickRaid(0.1f);
            Assert.IsFalse(game.RaidActive);
            Assert.IsTrue(game.RaidImminent);

            clock.Now += 5;
            game.TickRaid(0.1f);
            Assert.IsTrue(game.RaidActive);
            Assert.AreEqual(1, startedWave);
            Assert.AreEqual(-1, game.SecondsUntilRaid);
            Assert.AreEqual(3, game.Invaders.Count);
        }

        [Test]
        public void Raid_WithoutDefense_InvadersStealCoins()
        {
            var report = RunRaid();

            Assert.AreEqual(3, report.total);
            Assert.AreEqual(3, report.escaped);
            Assert.AreEqual(0, report.defeated);
            Assert.AreEqual(90, report.coinsStolen);
            Assert.AreEqual(910, game.State.coins);
            Assert.AreEqual(1, game.State.raidWave);
            Assert.AreEqual(clock.Now + 100, game.State.nextRaidAt);
            Assert.AreEqual(0, game.Invaders.Count);
        }

        [Test]
        public void Raid_WithDefense_InvadersAreDefeated()
        {
            int towerId = Place("tower", 0, 0);
            int shots = 0;
            game.DefenseFired += (objectId, invader) =>
            {
                Assert.AreEqual(towerId, objectId);
                shots++;
            };

            var report = RunRaid();

            Assert.AreEqual(3, report.defeated);
            Assert.AreEqual(0, report.escaped);
            Assert.AreEqual(0, report.coinsStolen);
            Assert.AreEqual(15, report.bountyCoins);
            Assert.AreEqual(1015, game.State.coins);
            Assert.AreEqual(3, shots);
        }

        [Test]
        public void Raid_WeakDefense_DoesNotStopInvaders()
        {
            Place("weak", 0, 0);

            var report = RunRaid();

            Assert.AreEqual(3, report.escaped);
        }

        [Test]
        public void Raid_ThiefCanBeStoppedWhileStealing()
        {
            // 한가운데 바로 옆에 약한 방어 시설을 둔다. 지나가는 동안에는 못 잡지만,
            // 침략자가 코인을 뒤지느라 머무는 동안에는 잡을 수 있다.
            catalog.placeables.Add(new PlaceableDef
            {
                id = "slow", kind = PlaceableKind.Defense, width = 1, height = 1, cost = 0,
                attackRange = 1.2f, attackDamage = 4, attackInterval = 0.5f
            });
            catalog.invaders[0].hp = 20;
            catalog.balance.raidBaseCount = 1;

            int goalX, goalY;
            game.GetRaidGoal(out goalX, out goalY);
            Assert.AreEqual(Land / 2, goalX);
            Assert.AreEqual(Land / 2, goalY);
            Place("slow", goalX, goalY + 1);

            catalog.balance.raidStealSeconds = 0f;
            Assert.AreEqual(1, RunRaid().escaped);

            catalog.balance.raidStealSeconds = 3f;
            bool sawStealing = false;
            var report = RunRaid(() =>
            {
                foreach (var invader in game.Invaders) sawStealing |= invader.stealing;
            });
            Assert.AreEqual(1, report.defeated);
            Assert.AreEqual(0, report.coinsStolen);
            Assert.IsTrue(sawStealing);
        }

        [Test]
        public void Raid_GoalMovesWhenTheCentreIsBuiltOver()
        {
            int goalX, goalY;
            Place("wall", Land / 2, Land / 2);

            game.GetRaidGoal(out goalX, out goalY);

            Assert.IsFalse(goalX == Land / 2 && goalY == Land / 2);
            Assert.LessOrEqual(Math.Abs(goalX - Land / 2) + Math.Abs(goalY - Land / 2), 1);
        }

        [Test]
        public void Raid_SafeCanBeMovedToAnEmptyCell()
        {
            int goalX, goalY;
            Assert.AreEqual(ActionResult.Ok, game.MoveSafe(1, 4));
            game.GetRaidGoal(out goalX, out goalY);
            Assert.AreEqual(1, goalX);
            Assert.AreEqual(4, goalY);
            Assert.AreEqual(1, game.State.safeX);

            // 막힌 칸과 땅 밖은 안 된다.
            Place("wall", 2, 4);
            Assert.AreEqual(ActionResult.InvalidPlacement, game.MoveSafe(2, 4));
            Assert.AreEqual(ActionResult.InvalidPlacement, game.MoveSafe(Land, 0));
            Assert.AreEqual(ActionResult.InvalidPlacement, game.MoveSafe(-1, 0));

            // 금고 칸에 무언가를 지으면 바로 옆 빈칸으로 비켜 선다.
            Place("wall", 1, 4);
            game.GetRaidGoal(out goalX, out goalY);
            Assert.IsFalse(goalX == 1 && goalY == 4);
            Assert.LessOrEqual(Math.Abs(goalX - 1) + Math.Abs(goalY - 4), 1);

            // 침략 중에는 옮길 수 없다.
            game.StartRaidNow();
            Assert.IsTrue(game.RaidActive);
            Assert.AreEqual(ActionResult.Busy, game.MoveSafe(0, 0));
        }

        [Test]
        public void Raid_StealingNeverMakesCoinsNegative()
        {
            game.State.coins = 40;

            var report = RunRaid();

            Assert.AreEqual(40, report.coinsStolen);
            Assert.AreEqual(0, game.State.coins);
        }

        [Test]
        public void Raid_InvadersWalkAroundObjects()
        {
            // 한 줄을 울타리로 막고 한 칸만 틔워 둔다. 한가운데 옆에도 몇 개 놓는다. 돌아갈 길은 남아 있다.
            var blocked = new HashSet<int>();
            var fences = new[] { 0, 1, 1, 1, 2, 1, 4, 1, 5, 1, 2, 3, 4, 3, 3, 4 };
            for (int i = 0; i < fences.Length; i += 2)
            {
                Place("wall", fences[i], fences[i + 1]);
                blocked.Add(fences[i] + fences[i + 1] * Land);
            }

            int samples = 0;
            RunRaid(() =>
            {
                foreach (var invader in game.Invaders)
                {
                    if (!invader.entered) continue;
                    if (invader.x < 0f || invader.y < 0f || invader.x >= Land || invader.y >= Land) continue;
                    int cell = (int)Math.Floor(invader.x) + (int)Math.Floor(invader.y) * Land;
                    Assert.IsFalse(blocked.Contains(cell), "침략자가 물체가 놓인 칸을 지나갔다");
                    samples++;
                }
            });

            Assert.Greater(samples, 0);
        }

        [Test]
        public void Raid_InvadersWalkOverDecorations()
        {
            // 금고 둘레를 장식으로 빙 둘러도 침략자는 넘어 들어온다.
            int goalX, goalY;
            game.GetRaidGoal(out goalX, out goalY);
            var fences = new HashSet<int>();
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    Place("fence", goalX + dx, goalY + dy);
                    fences.Add(goalX + dx + (goalY + dy) * Land);
                }
            }

            int x, y;
            game.GetRaidGoal(out x, out y);
            Assert.AreEqual(goalX, x);
            Assert.AreEqual(goalY, y);

            bool crossedFence = false;
            var report = RunRaid(() =>
            {
                foreach (var invader in game.Invaders)
                {
                    if (!invader.entered || invader.x < 0f || invader.y < 0f || invader.x >= Land || invader.y >= Land) continue;
                    crossedFence |= fences.Contains((int)Math.Floor(invader.x) + (int)Math.Floor(invader.y) * Land);
                }
            });
            Assert.IsTrue(crossedFence, "침략자가 장식을 넘어오지 않았다");
            Assert.Greater(report.escaped, 0);

            // 장식 칸에도 금고를 옮길 수 있다.
            Assert.AreEqual(ActionResult.Ok, game.MoveSafe(goalX + 1, goalY));
        }

        [Test]
        public void Raid_WalledOffTown_InvadersStillGetThrough()
        {
            // 땅 가장자리를 울타리로 빙 둘러 막는다. 길이 없어도 침략은 끝나야 한다.
            for (int i = 0; i < Land; i++)
            {
                Place("wall", i, 0);
                Place("wall", i, Land - 1);
                if (i > 0 && i < Land - 1)
                {
                    Place("wall", 0, i);
                    Place("wall", Land - 1, i);
                }
            }

            var report = RunRaid();

            Assert.AreEqual(3, report.escaped);
        }

        [Test]
        public void Raid_PlacingObjectsMidRaid_Replans()
        {
            game.StartRaidNow();
            for (int i = 0; i < 20; i++) game.TickRaid(0.05f);

            Place("wall", 2, 2);
            Assert.AreEqual(ActionResult.Ok, game.ExpandLand());

            for (int step = 0; step < 5000 && game.RaidActive; step++) game.TickRaid(0.05f);

            Assert.IsFalse(game.RaidActive);
            Assert.AreEqual(1, game.State.raidWave);
        }

        [Test]
        public void Raid_StrengthFollowsTheLevelNotTheWaveCount()
        {
            catalog.balance.raidLevelsPerExtra = 1;
            catalog.balance.raidHpGrowthPercent = 50;
            catalog.balance.raidMaxCount = 4;

            // 몇 번을 겪어도 레벨이 그대로면 세기도 그대로다.
            Assert.AreEqual(3, RunRaid().total);
            Assert.AreEqual(3, RunRaid().total);

            float hpAtLevel3 = 0f;
            game.State.level = 3;
            Assert.AreEqual(4, RunRaid(() =>
            {
                foreach (var invader in game.Invaders) hpAtLevel3 = Math.Max(hpAtLevel3, invader.maxHp);
            }).total);
            Assert.AreEqual(20f, hpAtLevel3);

            // 상한을 넘지 않는다.
            game.State.level = 9;
            Assert.AreEqual(4, RunRaid().total);
        }

        [Test]
        public void Raid_InvadersAppearFromTheirLevel()
        {
            catalog.invaders.Add(new InvaderDef { id = "big", hp = 10, speed = 2f, stealCoins = 1, firstLevel = 3 });
            catalog.balance.raidBaseCount = 1;

            for (int i = 0; i < 10; i++)
            {
                RunRaid(() =>
                {
                    foreach (var invader in game.Invaders) Assert.AreEqual("thief", invader.def.id);
                });
            }

            game.State.level = 3;
            bool sawBig = false;
            for (int i = 0; i < 30 && !sawBig; i++)
            {
                RunRaid(() =>
                {
                    foreach (var invader in game.Invaders) sawBig |= invader.def.id == "big";
                });
            }
            Assert.IsTrue(sawBig);
        }

        [Test]
        public void Raid_SafeKeepsMostOfTheCoins()
        {
            // 도둑 셋이 30씩 노리지만 한 번의 침략에는 가진 코인의 30%까지만 잃는다.
            catalog.balance.raidMaxStealPercent = 30;
            game.State.coins = 100;

            var report = RunRaid();

            Assert.AreEqual(3, report.escaped);
            Assert.AreEqual(30, report.coinsStolen);
            Assert.AreEqual(70, game.State.coins);
        }

        [Test]
        public void RaidHold_DelaysTheNextRaidWhileSolvingAQuiz()
        {
            game.TickRaid(0.1f);
            long scheduled = game.State.nextRaidAt;

            game.RaidHold = true;
            clock.Now = scheduled + 50;
            game.TickRaid(0.1f);
            Assert.IsFalse(game.RaidActive);
            Assert.AreEqual(clock.Now + 10, game.State.nextRaidAt);

            // 문제 창을 닫으면 경고 시간만큼 지난 뒤에 온다.
            game.RaidHold = false;
            game.TickRaid(0.1f);
            Assert.IsFalse(game.RaidActive);
            clock.Now += 10;
            game.TickRaid(0.1f);
            Assert.IsTrue(game.RaidActive);
        }

        [Test]
        public void ResumeRaidClock_SkipsRaidsMissedWhileAway()
        {
            game.TickRaid(0.1f);
            long scheduled = game.State.nextRaidAt;

            // 한참 꺼 두었다가 돌아왔다.
            clock.Now = scheduled + 5000;
            game.ResumeRaidClock();

            Assert.AreEqual(clock.Now + 100, game.State.nextRaidAt);
            game.TickRaid(0.1f);
            Assert.IsFalse(game.RaidActive);
        }

        [Test]
        public void ResumeRaidClock_KeepsATimerThatIsStillFarAway()
        {
            game.TickRaid(0.1f);
            long scheduled = game.State.nextRaidAt;

            clock.Now += 20;
            game.ResumeRaidClock();

            Assert.AreEqual(scheduled, game.State.nextRaidAt);
        }

        // ---------- 등급 ----------

        void AddGrades(int commonWeight, int eliteWeight)
        {
            catalog.grades.Add(new GradeDef { id = "common", displayName = "Common", weight = commonWeight });
            catalog.grades.Add(new GradeDef
            {
                id = "elite", displayName = "Elite", weight = eliteWeight,
                damagePercent = 250, rangePercent = 150, speedPercent = 200
            });
        }

        [Test]
        public void Grade_IsRolledOnlyForDefenses()
        {
            AddGrades(1, 1);

            var fence = game.FindObject(Place("fence", 0, 0));
            var tower = game.FindObject(Place("tower", 1, 0));

            Assert.AreEqual("", fence.gradeId);
            Assert.IsNull(game.GradeOf(fence));
            Assert.IsNotNull(game.GradeOf(tower));
            Assert.IsTrue(tower.gradeId == "common" || tower.gradeId == "elite");
        }

        [Test]
        public void Grade_WeightDecidesWhichGradeComesUp()
        {
            AddGrades(0, 5);
            Assert.AreEqual("elite", game.FindObject(Place("tower", 0, 0)).gradeId);
            Assert.AreEqual(100f, game.GradeChancePercent(catalog.grades[1]));
            Assert.AreEqual(0f, game.GradeChancePercent(catalog.grades[0]));

            catalog.grades[0].weight = 5;
            catalog.grades[1].weight = 0;
            Assert.AreEqual("common", game.FindObject(Place("tower", 1, 0)).gradeId);
        }

        [Test]
        public void Grade_RollsFollowTheWeightsRoughly()
        {
            AddGrades(3, 1);
            Assert.AreEqual(75f, game.GradeChancePercent(catalog.grades[0]));

            int elite = 0;
            const int Tries = 2000;
            for (int i = 0; i < Tries; i++)
            {
                int id = Place("weak", 0, 0);
                if (game.FindObject(id).gradeId == "elite") elite++;
                Assert.AreEqual(ActionResult.Ok, game.Remove(id));
            }

            // 기대값은 25%. 넉넉하게 18~32% 안이면 통과.
            Assert.Greater(elite, Tries * 18 / 100);
            Assert.Less(elite, Tries * 32 / 100);
        }

        [Test]
        public void Grade_ChangesAttackStats()
        {
            AddGrades(0, 1);
            var weak = game.FindObject(Place("weak", 0, 0));

            // 기본값: 사거리 1.2, 공격력 1, 간격 5초. 등급: 공격 250%, 사거리 150%, 빠르기 200%.
            Assert.AreEqual(3, game.AttackDamageOf(weak));
            Assert.AreEqual(1.8f, game.AttackRangeOf(weak), 0.0001f);
            Assert.AreEqual(2.5f, game.AttackIntervalOf(weak), 0.0001f);
        }

        [Test]
        public void Grade_MissingGradeFallsBackToTheLowest()
        {
            // 등급이 생기기 전에 놓은 시설은 gradeId가 비어 있다.
            var tower = game.FindObject(Place("tower", 0, 0));
            Assert.AreEqual("", tower.gradeId);
            Assert.IsNull(game.GradeOf(tower));
            Assert.AreEqual(100, game.AttackDamageOf(tower));

            AddGrades(1, 1);
            catalog.grades[0].damagePercent = 50;

            Assert.AreEqual("common", game.GradeOf(tower).id);
            Assert.AreEqual(50, game.AttackDamageOf(tower));
        }

        [Test]
        public void Grade_HigherGradeWinsWhereTheBaseOneLoses()
        {
            catalog.balance.raidBaseCount = 1;
            catalog.invaders[0].hp = 15;
            catalog.placeables.Add(new PlaceableDef
            {
                id = "slow", kind = PlaceableKind.Defense, width = 1, height = 1, cost = 0,
                attackRange = 1.2f, attackDamage = 4, attackInterval = 0.5f
            });
            AddGrades(1, 0);
            catalog.grades[1].damagePercent = 500;

            int goalX, goalY;
            game.GetRaidGoal(out goalX, out goalY);
            int id = Place("slow", goalX, goalY + 1);
            Assert.AreEqual(1, RunRaid().escaped);

            // 같은 자리에 높은 등급을 놓으면 막아 낸다.
            Assert.AreEqual(ActionResult.Ok, game.Remove(id));
            catalog.grades[0].weight = 0;
            catalog.grades[1].weight = 1;
            Place("slow", goalX, goalY + 1);
            Assert.AreEqual(1, RunRaid().defeated);
        }

        // ---------- 문제 풀기 ----------

        [Test]
        public void Quiz_CorrectAnswerPaysTheReward()
        {
            bool correct;
            int reward;

            Assert.AreEqual(ActionResult.Ok, game.AnswerQuiz("q1", 2, out correct, out reward));
            Assert.IsTrue(correct);
            Assert.AreEqual(30, reward);
            Assert.AreEqual(1030, game.State.coins);
            Assert.AreEqual(1, game.State.quizSolved);

            // 문제에 보상이 따로 적혀 있으면 그 값을 쓴다.
            Assert.AreEqual(ActionResult.Ok, game.AnswerQuiz("q2", 1, out correct, out reward));
            Assert.IsTrue(correct);
            Assert.AreEqual(70, reward);
            Assert.AreEqual(1100, game.State.coins);
        }

        [Test]
        public void Quiz_SometimesPaysAGem()
        {
            bool correct;
            int reward, gems;
            int before = game.State.gems;

            catalog.balance.quizGemChancePercent = 100;
            Assert.AreEqual(ActionResult.Ok, game.AnswerQuiz("q1", 2, out correct, out reward, out gems));
            Assert.AreEqual(1, gems);
            Assert.AreEqual(before + 1, game.State.gems);

            catalog.balance.quizGemChancePercent = 0;
            Assert.AreEqual(ActionResult.Ok, game.AnswerQuiz("q1", 2, out correct, out reward, out gems));
            Assert.AreEqual(0, gems);

            // 틀리면 확률과 상관없이 보석이 없다.
            catalog.balance.quizGemChancePercent = 100;
            Assert.AreEqual(ActionResult.Ok, game.AnswerQuiz("q1", 1, out correct, out reward, out gems));
            Assert.AreEqual(0, gems);
            Assert.AreEqual(before + 1, game.State.gems);
        }

        [Test]
        public void Harvest_SometimesNeedsAQuizFirst()
        {
            catalog.balance.storageUpgradeStep = 1;
            game.State.storageCapacity = 50;
            int plot = Place("plot", 0, 0);
            bool correct;
            int reward, gems;

            // 확률 0%: 문제 없이 수확.
            catalog.balance.harvestQuizChancePercent = 0;
            Assert.AreEqual(ActionResult.Ok, game.Plant(plot, "carrot"));
            clock.Now += 10;
            Assert.AreEqual(ActionResult.Ok, game.Harvest(plot));

            // 확률 100%: 수확하려고 누를 때 문제가 나오고, 맞히기 전에는 수확할 수 없다.
            catalog.balance.harvestQuizChancePercent = 100;
            Assert.AreEqual(ActionResult.Ok, game.Plant(plot, "carrot"));
            Assert.IsFalse(game.FindObject(plot).quizGate);
            clock.Now += 10;
            Assert.AreEqual(ActionResult.NeedsQuiz, game.Harvest(plot));
            Assert.IsTrue(game.FindObject(plot).quizGate);

            // 한 번 나온 문제는 다시 눌러도 남아 있다(확률이 0이 되어도).
            catalog.balance.harvestQuizChancePercent = 0;
            Assert.AreEqual(ActionResult.NeedsQuiz, game.Harvest(plot));

            // 틀리면 그대로다(작물도 남아 있다).
            Assert.AreEqual(ActionResult.Ok, game.AnswerQuiz("q1", 1, out correct, out reward, out gems, plot));
            Assert.IsFalse(correct);
            Assert.AreEqual(ActionResult.NeedsQuiz, game.Harvest(plot));
            Assert.AreEqual("carrot", game.FindObject(plot).cropId);

            // 기다렸다가 맞히면 수확할 수 있다.
            clock.Now += 5;
            Assert.AreEqual(ActionResult.Ok, game.AnswerQuiz("q1", 2, out correct, out reward, out gems, plot));
            Assert.IsTrue(correct);
            Assert.AreEqual(ActionResult.Ok, game.Harvest(plot));
            Assert.IsFalse(game.FindObject(plot).quizGate);
        }

        [Test]
        public void Defense_HighGradeIsUnderConstructionUntilQuizzesAreSolved()
        {
            catalog.grades.Add(new GradeDef { id = "common", displayName = "Common", weight = 0 });
            catalog.grades.Add(new GradeDef { id = "legend", displayName = "Legend", weight = 1, buildQuizzes = 2 });
            catalog.invaders[0].hp = 1;
            catalog.balance.raidBaseCount = 1;
            int tower = Place("tower", Land / 2 + 1, Land / 2);
            Assert.AreEqual(2, game.FindObject(tower).buildQuizzes);

            // 공사 중에는 공격하지 않는다.
            Assert.AreEqual(0, RunRaid().defeated);

            bool correct;
            int reward, gems;
            Assert.AreEqual(ActionResult.Ok, game.AnswerQuiz("q1", 2, out correct, out reward, out gems, tower));
            Assert.AreEqual(1, game.FindObject(tower).buildQuizzes);
            Assert.AreEqual(ActionResult.Ok, game.AnswerQuiz("q3", 2, out correct, out reward, out gems, tower));
            Assert.AreEqual(0, game.FindObject(tower).buildQuizzes);

            Assert.AreEqual(1, RunRaid().defeated);
        }

        [Test]
        public void Defense_CommonGradeNeedsNoQuiz()
        {
            catalog.grades.Add(new GradeDef { id = "common", displayName = "Common", weight = 1 });
            catalog.grades.Add(new GradeDef { id = "legend", displayName = "Legend", weight = 0, buildQuizzes = 5 });
            int tower = Place("tower", 0, 0);
            Assert.AreEqual(0, game.FindObject(tower).buildQuizzes);
        }

        [Test]
        public void FacilityUpgrade_NeedsLevelAndQuizzes()
        {
            catalog.GetPlaceable("wall").upgradeLevel = 2;
            catalog.balance.upgradeQuizzes = 3;
            int wall = Place("wall", 0, 0);
            bool correct;
            int reward, gems;

            game.State.level = 1;
            Assert.IsFalse(game.CanUpgrade(wall));
            Assert.AreEqual(2, game.UpgradeLockedLevel(wall));
            // 레벨이 모자라면 맞혀도 늘지 않는다.
            game.AnswerQuiz("q1", 2, out correct, out reward, out gems, wall);
            Assert.AreEqual(0, game.FindObject(wall).upgradeProgress);

            game.State.level = 2;
            Assert.IsTrue(game.CanUpgrade(wall));
            game.AnswerQuiz("q1", 2, out correct, out reward, out gems, wall);
            game.AnswerQuiz("q1", 2, out correct, out reward, out gems, wall);
            Assert.AreEqual(2, game.FindObject(wall).upgradeProgress);
            // 틀려도 맞힌 수는 그대로다.
            game.AnswerQuiz("q1", 1, out correct, out reward, out gems, wall);
            Assert.AreEqual(2, game.FindObject(wall).upgradeProgress);
            clock.Now += 5;
            game.AnswerQuiz("q1", 2, out correct, out reward, out gems, wall);

            var obj = game.FindObject(wall);
            Assert.IsTrue(obj.upgraded);
            Assert.IsFalse(game.CanUpgrade(wall));
            Assert.AreEqual(6, game.QueueSlotsOf(obj, catalog.GetPlaceable("wall")));
            Assert.AreEqual(2, game.ParallelOf(obj));
        }

        [Test]
        public void Quiz_OnlyQuizButtonAnswersCountForTheCombo()
        {
            catalog.grades.Add(new GradeDef { id = "common", displayName = "Common", weight = 0 });
            catalog.grades.Add(new GradeDef { id = "legend", displayName = "Legend", weight = 1, buildQuizzes = 5 });
            int tower = Place("tower", 0, 0);
            bool correct;
            int reward, gems;

            game.AnswerQuiz("q1", 2, out correct, out reward, out gems);
            game.AnswerQuiz("q1", 2, out correct, out reward, out gems);
            Assert.AreEqual(2, game.QuizCombo);

            // 공사 문제는 맞혀도 콤보가 늘지 않고, 틀려도 끊기지 않는다.
            game.AnswerQuiz("q1", 2, out correct, out reward, out gems, tower);
            Assert.AreEqual(2, game.QuizCombo);
            game.AnswerQuiz("q1", 1, out correct, out reward, out gems, tower);
            Assert.AreEqual(2, game.QuizCombo);
        }

        [Test]
        public void Quiz_ComboGrowsBreaksOnWrongAnswerAndExpires()
        {
            catalog.balance.comboTimeoutSeconds = 600;
            catalog.balance.comboTiers.Add(new ComboTierDef { combo = 3, speedPercent = 5 });
            catalog.balance.comboTiers.Add(new ComboTierDef { combo = 5, speedPercent = 10 });
            bool correct;
            int reward, gems;

            for (int i = 0; i < 3; i++) game.AnswerQuiz("q1", 2, out correct, out reward, out gems);
            Assert.AreEqual(3, game.QuizCombo);
            Assert.AreEqual(5, game.ComboSpeedPercent);
            Assert.AreEqual(5, game.NextComboTier.combo);

            game.AnswerQuiz("q1", 2, out correct, out reward, out gems);
            game.AnswerQuiz("q1", 2, out correct, out reward, out gems);
            Assert.AreEqual(10, game.ComboSpeedPercent);
            Assert.IsNull(game.NextComboTier);

            // 틀리면 끊긴다.
            game.AnswerQuiz("q1", 1, out correct, out reward, out gems);
            Assert.AreEqual(0, game.QuizCombo);
            Assert.AreEqual(0, game.ComboSpeedPercent);

            // 오래 쉬어도 끊긴다.
            clock.Now += 5;
            game.AnswerQuiz("q1", 2, out correct, out reward, out gems);
            game.AnswerQuiz("q1", 2, out correct, out reward, out gems);
            Assert.AreEqual(2, game.QuizCombo);
            clock.Now += 599;
            Assert.AreEqual(2, game.QuizCombo);
            clock.Now += 2;
            Assert.AreEqual(0, game.QuizCombo);
            game.AnswerQuiz("q1", 2, out correct, out reward, out gems);
            Assert.AreEqual(1, game.QuizCombo);
        }

        [Test]
        public void Quiz_WrongAnswerPaysNothingAndLocksForAWhile()
        {
            bool correct;
            int reward;

            Assert.AreEqual(ActionResult.Ok, game.AnswerQuiz("q1", 1, out correct, out reward));
            Assert.IsFalse(correct);
            Assert.AreEqual(0, reward);
            Assert.AreEqual(1000, game.State.coins);
            Assert.AreEqual(5, game.QuizLockSeconds);

            Assert.IsNull(game.DrawQuiz());
            Assert.AreEqual(ActionResult.NotReady, game.AnswerQuiz("q1", 2, out correct, out reward));
            Assert.AreEqual(1000, game.State.coins);

            clock.Now += 5;
            Assert.AreEqual(0, game.QuizLockSeconds);
            Assert.IsNotNull(game.DrawQuiz());
        }

        [Test]
        public void Quiz_UnknownQuestionIsRejected()
        {
            bool correct;
            int reward;
            Assert.AreEqual(ActionResult.NotFound, game.AnswerQuiz("nope", 1, out correct, out reward));
        }

        [Test]
        public void Quiz_DoesNotRepeatTheSameQuestionBackToBack()
        {
            string previous = null;
            var seen = new HashSet<string>();

            for (int i = 0; i < 40; i++)
            {
                var quiz = game.DrawQuiz();
                Assert.IsNotNull(quiz);
                Assert.AreNotEqual(previous, quiz.id);
                previous = quiz.id;
                seen.Add(quiz.id);
            }

            Assert.AreEqual(3, seen.Count);
        }

        [Test]
        public void Quiz_EmptyBankGivesNoQuestion()
        {
            catalog.SetQuiz(new QuizBank());

            Assert.AreEqual(0, game.QuizCount);
            Assert.IsNull(game.DrawQuiz());
        }

        [Test]
        public void Quiz_SetQuizNumbersQuestionsWithoutIds()
        {
            Assert.AreEqual("q1", catalog.quiz.questions[0].id);
            Assert.AreEqual("q3", catalog.quiz.questions[2].id);
            Assert.AreEqual("3 + 3", catalog.GetQuiz("q3").question);
        }

        [Test]
        public void Validate_ReportsBadQuizAndDefenseData()
        {
            catalog.quiz.questions.Add(new QuizDef { id = "bad", question = "?", choices = new List<string> { "a", "b" }, answer = 3 });
            catalog.placeables.Add(new PlaceableDef { id = "broken", kind = PlaceableKind.Defense, width = 1, height = 1 });

            var errors = catalog.Validate();

            Assert.IsTrue(errors.Exists(e => e.Contains("bad")));
            Assert.IsTrue(errors.Exists(e => e.Contains("broken")));
        }
    }
}
