using System;
using System.Collections.Generic;

namespace ZooZooClub.Core
{
    // 문제 풀기: 문제 은행에서 문제를 하나 뽑아 보여 주고, 맞히면 코인을 준다.
    // 틀리면 잠깐 동안 다음 문제를 받을 수 없다(아무거나 눌러서 코인을 버는 것을 막는다).
    public sealed partial class Game
    {
        /// <summary>같은 문제가 다시 나오기까지 사이에 끼는 문제 수의 상한.</summary>
        const int QuizRecentLimit = 8;

        public int QuizCount
        {
            get { return Catalog.quiz != null && Catalog.quiz.questions != null ? Catalog.quiz.questions.Count : 0; }
        }

        /// <summary>틀린 뒤 다음 문제를 받을 수 있을 때까지 남은 시간(초). 바로 받을 수 있으면 0.</summary>
        public long QuizLockSeconds
        {
            get { return Math.Max(0, State.quizLockedUntil - Now); }
        }

        /// <summary>이 문제를 맞히면 받는 코인.</summary>
        public int QuizRewardFor(QuizDef quiz)
        {
            if (quiz == null) return 0;
            if (quiz.reward > 0) return quiz.reward;
            return Catalog.quiz != null ? Math.Max(0, Catalog.quiz.rewardCoins) : 0;
        }

        /// <summary>
        /// 풀 문제를 하나 뽑는다. 최근에 나온 문제는 피한다.
        /// 문제가 없거나, 틀린 뒤 기다리는 중이면 null.
        /// </summary>
        public QuizDef DrawQuiz()
        {
            if (QuizCount == 0 || QuizLockSeconds > 0) return null;

            var questions = Catalog.quiz.questions;
            var candidates = new List<QuizDef>();
            foreach (var question in questions)
            {
                if (question == null || question.choices == null || question.choices.Count < 2) continue;
                if (!State.quizRecent.Contains(question.id)) candidates.Add(question);
            }

            if (candidates.Count == 0)
            {
                // 전부 최근에 나온 문제다. 기록을 비우고 아무 문제나 고른다.
                State.quizRecent.Clear();
                foreach (var question in questions)
                {
                    if (question != null && question.choices != null && question.choices.Count >= 2) candidates.Add(question);
                }
                if (candidates.Count == 0) return null;
            }

            var picked = candidates[NextRandom(candidates.Count)];

            State.quizRecent.Add(picked.id);
            int keep = Math.Min(QuizRecentLimit, Math.Max(0, questions.Count - 1));
            while (State.quizRecent.Count > keep) State.quizRecent.RemoveAt(0);

            NotifyChanged();
            return picked;
        }

        /// <summary>
        /// 답을 낸다. choice는 문제에 적힌 보기의 순서(1부터)다.
        /// 맞히면 코인을 받고, 틀리면 잠깐 동안 다음 문제를 받을 수 없다.
        /// </summary>
        public ActionResult AnswerQuiz(string quizId, int choice, out bool correct, out int reward)
        {
            int gems;
            return AnswerQuiz(quizId, choice, out correct, out reward, out gems);
        }

        /// <summary>위와 같고, 맞혔을 때 낮은 확률로 받은 보석 수(0 또는 1)도 돌려준다.</summary>
        public ActionResult AnswerQuiz(string quizId, int choice, out bool correct, out int reward, out int gems)
        {
            correct = false;
            reward = 0;
            gems = 0;

            var quiz = Catalog.GetQuiz(quizId);
            if (quiz == null) return ActionResult.NotFound;
            if (QuizLockSeconds > 0) return ActionResult.NotReady;

            correct = choice == quiz.answer;
            if (correct)
            {
                reward = QuizRewardFor(quiz);
                State.coins += reward;
                State.quizSolved++;

                int chance = Catalog.balance != null ? Catalog.balance.quizGemChancePercent : 0;
                if (chance > 0 && NextRandom(100) < chance)
                {
                    gems = 1;
                    State.gems += gems;
                }
            }
            else
            {
                int wait = Catalog.quiz != null ? Math.Max(0, Catalog.quiz.wrongCooldownSeconds) : 0;
                State.quizLockedUntil = Now + wait;
            }

            NotifyChanged();
            return ActionResult.Ok;
        }
    }
}
