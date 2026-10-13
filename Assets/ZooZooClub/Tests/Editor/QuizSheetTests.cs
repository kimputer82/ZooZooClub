using System.Collections.Generic;
using NUnit.Framework;
using ZooZooClub.Core;

namespace ZooZooClub.Tests
{
    public class QuizSheetTests
    {
        static readonly string[] Header = { "문제", "보기1", "보기2", "보기3", "보기4", "정답", "보상" };

        static QuizSheetResult Read(params string[][] body)
        {
            var rows = new List<string[]> { Header };
            rows.AddRange(body);
            return QuizSheet.Read(rows);
        }

        [Test]
        public void Read_TurnsRowsIntoQuestions()
        {
            var result = Read(
                new[] { "7 + 8 은?", "13", "14", "15", "16", "3", "" },
                new[] { "일주일은 며칠?", "5일", "6일", "7일", "8일", "3", "60" });

            Assert.AreEqual(0, result.problems.Count);
            Assert.AreEqual(0, result.notices.Count);
            Assert.AreEqual(2, result.questions.Count);

            var first = result.questions[0];
            Assert.AreEqual("7 + 8 은?", first.question);
            CollectionAssert.AreEqual(new[] { "13", "14", "15", "16" }, first.choices);
            Assert.AreEqual(3, first.answer);
            Assert.AreEqual(0, first.reward);

            Assert.AreEqual(60, result.questions[1].reward);
        }

        [Test]
        public void Read_FindsColumnsByTitleWhateverTheOrder()
        {
            var rows = new List<string[]>
            {
                new[] { "단원", "정답", "보기 2", "보기 1", " 문제 ", "메모" },
                new[] { "1단원", "2", "아니다", "맞다", "지구는 둥글다", "쉬움" }
            };

            var result = QuizSheet.Read(rows);

            Assert.AreEqual(0, result.problems.Count);
            Assert.AreEqual(1, result.questions.Count);
            Assert.AreEqual("지구는 둥글다", result.questions[0].question);
            CollectionAssert.AreEqual(new[] { "맞다", "아니다" }, result.questions[0].choices);
            Assert.AreEqual(2, result.questions[0].answer);
        }

        [Test]
        public void Read_MissingTitlesStopEverything()
        {
            var rows = new List<string[]>
            {
                new[] { "질문 내용", "1번", "2번", "답안" },
                new[] { "7 + 8 은?", "14", "15", "2" }
            };

            var result = QuizSheet.Read(rows);

            Assert.AreEqual(0, result.questions.Count);
            Assert.AreEqual(3, result.problems.Count);
        }

        [Test]
        public void Read_SkipsBlankChoicesAndKeepsTheAnswerPointingAtTheSameChoice()
        {
            // 보기2를 비워 두었으므로 엑셀의 3번 보기가 게임에서는 두 번째 보기가 된다.
            var result = Read(new[] { "물은 몇 도에서 끓을까?", "50도", "", "100도", "", "3", "" });

            Assert.AreEqual(0, result.problems.Count);
            CollectionAssert.AreEqual(new[] { "50도", "100도" }, result.questions[0].choices);
            Assert.AreEqual(2, result.questions[0].answer);
        }

        [Test]
        public void Read_AnswerCanBeWrittenInSeveralWays()
        {
            var result = Read(
                new[] { "가", "사과", "배", "감", "귤", "2번", "" },
                new[] { "나", "사과", "배", "감", "귤", "③", "" },
                new[] { "다", "사과", "배", "감", "귤", "4.0", "" },
                new[] { "라", "사과", "배", "감", "귤", "감", "" },
                new[] { "마", "13", "14", "15", "16", "15", "" });

            Assert.AreEqual(0, result.problems.Count);
            Assert.AreEqual(2, result.questions[0].answer);
            Assert.AreEqual(3, result.questions[1].answer);
            Assert.AreEqual(4, result.questions[2].answer);
            Assert.AreEqual(3, result.questions[3].answer);
            Assert.AreEqual(3, result.questions[4].answer);
        }

        [Test]
        public void Read_NumberAnswerMeansThePositionAndSaysSoWhenItCouldBeMisread()
        {
            // 보기가 4, 3, 2, 1이고 정답에 3을 적으면 3번 보기(값 2)로 읽는다.
            var result = Read(new[] { "가장 작은 짝수는?", "4", "3", "2", "1", "3", "" });

            Assert.AreEqual(0, result.problems.Count);
            Assert.AreEqual(3, result.questions[0].answer);
            Assert.AreEqual(1, result.notices.Count);
            StringAssert.Contains("2행", result.notices[0]);
        }

        [Test]
        public void Read_ReportsBadRowsWithTheirLineAndKeepsTheGoodOnes()
        {
            var result = Read(
                new[] { "", "1", "2", "3", "4", "1", "" },
                new[] { "보기가 하나뿐", "1", "", "", "", "1", "" },
                new[] { "정답이 없음", "1", "2", "3", "4", "", "" },
                new[] { "정답이 빈 보기를 가리킴", "1", "2", "", "", "4", "" },
                new[] { "정답이 엉뚱함", "사과", "배", "감", "귤", "포도", "" },
                new[] { "멀쩡한 문제", "사과", "배", "감", "귤", "1", "" });

            Assert.AreEqual(1, result.questions.Count);
            Assert.AreEqual("멀쩡한 문제", result.questions[0].question);
            Assert.AreEqual(5, result.problems.Count);
            StringAssert.StartsWith("2행", result.problems[0]);
            StringAssert.StartsWith("3행", result.problems[1]);
            StringAssert.StartsWith("4행", result.problems[2]);
            StringAssert.StartsWith("5행", result.problems[3]);
            StringAssert.StartsWith("6행", result.problems[4]);
        }

        [Test]
        public void Read_IgnoresEmptyRowsAndShortRows()
        {
            var result = Read(
                new string[0],
                new[] { "", " ", "" },
                null,
                new[] { "OX: 고래는 물고기다", "O", "X", "", "", "2" });

            Assert.AreEqual(0, result.problems.Count);
            Assert.AreEqual(1, result.questions.Count);
            Assert.AreEqual(0, result.questions[0].reward);
        }

        [Test]
        public void Read_NotesOddRewardsAndRepeatedQuestions()
        {
            var result = Read(
                new[] { "같은 문제", "사과", "배", "", "", "1", "많이" },
                new[] { "같은 문제", "사과", "배", "", "", "2", "45.0" });

            Assert.AreEqual(0, result.problems.Count);
            Assert.AreEqual(2, result.questions.Count);
            Assert.AreEqual(0, result.questions[0].reward);
            Assert.AreEqual(45, result.questions[1].reward);
            Assert.AreEqual(2, result.notices.Count);
        }

        [Test]
        public void Read_QuestionsPassTheCatalogCheck()
        {
            var result = Read(
                new[] { "7 + 8 은?", "13", "14", "15", "16", "3", "" },
                new[] { "OX: 고래는 물고기다", "O", "X", "", "", "2", "" });

            var catalog = new GameCatalog();
            var bank = new QuizBank();
            bank.questions.AddRange(result.questions);
            catalog.SetQuiz(bank);

            Assert.AreEqual("q1", catalog.quiz.questions[0].id);
            Assert.IsNotNull(catalog.GetQuiz("q2"));
            foreach (string error in catalog.Validate())
            {
                StringAssert.DoesNotContain("quiz", error);
            }
        }
    }
}
