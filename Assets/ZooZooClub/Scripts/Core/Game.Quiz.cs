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

        // ---------- 콤보 ----------

        /// <summary>지금 이어지고 있는 콤보(연속 정답 수). 마지막 정답 뒤 너무 오래 지났으면 0.</summary>
        public int QuizCombo
        {
            get
            {
                if (State.quizCombo <= 0) return 0;
                return ComboSecondsLeft > 0 ? State.quizCombo : 0;
            }
        }

        /// <summary>콤보가 끊기기까지 남은 시간(초). 콤보가 없으면 0.</summary>
        public long ComboSecondsLeft
        {
            get
            {
                if (State.quizCombo <= 0) return 0;
                int timeout = Catalog.balance != null ? Catalog.balance.comboTimeoutSeconds : 0;
                if (timeout <= 0) return long.MaxValue;
                return Math.Max(0L, State.quizComboAt + timeout - Now);
            }
        }

        /// <summary>지금 콤보로 받는 생산 시간 감소(%). 도달한 가장 높은 단계의 값.</summary>
        public int ComboSpeedPercent
        {
            get
            {
                int combo = QuizCombo;
                int best = 0;
                int bestCombo = -1;
                if (Catalog.balance == null || Catalog.balance.comboTiers == null) return 0;
                foreach (var tier in Catalog.balance.comboTiers)
                {
                    if (tier == null || tier.combo > combo || tier.combo <= bestCombo) continue;
                    bestCombo = tier.combo;
                    best = tier.speedPercent;
                }
                return Math.Max(0, Math.Min(50, best));
            }
        }

        /// <summary>다음 콤보 단계. 이미 가장 높은 단계면 null.</summary>
        public ComboTierDef NextComboTier
        {
            get
            {
                int combo = QuizCombo;
                ComboTierDef next = null;
                if (Catalog.balance == null || Catalog.balance.comboTiers == null) return null;
                foreach (var tier in Catalog.balance.comboTiers)
                {
                    if (tier != null && tier.combo > combo && (next == null || tier.combo < next.combo)) next = tier;
                }
                return next;
            }
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

        /// <summary>
        /// 위와 같고, 맞혔을 때 낮은 확률로 받은 보석 수(0 또는 1)도 돌려준다.
        /// gateObjectId를 주면 그 배치물 때문에 푸는 문제다: 맞히면 밭의 수확 문제를 지우거나 방어 시설의 공사 문제를 하나 줄인다.
        /// </summary>
        public ActionResult AnswerQuiz(string quizId, int choice, out bool correct, out int reward, out int gems,
            int gateObjectId = 0)
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

                // 콤보: 이어서 맞히면 늘고, 너무 오래 쉬었으면 처음부터 다시 센다.
                State.quizCombo = QuizCombo + 1;
                State.quizComboAt = Now;

                var gate = gateObjectId != 0 ? FindObject(gateObjectId) : null;
                if (gate != null)
                {
                    if (gate.quizGate) gate.quizGate = false;
                    else if (gate.buildQuizzes > 0) gate.buildQuizzes--;
                    else if (CanUpgrade(gate.id))
                    {
                        gate.upgradeProgress++;
                        if (gate.upgradeProgress >= UpgradeQuizCount)
                        {
                            gate.upgraded = true;
                            gate.upgradeProgress = 0;
                        }
                    }
                }

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
                State.quizCombo = 0;
            }

            NotifyChanged();
            return ActionResult.Ok;
        }
    }
}
