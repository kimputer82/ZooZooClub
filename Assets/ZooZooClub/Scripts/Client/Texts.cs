using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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

        // "3/4", "1과 1/3", "2와 2/5" 꼴의 분수. 대분수의 "과/와"는 그리지 않고 자연수 뒤에 분수를 붙인다.
        static readonly Regex FractionPattern = new Regex(@"(?:(\d+)\s*[과와]\s*)?(\d+)/(\d+)");

        /// <summary>글에 "3/4" 같은 분수가 들어 있는지.</summary>
        public static bool HasFraction(string text)
        {
            return !string.IsNullOrEmpty(text) && FractionPattern.IsMatch(text);
        }

        /// <summary>
        /// 분수를 교과서처럼 분자·가로줄·분모를 위아래로 쌓은 모양으로 그리는 꾸밈 글(TextMeshPro)로 바꾼다.
        /// 분수가 아닌 부분은 적힌 그대로 보이도록 감싼다. 이 글을 쓰는 칸은 richText를 켜야 한다.
        /// </summary>
        public static string MathRich(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var builder = new StringBuilder();
            int last = 0;
            foreach (Match match in FractionPattern.Matches(text))
            {
                AppendPlain(builder, text.Substring(last, match.Index - last));
                if (match.Groups[1].Success)
                {
                    AppendPlain(builder, match.Groups[1].Value);
                    builder.Append("<space=0.22em>");
                }
                AppendFraction(builder, match.Groups[2].Value, match.Groups[3].Value);
                last = match.Index + match.Length;
            }
            AppendPlain(builder, text.Substring(last));
            return builder.ToString();
        }

        static void AppendPlain(StringBuilder builder, string plain)
        {
            if (plain.Length == 0) return;
            // 꺾쇠 괄호 같은 글자가 꾸밈 명령으로 읽히지 않게 한다.
            builder.Append("<noparse>").Append(plain.Replace("</noparse>", "</noparse >")).Append("</noparse>");
        }

        static string Em(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture) + "em";
        }

        /// <summary>
        /// 분자를 올리고 분모를 내린 뒤 가운데에 가로줄을 긋는다. 숫자마다 같은 폭(mspace)을 주고, 짧은 쪽은 양옆을 띄워 가운데를 맞춘다.
        /// 분자·줄·분모는 같은 자리에 겹쳐 그리므로 그린 폭만큼 뒤로 돌아가며(음수 space) 놓는다.
        /// </summary>
        static void AppendFraction(StringBuilder builder, string numerator, string denominator)
        {
            const float Digit = 0.56f;
            int width = System.Math.Max(numerator.Length, denominator.Length);
            float total = width * Digit + 0.2f;
            float padTop = (total - numerator.Length * Digit) * 0.5f;
            float padBottom = (total - denominator.Length * Digit) * 0.5f;
            // 가로줄 글자는 그림이 자리 폭(Digit)보다 넓어 양옆으로 0.22em쯤 삐져나온다. 그만큼 빼고 센다.
            int bars = System.Math.Max(1, (int)System.Math.Round((total - 0.44f) / Digit));
            float barPad = (total - bars * Digit) * 0.5f;

            builder.Append("<space=0.08em><size=90%><mspace=").Append(Em(Digit)).Append('>');
            builder.Append("<space=").Append(Em(padTop)).Append("><voffset=0.58em>").Append(numerator)
                .Append("</voffset><space=").Append(Em(padTop)).Append('>');
            builder.Append("<space=").Append(Em(-total)).Append('>');
            builder.Append("<space=").Append(Em(barPad)).Append("><voffset=0.12em>").Append(new string('\u2500', bars))
                .Append("</voffset><space=").Append(Em(barPad)).Append('>');
            builder.Append("<space=").Append(Em(-total)).Append('>');
            builder.Append("<space=").Append(Em(padBottom)).Append("><voffset=-0.4em>").Append(denominator)
                .Append("</voffset><space=").Append(Em(padBottom)).Append('>');
            builder.Append("</mspace></size><space=0.08em>");
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
