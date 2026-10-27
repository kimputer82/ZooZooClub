using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZooZooClub.Core;

namespace ZooZooClub.Tests
{
    public class OnlineTests
    {
        [Test]
        public void MiniJson_ReadsAndWrites()
        {
            var value = MiniJson.Parse("{\"a\": 1, \"b\": [true, null, \"x\\\"y\"], \"c\": {\"d\": -2.5}, \"e\": \"\\ud55c\\uae00\"}");
            var map = MiniJson.AsObject(value);
            Assert.AreEqual(1.0, MiniJson.AsNumber(MiniJson.Get(map, "a")));
            var list = (List<object>)MiniJson.Get(map, "b");
            Assert.AreEqual(true, list[0]);
            Assert.IsNull(list[1]);
            Assert.AreEqual("x\"y", list[2]);
            Assert.AreEqual(-2.5, MiniJson.AsNumber(MiniJson.Get(MiniJson.AsObject(MiniJson.Get(map, "c")), "d")));
            Assert.AreEqual("한글", MiniJson.AsString(MiniJson.Get(map, "e")));

            string text = MiniJson.Serialize(new Dictionary<string, object> { { "name", "김\n상진" }, { "level", 6 }, { "ok", true } });
            var back = MiniJson.AsObject(MiniJson.Parse(text));
            Assert.AreEqual("김\n상진", MiniJson.AsString(MiniJson.Get(back, "name")));
            Assert.AreEqual(6.0, MiniJson.AsNumber(MiniJson.Get(back, "level")));
        }

        [Test]
        public void MiniJson_RejectsBrokenText()
        {
            Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\": }"));
            Assert.Throws<FormatException>(() => MiniJson.Parse("[1, 2"));
        }

        [Test]
        public void RoomCode_GeneratesAndNormalizes()
        {
            var random = new Random(3);
            string code = RoomCode.Generate(random);
            Assert.AreEqual(6, code.Length);
            Assert.AreEqual(code, RoomCode.Normalize(" " + code.ToLowerInvariant() + " "));
            Assert.AreEqual("AB12CD", RoomCode.Normalize("ab-12 cd"));
            Assert.AreEqual("0101", RoomCode.Normalize("OIol"));
            Assert.IsNull(RoomCode.Normalize("abc"));
            Assert.IsNull(RoomCode.Normalize("한글코드"));
            Assert.IsNull(RoomCode.Normalize(""));
        }

        [Test]
        public void RankBoard_SortsAndTies()
        {
            string json = "{\"a\": {\"name\": \"철수\", \"level\": 3, \"xp\": 10, \"quizSolved\": 2, \"updatedAt\": 1},"
                + "\"b\": {\"name\": \"영희\", \"level\": 5, \"xp\": 0, \"quizSolved\": 9, \"updatedAt\": 1},"
                + "\"c\": {\"name\": \"민수\", \"level\": 3, \"xp\": 10, \"quizSolved\": 2, \"updatedAt\": 1},"
                + "\"d\": {\"level\": 9},"
                + "\"e\": \"garbage\"}";
            var sorted = RankBoard.FromPlayersJson(MiniJson.Parse(json));

            Assert.AreEqual(3, sorted.Count);
            Assert.AreEqual("영희", sorted[0].name);
            Assert.AreEqual("민수", sorted[1].name);
            Assert.AreEqual("철수", sorted[2].name);
            CollectionAssert.AreEqual(new[] { 1, 2, 2 }, RankBoard.Places(sorted));
        }

        [Test]
        public void RankBoard_SortsByCharmAndByQuiz()
        {
            var a = new RankEntry { name = "가", level = 9, quizSolved = 10, charm = 50 };
            var b = new RankEntry { name = "나", level = 3, quizSolved = 90, charm = 200 };
            var c = new RankEntry { name = "다", level = 5, quizSolved = 90, charm = 10 };
            var all = new List<RankEntry> { a, b, c };

            CollectionAssert.AreEqual(new[] { "가", "다", "나" }, RankBoard.Sort(all, RankKind.Level).ConvertAll(e => e.name));
            CollectionAssert.AreEqual(new[] { "나", "가", "다" }, RankBoard.Sort(all, RankKind.Charm).ConvertAll(e => e.name));
            // 문제 수가 같으면 레벨이 높은 쪽이 먼저.
            var byQuiz = RankBoard.Sort(all, RankKind.Quiz);
            CollectionAssert.AreEqual(new[] { "다", "나", "가" }, byQuiz.ConvertAll(e => e.name));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, RankBoard.Places(byQuiz, RankKind.Quiz));
        }

        [Test]
        public void RankEntry_RoundTrips()
        {
            var entry = new RankEntry { name = "김상진", level = 6, xp = 459, quizSolved = 13, charm = 77, updatedAt = 1700000000 };
            var back = RankEntry.FromJson(MiniJson.AsObject(MiniJson.Parse(MiniJson.Serialize(entry.ToJson()))));
            Assert.AreEqual("김상진", back.name);
            Assert.AreEqual(6, back.level);
            Assert.AreEqual(459, back.xp);
            Assert.AreEqual(13, back.quizSolved);
            Assert.AreEqual(77, back.charm);
            Assert.AreEqual(1700000000, back.updatedAt);
        }
    }
}
