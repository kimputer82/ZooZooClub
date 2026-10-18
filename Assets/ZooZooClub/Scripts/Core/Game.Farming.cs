using System;

namespace ZooZooClub.Core
{
    // 재배: 밭에 작물을 심고, 다 자라면 수확해 창고에 넣는다.
    public sealed partial class Game
    {
        PlacedObject FindPlot(int objectId)
        {
            var obj = FindObject(objectId);
            if (obj == null) return null;
            var def = Catalog.GetPlaceable(obj.defId);
            if (def == null || def.kind != PlaceableKind.Plot) return null;
            return obj;
        }

        public ActionResult Plant(int objectId, string cropId)
        {
            var plot = FindPlot(objectId);
            if (plot == null) return ActionResult.NotFound;
            if (!string.IsNullOrEmpty(plot.cropId)) return ActionResult.Busy;

            var crop = Catalog.GetCrop(cropId);
            if (crop == null) return ActionResult.NotFound;
            if (State.level < crop.unlockLevel) return ActionResult.LockedByLevel;
            if (State.coins < crop.seedCost) return ActionResult.NotEnoughCoins;

            State.coins -= crop.seedCost;
            plot.cropId = crop.id;
            plot.plantedAt = Now;
            // 수확할 때 문제를 풀어야 하는지는 심을 때 정해 둔다(다시 눌러서 피하지 못하게).
            int quizChance = Catalog.balance != null ? Catalog.balance.harvestQuizChancePercent : 0;
            plot.quizGate = quizChance > 0 && QuizCount > 0 && NextRandom(100) < quizChance;
            NotifyChanged();
            return ActionResult.Ok;
        }

        /// <summary>작물이 자란 정도(0~1). 빈 밭이거나 밭이 아니면 0.</summary>
        public float GrowthProgress(int objectId)
        {
            var plot = FindPlot(objectId);
            if (plot == null || string.IsNullOrEmpty(plot.cropId)) return 0f;

            var crop = Catalog.GetCrop(plot.cropId);
            if (crop == null || crop.growSeconds <= 0) return 1f;

            float progress = (float)(Now - plot.plantedAt) / crop.growSeconds;
            return Math.Max(0f, Math.Min(1f, progress));
        }

        /// <summary>수확까지 남은 초. 빈 밭이면 0.</summary>
        public long SecondsUntilRipe(int objectId)
        {
            var plot = FindPlot(objectId);
            if (plot == null || string.IsNullOrEmpty(plot.cropId)) return 0;

            var crop = Catalog.GetCrop(plot.cropId);
            if (crop == null) return 0;
            return Math.Max(0L, plot.plantedAt + crop.growSeconds - Now);
        }

        public bool IsRipe(int objectId)
        {
            var plot = FindPlot(objectId);
            if (plot == null || string.IsNullOrEmpty(plot.cropId)) return false;
            return SecondsUntilRipe(objectId) == 0;
        }

        /// <summary>이 물건이 보따리(창고에서 열어 보는 것)인지.</summary>
        public bool IsBag(string itemId)
        {
            var balance = Catalog.balance;
            return balance != null && !string.IsNullOrEmpty(balance.bagItemId) && balance.bagItemId == itemId;
        }

        /// <summary>
        /// 창고의 보따리 하나를 연다. bagRewards 가운데 하나가 weight에 비례한 확률로 나와 창고에 들어간다.
        /// 보따리 한 칸이 빠지고 한 칸이 들어오므로 창고가 가득 차 있어도 열 수 있다.
        /// </summary>
        public ActionResult OpenBag(out string rewardItemId)
        {
            rewardItemId = null;
            var balance = Catalog.balance;
            if (balance == null || string.IsNullOrEmpty(balance.bagItemId)) return ActionResult.NotFound;
            if (CountOf(balance.bagItemId) < 1) return ActionResult.NotEnoughItems;

            int total = 0;
            foreach (var reward in balance.bagRewards)
            {
                if (reward != null && reward.weight > 0 && Catalog.GetItem(reward.itemId) != null) total += reward.weight;
            }
            if (total <= 0) return ActionResult.NotFound;

            int roll = NextRandom(total);
            foreach (var reward in balance.bagRewards)
            {
                if (reward == null || reward.weight <= 0 || Catalog.GetItem(reward.itemId) == null) continue;
                if (roll < reward.weight)
                {
                    rewardItemId = reward.itemId;
                    break;
                }
                roll -= reward.weight;
            }

            RemoveItem(balance.bagItemId, 1);
            AddItem(rewardItemId, 1);
            NotifyChanged();
            return ActionResult.Ok;
        }

        public ActionResult Harvest(int objectId)
        {
            bool foundBag;
            return Harvest(objectId, out foundBag);
        }

        /// <summary>
        /// 다 자란 밭을 수확한다. 낮은 확률로 보따리도 함께 얻는다(창고에 자리가 남아 있을 때만).
        /// </summary>
        public ActionResult Harvest(int objectId, out bool foundBag)
        {
            foundBag = false;
            var plot = FindPlot(objectId);
            if (plot == null || string.IsNullOrEmpty(plot.cropId)) return ActionResult.NotFound;
            if (!IsRipe(objectId)) return ActionResult.NotReady;
            if (plot.quizGate && QuizCount > 0) return ActionResult.NeedsQuiz;

            var crop = Catalog.GetCrop(plot.cropId);
            if (crop == null)
            {
                // 카탈로그에서 사라진 작물이 저장 파일에 남아 있으면 밭만 비운다.
                plot.cropId = "";
                NotifyChanged();
                return ActionResult.NotFound;
            }

            if (FreeStorage < crop.harvestAmount) return ActionResult.StorageFull;

            AddItem(crop.harvestItemId, crop.harvestAmount);

            var balance = Catalog.balance;
            if (balance != null && !string.IsNullOrEmpty(balance.bagItemId) && balance.harvestBagChancePercent > 0
                && NextRandom(100) < balance.harvestBagChancePercent && FreeStorage >= 1
                && Catalog.GetItem(balance.bagItemId) != null)
            {
                AddItem(balance.bagItemId, 1);
                foundBag = true;
            }

            plot.cropId = "";
            plot.plantedAt = 0;
            plot.quizGate = false;
            AddXp(crop.harvestXp);
            NotifyChanged();
            return ActionResult.Ok;
        }
    }
}
