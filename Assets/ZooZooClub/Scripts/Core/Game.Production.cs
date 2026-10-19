using System;

namespace ZooZooClub.Core
{
    // 생산: 시설에 레시피를 줄 세우면 앞에서부터 차례로 만들어진다.
    // 재료는 줄을 세울 때 바로 소비되고, 완성품은 수거할 때 창고로 들어간다.
    // 생산량 업그레이드를 마친 시설은 대기열이 여러 줄(ParallelOf)이 되고, 줄마다 맨 앞 작업이 함께 만들어진다.
    public sealed partial class Game
    {
        PlacedObject FindFacility(int objectId, out PlaceableDef def)
        {
            def = null;
            var obj = FindObject(objectId);
            if (obj == null) return null;

            var found = Catalog.GetPlaceable(obj.defId);
            if (found == null || found.kind != PlaceableKind.Facility) return null;

            def = found;
            return obj;
        }

        // ---------- 생산량 업그레이드 ----------

        /// <summary>업그레이드를 마치려면 맞혀야 하는 문제 수.</summary>
        public int UpgradeQuizCount
        {
            get { return Math.Max(1, Catalog.balance != null ? Catalog.balance.upgradeQuizzes : 10); }
        }

        /// <summary>이 시설의 대기열 칸 수. 업그레이드를 마쳤으면 늘어난다.</summary>
        public int QueueSlotsOf(PlacedObject facility, PlaceableDef def)
        {
            if (def == null) return 0;
            if (facility != null && facility.upgraded && Catalog.balance != null)
                return Math.Max(def.queueSlots, Catalog.balance.upgradeQueueSlots);
            return def.queueSlots;
        }

        /// <summary>이 시설에서 동시에 만들어지는 수. 업그레이드 전에는 1.</summary>
        public int ParallelOf(PlacedObject facility)
        {
            if (facility != null && facility.upgraded && Catalog.balance != null)
                return Math.Max(1, Catalog.balance.upgradeParallel);
            return 1;
        }

        /// <summary>업그레이드가 있는 시설인데 아직 레벨이 모자라면 필요한 레벨, 아니면 0.</summary>
        public int UpgradeLockedLevel(int objectId)
        {
            PlaceableDef def;
            var facility = FindFacility(objectId, out def);
            if (facility == null || facility.upgraded || def.upgradeLevel <= 0) return 0;
            return State.level < def.upgradeLevel ? def.upgradeLevel : 0;
        }

        /// <summary>지금 이 시설의 업그레이드 문제를 풀 수 있는지(업그레이드가 있고, 레벨이 되고, 아직 안 했음).</summary>
        public bool CanUpgrade(int objectId)
        {
            PlaceableDef def;
            var facility = FindFacility(objectId, out def);
            if (facility == null || facility.upgraded || def.upgradeLevel <= 0) return false;
            return State.level >= def.upgradeLevel;
        }

        /// <summary>
        /// 줄 하나에 들어갈 수보다 많이 선 작업을 다른 줄로 옮긴다. 대기열을 줄로 나누기 전에 업그레이드한 저장을 읽을 때 쓴다.
        /// 시각은 건드리지 않으므로 생산 시간은 그대로 흘러간다.
        /// </summary>
        void SpreadQueuesIntoLanes()
        {
            foreach (var obj in State.objects)
            {
                if (obj.queue == null || obj.queue.Count == 0) continue;
                var def = Catalog.GetPlaceable(obj.defId);
                if (def == null || def.kind != PlaceableKind.Facility) continue;

                int lanes = ParallelOf(obj);
                int perLane = SlotsPerLane(obj, def);
                var counts = new int[lanes];
                foreach (var job in obj.queue)
                {
                    if (job.lane < 0 || job.lane >= lanes) job.lane = 0;
                    if (counts[job.lane] < perLane)
                    {
                        counts[job.lane]++;
                        continue;
                    }
                    for (int l = 0; l < lanes; l++)
                    {
                        if (counts[l] >= perLane) continue;
                        job.lane = l;
                        counts[l]++;
                        break;
                    }
                }
            }
        }

        /// <summary>줄 하나에 설 수 있는 작업 수.</summary>
        public int SlotsPerLane(PlacedObject facility, PlaceableDef def)
        {
            return Math.Max(1, QueueSlotsOf(facility, def) / ParallelOf(facility));
        }

        /// <summary>그 줄에 선 작업 수.</summary>
        public int CountInLane(PlacedObject facility, int lane)
        {
            int count = 0;
            foreach (var job in facility.queue)
            {
                if (job.lane == lane) count++;
            }
            return count;
        }

        /// <summary>그 줄의 맨 뒤 작업이 끝나는 시각. 비어 있으면 now.</summary>
        long LaneFreeAt(PlacedObject facility, int lane, long now)
        {
            long free = now;
            foreach (var job in facility.queue)
            {
                if (job.lane == lane) free = Math.Max(free, job.finishAt);
            }
            return free;
        }

