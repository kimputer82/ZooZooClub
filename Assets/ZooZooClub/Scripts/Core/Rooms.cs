using System;
using System.Collections.Generic;

namespace ZooZooClub.Core
{
    /// <summary>
    /// 선생님이 만든 방의 코드. 학생은 이 코드로 같은 방에 들어와 서로 랭킹을 본다.
    /// 코드는 헷갈리는 글자(0/O, 1/I/L)를 뺀 영문 대문자와 숫자 6자리다.
    /// </summary>
    public static class RoomCode
    {
        public const int Length = 6;
        const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        public static string Generate(Random random)
        {
            var chars = new char[Length];
            for (int i = 0; i < Length; i++) chars[i] = Alphabet[random.Next(Alphabet.Length)];
            return new string(chars);
        }

        /// <summary>학생이 적은 코드를 정리한다(빈칸 제거, 대문자). 코드로 쓸 수 없으면 null.</summary>
        public static string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            var builder = new System.Text.StringBuilder();
            foreach (char c in input)
            {
                if (char.IsWhiteSpace(c) || c == '-') continue;
                char upper = char.ToUpperInvariant(c);
                if (upper == 'O') upper = '0';
                if (upper == 'I' || upper == 'L') upper = '1';
                if (!char.IsLetterOrDigit(upper) || upper > 'Z') return null;
                builder.Append(upper);
            }
            string code = builder.ToString();
            return code.Length >= 4 && code.Length <= 8 ? code : null;
        }
    }

    /// <summary>방 안의 한 사람 순위 정보.</summary>
    public sealed class RankEntry
    {
        public string name = "";
        public int level;
        public int xp;
        public int quizSolved;
        /// <summary>마을 매력(놓여 있는 장식의 매력 합).</summary>
        public int charm;
        public long updatedAt;

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>
            {
                { "name", name }, { "level", level }, { "xp", xp }, { "quizSolved", quizSolved }, { "charm", charm }, { "updatedAt", updatedAt }
            };
        }

        public static RankEntry FromJson(Dictionary<string, object> map)
        {
            if (map == null) return null;
            var entry = new RankEntry
            {
                name = MiniJson.AsString(MiniJson.Get(map, "name")),
                level = (int)MiniJson.AsNumber(MiniJson.Get(map, "level"), 1),
                xp = (int)MiniJson.AsNumber(MiniJson.Get(map, "xp")),
                quizSolved = (int)MiniJson.AsNumber(MiniJson.Get(map, "quizSolved")),
                charm = (int)MiniJson.AsNumber(MiniJson.Get(map, "charm")),
                updatedAt = (long)MiniJson.AsNumber(MiniJson.Get(map, "updatedAt"))
            };
            return entry.name.Length > 0 ? entry : null;
        }
    }

    /// <summary>무엇으로 순위를 매기는지.</summary>
    public enum RankKind
    {
        Level,
        Charm,
        Quiz
    }

    /// <summary>
    /// 방의 순위표. 레벨 순위는 레벨이 높은 순(같으면 경험치), 매력 순위는 매력이 높은 순,
    /// 문제풀이 순위는 맞힌 문제가 많은 순. 같으면 다른 점수를 차례로 보고, 마지막으로 이름순.
    /// </summary>
    public static class RankBoard
    {
        /// <summary>그 순위에서 첫째로 보는 점수.</summary>
        public static long Score(RankEntry e, RankKind kind)
        {
            switch (kind)
            {
                case RankKind.Charm: return e.charm;
                case RankKind.Quiz: return e.quizSolved;
                default: return (long)e.level * 100000000L + e.xp;
            }
        }

        static int Compare(RankEntry a, RankEntry b, RankKind kind)
        {
            int c = Score(b, kind).CompareTo(Score(a, kind));
            if (c == 0 && kind != RankKind.Level) c = Score(b, RankKind.Level).CompareTo(Score(a, RankKind.Level));
            if (c == 0 && kind != RankKind.Quiz) c = b.quizSolved.CompareTo(a.quizSolved);
            if (c == 0 && kind != RankKind.Charm) c = b.charm.CompareTo(a.charm);
            return c;
        }

        public static List<RankEntry> Sort(IEnumerable<RankEntry> entries, RankKind kind)
        {
            var list = new List<RankEntry>();
            foreach (var entry in entries)
            {
                if (entry != null) list.Add(entry);
            }
            list.Sort((a, b) =>
            {
                int c = Compare(a, b, kind);
                if (c == 0) c = string.CompareOrdinal(a.name, b.name);
                return c;
            });
            return list;
        }

        /// <summary>1부터 세는 순위. 그 순위의 점수가 같으면 같은 순위(1, 1, 3).</summary>
        public static int[] Places(List<RankEntry> sorted, RankKind kind)
        {
            var places = new int[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                places[i] = i + 1;
                if (i > 0 && Compare(sorted[i - 1], sorted[i], kind) == 0) places[i] = places[i - 1];
            }
            return places;
        }

        public static List<RankEntry> Sort(IEnumerable<RankEntry> entries)
        {
            var list = new List<RankEntry>();
            foreach (var entry in entries)
            {
                if (entry != null) list.Add(entry);
            }
            list.Sort((a, b) =>
            {
                int c = b.level.CompareTo(a.level);
                if (c == 0) c = b.xp.CompareTo(a.xp);
                if (c == 0) c = b.quizSolved.CompareTo(a.quizSolved);
                if (c == 0) c = string.CompareOrdinal(a.name, b.name);
                return c;
            });
            return list;
        }

        /// <summary>서버에서 받은 {이름키: {name, level, ...}} 사전을 순위표로 바꾼다.</summary>
        public static List<RankEntry> FromPlayersJson(object players)
        {
            var list = new List<RankEntry>();
            var map = MiniJson.AsObject(players);
            if (map == null) return list;
            foreach (var pair in map)
            {
                var entry = RankEntry.FromJson(MiniJson.AsObject(pair.Value));
                if (entry != null) list.Add(entry);
            }
            return Sort(list);
        }

        /// <summary>1부터 세는 순위. 같은 점수면 같은 순위(1, 1, 3).</summary>
        public static int[] Places(List<RankEntry> sorted)
        {
            var places = new int[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                places[i] = i + 1;
                if (i > 0 && Same(sorted[i - 1], sorted[i])) places[i] = places[i - 1];
            }
            return places;
        }

        static bool Same(RankEntry a, RankEntry b)
        {
            return a.level == b.level && a.xp == b.xp && a.quizSolved == b.quizSolved;
        }
    }
}
