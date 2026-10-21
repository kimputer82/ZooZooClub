// 브라우저(WebGL)에서 선생님이 고른 엑셀 파일(.xlsx)을 읽어 표(글자 칸의 2차원 배열)로 돌려준다.
// .xlsx는 XML 몇 개를 묶은 zip 파일이라, 브라우저의 DecompressionStream으로 풀고 DOMParser로 첫 번째 시트를 읽는다.
// 결과는 window.zooFileResult에 JSON 글로 남기고, 게임이 매 프레임 ZooFileTake로 가져간다.
//   {"rows":[["문제","보기1",...],...],"name":"파일 이름"}  또는  {"error":"이유"}  또는 {"cancel":true}
mergeInto(LibraryManager.library, {
  ZooFilePick: function () {
    window.zooFileResult = null;
    window.zooFileBusy = true;
    var finish = function (obj) { window.zooFileBusy = false; window.zooFileResult = JSON.stringify(obj); };

    var u16 = function (b, p) { return b[p] | (b[p + 1] << 8); };
    var u32 = function (b, p) { return (b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24)) >>> 0; };

    var unzip = async function (buffer) {
      var b = new Uint8Array(buffer);
      var eocd = -1;
      for (var i = b.length - 22; i >= Math.max(0, b.length - 65557); i--) {
        if (u32(b, i) === 0x06054b50) { eocd = i; break; }
      }
      if (eocd < 0) throw new Error("엑셀 파일(.xlsx)이 아니에요.");
      var count = u16(b, eocd + 10), p = u32(b, eocd + 16), entries = {};
      var dec = new TextDecoder("utf-8");
      for (var n = 0; n < count; n++) {
        if (u32(b, p) !== 0x02014b50) break;
        var method = u16(b, p + 10), csize = u32(b, p + 20);
        var nameLen = u16(b, p + 28), extraLen = u16(b, p + 30), commentLen = u16(b, p + 32), local = u32(b, p + 42);
        var name = dec.decode(b.subarray(p + 46, p + 46 + nameLen));
        entries[name] = { method: method, csize: csize, local: local };
        p += 46 + nameLen + extraLen + commentLen;
      }
      var read = async function (name) {
        var e = entries[name];
        if (!e) return null;
        var lp = e.local, start = lp + 30 + u16(b, lp + 26) + u16(b, lp + 28);
        var data = b.subarray(start, start + e.csize);
        if (e.method === 0) return dec.decode(data);
        if (e.method !== 8) throw new Error("이 압축 방식은 읽지 못해요.");
        if (typeof DecompressionStream === "undefined") throw new Error("이 브라우저는 엑셀 파일을 읽지 못해요. 크롬이나 엣지 최신판을 써 주세요.");
        var stream = new Blob([data]).stream().pipeThrough(new DecompressionStream("deflate-raw"));
        return dec.decode(await new Response(stream).arrayBuffer());
      };
      return read;
    };

    var parse = function (text) { return new DOMParser().parseFromString(text, "application/xml"); };
    var byName = function (node, name) {
      var out = [], all = node.getElementsByTagName("*");
      for (var i = 0; i < all.length; i++) if (all[i].localName === name) out.push(all[i]);
      return out;
    };
    var kids = function (node, name) {
      var out = [];
      for (var c = node.firstElementChild; c; c = c.nextElementSibling) if (c.localName === name) out.push(c);
      return out;
    };
    var textOf = function (item) {
      var s = "";
      for (var c = item.firstElementChild; c; c = c.nextElementSibling) {
        if (c.localName === "t") s += c.textContent;
        else if (c.localName === "r") kids(c, "t").forEach(function (t) { s += t.textContent; });
      }
      return s;
    };
    var cleanNumber = function (v) {
      if (v === null || v === "") return "";
      var x = Number(v);
      if (!isFinite(x)) return v;
      if (Math.floor(x) === x && Math.abs(x) < 1e15) return String(x);
      return String(Number(x.toPrecision(15)));
    };
    var columnOf = function (ref, fallback) {
      if (!ref) return fallback;
      var col = 0, letters = 0;
      for (var i = 0; i < ref.length; i++) {
        var ch = ref.charCodeAt(i) & ~32;
        if (ch < 65 || ch > 90) break;
        col = col * 26 + (ch - 64); letters++;
      }
      return letters > 0 ? col - 1 : fallback;
    };

    var readSheet = async function (buffer) {
      var read = await unzip(buffer);
      var sheetPath = "xl/worksheets/sheet1.xml";
      var wb = await read("xl/workbook.xml"), rels = await read("xl/_rels/workbook.xml.rels");
      if (wb && rels) {
        var first = byName(parse(wb).documentElement, "sheet")[0], rid = null;
        if (first) for (var a = 0; a < first.attributes.length; a++) {
          var at = first.attributes[a];
          if (at.localName === "id" && at.namespaceURI) rid = at.value;
        }
        if (rid) byName(parse(rels).documentElement, "Relationship").forEach(function (r) {
          if (r.getAttribute("Id") === rid) {
            var t = r.getAttribute("Target") || "";
            sheetPath = t.charAt(0) === "/" ? t.substring(1) : "xl/" + t;
          }
        });
      }
      var shared = [], ss = await read("xl/sharedStrings.xml");
      if (ss) kids(parse(ss).documentElement, "si").forEach(function (si) { shared.push(textOf(si)); });
      var sheetText = await read(sheetPath);
      if (!sheetText) throw new Error("시트를 찾지 못했어요.");
      var rows = [];
      var rowEls = byName(parse(sheetText).documentElement, "row");
      for (var i = 0; i < rowEls.length; i++) {
        var rowEl = rowEls[i], num = rows.length + 1, r = parseInt(rowEl.getAttribute("r"), 10);
        if (r >= num) num = r;
        if (num > 5000) break;
        while (rows.length < num - 1) rows.push([]);
        var cells = [];
        kids(rowEl, "c").forEach(function (cell) {
          var col = columnOf(cell.getAttribute("r"), cells.length);
          if (col >= 60) return;
          while (cells.length <= col) cells.push("");
          var type = cell.getAttribute("t") || "n", vEl = kids(cell, "v")[0], v = vEl ? vEl.textContent : null, value;
          if (type === "s") { var k = parseInt(v, 10); value = k >= 0 && k < shared.length ? shared[k] : ""; }
          else if (type === "inlineStr") { var is = kids(cell, "is")[0]; value = is ? textOf(is) : ""; }
          else if (type === "b") value = v === "1" ? "TRUE" : "FALSE";
          else if (type === "str" || type === "e") value = v || "";
          else value = cleanNumber(v);
          cells[col] = value;
        });
        rows.push(cells);
      }
      return rows;
    };
    window.zooReadXlsx = readSheet;

    var input = document.getElementById("zoo-file-input");
    if (!input) {
      input = document.createElement("input");
      input.id = "zoo-file-input";
      input.type = "file";
      input.accept = ".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
      input.style.display = "none";
      document.body.appendChild(input);
    }
    input.value = "";
    input.onchange = function () {
      var file = input.files && input.files[0];
      if (!file) { finish({ cancel: true }); return; }
      if (file.size > 5 * 1024 * 1024) { finish({ error: "파일이 너무 커요(5MB 이하)." }); return; }
      file.arrayBuffer().then(readSheet).then(function (rows) {
        finish({ rows: rows, name: file.name });
      }).catch(function (e) {
        finish({ error: (e && e.message) ? e.message : "파일을 읽지 못했어요." });
      });
    };
    // 파일 창을 닫기만 했을 때(취소)도 알 수 있게 한다(지원하는 브라우저에서).
    input.oncancel = function () { finish({ cancel: true }); };
    input.click();
  },

  ZooFileTake: function () {
    var value = window.zooFileResult;
    if (!value) return null;
    window.zooFileResult = null;
    var size = lengthBytesUTF8(value) + 1;
    var buffer = _malloc(size);
    stringToUTF8(value, buffer, size);
    return buffer;
  }
});
