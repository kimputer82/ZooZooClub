// 브라우저(WebGL)에서 한글을 입력하기 위한 진짜 HTML 입력 칸.
// 유니티의 입력 칸은 브라우저에서 한글 조합(IME)을 못 받으므로, 로그인 화면의 이름 칸 자리에
// HTML <input>을 얹어 두고 그 값을 읽어 간다. 위치는 캔버스 기준 비율(0~1)로 받는다.
mergeInto(LibraryManager.library, {
  ZooWebInputShow: function (left, top, width, height, placeholderPtr, maxLength) {
    var input = document.getElementById("zoo-name-input");
    if (!input) {
      input = document.createElement("input");
      input.id = "zoo-name-input";
      input.type = "text";
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
      var style = document.createElement("style");
      style.textContent = "#zoo-name-input::placeholder { color: rgba(150, 118, 88, 0.55); font-weight: normal; }";
      document.head.appendChild(style);
      document.body.appendChild(input);
      window.zooWebInputSubmit = false;
    }
    input.placeholder = UTF8ToString(placeholderPtr);
    input.maxLength = maxLength;

    var canvas = document.querySelector("#unity-canvas") || document.querySelector("canvas");
    var rect = canvas ? canvas.getBoundingClientRect() : { left: 0, top: 0, width: window.innerWidth, height: window.innerHeight };
    var px = rect.left + rect.width * left;
    var py = rect.top + rect.height * top;
    var pw = rect.width * width;
    var ph = rect.height * height;
    input.style.left = px + "px";
    input.style.top = py + "px";
    input.style.width = pw + "px";
    input.style.height = ph + "px";
    input.style.fontSize = Math.max(12, ph * 0.42) + "px";
    input.style.display = "block";
  },

  ZooWebInputFocus: function () {
    var input = document.getElementById("zoo-name-input");
    if (input) input.focus();
  },

  ZooWebInputHide: function () {
    var input = document.getElementById("zoo-name-input");
    if (input) { input.style.display = "none"; input.blur(); }
  },

  ZooWebInputClear: function () {
    var input = document.getElementById("zoo-name-input");
    if (input) input.value = "";
    window.zooWebInputSubmit = false;
  },

  ZooWebInputGetValue: function () {
    var input = document.getElementById("zoo-name-input");
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
  }
});
