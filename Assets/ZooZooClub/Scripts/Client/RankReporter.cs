using System;
using UnityEngine;
using ZooZooClub.Core;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 방에 들어온 학생의 레벨을 서버에 올린다: 들어올 때, 레벨이 오를 때, 그리고 일정 시간마다(online.json의 rankUploadSeconds).
    /// 실패해도 조용히 다음 기회에 다시 올린다.
    /// </summary>
    public sealed class RankReporter : MonoBehaviour
    {
        Game game;
        float nextUpload;
        bool uploading;
        bool wanted;

        public static DateTime LastUploadAt { get; private set; }
        public static string LastError { get; private set; }

        public void Init(Game game)
        {
            this.game = game;
            game.LeveledUp += OnLeveledUp;
            wanted = true;
            nextUpload = 0f;
        }

        void OnDestroy()
        {
            if (game != null) game.LeveledUp -= OnLeveledUp;
        }

        void OnLeveledUp(int level)
        {
            wanted = true;
        }

        void Update()
        {
            if (game == null || uploading) return;
            if (Time.unscaledTime >= nextUpload) wanted = true;
            if (!wanted) return;

            wanted = false;
            uploading = true;
            nextUpload = Time.unscaledTime + Online.RankUploadSeconds;

            var state = game.State;
            var entry = new RankEntry { name = state.playerName, level = state.level, xp = state.xp, quizSolved = state.quizSolved,
                charm = game.TotalCharm };
            string room = GameSession.Instance != null ? GameSession.Instance.ActiveRoom : "";
            if (string.IsNullOrEmpty(room)) { uploading = false; return; }
            string roomLock = GameSession.Instance != null ? GameSession.Instance.CurrentLock : "";
            Online.PostPlayer(room, entry, roomLock, error =>
            {
                uploading = false;
                LastError = error;
                if (error == null) LastUploadAt = DateTime.Now;
                // 방이 닫혔거나 서버가 안 되면 조금 뒤에 다시 해 본다.
                else nextUpload = Time.unscaledTime + 20f;
            });
        }

        /// <summary>지금 바로 올린다(순위표를 열 때).</summary>
        public void UploadNow()
        {
            wanted = true;
        }
    }
}
