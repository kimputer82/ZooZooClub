using System;
using System.Collections.Generic;

namespace ZooZooClub.Core
{
    // 주문: 동물 주민이 원하는 물건을 납품하면 코인과 경험치를 받는다.
    // 주문 칸 수는 고정이고, 납품하거나 버린 칸에는 잠시 뒤 새 주문이 들어온다.
    public sealed partial class Game
    {
        public Order FindOrder(int orderId)
        {
            foreach (var order in State.orders)
            {
                if (order.id == orderId) return order;
            }
            return null;
        }

        public bool IsOrderAvailable(Order order)
        {
            return order != null && order.availableAt <= Now;
        }

        public ActionResult DeliverOrder(int orderId)
        {
            var order = FindOrder(orderId);
            if (order == null) return ActionResult.NotFound;
            if (!IsOrderAvailable(order)) return ActionResult.NotReady;
            if (!HasItems(order.items)) return ActionResult.NotEnoughItems;

            RemoveItems(order.items);
            State.coins += order.rewardCoins;
            int xp = order.rewardXp;

            ReplaceOrder(order, Catalog.balance.orderCooldownSeconds);
            AddXp(xp);
            NotifyChanged();
            return ActionResult.Ok;
        }

        public ActionResult DiscardOrder(int orderId)
        {
            var order = FindOrder(orderId);
            if (order == null) return ActionResult.NotFound;
            if (!IsOrderAvailable(order)) return ActionResult.NotReady;

            ReplaceOrder(order, Catalog.balance.orderDiscardCooldownSeconds);
            NotifyChanged();
            return ActionResult.Ok;
        }

        /// <summary>빈 주문 칸을 채운다. 게임 시작 시 한 번 호출된다.</summary>
        public void RefillOrders()
        {
            int slots = Catalog.balance.orderSlots;
            while (State.orders.Count < slots)
            {
                var order = GenerateOrder(Now);
                if (order == null) break;
                State.orders.Add(order);
            }
        }

        void ReplaceOrder(Order old, int cooldownSeconds)
        {
            int index = State.orders.IndexOf(old);
            var next = GenerateOrder(Now + Math.Max(0, cooldownSeconds));

            if (next == null)
            {
                if (index >= 0) State.orders.RemoveAt(index);
                return;
            }

            if (index >= 0) State.orders[index] = next;
            else State.orders.Add(next);
        }

        /// <summary>현재 레벨에서 얻을 수 있는 물건만으로 주문을 만든다.</summary>
        Order GenerateOrder(long availableAt)
        {
            var pool = new List<string>();

            foreach (var crop in Catalog.crops)
            {
                if (crop.unlockLevel > State.level) continue;
                if (!pool.Contains(crop.harvestItemId)) pool.Add(crop.harvestItemId);
            }

            foreach (var recipe in Catalog.recipes)
            {
                if (recipe.unlockLevel > State.level) continue;
                var facility = Catalog.GetPlaceable(recipe.facilityId);
                if (facility == null || facility.unlockLevel > State.level) continue;
                if (!pool.Contains(recipe.outputItemId)) pool.Add(recipe.outputItemId);
            }

            if (pool.Count == 0) return null;

            var balance = Catalog.balance;
            var order = new Order
            {
                id = State.nextOrderId++,
                availableAt = availableAt
            };

            // 이미 마을에 온 주민만 주문을 낸다.
            var customers = new List<ResidentDef>();
            foreach (var resident in Catalog.residents)
            {
                if (resident.unlockLevel <= State.level) customers.Add(resident);
            }
            if (customers.Count > 0)
            {
                order.customerId = customers[NextRandom(customers.Count)].id;
            }

            // 레벨이 오를수록 한 주문에 섞이는 물건 종류가 늘어난다(최대 3종).
            int maxKinds = Math.Min(3, 1 + State.level / 2);
            int kinds = Math.Min(pool.Count, 1 + NextRandom(maxKinds));
            long total = 0;

            for (int i = 0; i < kinds; i++)
            {
                int pick = NextRandom(pool.Count);
                string itemId = pool[pick];
                pool.RemoveAt(pick);

                int amount = 1 + NextRandom(3);
                order.items.Add(new ItemAmount(itemId, amount));

                var def = Catalog.GetItem(itemId);
                if (def != null) total += (long)def.value * amount;
            }

            order.rewardCoins = (int)Math.Max(1L, total * balance.orderRewardPercent / 100);
            order.rewardXp = (int)Math.Max(1L, total * balance.orderXpPercent / 100);
            return order;
        }
    }
}
