using NUnit.Framework;
using ZooZooClub.Core;

namespace ZooZooClub.Tests
{
    public class PlayerNameTests
    {
        static string Clean(string input)
        {
            string error;
            return PlayerName.Clean(input, out error);
        }

        static string ErrorOf(string input)
        {
            string error;
            Assert.IsNull(PlayerName.Clean(input, out error));
            return error;
        }

        [Test]
        public void Clean_KeepsOrdinaryNames()
        {
            Assert.AreEqual("김민준", Clean("김민준"));
            Assert.AreEqual("Mina", Clean("Mina"));
            Assert.AreEqual("민준3", Clean("민준3"));
        }

        [Test]
        public void Clean_TidiesSpaces()
        {
            Assert.AreEqual("김민준", Clean("  김민준 "));
            Assert.AreEqual("김 민준", Clean("김   민준"));
            Assert.AreEqual("김 민준", Clean("\t김 \n민준"));
        }

        [Test]
        public void Clean_RejectsEmptyNames()
        {
            StringAssert.Contains("적어", ErrorOf(""));
            StringAssert.Contains("적어", ErrorOf("   "));
            StringAssert.Contains("적어", ErrorOf(null));
        }

        [Test]
        public void Clean_RejectsCharactersThatCannotBeAFileName()
        {
            foreach (string name in new[] { "민준!", "a/b", "..", "민준.json", "C:\\x", "민준?", "<민준>", "민준*" })
            {
                StringAssert.Contains("한글", ErrorOf(name), name);
            }
        }

        [Test]
        public void Clean_LimitsLength()
        {
            Assert.AreEqual("가나다라마바사아자차", Clean("가나다라마바사아자차"));
            StringAssert.Contains("10글자", ErrorOf("가나다라마바사아자차카"));

            // 빈칸은 글자 수에 들어가지만 앞뒤 빈칸은 세지 않는다.
            Assert.AreEqual("가나다라 마바사아자", Clean("  가나다라 마바사아자  "));
        }

        [Test]
        public void Key_TreatsSpacingAndCaseAsTheSamePerson()
        {
            Assert.AreEqual(PlayerName.Key("김민준"), PlayerName.Key(" 김 민준 "));
            Assert.AreEqual(PlayerName.Key("mina"), PlayerName.Key("Mina"));
            Assert.AreNotEqual(PlayerName.Key("김민준"), PlayerName.Key("김민준2"));
            Assert.IsNull(PlayerName.Key("민준!"));
        }
    }
}
