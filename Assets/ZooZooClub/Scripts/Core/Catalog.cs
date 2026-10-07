using System;
using System.Collections.Generic;

namespace ZooZooClub.Core
{
    // 게임 데이터 정의. 모든 클래스는 public 필드와 List만 사용해서
    // UnityEngine.JsonUtility로 그대로 읽고 쓸 수 있다.

    [Serializable]
    public class ItemAmount
    {
        public string itemId;
        public int amount;

        public ItemAmount() { }

        public ItemAmount(string itemId, int amount)
        {
            this.itemId = itemId;
            this.amount = amount;
        }
    }

    [Serializable]
    public class ItemDef
    {
        public string id;
        public string displayName;
        public int value;
    }

    [Serializable]
    public class CropDef
    {
        public string id;
        public string displayName;
        public string harvestItemId;
        public int harvestAmount = 2;
        public int growSeconds;
        public int seedCost;
        public int harvestXp;
        public int unlockLevel = 1;
    }

    [Serializable]
    public class RecipeDef
    {
        public string id;
        public string facilityId;
        public string outputItemId;
        public int outputAmount = 1;
        public List<ItemAmount> inputs = new List<ItemAmount>();
        public int craftSeconds;
        public int xp;
        public int unlockLevel = 1;
    }

    public static class PlaceableKind
    {
        public const string Plot = "plot";
        public const string Facility = "facility";
        public const string Decor = "decor";
    }

    [Serializable]
    public class PlaceableDef
    {
        public string id;
        public string displayName;
        public string kind;
        public int width = 1;
        public int height = 1;
        public int cost;
        public int unlockLevel = 1;
        public int queueSlots;
        public int charm;
        public int maxCount;
    }

    /// <summary>마을 주민. 주문을 내는 손님이기도 하고, 시설에 배치하면 생산을 빠르게 해 준다.</summary>
    [Serializable]
    public class ResidentDef
    {
        public string id;
        public string displayName;
        public string species;
        public int unlockLevel = 1;
        public int craftSpeedBonusPercent;
    }

    [Serializable]
    public class LevelDef
    {
        public int xpToNext;
        public int rewardCoins;
        public int rewardGems;
    }

    [Serializable]
    public class StarterObject
    {
        public string defId;
        public int x;
        public int y;
    }

    [Serializable]
    public class BalanceDef
    {
        public int startCoins = 200;
        public int startGems = 10;
        public int startStorage = 50;
        public int storageUpgradeStep = 25;
        public int storageUpgradeBaseCost = 150;
        public int startLandSize = 8;
        public int maxLandSize = 20;
        public int landExpandStep = 2;
        public int landExpandBaseCost = 300;
        public int landExpandFirstLevel = 2;
        public int landExpandLevelStep = 2;
        public int orderSlots = 4;
        public int orderCooldownSeconds = 10;
        public int orderDiscardCooldownSeconds = 60;
        public int orderRewardPercent = 150;
        public int orderXpPercent = 30;
        public int sellBackPercent = 50;
        public List<StarterObject> starterObjects = new List<StarterObject>();
    }

    [Serializable]
    public class GameCatalog
    {
        public List<ItemDef> items = new List<ItemDef>();
        public List<CropDef> crops = new List<CropDef>();
        public List<RecipeDef> recipes = new List<RecipeDef>();
        public List<PlaceableDef> placeables = new List<PlaceableDef>();
        public List<ResidentDef> residents = new List<ResidentDef>();
        public List<LevelDef> levels = new List<LevelDef>();
        public BalanceDef balance = new BalanceDef();

        Dictionary<string, ItemDef> itemsById;
        Dictionary<string, CropDef> cropsById;
        Dictionary<string, RecipeDef> recipesById;
        Dictionary<string, PlaceableDef> placeablesById;
        Dictionary<string, ResidentDef> residentsById;

        public ItemDef GetItem(string id)
        {
            if (itemsById == null) itemsById = Index(items, d => d.id);
            return Find(itemsById, id);
        }

        public CropDef GetCrop(string id)
        {
            if (cropsById == null) cropsById = Index(crops, d => d.id);
            return Find(cropsById, id);
        }

        public RecipeDef GetRecipe(string id)
        {
            if (recipesById == null) recipesById = Index(recipes, d => d.id);
            return Find(recipesById, id);
        }

        public PlaceableDef GetPlaceable(string id)
        {
            if (placeablesById == null) placeablesById = Index(placeables, d => d.id);
            return Find(placeablesById, id);
        }

        public ResidentDef GetResident(string id)
        {
            if (residentsById == null) residentsById = Index(residents, d => d.id);
            return Find(residentsById, id);
        }

        /// <summary>데이터 간 참조가 깨진 곳을 찾아 사람이 읽을 수 있는 오류 목록으로 돌려준다.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (balance == null) errors.Add("balance is missing");

            foreach (var crop in crops)
            {
                if (GetItem(crop.harvestItemId) == null)
                    errors.Add("crop '" + crop.id + "' refers to unknown item '" + crop.harvestItemId + "'");
                if (crop.growSeconds <= 0)
                    errors.Add("crop '" + crop.id + "' needs growSeconds > 0");
            }

            foreach (var recipe in recipes)
            {
                var facility = GetPlaceable(recipe.facilityId);
                if (facility == null || facility.kind != PlaceableKind.Facility)
                    errors.Add("recipe '" + recipe.id + "' refers to unknown facility '" + recipe.facilityId + "'");
                if (GetItem(recipe.outputItemId) == null)
                    errors.Add("recipe '" + recipe.id + "' refers to unknown item '" + recipe.outputItemId + "'");
                foreach (var input in recipe.inputs)
                {
                    if (GetItem(input.itemId) == null)
                        errors.Add("recipe '" + recipe.id + "' refers to unknown input '" + input.itemId + "'");
                }
            }

            foreach (var placeable in placeables)
            {
                if (placeable.width <= 0 || placeable.height <= 0)
                    errors.Add("placeable '" + placeable.id + "' needs a positive size");
                if (placeable.kind == PlaceableKind.Facility && placeable.queueSlots <= 0)
                    errors.Add("facility '" + placeable.id + "' needs queueSlots > 0");
            }

            foreach (var resident in residents)
            {
                if (resident.craftSpeedBonusPercent < 0 || resident.craftSpeedBonusPercent >= 100)
                    errors.Add("resident '" + resident.id + "' needs craftSpeedBonusPercent between 0 and 99");
            }

            if (balance != null)
            {
                foreach (var starter in balance.starterObjects)
                {
                    if (GetPlaceable(starter.defId) == null)
                        errors.Add("starter object refers to unknown placeable '" + starter.defId + "'");
                }
            }

            return errors;
        }

        static Dictionary<string, T> Index<T>(List<T> list, Func<T, string> key) where T : class
        {
            var result = new Dictionary<string, T>();
            if (list == null) return result;
            foreach (var entry in list)
            {
                if (entry == null) continue;
                var id = key(entry);
                if (!string.IsNullOrEmpty(id)) result[id] = entry;
            }
            return result;
        }

        static T Find<T>(Dictionary<string, T> index, string id) where T : class
        {
            if (string.IsNullOrEmpty(id)) return null;
            T value;
            return index.TryGetValue(id, out value) ? value : null;
        }
    }
}
