using System.Text;

namespace ZooZooClub.Core
{
    /// <summary>
    /// 로그인할 때 적는 학생 이름의 규칙. 이름마다 저장 파일이 따로 생기므로,
    /// 파일 이름으로 써도 안전한 글자만 받는다.
    /// </summary>
    public static class PlayerName
    {
        public const int MaxLength = 10;

        /// <summary>
        /// 이름을 다듬는다: 앞뒤 빈칸을 떼고, 이어진 빈칸은 하나로 줄인다.
        /// 쓸 수 없는 이름이면 null을 돌려주고 error에 아이에게 보여 줄 이유를 적는다.
        /// </summary>
        public static string Clean(string input, out string error)
        {
            error = null;
            var builder = new StringBuilder();
            bool pendingSpace = false;
            foreach (char c in input ?? "")
            {
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }
                if (!char.IsLetterOrDigit(c))
                {
                    error = "이름에는 한글, 영어, 숫자만 쓸 수 있어요.";
                    return null;
                }
                if (pendingSpace) builder.Append(' ');
                pendingSpace = false;
                builder.Append(c);
            }

            if (builder.Length == 0)
            {
                error = "이름을 적어 주세요.";
                return null;
            }
            if (builder.Length > MaxLength)
            {
                error = "이름은 " + MaxLength + "글자까지 쓸 수 있어요.";
                return null;
            }
            return builder.ToString();
        }

        /// <summary>
        /// 같은 사람인지 가리는 열쇠. 빈칸과 대소문자가 달라도 같은 이름으로 본다
        /// ("김 민준"과 "김민준", "Mina"와 "mina"). 쓸 수 없는 이름이면 null.
        /// </summary>
        public static string Key(string name)
        {
            string error;
            string clean = Clean(name, out error);
            if (clean == null) return null;
            return clean.Replace(" ", "").ToLowerInvariant();
        }
    }
}
