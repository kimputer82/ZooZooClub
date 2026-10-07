using System;
using System.Collections.Generic;

namespace ZooZooClub.Core
{
    // 저장되는 플레이어 상태. 시간은 모두 유닉스 초 단위의 절대 시각으로 기록해서
    // 게임을 꺼 둔 동안에도 작물과 생산이 진행된 것으로 계산된다.

    [Serializable]
    public class CraftJob
    {
        public string recipeId;
        public long startAt;
        public long finishAt;
    }

    [Serializable]
    public class PlacedObject
    {
        public int id;
        public string defId;
        public int x;
        public int y;

        // 밭일 때: 심긴 작물과 심은 시각. 비어 있으면 cropId는 빈 문자열.
        public string cropId = "";
        public long plantedAt;

        // 생산 시설일 때: 진행 중이거나 완료 후 수거를 기다리는 작업.
        public List<CraftJob> queue = new List<CraftJob>();
    }

    [Serializable]
    public class ResidentAssignment
    {
        public string residentId;
        public int objectId;
    }

    [Serializable]
    public class Order
    {
        public int id;
        public string customerId = "";
        public List<ItemAmount> items = new List<ItemAmount>();
        public int rewardCoins;
        public int rewardXp;
        public long availableAt;
    }

    [Serializable]
    public class GameState
    {
        public int version = 1;
        public long createdAt;
        public long coins;
        public int gems;
        public int level = 1;
        public int xp;
        public int storageCapacity;
        public int landWidth;
        public int landHeight;
        public int nextObjectId = 1;
        public int nextOrderId = 1;
        public int rngSeed = 1;
        public List<ItemAmount> inventory = new List<ItemAmount>();
        public List<PlacedObject> objects = new List<PlacedObject>();
        public List<Order> orders = new List<Order>();
        public List<ResidentAssignment> assignments = new List<ResidentAssignment>();

        public static GameState CreateNew(GameCatalog catalog, long now)
        {
            if (catalog == null) throw new ArgumentNullException("catalog");
            var balance = catalog.balance;
            var state = new GameState
            {
                createdAt = now,
                coins = balance.startCoins,
                gems = balance.startGems,
                storageCapacity = balance.startStorage,
                landWidth = balance.startLandSize,
                landHeight = balance.startLandSize,
                rngSeed = unchecked((int)now) | 1
            };

            foreach (var starter in balance.starterObjects)
            {
                state.objects.Add(new PlacedObject
                {
                    id = state.nextObjectId++,
                    defId = starter.defId,
                    x = starter.x,
                    y = starter.y
                });
            }

            return state;
        }

        /// <summary>오래된 저장 파일이나 손으로 고친 파일에서 빠진 값을 채운다.</summary>
        public void Normalize()
        {
            if (level < 1) level = 1;
            if (nextObjectId < 1) nextObjectId = 1;
            if (nextOrderId < 1) nextOrderId = 1;
            if (inventory == null) inventory = new List<ItemAmount>();
            if (objects == null) objects = new List<PlacedObject>();
            if (orders == null) orders = new List<Order>();
            if (assignments == null) assignments = new List<ResidentAssignment>();

            foreach (var obj in objects)
            {
                if (obj.cropId == null) obj.cropId = "";
                if (obj.queue == null) obj.queue = new List<CraftJob>();
                if (obj.id >= nextObjectId) nextObjectId = obj.id + 1;
            }

            foreach (var order in orders)
            {
                if (order.items == null) order.items = new List<ItemAmount>();
                if (order.id >= nextOrderId) nextOrderId = order.id + 1;
            }

            // 사라진 시설에 남아 있는 배치 기록을 지운다.
            for (int i = assignments.Count - 1; i >= 0; i--)
            {
                var assignment = assignments[i];
                bool found = false;
                foreach (var obj in objects)
                {
                    if (obj.id == assignment.objectId)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found || string.IsNullOrEmpty(assignment.residentId)) assignments.RemoveAt(i);
            }
        }
    }
}