        public ActionResult StartCraft(int objectId, string recipeId)
        {
            PlaceableDef def;
            var facility = FindFacility(objectId, out def);
            if (facility == null) return ActionResult.NotFound;

            var recipe = Catalog.GetRecipe(recipeId);
            if (recipe == null || recipe.facilityId != def.id) return ActionResult.NotFound;
            if (State.level < recipe.unlockLevel) return ActionResult.LockedByLevel;
            // 자리가 남은 줄 가운데 가장 먼저 비는 줄에 선다(같으면 앞 줄).
            int lanes = ParallelOf(facility);
            int perLane = SlotsPerLane(facility, def);
            long now = Now;
            int lane = -1;
            long laneFree = long.MaxValue;
            for (int l = 0; l < lanes; l++)
            {
                if (CountInLane(facility, l) >= perLane) continue;
                long free = LaneFreeAt(facility, l, now);
                if (free < laneFree)
                {
                    lane = l;
                    laneFree = free;
                }
            }
            if (lane < 0) return ActionResult.QueueFull;
            if (!HasItems(recipe.inputs)) return ActionResult.NotEnoughItems;

            RemoveItems(recipe.inputs);

            long start = laneFree;

            facility.queue.Add(new CraftJob
            {
                recipeId = recipe.id,
                startAt = start,
                finishAt = start + CraftSecondsFor(objectId, recipe),
                lane = lane
            });

            NotifyChanged();
            return ActionResult.Ok;
        }

        /// <summary>완성되어 수거를 기다리는 작업 수.</summary>
        public int ReadyJobCount(int objectId)
        {
            PlaceableDef def;
            var facility = FindFacility(objectId, out def);
            if (facility == null) return 0;

            int ready = 0;
            long now = Now;
            foreach (var job in facility.queue)
            {
                if (job.finishAt <= now) ready++;
            }
            return ready;
        }

        /// <summary>지금 만들고 있는 작업. 없으면 null.</summary>
        CraftJob CurrentJob(PlacedObject facility)
        {
            // 줄이 여럿이면 지금 만들고 있는 것 가운데 가장 먼저 끝나는 것.
            long now = Now;
            CraftJob best = null;
            foreach (var job in facility.queue)
            {
                if (job.finishAt <= now || job.startAt > now) continue;
                if (best == null || job.finishAt < best.finishAt) best = job;
            }
            if (best != null) return best;
            foreach (var job in facility.queue)
            {
                if (job.finishAt > now) return job;
            }
            return null;
        }

        /// <summary>지금 만들고 있는 작업을 바로 끝내는 데 드는 보석. 만들고 있는 것이 없으면 0.</summary>
        public int FinishNowCost(int objectId)
        {
            PlaceableDef def;
            var facility = FindFacility(objectId, out def);
            if (facility == null) return 0;
            var job = CurrentJob(facility);
            if (job == null) return 0;

            long remaining = job.finishAt - Now;
            long step = Math.Max(1, Catalog.balance != null ? Catalog.balance.gemFinishSeconds : 120);
            return (int)Math.Max(1L, (remaining + step - 1) / step);
        }

        /// <summary>
        /// 보석을 써서 지금 만들고 있는 작업을 바로 끝낸다. 뒤에 줄 선 작업들도 그만큼 앞당겨진다.
        /// 끝난 완성품은 평소처럼 수거해야 창고에 들어간다.
        /// </summary>
        public ActionResult FinishNow(int objectId)
        {
            PlaceableDef def;
            var facility = FindFacility(objectId, out def);
            if (facility == null) return ActionResult.NotFound;
            var job = CurrentJob(facility);
            if (job == null) return ActionResult.NotReady;

            int cost = FinishNowCost(objectId);
            if (State.gems < cost) return ActionResult.NotEnoughGems;
            State.gems -= cost;

            long now = Now;
            int index = facility.queue.IndexOf(job);
            job.finishAt = now;
            // 같은 줄에서 그 뒤에 기다리던 작업들만 앞당긴다(다른 줄은 그대로 흘러간다).
            long previousFinish = now;
            for (int i = index + 1; i < facility.queue.Count; i++)
            {
                var next = facility.queue[i];
                if (next.lane != job.lane) continue;
                long duration = next.finishAt - next.startAt;
                next.startAt = Math.Max(now, previousFinish);
                next.finishAt = next.startAt + duration;
                previousFinish = next.finishAt;
            }

            NotifyChanged();
            return ActionResult.Ok;
        }

        /// <summary>완성된 작업을 창고에 들어가는 만큼 수거한다(동시에 만들면 뒤의 것이 먼저 끝날 수도 있다).</summary>
        public ActionResult CollectCrafted(int objectId)
        {
            PlaceableDef def;
            var facility = FindFacility(objectId, out def);
            if (facility == null) return ActionResult.NotFound;

            long now = Now;
            int collected = 0;
            bool blockedByStorage = false;

            int index = 0;
            while (index < facility.queue.Count)
            {
                var job = facility.queue[index];
                if (job.finishAt > now)
                {
                    index++;
                    continue;
                }

                var recipe = Catalog.GetRecipe(job.recipeId);
                if (recipe == null)
                {
                    facility.queue.RemoveAt(index);
                    continue;
                }

                if (FreeStorage < recipe.outputAmount)
                {
                    blockedByStorage = true;
                    break;
                }

                AddItem(recipe.outputItemId, recipe.outputAmount);
                AddXp(recipe.xp);
                facility.queue.RemoveAt(index);
                collected++;
            }

            if (collected > 0)
            {
                NotifyChanged();
                return ActionResult.Ok;
            }

            return blockedByStorage ? ActionResult.StorageFull : ActionResult.NotReady;
        }
    }
}
