// 브라우저(WebGL)에서 배경음을 HTML <audio>로 튼다.
// 유니티의 압축 소리(Vorbis)는 브라우저 빌드에서 풀리지 않는 일이 있어서, 배경음은 페이지 옆의 ogg 파일을 브라우저가 직접 재생한다.
mergeInto(LibraryManager.library, {
  ZooWebMusicPlay: function (urlPtr, volume) {
    var url = UTF8ToString(urlPtr);
    var audio = document.getElementById("zoo-music");
    if (!audio) {
      audio = document.createElement("audio");
      audio.id = "zoo-music";
      audio.loop = true;
      audio.preload = "auto";
      audio.style.display = "none";
      document.body.appendChild(audio);
    }
    audio.volume = volume;
    if (audio.getAttribute("src") !== url) audio.src = url;
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
