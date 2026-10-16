// 브라우저(WebGL)에서 배경음을 HTML <audio>로 튼다.
// 유니티의 압축 소리(Vorbis)는 브라우저 빌드에서 풀리지 않는 일이 있어서, 배경음은 페이지 옆의 ogg 파일을 브라우저가 직접 재생한다.
// 곡 목록은 "a.ogg|b.ogg"처럼 받아서, 한 곡이 끝나면 다음 곡을 틀고 마지막 다음에는 처음으로 돌아간다.
mergeInto(LibraryManager.library, {
  ZooWebMusicPlay: function (urlsPtr, volume) {
    var list = UTF8ToString(urlsPtr).split("|").filter(function (u) { return u.length > 0; });
    if (list.length === 0) return;
    var audio = document.getElementById("zoo-music");
    if (!audio) {
      audio = document.createElement("audio");
      audio.id = "zoo-music";
      audio.preload = "auto";
      audio.style.display = "none";
      document.body.appendChild(audio);
      audio.addEventListener("ended", function () {
        var tracks = window.zooMusicTracks || [];
        if (tracks.length === 0) return;
        window.zooMusicIndex = (window.zooMusicIndex + 1) % tracks.length;
        audio.src = tracks[window.zooMusicIndex];
        var next = audio.play();
        if (next && next.catch) next.catch(function (e) { console.warn("[Zoo] music play blocked: " + e); });
      });
      // 파일이 없거나 못 읽는 곡은 건너뛴다(목록이 한 곡뿐이면 멈춘다).
      audio.addEventListener("error", function () {
        var tracks = window.zooMusicTracks || [];
        if (tracks.length <= 1) return;
        window.zooMusicIndex = (window.zooMusicIndex + 1) % tracks.length;
        audio.src = tracks[window.zooMusicIndex];
      });
    }
    audio.loop = list.length === 1;
    audio.volume = volume;
    var sameList = window.zooMusicTracks && window.zooMusicTracks.join("|") === list.join("|");
    if (!sameList) {
      window.zooMusicTracks = list;
      window.zooMusicIndex = 0;
      audio.src = list[0];
    }
    var p = audio.play();
    if (p && p.catch) p.catch(function (e) { console.warn("[Zoo] music play blocked: " + e); });
  },
  ZooWebMusicStop: function () {
    var audio = document.getElementById("zoo-music");
    if (audio) audio.pause();
  },
  ZooWebMusicMute: function (muted) {
    var audio = document.getElementById("zoo-music");
    if (audio) audio.muted = !!muted;
  }
});
