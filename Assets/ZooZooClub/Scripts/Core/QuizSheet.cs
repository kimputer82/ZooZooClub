using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZooZooClub.Core
{
    /// <summary>표로 적은 문제를 읽은 결과.</summary>
    public class QuizSheetResult
    {
        /// <summary>읽어 들인 문제. id는 비어 있다(문제 은행에 넣을 때 붙인다).</summary>
        public readonly List<QuizDef> questions = new List<QuizDef>();
        /// <summary>등록하지 못한 줄과 그 이유. 사람이 읽는 글이다.</summary>
        public readonly List<string> problems = new List<string>();
        /// <summary>등록은 했지만 한 번 확인해 보면 좋은 줄.</summary>
        public readonly List<string> notices = new List<string>();
    }

    /// <summary>
    /// 엑셀 같은 표로 적은 문제를 문제 은행의 문제로 바꾼다.
    ///
    /// 첫 줄은 제목 줄이고 다음 제목을 찾는다(순서는 상관없고, 띄어쓰기와 대소문자는 무시한다).
    ///   문제            반드시 있어야 한다
    ///   보기1 ~ 보기4   둘 이상 있어야 한다. 빈칸으로 둔 보기는 건너뛴다(OX 문제는 보기 둘만 적는다)
    ///   정답            보기 번호(1~4). 보기의 글을 그대로 적어도 된다
    ///   보상            맞히면 받는 코인. 비워 두면 기본 보상
    /// 그 밖의 열(메모, 단원 등)은 무시한다.
    /// </summary>
    public static class QuizSheet
    {
        public const int MaxChoices = 4;
        public const int MinChoices = 2;

        static readonly string[] QuestionNames = { "문제", "질문", "question" };
        static readonly string[] ChoiceNames = { "보기", "선택지", "choice" };
        static readonly string[] AnswerNames = { "정답", "답", "answer" };
        static readonly string[] RewardNames = { "보상", "코인", "reward" };

        /// <summary>
        /// rows[0]이 제목 줄이다. 오류 글에는 엑셀에서 보이는 줄 번호(제목 줄이 1)를 적는다.
        /// </summary>
        /// <summary>한 방에 올릴 수 있는 문제의 최대 개수.</summary>
        public const int MaxRoomQuestions = 1000;

        /// <summary>
        /// 표에서 읽은 문제로 방 문제 은행을 만든다. 기본 보상과 틀린 뒤 기다리는 시간은 defaults(게임의 기본 문제 은행)를 따른다.
        /// id는 기본 문제(q001…)와 겹치지 않게 r001부터 붙인다.
        /// </summary>
        public static QuizBank ToBank(IList<QuizDef> questions, QuizBank defaults)
        {
            var bank = new QuizBank();
            if (defaults != null)
            {
                bank.rewardCoins = defaults.rewardCoins;
                bank.wrongCooldownSeconds = defaults.wrongCooldownSeconds;
            }
            if (questions == null) return bank;
            for (int i = 0; i < questions.Count && i < MaxRoomQuestions; i++)
            {
                var source = questions[i];
                if (source == null) continue;
                bank.questions.Add(new QuizDef
                {
                    id = "r" + (bank.questions.Count + 1).ToString("000"),
                    question = source.question,
                    choices = new List<string>(source.choices ?? new List<string>()),
                    answer = source.answer,
                    reward = source.reward
                });
            }
            return bank;
        }

        public static QuizSheetResult Read(IList<string[]> rows)
        {
            var result = new QuizSheetResult();
            if (rows == null || rows.Count == 0)
            {
                result.problems.Add("표가 비어 있어요.");
                return result;
            }

            int questionColumn = -1;
            int answerColumn = -1;
            int rewardColumn = -1;
            var choiceColumns = new int[MaxChoices];
            for (int i = 0; i < MaxChoices; i++) choiceColumns[i] = -1;

            string[] header = rows[0] ?? new string[0];
            for (int column = 0; column < header.Length; column++)
            {
                string name = Normalize(header[column]);
                if (name.Length == 0) continue;

                int choiceNumber = ChoiceNumber(name);
                if (choiceNumber >= 1 && choiceNumber <= MaxChoices)
                {
                    if (choiceColumns[choiceNumber - 1] < 0) choiceColumns[choiceNumber - 1] = column;
                }
                else if (questionColumn < 0 && Matches(name, QuestionNames)) questionColumn = column;
                else if (answerColumn < 0 && Matches(name, AnswerNames)) answerColumn = column;
                else if (rewardColumn < 0 && Matches(name, RewardNames)) rewardColumn = column;
            }

            int choiceColumnCount = 0;
            foreach (int column in choiceColumns)
            {
                if (column >= 0) choiceColumnCount++;
            }

            if (questionColumn < 0) result.problems.Add("첫 줄에 '문제' 제목이 없어요.");
            if (choiceColumnCount < MinChoices) result.problems.Add("첫 줄에 '보기1', '보기2' 제목이 있어야 해요.");
            if (answerColumn < 0) result.problems.Add("첫 줄에 '정답' 제목이 없어요.");
            if (result.problems.Count > 0) return result;

            var seen = new Dictionary<string, int>();
            for (int index = 1; index < rows.Count; index++)
            {
                string[] row = rows[index];
                int line = index + 1;
                if (IsEmpty(row)) continue;

                string question = Cell(row, questionColumn);
                if (question.Length == 0)
                {
                    result.problems.Add(line + "행: 문제가 비어 있어요.");
                    continue;
                }

                // 빈 보기는 건너뛰므로, 엑셀의 보기 번호가 몇 번째 보기가 되는지를 적어 둔다.
                var choices = new List<string>();
                var positionOfNumber = new int[MaxChoices];
                for (int i = 0; i < MaxChoices; i++)
                {
                    string choice = choiceColumns[i] >= 0 ? Cell(row, choiceColumns[i]) : "";
                    if (choice.Length == 0)
                    {
                        positionOfNumber[i] = 0;
                        continue;
                    }
                    choices.Add(choice);
                    positionOfNumber[i] = choices.Count;
                }

                if (choices.Count < MinChoices)
                {
                    result.problems.Add(line + "행: 보기가 " + MinChoices + "개 이상 있어야 해요.");
                    continue;
                }

                string answerText = Cell(row, answerColumn);
                if (answerText.Length == 0)
                {
                    result.problems.Add(line + "행: 정답이 비어 있어요.");
                    continue;
                }

                int answer = 0;
                int number;
                if (TryReadChoiceNumber(answerText, out number))
                {
                    if (number >= 1 && number <= MaxChoices) answer = positionOfNumber[number - 1];
                    if (answer > 0)
                    {
                        // 보기가 숫자인 문제에서 보기 번호 대신 값을 적은 것은 아닌지 알려 준다.
                        int sameText = choices.IndexOf(answerText) + 1;
                        if (sameText > 0 && sameText != answer)
                        {
                            result.notices.Add(line + "행: 정답 " + answerText + "을(를) " + number
                                + "번 보기(" + choices[answer - 1] + ")로 읽었어요. 값이 "
                                + answerText + "인 보기를 뜻했다면 그 보기의 번호를 적어 주세요.");
                        }
                    }
                }

                // 번호가 아니면 보기의 글을 그대로 적은 것으로 본다.
                if (answer == 0) answer = choices.IndexOf(answerText) + 1;

                if (answer == 0)
                {
                    result.problems.Add(line + "행: 정답 '" + answerText
                        + "'에 맞는 보기가 없어요. 보기 번호(1~" + MaxChoices + ")를 적어 주세요.");
                    continue;
                }

                int reward = 0;
                string rewardText = rewardColumn >= 0 ? Cell(row, rewardColumn) : "";
                if (rewardText.Length > 0)
                {
                    double value;
                    if (double.TryParse(rewardText, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                        && value >= 0 && value <= 1000000)
                    {
                        reward = (int)Math.Round(value, MidpointRounding.AwayFromZero);
                    }
                    else
                    {
                        result.notices.Add(line + "행: 보상 '" + rewardText + "'은(는) 숫자가 아니어서 기본 보상으로 넣었어요.");
                    }
                }

                int earlier;
                if (seen.TryGetValue(question, out earlier))
                {
                    result.notices.Add(line + "행: " + earlier + "행과 같은 문제예요.");
                }
                else
                {
                    seen[question] = line;
                }

                var quiz = new QuizDef { id = "", question = question, answer = answer, reward = reward };
                quiz.choices.AddRange(choices);
                result.questions.Add(quiz);
            }

            return result;
        }

        static string Cell(string[] row, int column)
        {
            if (row == null || column < 0 || column >= row.Length || row[column] == null) return "";
            return row[column].Trim();
        }

        static bool IsEmpty(string[] row)
        {
            if (row == null) return true;
            foreach (string cell in row)
            {
                if (!string.IsNullOrWhiteSpace(cell)) return false;
            }
            return true;
        }

        /// <summary>제목을 견주기 좋게: 빈칸을 없애고 소문자로.</summary>
        static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var builder = new System.Text.StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (!char.IsWhiteSpace(c)) builder.Append(char.ToLowerInvariant(c));
            }
            return builder.ToString();
        }

        static bool Matches(string name, string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (name == candidate) return true;
            }
            return false;
        }

        /// <summary>"보기3" 같은 제목이면 3, 아니면 0.</summary>
        static int ChoiceNumber(string name)
        {
            foreach (string prefix in ChoiceNames)
            {
                if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                int number;
                if (TryReadWholeNumber(name.Substring(prefix.Length), out number)) return number;
            }
            return 0;
        }

        /// <summary>"3", "3번", "③", "3.0"을 3으로 읽는다.</summary>
        static bool TryReadChoiceNumber(string text, out int number)
        {
            number = 0;
            if (text.Length == 1 && text[0] >= '①' && text[0] <= '⑨')
            {
                number = text[0] - '①' + 1;
                return true;
            }
            if (text.EndsWith("번", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1).Trim();
            return TryReadWholeNumber(text, out number);
        }

        static bool TryReadWholeNumber(string text, out int number)
        {
            number = 0;
            double value;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            if (value != Math.Floor(value) || value < -1000000 || value > 1000000) return false;
            number = (int)value;
            return true;
        }
    }
}
