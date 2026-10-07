using System;
using System.Collections.Generic;

namespace ZooZooClub.Core
{
    public interface IClock
    {
        /// <summary>현재 시각(유닉스 초).</summary>
        long Now { get; }
    }

    public sealed class SystemClock : IClock
    {
        public long Now
        {
            get { return DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }
        }
    }

    public enum ActionResult
    {
        Ok,
        NotFound,
        LockedByLevel,
        NotEnoughCoins,
        NotEnoughItems,
        StorageFull,
        Busy,
        NotReady,
        QueueFull,
        InvalidPlacement,
        MaxReached
    }

    /// <summary>
    /// 게임 규칙 전체의 진입점. 화면 쪽 코드는 이 클래스의 메서드만 호출하고,
    /// 결과로 받은 ActionResult와 Changed 이벤트로 화면을 갱신한다.
    /// </summary>
    public sealed partial class Game
    {
        readonly IClock clock;

        public GameCatalog Catalog { get; private set; }
        public GameState State { get; private set; }

        /// <summary>상태가 바뀔 때마다 호출된다.</summary>
        public event Action Changed;

        /// <summary>레벨이 오를 때 새 레벨과 함께 호출된다.</summary>
        public event Action<int> LeveledUp;

        public Game(GameCatalog catalog, GameState state, IClock clock)
        {
            if (catalog == null) throw new ArgumentNullException("catalog");
            if (state == null) throw new ArgumentNullException("state");
            if (clock == null) throw new ArgumentNullException("clock");

            Catalog = catalog;
            State = state;
            this.clock = clock;

            State.Normalize();
            RefillOrders();
        }

        public long Now
        {
            get { return clock.Now; }
        }

        void NotifyChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }

        // ---------- 창고 ----------

        public int CountOf(string itemId)
        {
            foreach (var entry in State.inventory)
            {
                if (entry.itemId == itemId) return entry.amount;
            }
            return 0;
        }

        public int StoredTotal
        {
            get
            {
                int total = 0;
                foreach (var entry in State.inventory) total += entry.amount;
                return total;
            }
        }

        public int FreeStorage
        {
            get { return Math.Max(0, State.storageCapacity - StoredTotal); }
        }

        public bool HasItems(List<ItemAmount> items)
        {
            foreach (var needed in items)
            {
                if (CountOf(needed.itemId) < needed.amount) return false;
            }
            return true;
        }

        void AddItem(string itemId, int amount)
        {
            if (amount <= 0) return;
            foreach (var entry in State.inventory)
            {
                if (entry.itemId == itemId)
                {
                    entry.amount += amount;
                    return;
                }
            }
            State.inventory.Add(new ItemAmount(itemId, amount));
        }

        void RemoveItem(string itemId, int amount)
        {
            for (int i = 0; i < State.inventory.Count; i++)
            {
                var entry = State.inventory[i];
                if (entry.itemId != itemId) continue;
                entry.amount -= amount;
                if (entry.amount <= 0) State.inventory.RemoveAt(i);
                return;
            }
        }

        void RemoveItems(List<ItemAmount> items)
        {
            foreach (var entry in items) RemoveItem(entry.itemId, entry.amount);
        }

        public ActionResult SellItem(string itemId, int amount)
        {
            var def = Catalog.GetItem(itemId);
            if (def == null || amount <= 0) return ActionResult.NotFound;
            if (CountOf(itemId) < amount) return ActionResult.NotEnoughItems;

            RemoveItem(itemId, amount);
            State.coins += (long)def.value * amount;
            NotifyChanged();
            return ActionResult.Ok;
        }

        public int StorageUpgradeCost
        {
            get
            {
                var balance = Catalog.balance;
                int step = Math.Max(1, balance.storageUpgradeStep);
                int upgrades = Math.Max(0, (State.storageCapacity - balance.startStorage) / step);
                return balance.storageUpgradeBaseCost * (upgrades + 1);
            }
        }

        public ActionResult UpgradeStorage()
        {
            int cost = StorageUpgradeCost;
            if (State.coins < cost) return ActionResult.NotEnoughCoins;

            State.coins -= cost;
            State.storageCapacity += Math.Max(1, Catalog.balance.storageUpgradeStep);
            NotifyChanged();
            return ActionResult.Ok;
        }

        // ---------- 레벨 ----------

        public int MaxLevel
        {
            get { return Catalog.levels.Count + 1; }
        }

        /// <summary>다음 레벨까지 필요한 경험치. 최고 레벨이면 0.</summary>
        public int XpToNextLevel
        {
            get
            {
                if (State.level >= MaxLevel) return 0;
                return Catalog.levels[State.level - 1].xpToNext;
            }
        }

        void AddXp(int amount)
        {
            if (amount <= 0) return;
            State.xp += amount;

            while (State.level < MaxLevel)
            {
                var def = Catalog.levels[State.level - 1];
                if (State.xp < def.xpToNext) break;

                State.xp -= def.xpToNext;
                State.level++;
                State.coins += def.rewardCoins;
                State.gems += def.rewardGems;

                var handler = LeveledUp;
                if (handler != null) handler(State.level);
            }
        }

        // ---------- 공용 ----------

        public PlacedObject FindObject(int objectId)
        {
            foreach (var obj in State.objects)
            {
                if (obj.id == objectId) return obj;
            }
            return null;
        }

        int NextRandom(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            unchecked
            {
                State.rngSeed = State.rngSeed * 1103515245 + 12345;
            }
            return ((State.rngSeed >> 16) & 0x7fff) % maxExclusive;
        }
    }
}
