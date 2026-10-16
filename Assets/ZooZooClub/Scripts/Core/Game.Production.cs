using System;

namespace ZooZooClub.Core
{
    // 생산: 시설에 레시피를 줄 세우면 앞에서부터 차례로 만들어진다.
    // 재료는 줄을 세울 때 바로 소비되고, 완성품은 수거할 때 창고로 들어간다.
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

        public ActionResult StartCraft(int objectId, string recipeId)
        {
            PlaceableDef def;
            var facility = FindFacility(objectId, out def);
            if (facility == null) return ActionResult.NotFound;

            var recipe = Catalog.GetRecipe(recipeId);
            if (recipe == null || recipe.facilityId != def.id) return ActionResult.NotFound;
            if (State.level < recipe.unlockLevel) return ActionResult.LockedByLevel;
            if (facility.queue.Count >= def.queueSlots) return ActionResult.QueueFull;
            if (!HasItems(recipe.inputs)) return ActionResult.NotEnoughItems;

            RemoveItems(recipe.inputs);

            long start = Now;
            if (facility.queue.Count > 0)
            {
                start = Math.Max(start, facility.queue[facility.queue.Count - 1].finishAt);
            }

            facility.queue.Add(new CraftJob
            {
                recipeId = recipe.id,
                startAt = start,
                finishAt = start + CraftSecondsFor(objectId, recipe)
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

        /// <summary>지금 만들고 있는(아직 덜 된 것 중 맨 앞) 작업. 없으면 null.</summary>
        CraftJob CurrentJob(PlacedObject facility)
        {
            long now = Now;
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
            long saved = job.finishAt - now;
            int index = facility.queue.IndexOf(job);
            job.finishAt = now;
            for (int i = index + 1; i < facility.queue.Count; i++)
            {
                var next = facility.queue[i];
                next.startAt = Math.Max(now, next.startAt - saved);
                next.finishAt = Math.Max(next.startAt, next.finishAt - saved);
            }

            NotifyChanged();
            return ActionResult.Ok;
        }

        /// <summary>완성된 작업을 창고에 들어가는 만큼 앞에서부터 수거한다.</summary>
        public ActionResult CollectCrafted(int objectId)
        {
            PlaceableDef def;
            var facility = FindFacility(objectId, out def);
            if (facility == null) return ActionResult.NotFound;

            long now = Now;
            int collected = 0;
            bool blockedByStorage = false;

            while (facility.queue.Count > 0)
            {
                var job = facility.queue[0];
                if (job.finishAt > now) break;

                var recipe = Catalog.GetRecipe(job.recipeId);
                if (recipe == null)
                {
                    facility.queue.RemoveAt(0);
                    continue;
                }

                if (FreeStorage < recipe.outputAmount)
                {
                    blockedByStorage = true;
                    break;
                }

                AddItem(recipe.outputItemId, recipe.outputAmount);
                AddXp(recipe.xp);
                facility.queue.RemoveAt(0);
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
