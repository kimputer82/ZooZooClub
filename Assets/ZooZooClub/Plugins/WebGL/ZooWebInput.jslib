// 브라우저(WebGL)에서 한글을 입력하기 위한 진짜 HTML 입력 칸.
// 유니티의 입력 칸은 브라우저에서 한글 조합(IME)을 못 받으므로, 글자를 적는 자리마다 HTML <input>을 얹어 두고
// 그 값을 읽어 간다. id로 여러 칸을 구분하고, 위치는 캔버스 기준 비율(0~1)로 받는다.
mergeInto(LibraryManager.library, {
  ZooWebInputShow: function (idPtr, left, top, width, height, placeholderPtr, maxLength, password) {
    var id = "zoo-input-" + UTF8ToString(idPtr);
    var input = document.getElementById(id);
    if (!input) {
      if (!document.getElementById("zoo-input-style")) {
        var style = document.createElement("style");
        style.id = "zoo-input-style";
        style.textContent = ".zoo-input::placeholder { color: rgba(150, 118, 88, 0.55); font-weight: normal; }";
        document.head.appendChild(style);
      }
      input = document.createElement("input");
      input.id = id;
      input.className = "zoo-input";
      input.type = password ? "password" : "text";
      input.autocomplete = "off";
      input.spellcheck = false;
      input.style.position = "absolute";
      input.style.boxSizing = "border-box";
      input.style.border = "none";
      input.style.outline = "none";
      input.style.background = "transparent";
      input.style.textAlign = "center";
      input.style.color = "#5c3a21";
      input.style.fontFamily = '"Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans KR", sans-serif';
      input.style.fontWeight = "bold";
      input.style.padding = "0";
      input.style.margin = "0";
      input.style.zIndex = "10";
      input.addEventListener("keydown", function (e) {
        if (e.key === "Enter") { window.zooWebInputSubmit = true; e.preventDefault(); }
        e.stopPropagation();
      });
      input.addEventListener("keyup", function (e) { e.stopPropagation(); });
      input.addEventListener("keypress", function (e) { e.stopPropagation(); });
      document.body.appendChild(input);
      if (window.zooWebInputSubmit === undefined) window.zooWebInputSubmit = false;
    }
    input.placeholder = UTF8ToString(placeholderPtr);
    input.maxLength = maxLength;

    var canvas = document.querySelector("#unity-canvas") || document.querySelector("canvas");
    var rect = canvas ? canvas.getBoundingClientRect() : { left: 0, top: 0, width: window.innerWidth, height: window.innerHeight };
    var ph = rect.height * height;
    input.style.left = (rect.left + rect.width * left) + "px";
    input.style.top = (rect.top + rect.height * top) + "px";
    input.style.width = (rect.width * width) + "px";
    input.style.height = ph + "px";
    // 안내 글이 칸보다 길면 글자 크기를 줄여 한 줄에 다 보이게 한다(한글은 대략 글자 하나가 1em).
    var pw = rect.width * width - 24;
    var placeholderLen = Math.max(1, input.placeholder.length);
    var fit = pw / (placeholderLen * 0.92);
    input.style.fontSize = Math.max(12, Math.min(ph * 0.42, fit)) + "px";
    input.style.display = "block";
  },

  ZooWebInputFocus: function (idPtr) {
    var input = document.getElementById("zoo-input-" + UTF8ToString(idPtr));
    if (input) input.focus();
  },

  ZooWebInputHide: function (idPtr) {
    var input = document.getElementById("zoo-input-" + UTF8ToString(idPtr));
    if (input) { input.style.display = "none"; input.blur(); }
  },

  ZooWebInputHideAll: function () {
    var inputs = document.getElementsByClassName("zoo-input");
    for (var i = 0; i < inputs.length; i++) { inputs[i].style.display = "none"; inputs[i].blur(); }
  },

  ZooWebInputSetValue: function (idPtr, valuePtr) {
    var input = document.getElementById("zoo-input-" + UTF8ToString(idPtr));
    if (input) input.value = UTF8ToString(valuePtr);
  },

  ZooWebInputClear: function (idPtr) {
    var input = document.getElementById("zoo-input-" + UTF8ToString(idPtr));
    if (input) input.value = "";
    window.zooWebInputSubmit = false;
  },

  ZooWebInputGetValue: function (idPtr) {
    var input = document.getElementById("zoo-input-" + UTF8ToString(idPtr));
    var value = input ? input.value : "";
    var size = lengthBytesUTF8(value) + 1;
    var buffer = _malloc(size);
    stringToUTF8(value, buffer, size);
    return buffer;
  },

  ZooWebInputTakeSubmit: function () {
    var flag = window.zooWebInputSubmit ? 1 : 0;
    window.zooWebInputSubmit = false;
    return flag;
  },

  // 저장 파일을 브라우저 저장소(IndexedDB)에 바로 옮긴다. 자동 동기화가 있지만, 탭을 닫기 직전에도 남도록 저장할 때마다 부른다.
  ZooWebSyncSaves: function () {
    if (typeof FS !== "undefined" && FS.syncfs) {
      FS.syncfs(false, function (err) { if (err) console.warn("[Zoo] save sync failed: " + err); });
    }
  }
});
