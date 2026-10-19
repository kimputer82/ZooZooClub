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
        public const string Defense = "defense";
        /// <summary>바닥 장식. 땅 위에 깔리므로 그 위에 다른 배치물을 놓을 수 있고, 주민과 침략자가 밟고 지나간다.</summary>
        public const string Floor = "floor";

        /// <summary>땅바닥 자리를 쓰는지. 바닥과 밭은 서로 겹칠 수 없다.</summary>
        public static bool UsesGround(PlaceableDef def)
        {
            return def != null && (def.kind == Floor || def.kind == Plot);
        }

        /// <summary>땅 위 자리를 쓰는지(바닥이 아닌 모든 것). 밭은 땅바닥과 땅 위 자리를 모두 쓴다.</summary>
        public static bool UsesAbove(PlaceableDef def)
        {
            return def != null && def.kind != Floor;
        }

        /// <summary>두 배치물이 같은 칸에 함께 있을 수 없는지.</summary>
        public static bool Conflicts(PlaceableDef a, PlaceableDef b)
        {
            return (UsesGround(a) && UsesGround(b)) || (UsesAbove(a) && UsesAbove(b));
        }

        /// <summary>침략자의 길을 막는지. 바닥과 장식은 넘어 다닐 수 있어서 막지 않는다(장식으로 마을을 둘러막지 못하게).</summary>
        public static bool BlocksInvaders(PlaceableDef def)
        {
            return def != null && def.kind != Floor && def.kind != Decor;
        }

        /// <summary>지나가는 길을 막는지. 바닥은 막지 않는다.</summary>
        public static bool BlocksWalking(PlaceableDef def)
        {
            return def != null && def.kind != Floor;
        }
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
        // 생산 시설일 때: 이 레벨부터 생산량 업그레이드를 할 수 있다. 0이면 업그레이드가 없다.
        public int upgradeLevel;
        public int charm;
        public int maxCount;

        // 놓을 수 있는 방향의 수. 1이면 돌릴 수 없고, 2면 앞·뒤, 4면 앞·옆·뒤·옆.
        public int rotations = 1;
        // true면 뒷모습 대신 좌우로 뒤집은 모습과 번갈아 놓는다(울타리처럼 한 칸짜리이고 뒷모습 그림이 없는 것). rotations는 2로 둔다.
        public bool mirror;

        // 방어 시설일 때: 사거리(칸), 한 번 공격의 피해량, 공격 사이 간격(초).
        public float attackRange;
        public int attackDamage;
        public float attackInterval;
    }

    /// <summary>
    /// 배치물의 방향. 0은 앞모습, 2는 뒷모습(180도 돌린 것)이고 차지하는 칸이 같다.
    /// 1과 3은 각각 0과 2를 좌우로 뒤집은 것으로, 가로와 세로 칸 수가 서로 바뀐다.
    /// </summary>
    public static class Rotation
    {
        public const int Count = 4;

        /// <summary>이 방향일 때 가로·세로 칸 수가 서로 바뀌는지.</summary>
        public static bool Swaps(int rotation)
        {
            return (rotation & 1) == 1;
        }

        /// <summary>이 방향일 때 그림을 좌우로 뒤집는지.</summary>
        public static bool Mirrored(int rotation)
        {
            return (rotation & 1) == 1;
        }

        /// <summary>이 방향일 때 뒷모습 그림을 쓰는지.</summary>
        public static bool ShowsBack(int rotation)
        {
            return rotation == 2 || rotation == 3;
        }

        /// <summary>이 배치물에 쓸 수 있는 방향으로 맞춘다. 쓸 수 없는 값이면 0.</summary>
        public static int Normalize(PlaceableDef def, int rotation)
        {
            if (def == null || def.rotations <= 1) return 0;
            rotation = ((rotation % Count) + Count) % Count;
            if (def.rotations >= Count) return rotation;
            // 두 방향뿐일 때: 뒤집기 방식이면 0과 1, 아니면 0과 2만 쓴다.
            if (def.mirror) return rotation == 1 || rotation == 3 ? 1 : 0;
            return Swaps(rotation) ? 0 : rotation;
        }

        /// <summary>한 번 돌렸을 때의 방향.</summary>
        public static int Next(PlaceableDef def, int rotation)
        {
            if (def == null || def.rotations <= 1) return 0;
            rotation = Normalize(def, rotation);
            if (def.rotations >= Count) return (rotation + 1) % Count;
            if (def.mirror) return rotation == 0 ? 1 : 0;
            return rotation == 0 ? 2 : 0;
        }

        public static int Width(PlaceableDef def, int rotation)
        {
            return Swaps(rotation) ? def.height : def.width;
        }

        public static int Height(PlaceableDef def, int rotation)
        {
            return Swaps(rotation) ? def.width : def.height;
        }
    }

    /// <summary>
    /// 방어 시설의 등급. 살 때 weight에 비례하는 확률로 하나가 정해지고, 등급에 따라 능력치가 달라진다.
    /// 목록의 순서가 곧 낮은 등급에서 높은 등급 순서다.
    /// </summary>
    [Serializable]
    public class GradeDef
    {
        public string id;
        public string displayName;
        /// <summary>뽑힐 확률의 비중. 모든 등급의 weight를 더한 값에 대한 비율이 확률이 된다.</summary>
        public int weight = 1;
        /// <summary>공격력, 사거리, 공격 빠르기가 기본값의 몇 %인지.</summary>
        public int damagePercent = 100;
        public int rangePercent = 100;
        public int speedPercent = 100;
        /// <summary>화면에 표시할 색("#RRGGBB").</summary>
        public string color = "#FFFFFF";
        /// <summary>이 등급이 나오면 공사를 마치기 위해 맞혀야 하는 문제 수. 0이면 바로 완성된다.</summary>
        public int buildQuizzes;
    }

    /// <summary>주기적으로 쳐들어와 코인을 훔쳐 가는 침략자.</summary>
    [Serializable]
    public class InvaderDef
    {
        public string id;
        public string displayName;
        public int hp;
        /// <summary>걷는 빠르기(초당 칸 수).</summary>
        public float speed = 1f;
        /// <summary>마을 한가운데에 닿으면 훔쳐 가는 코인.</summary>
        public int stealCoins;
        /// <summary>물리치면 받는 코인과 경험치.</summary>
        public int bounty;
        public int xp;
        /// <summary>마을이 이 레벨이 된 뒤부터 나타난다.</summary>
        public int firstLevel = 1;
    }

    /// <summary>문제 하나. answer는 choices 가운데 정답이 몇 번째인지(1부터 센다).</summary>
    [Serializable]
    public class QuizDef
    {
        public string id;
        public string question;
        public List<string> choices = new List<string>();
        public int answer = 1;
        /// <summary>맞히면 받는 코인. 0이면 문제 은행의 기본값을 쓴다.</summary>
        public int reward;
    }

    /// <summary>문제 은행. Resources/quiz.json 에서 읽는다.</summary>
    [Serializable]
    public class QuizBank
    {
        public int rewardCoins = 30;
        /// <summary>틀린 뒤 다음 문제를 받을 때까지 기다리는 시간(초).</summary>
        public int wrongCooldownSeconds = 5;
        public List<QuizDef> questions = new List<QuizDef>();
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

        // 침략: 이 레벨부터 raidIntervalSeconds마다 침략자가 온다. 오기 raidWarningSeconds 전부터 경고한다.
        public int raidUnlockLevel = 2;
        public int raidIntervalSeconds = 240;
        public int raidWarningSeconds = 30;
        // 침략의 세기는 몇 번째 침략인지가 아니라 마을의 레벨을 따른다(아무리 오래 해도 감당 못 할 만큼 세지지 않는다).
        // 침략자 수는 raidBaseCount에서 시작해 침략이 열린 레벨보다 raidLevelsPerExtra레벨 오를 때마다 하나씩 늘고 raidMaxCount를 넘지 않는다.
        public int raidBaseCount = 2;
        public int raidLevelsPerExtra = 2;
        public int raidMaxCount = 8;
        // 레벨이 하나 오를 때마다 침략자 체력이 이만큼(%)씩 늘고 raidHpMaxPercent를 넘지 않는다.
        public int raidHpGrowthPercent = 10;
        public int raidHpMaxPercent = 200;
        // 침략 한 번에 잃을 수 있는 코인은 침략이 시작될 때 가진 코인의 이 비율(%)까지다(나머지는 금고가 지킨다).
        public int raidMaxStealPercent = 30;
        public float raidSpawnGapSeconds = 1.2f;
        // 마을 한가운데에 닿은 침략자가 코인을 훔치는 데 걸리는 시간. 그 안에 물리치면 코인을 지킨다.
        public float raidStealSeconds = 3f;
        public List<StarterObject> starterObjects = new List<StarterObject>();
        // 보석으로 생산을 바로 끝낼 때, 남은 시간 이만큼(초)마다 보석 1개. 남은 시간이 짧아도 최소 1개.
        public int gemFinishSeconds = 120;
        // 문제를 맞혔을 때 보석 1개를 받을 확률(%).
        public int quizGemChancePercent = 3;
        // 밭에서 수확할 때 harvestBagChancePercent% 확률로 보따리(bagItemId) 하나를 함께 얻는다.
        // 창고에서 보따리를 열면 bagRewards 가운데 하나가 weight에 비례한 확률로 나온다.
        public string bagItemId = "";
        public int harvestBagChancePercent;
        public List<BagRewardDef> bagRewards = new List<BagRewardDef>();
        // 생산 시설 업그레이드: 문제를 upgradeQuizzes개 맞히면 대기열이 upgradeQueueSlots칸이 되고,
        // upgradeParallel개가 동시에 만들어진다.
        public int upgradeQuizzes = 10;
        public int upgradeQueueSlots = 6;
        public int upgradeParallel = 2;
        // 수확하려고 누를 때 이 확률(%)로 문제가 나오고, 맞혀야 수확할 수 있다.
        public int harvestQuizChancePercent;
        // 문제 콤보: 문제 풀기 버튼으로 연속으로 맞힌 수가 combo 이상이면 생산 시간이 speedPercent% 줄어든다.
        // 수확·공사·업그레이드 때문에 푼 문제는 콤보를 늘리지도 끊지도 않는다.
        // 틀리거나, 마지막으로 맞힌 뒤 comboTimeoutSeconds 동안 맞히지 않으면 콤보가 끊긴다.
        public int comboTimeoutSeconds = 600;
        public List<ComboTierDef> comboTiers = new List<ComboTierDef>();
        // 마을 매력이 charm 이상이면 모든 시설의 생산 시간이 speedPercent% 줄어든다. 가장 높은 단계 하나만 적용된다.
        public List<CharmTierDef> charmTiers = new List<CharmTierDef>();
    }

    /// <summary>보따리에서 나올 수 있는 물건. weight에 비례하는 확률로 하나가 나온다.</summary>
    [Serializable]
    public class BagRewardDef
    {
        public string itemId;
        public int weight = 1;
    }

    /// <summary>문제 콤보 단계. 연속 정답이 combo 이상이면 모든 생산 시설의 생산 시간이 speedPercent% 줄어든다.</summary>
    [Serializable]
    public class ComboTierDef
    {
        public int combo;
        public int speedPercent;
    }

    /// <summary>마을 매력 단계. 매력이 charm 이상이면 모든 생산 시설의 생산 시간이 speedPercent% 줄어든다.</summary>
    [Serializable]
    public class CharmTierDef
    {
        public int charm;
        public int speedPercent;
    }

    [Serializable]
    public class GameCatalog
    {
        public List<ItemDef> items = new List<ItemDef>();
        public List<CropDef> crops = new List<CropDef>();
        public List<RecipeDef> recipes = new List<RecipeDef>();
        public List<PlaceableDef> placeables = new List<PlaceableDef>();
        public List<ResidentDef> residents = new List<ResidentDef>();
        public List<InvaderDef> invaders = new List<InvaderDef>();
        public List<GradeDef> grades = new List<GradeDef>();
        public List<LevelDef> levels = new List<LevelDef>();
        public BalanceDef balance = new BalanceDef();

        /// <summary>문제 은행. catalog.json이 아니라 quiz.json에서 따로 읽어 넣는다.</summary>
        public QuizBank quiz = new QuizBank();

        Dictionary<string, ItemDef> itemsById;
        Dictionary<string, CropDef> cropsById;
        Dictionary<string, RecipeDef> recipesById;
        Dictionary<string, PlaceableDef> placeablesById;
        Dictionary<string, ResidentDef> residentsById;
        Dictionary<string, InvaderDef> invadersById;
        Dictionary<string, GradeDef> gradesById;
        Dictionary<string, QuizDef> quizById;

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

        public InvaderDef GetInvader(string id)
        {
            if (invadersById == null) invadersById = Index(invaders, d => d.id);
            return Find(invadersById, id);
        }

        public GradeDef GetGrade(string id)
        {
            if (gradesById == null) gradesById = Index(grades, d => d.id);
            return Find(gradesById, id);
        }

        /// <summary>등급이 몇 번째인지(가장 낮은 등급이 0). 없으면 -1.</summary>
        public int GradeIndex(string id)
        {
            for (int i = 0; i < grades.Count; i++)
            {
                if (grades[i] != null && grades[i].id == id) return i;
            }
            return -1;
        }

        public QuizDef GetQuiz(string id)
        {
            if (quizById == null) quizById = Index(quiz != null ? quiz.questions : null, d => d.id);
            return Find(quizById, id);
        }

        /// <summary>문제 은행을 바꿔 끼운다. id가 비어 있는 문제에는 순서대로 id를 붙인다.</summary>
        public void SetQuiz(QuizBank bank)
        {
            quiz = bank ?? new QuizBank();
            if (quiz.questions == null) quiz.questions = new List<QuizDef>();
            for (int i = 0; i < quiz.questions.Count; i++)
            {
                var question = quiz.questions[i];
                if (question != null && string.IsNullOrEmpty(question.id)) question.id = "q" + (i + 1);
            }
            quizById = null;
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

            if (balance != null && !string.IsNullOrEmpty(balance.bagItemId))
            {
                if (GetItem(balance.bagItemId) == null)
                    errors.Add("bagItemId refers to unknown item '" + balance.bagItemId + "'");
                foreach (var reward in balance.bagRewards)
                {
                    if (reward == null || GetItem(reward.itemId) == null)
                        errors.Add("bagRewards refers to unknown item '" + (reward != null ? reward.itemId : "null") + "'");
                }
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
                if (placeable.kind == PlaceableKind.Defense
                    && (placeable.attackRange <= 0f || placeable.attackDamage <= 0 || placeable.attackInterval <= 0f))
                    errors.Add("defense '" + placeable.id + "' needs attackRange, attackDamage and attackInterval > 0");
            }

            foreach (var grade in grades)
            {
                if (grade.weight < 0 || grade.damagePercent <= 0 || grade.rangePercent <= 0 || grade.speedPercent <= 0)
                    errors.Add("grade '" + grade.id + "' needs weight >= 0 and positive percents");
            }

            foreach (var invader in invaders)
            {
                if (invader.hp <= 0 || invader.speed <= 0f)
                    errors.Add("invader '" + invader.id + "' needs hp and speed > 0");
            }

            if (quiz != null && quiz.questions != null)
            {
                foreach (var question in quiz.questions)
                {
                    if (question == null) continue;
                    int count = question.choices != null ? question.choices.Count : 0;
                    if (count < 2)
                        errors.Add("quiz '" + question.id + "' needs at least 2 choices");
                    else if (question.answer < 1 || question.answer > count)
                        errors.Add("quiz '" + question.id + "' has answer " + question.answer + " but only " + count + " choices");
                }
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
