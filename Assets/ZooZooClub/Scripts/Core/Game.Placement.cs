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

        /// <summary>놓여 있는 배치물이 지금 방향에서 차지하는 가로·세로 칸 수.</summary>
        public void GetFootprint(PlacedObject obj, out int width, out int height)
        {
            var def = obj != null ? Catalog.GetPlaceable(obj.defId) : null;
            if (def == null)
            {
                width = 0;
                height = 0;
                return;
            }
            width = Rotation.Width(def, obj.rotation);
            height = Rotation.Height(def, obj.rotation);
        }

        /// <summary>
        /// 해당 위치에 그 방향으로 놓을 수 있는지 확인한다. 옮기거나 돌릴 때는 자기 자신을 ignoreObjectId로 넘긴다.
        /// </summary>
        public bool CanPlace(string defId, int x, int y, int ignoreObjectId = 0, int rotation = 0)
        {
            var def = Catalog.GetPlaceable(defId);
            if (def == null) return false;

            rotation = Rotation.Normalize(def, rotation);
            int width = Rotation.Width(def, rotation);
            int height = Rotation.Height(def, rotation);
            if (!IsInsideLand(x, y, width, height)) return false;

            foreach (var other in State.objects)
            {
                if (other.id == ignoreObjectId) continue;
                if (!PlaceableKind.Conflicts(def, Catalog.GetPlaceable(other.defId))) continue;
                int otherWidth, otherHeight;
                GetFootprint(other, out otherWidth, out otherHeight);
                if (otherWidth <= 0) continue;

                bool overlaps = x < other.x + otherWidth
                    && other.x < x + width
                    && y < other.y + otherHeight
                    && other.y < y + height;
                if (overlaps) return false;
            }

            return true;
        }

        /// <summary>지금 살 수 있는 기간인가(기간 한정 장식). 기간이 없으면 언제나 true.</summary>
        public bool IsOnSale(PlaceableDef def)
        {
            return def != null && Now < def.AvailableUntilUnix;
        }

        /// <summary>가지고 있는 수(마을에 놓인 것과 보관함에 넣어 둔 것을 합친다). 최대 개수를 셀 때 쓴다.</summary>
        public int CountPlaced(string defId)
        {
            int count = 0;
            foreach (var obj in State.objects)
            {
                if (obj.defId == defId) count++;
            }
            foreach (var obj in State.stored)
            {
                if (obj.defId == defId) count++;
            }
            return count;
        }

        /// <summary>보관함에서 한 칸으로 묶어 보여 줄 같은 종류(배치물·등급·업그레이드·남은 짓기 문제가 모두 같은 것).</summary>
        public static string StoredKey(PlacedObject obj)
        {
            return obj.defId + "|" + (obj.gradeId ?? "") + "|" + (obj.upgraded ? 1 : 0) + "|" + obj.buildQuizzes;
        }

        public PlacedObject FindStored(int storedId)
        {
            foreach (var obj in State.stored)
            {
                if (obj.id == storedId) return obj;
            }
            return null;
        }

        /// <summary>
        /// 마을에서 거둬 보관함에 넣는다(코인은 돌려받지 않고, 나중에 무료로 다시 놓는다).
        /// 작물이 심겨 있거나 생산 중이면 Busy, 침략 중이면 NotReady. 일하던 주민은 쉬게 된다.
        /// </summary>
        public ActionResult Store(int objectId)
        {
            var obj = FindObject(objectId);
            if (obj == null) return ActionResult.NotFound;
            if (!string.IsNullOrEmpty(obj.cropId) || obj.queue.Count > 0) return ActionResult.Busy;
            if (RaidActive) return ActionResult.NotReady;

            ClearAssignmentsFor(obj.id);
            State.objects.Remove(obj);
            obj.quizGate = false;
            State.stored.Add(obj);
            layoutChanged = true;
            NotifyChanged();
            return ActionResult.Ok;
        }

        /// <summary>보관함의 배치물을 그 자리에 다시 놓는다. 값은 들지 않고, 레벨·문제·판매 기간 조건도 보지 않는다.</summary>
        public ActionResult PlaceStored(int storedId, int x, int y, int rotation = 0)
        {
            var obj = FindStored(storedId);
            if (obj == null) return ActionResult.NotFound;
            var def = Catalog.GetPlaceable(obj.defId);
            if (def == null) return ActionResult.NotFound;

            rotation = Rotation.Normalize(def, rotation);
            if (!CanPlace(obj.defId, x, y, 0, rotation)) return ActionResult.InvalidPlacement;

            State.stored.Remove(obj);
            obj.x = x;
            obj.y = y;
            obj.rotation = rotation;
            State.objects.Add(obj);
            layoutChanged = true;
            NotifyChanged();
            return ActionResult.Ok;
        }

        public ActionResult BuyAndPlace(string defId, int x, int y, out int objectId, int rotation = 0)
        {
            objectId = 0;
            var def = Catalog.GetPlaceable(defId);
            if (def == null) return ActionResult.NotFound;
            if (State.level < def.unlockLevel) return ActionResult.LockedByLevel;
            if (State.quizSolved < def.requiredQuizzes) return ActionResult.LockedByQuiz;
            if (!IsOnSale(def)) return ActionResult.EventEnded;
            if (def.maxCount > 0 && CountPlaced(defId) >= def.maxCount) return ActionResult.MaxReached;
            if (State.coins < def.cost) return ActionResult.NotEnoughCoins;

            rotation = Rotation.Normalize(def, rotation);
            if (!CanPlace(defId, x, y, 0, rotation)) return ActionResult.InvalidPlacement;

            State.coins -= def.cost;
            var obj = new PlacedObject
            {
                id = State.nextObjectId++,
                defId = def.id,
                x = x,
                y = y,
                rotation = rotation
            };
            if (def.kind == PlaceableKind.Defense)
            {
                obj.gradeId = RollGrade();
                // 높은 등급이 나오면 문제를 맞혀야 공사가 끝난다. 문제가 없으면 바로 완성.
                var grade = GradeOf(obj);
                obj.buildQuizzes = grade != null && QuizCount > 0 ? Math.Max(0, grade.buildQuizzes) : 0;
            }
            else if (def.buildQuizzes > 0 && QuizCount > 0)
            {
                // 문제로 짓는 특별 장식: 놓은 뒤 문제를 다 맞혀야 완성된다(그 전에는 매력이 오르지 않는다).
                obj.buildQuizzes = def.buildQuizzes;
            }
            State.objects.Add(obj);
            objectId = obj.id;
            layoutChanged = true;
            NotifyChanged();
            return ActionResult.Ok;
        }

        /// <summary>옮긴다. rotation을 주면 방향도 함께 바꾸고, 주지 않으면(-1) 지금 방향을 지킨다.</summary>
        public ActionResult Move(int objectId, int x, int y, int rotation = -1)
        {
            var obj = FindObject(objectId);
            if (obj == null) return ActionResult.NotFound;

            var def = Catalog.GetPlaceable(obj.defId);
            int newRotation = rotation < 0 ? obj.rotation : Rotation.Normalize(def, rotation);
            if (!CanPlace(obj.defId, x, y, obj.id, newRotation)) return ActionResult.InvalidPlacement;

            obj.x = x;
            obj.y = y;
            obj.rotation = newRotation;
            layoutChanged = true;
            NotifyChanged();
            return ActionResult.Ok;
        }

        /// <summary>제자리에서 한 번 돌린다. 돌린 모양이 들어갈 자리가 없으면 돌리지 못한다.</summary>
        public ActionResult Rotate(int objectId)
        {
            var obj = FindObject(objectId);
            if (obj == null) return ActionResult.NotFound;

            var def = Catalog.GetPlaceable(obj.defId);
            if (def == null || def.rotations <= 1) return ActionResult.NotReady;

            return Move(objectId, obj.x, obj.y, Rotation.Next(def, obj.rotation));
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
            layoutChanged = true;
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
                    if (def != null && obj.buildQuizzes <= 0) total += def.charm;
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
            layoutChanged = true;
            NotifyChanged();
            return ActionResult.Ok;
        }
    }
}
