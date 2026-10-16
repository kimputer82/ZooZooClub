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

        /// <summary>방향(0~3). 뜻은 Rotation 클래스에 적혀 있다. x, y는 어느 방향이든 차지하는 칸의 앞쪽 꼭짓점 칸이다.</summary>
        public int rotation;

        // 밭일 때: 심긴 작물과 심은 시각. 비어 있으면 cropId는 빈 문자열.
        public string cropId = "";
        public long plantedAt;

        // 생산 시설일 때: 진행 중이거나 완료 후 수거를 기다리는 작업.
        public List<CraftJob> queue = new List<CraftJob>();

        // 방어 시설일 때: 살 때 정해진 등급. 비어 있으면 가장 낮은 등급으로 친다.
        public string gradeId = "";
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
        /// <summary>이 마을 주인의 이름(로그인할 때 적은 이름).</summary>
        public string playerName = "";
        /// <summary>마지막으로 들어간 방 코드(선생님이 만든 방). 빈 문자열이면 혼자 하는 중.</summary>
        public string roomCode = "";
        /// <summary>마지막으로 저장한 시각(유닉스 초). 이 컴퓨터의 저장과 서버의 저장 가운데 더 새것을 고를 때 쓴다.</summary>
        public long savedAt;
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

        // 침략: 다음 침략이 오는 시각(0이면 아직 정해지지 않음)과 지금까지 치른 침략 횟수.
        public long nextRaidAt;
        public int raidWave;

        // 문제 풀기: 맞힌 횟수, 최근에 나온 문제(같은 문제가 연달아 나오지 않게), 틀린 뒤 다시 풀 수 있는 시각.
        public int quizSolved;
        public List<string> quizRecent = new List<string>();
        public long quizLockedUntil;

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
            if (quizRecent == null) quizRecent = new List<string>();
            if (playerName == null) playerName = "";
            if (roomCode == null) roomCode = "";
            if (raidWave < 0) raidWave = 0;

            foreach (var obj in objects)
            {
                if (obj.cropId == null) obj.cropId = "";
                if (obj.gradeId == null) obj.gradeId = "";
                if (obj.rotation < 0 || obj.rotation >= Rotation.Count) obj.rotation = 0;
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
