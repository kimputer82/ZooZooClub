using System.Collections.Generic;
using System.Text;
using ZooZooClub.Core;

namespace ZooZooClub.Client
{
    /// <summary>화면에 보여 줄 문구를 만드는 곳.</summary>
    public static class Texts
    {
        public static string Describe(ActionResult result)
        {
            switch (result)
            {
                case ActionResult.Ok: return "완료!";
                case ActionResult.NotFound: return "대상을 찾을 수 없어요.";
                case ActionResult.LockedByLevel: return "레벨이 더 올라야 해요.";
                case ActionResult.NotEnoughCoins: return "코인이 부족해요.";
                case ActionResult.NotEnoughItems: return "재료가 부족해요.";
                case ActionResult.StorageFull: return "창고가 가득 찼어요.";
                case ActionResult.Busy: return "지금은 사용 중이에요.";
                case ActionResult.NotReady: return "아직 준비되지 않았어요.";
                case ActionResult.QueueFull: return "대기열이 가득 찼어요.";
                case ActionResult.InvalidPlacement: return "여기에는 놓을 수 없어요.";
                case ActionResult.MaxReached: return "더 늘릴 수 없어요.";
                case ActionResult.NotEnoughGems: return "보석이 부족해요.";
                case ActionResult.NeedsQuiz: return "문제를 맞혀야 해요.";
                default: return result.ToString();
            }
        }

        /// <summary>등급을 별 개수로 나타낸다. 가장 낮은 등급이 별 하나.</summary>
        public static string GradeStars(GameCatalog catalog, GradeDef grade)
        {
            if (grade == null) return "";
            return new string('★', System.Math.Max(1, catalog.GradeIndex(grade.id) + 1));
        }

        /// <summary>글 속에서 등급 색을 입힐 때 쓰는 태그. 닫을 때는 &lt;/color&gt;.</summary>
        public static string GradeColorTag(GradeDef grade)
        {
            string color = grade != null && !string.IsNullOrEmpty(grade.color) ? grade.color : "#FFFFFF";
            return "<color=" + color + ">";
        }

        public static string Duration(long seconds)
        {
            if (seconds < 0) seconds = 0;
            if (seconds >= 3600)
            {
                return (seconds / 3600) + ":" + ((seconds % 3600) / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
            }
            return (seconds / 60) + ":" + (seconds % 60).ToString("00");
        }

        /// <summary>낱말 뒤에 붙는 "이/가". 마지막 글자에 받침이 있으면 "이", 없으면 "가"(한글이 아니면 "이(가)").</summary>
        public static string SubjectParticle(string word)
        {
            if (string.IsNullOrEmpty(word)) return "이(가)";
            char last = word[word.Length - 1];
            if (last < '\uAC00' || last > '\uD7A3') return "이(가)";
            return (last - 0xAC00) % 28 != 0 ? "이" : "가";
        }

        public static string ItemName(GameCatalog catalog, string itemId)
        {
            var def = catalog.GetItem(itemId);
            return def != null ? def.displayName : itemId;
        }

        /// <summary>"밀 2, 당근 1" 또는 보유량을 함께 보여 주는 "밀 3/2, 당근 0/1".</summary>
        public static string ItemList(Game game, List<ItemAmount> items, bool withOwned)
        {
            var builder = new StringBuilder();
            foreach (var entry in items)
            {
                if (builder.Length > 0) builder.Append(", ");
                builder.Append(ItemName(game.Catalog, entry.itemId)).Append(' ');
                if (withOwned) builder.Append(game.CountOf(entry.itemId)).Append('/');
                builder.Append(entry.amount);
            }
            return builder.ToString();
        }
    }
}
