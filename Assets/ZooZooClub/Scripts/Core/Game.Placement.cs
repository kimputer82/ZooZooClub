using System;

namespace ZooZooClub.Core
{
    // 배치: 마을 땅은 landWidth x landHeight 칸의 격자다.
    // 밭, 생산 시설, 장식 모두 같은 규칙으로 놓고 옮기고 치운다.
    public sealed partial class Game
    {
        public bool IsInsideLand(int x, int y, int width, int height)
        {
            return x >= 0 && y >= 0
                && x + width <= State.landWidth
                && y + height <= State.landHeight;
        }

        /// <summary>해당 위치에 놓을 수 있는지 확인한다. 옮길 때는 자기 자신을 ignoreObjectId로 넘긴다.</summary>
        public bool CanPlace(string defId, int x, int y, int ignoreObjectId = 0)
        {
            var def = Catalog.GetPlaceable(defId);
            if (def == null) return false;
            if (!IsInsideLand(x, y, def.width, def.height)) return false;

            foreach (var other in State.objects)
            {
                if (other.id == ignoreObjectId) continue;
                var otherDef = Catalog.GetPlaceable(other.defId);
                if (otherDef == null) continue;

                bool overlaps = x < other.x + otherDef.width
                    && other.x < x + def.width
                    && y < other.y + otherDef.height
                    && other.y < y + def.height;
                if (overlaps) return false;
            }

            return true;
        }

        public int CountPlaced(string defId)
        {
            int count = 0;
            foreach (var obj in State.objects)
            {
                if (obj.defId == defId) count++;
            }
            return count;
        }

        public ActionResult BuyAndPlace(string defId, int x, int y, out int objectId)
        {
            objectId = 0;
            var def = Catalog.GetPlaceable(defId);
            if (def == null) return ActionResult.NotFound;
            if (State.level < def.unlockLevel) return ActionResult.LockedByLevel;
            if (def.maxCount > 0 && CountPlaced(defId) >= def.maxCount) return ActionResult.MaxReached;
            if (State.coins < def.cost) return ActionResult.NotEnoughCoins;
            if (!CanPlace(defId, x, y)) return ActionResult.InvalidPlacement;

            State.coins -= def.cost;
            var obj = new PlacedObject
            {
                id = State.nextObjectId++,
                defId = def.id,
                x = x,
                y = y
            };
            State.objects.Add(obj);
            objectId = obj.id;
            NotifyChanged();
            return ActionResult.Ok;
        }

        public ActionResult Move(int objectId, int x, int y)
        {
            var obj = FindObject(objectId);
            if (obj == null) return ActionResult.NotFound;
            if (!CanPlace(obj.defId, x, y, obj.id)) return ActionResult.InvalidPlacement;

            obj.x = x;
            obj.y = y;
            NotifyChanged();
            return ActionResult.Ok;
        }

        /// <summary>치우고 구매가의 일부를 돌려받는다. 작물이 심겨 있거나 생산 중이면 치울 수 없다.</summary>
        public ActionResult Remove(int objectId)
        {
            var obj = FindObject(objectId);
            if (obj == null) return ActionResult.NotFound;
            if (!string.IsNullOrEmpty(obj.cropId) || obj.queue.Count > 0) return ActionResult.Busy;

            var def = Catalog.GetPlaceable(obj.defId);
            if (def != null)
            {
                State.coins += (long)def.cost * Catalog.balance.sellBackPercent / 100;
            }

            ClearAssignmentsFor(obj.id);
            State.objects.Remove(obj);
            NotifyChanged();
            return ActionResult.Ok;
        }

        /// <summary>놓여 있는 장식의 매력 점수 합계.</summary>
        public int TotalCharm
        {
            get
            {
                int total = 0;
                foreach (var obj in State.objects)
                {
                    var def = Catalog.GetPlaceable(obj.defId);
                    if (def != null) total += def.charm;
                }
                return total;
            }
        }

        // ---------- 마을 확장 ----------

        int LandExpansionCount
        {
            get
            {
                var balance = Catalog.balance;
                int step = Math.Max(1, balance.landExpandStep);
                return Math.Max(0, (State.landWidth - balance.startLandSize) / step);
            }
        }

        public bool CanExpandFurther
        {
            get { return State.landWidth < Catalog.balance.maxLandSize; }
        }

        public int LandExpandCost
        {
            get { return Catalog.balance.landExpandBaseCost * (LandExpansionCount + 1); }
        }

        public int LandExpandRequiredLevel
        {
            get
            {
                var balance = Catalog.balance;
                return balance.landExpandFirstLevel + LandExpansionCount * balance.landExpandLevelStep;
            }
        }

        public ActionResult ExpandLand()
        {
            if (!CanExpandFurther) return ActionResult.MaxReached;
            if (State.level < LandExpandRequiredLevel) return ActionResult.LockedByLevel;

            int cost = LandExpandCost;
            if (State.coins < cost) return ActionResult.NotEnoughCoins;

            var balance = Catalog.balance;
            int step = Math.Max(1, balance.landExpandStep);
            State.coins -= cost;
            State.landWidth = Math.Min(balance.maxLandSize, State.landWidth + step);
            State.landHeight = Math.Min(balance.maxLandSize, State.landHeight + step);
            NotifyChanged();
            return ActionResult.Ok;
        }
    }
}
