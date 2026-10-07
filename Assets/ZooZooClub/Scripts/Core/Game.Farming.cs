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

        public ActionResult Harvest(int objectId)
        {
            var plot = FindPlot(objectId);
            if (plot == null || string.IsNullOrEmpty(plot.cropId)) return ActionResult.NotFound;
            if (!IsRipe(objectId)) return ActionResult.NotReady;

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
            plot.cropId = "";
            plot.plantedAt = 0;
            AddXp(crop.harvestXp);
            NotifyChanged();
            return ActionResult.Ok;
        }
    }
}
