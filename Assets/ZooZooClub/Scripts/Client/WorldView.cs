using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using ZooZooClub.Core;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 마을 화면. 바닥 격자와 배치물을 그리고, 마우스 입력(선택, 배치, 이동, 화면 끌기, 확대)을 처리한다.
    /// 게임 규칙은 건드리지 않고 Game의 메서드만 호출한다.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        enum Mode
        {
            None,
            Placing,
            Moving
        }

        sealed class ObjectView
        {
            public int id;
            public int x;
            public int y;
            public PlaceableDef def;
            public GameObject root;
            public readonly List<SpriteRenderer> bases = new List<SpriteRenderer>();
            public SpriteRenderer body;
            public TextMeshPro label;
        }

        const float DragThresholdPixels = 10f;
        const float MinZoom = 2f;
        const float MaxZoom = 12f;
        const int GroundOrder = -5000;
        const int BaseOrder = -4000;
        const int GhostOrder = 7000;
        const int LabelOrder = 6000;

        Game game;
        Camera cam;

        Transform groundRoot;
        Transform objectRoot;
        Transform ghostRoot;
        int groundWidth;
        int groundHeight;

        readonly Dictionary<int, ObjectView> views = new Dictionary<int, ObjectView>();
        readonly List<int> removeBuffer = new List<int>();
        readonly List<SpriteRenderer> ghostCells = new List<SpriteRenderer>();

        bool syncNeeded = true;
        float nextVisualRefresh;

        Mode mode;
        string placingDefId;
        int movingObjectId;

        bool pressActive;
        bool pressOverUi;
        bool dragging;
        Vector2 pressPosition;
        Vector2 lastPosition;

        public int SelectedObjectId { get; set; }

        /// <summary>배치물을 눌렀을 때.</summary>
        public event Action<PlacedObject> ObjectClicked;

        /// <summary>빈 땅이나 땅 밖을 눌렀을 때.</summary>
        public event Action EmptyClicked;

        /// <summary>플레이어에게 알려 줄 짧은 문구.</summary>
        public event Action<string> Message;

        public bool InPlacementMode
        {
            get { return mode != Mode.None; }
        }

        public string ModeHint
        {
            get
            {
                if (mode == Mode.Placing)
                {
                    var def = game.Catalog.GetPlaceable(placingDefId);
                    string name = def != null ? def.displayName : placingDefId;
                    return name + " 놓을 곳을 클릭하세요  (우클릭 또는 Esc: 그만두기)";
                }
                if (mode == Mode.Moving) return "옮길 곳을 클릭하세요  (우클릭 또는 Esc: 취소)";
                return "";
            }
        }

        public void Init(Game game)
        {
            this.game = game;
            game.Changed += OnGameChanged;

            groundRoot = NewChild("Ground");
            objectRoot = NewChild("Objects");
            ghostRoot = NewChild("Ghost");

            SetUpCamera();
            Sync();
        }

        void OnDestroy()
        {
            if (game != null) game.Changed -= OnGameChanged;
        }

        void OnGameChanged()
        {
            syncNeeded = true;
        }

        Transform NewChild(string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(transform, false);
            return child;
        }

        void SetUpCamera()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }

            cam.orthographic = true;
            cam.orthographicSize = 4.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = PlaceholderArt.Sky;

            var center = IsoMath.CellToWorld(game.State.landWidth * 0.5f, game.State.landHeight * 0.5f);
            cam.transform.position = new Vector3(center.x, center.y, -10f);
        }

        // ---------- 명령 ----------

        public void BeginPlacing(string defId)
        {
            mode = Mode.Placing;
            placingDefId = defId;
            movingObjectId = 0;
        }

        public void BeginMoving(int objectId)
        {
            if (game.FindObject(objectId) == null) return;
            mode = Mode.Moving;
            movingObjectId = objectId;
            placingDefId = null;
        }

        public void CancelMode()
        {
            mode = Mode.None;
            placingDefId = null;
            movingObjectId = 0;
            HideGhost();
        }

        // ---------- 매 프레임 ----------

        void Update()
        {
            if (game == null) return;

            if (syncNeeded)
            {
                syncNeeded = false;
                Sync();
            }

            if (Time.unscaledTime >= nextVisualRefresh)
            {
                nextVisualRefresh = Time.unscaledTime + 0.25f;
                foreach (var view in views.Values) RefreshVisual(view);
            }

            HandleInput();
        }

        void HandleInput()
        {
            var mouse = Mouse.current;
            if (mouse == null || cam == null) return;

            Vector2 position = mouse.position.ReadValue();
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            var keyboard = Keyboard.current;
            bool cancel = mouse.rightButton.wasPressedThisFrame
                || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame);
            if (cancel && mode != Mode.None)
            {
                CancelMode();
            }

            if (!overUi)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    float factor = scroll > 0f ? 0.9f : 1.1f;
                    cam.orthographicSize = Mathf.Clamp(cam.orthographicSize * factor, MinZoom, MaxZoom);
                }
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                pressActive = true;
                pressOverUi = overUi;
                dragging = false;
                pressPosition = position;
                lastPosition = position;
            }

            if (pressActive && mouse.leftButton.isPressed && !pressOverUi)
            {
                if (!dragging && (position - pressPosition).magnitude > DragThresholdPixels) dragging = true;
                if (dragging)
                {
                    float unitsPerPixel = cam.orthographicSize * 2f / Mathf.Max(1, Screen.height);
                    Vector2 delta = (position - lastPosition) * unitsPerPixel;
                    cam.transform.position -= new Vector3(delta.x, delta.y, 0f);
                }
                lastPosition = position;
            }

            if (pressActive && mouse.leftButton.wasReleasedThisFrame)
            {
                pressActive = false;
                if (!dragging && !pressOverUi && !overUi) Click(ScreenToCell(position));
            }

            if (mode != Mode.None && !overUi) ShowGhost(ScreenToCell(position));
            else HideGhost();
        }

        Vector2Int ScreenToCell(Vector2 screenPosition)
        {
            var world = cam.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, -cam.transform.position.z));
            return IsoMath.WorldToCell(world);
        }

        void Click(Vector2Int cell)
        {
            if (mode == Mode.Placing)
            {
                int newId;
                var result = game.BuyAndPlace(placingDefId, cell.x, cell.y, out newId);
                if (result != ActionResult.Ok)
                {
                    Notify(Texts.Describe(result));
                    if (result != ActionResult.InvalidPlacement) CancelMode();
                    return;
                }

                // 밭과 장식은 이어서 여러 개 놓을 수 있게 배치 상태를 유지한다.
                var def = game.Catalog.GetPlaceable(placingDefId);
                bool keepPlacing = def != null
                    && def.kind != PlaceableKind.Facility
                    && game.State.coins >= def.cost
                    && (def.maxCount <= 0 || game.CountPlaced(def.id) < def.maxCount);
                if (!keepPlacing) CancelMode();
                return;
            }

            if (mode == Mode.Moving)
            {
                var result = game.Move(movingObjectId, cell.x, cell.y);
                if (result == ActionResult.Ok) CancelMode();
                else Notify(Texts.Describe(result));
                return;
            }

            var obj = ObjectAt(cell.x, cell.y);
            if (obj != null)
            {
                var handler = ObjectClicked;
                if (handler != null) handler(obj);
            }
            else
            {
                var handler = EmptyClicked;
                if (handler != null) handler();
            }
        }

        void Notify(string text)
        {
            var handler = Message;
            if (handler != null) handler(text);
        }

        PlacedObject ObjectAt(int x, int y)
        {
            foreach (var obj in game.State.objects)
            {
                var def = game.Catalog.GetPlaceable(obj.defId);
                if (def == null) continue;
                if (x >= obj.x && x < obj.x + def.width && y >= obj.y && y < obj.y + def.height) return obj;
            }
            return null;
        }

        // ---------- 그리기 ----------

        void Sync()
        {
            SyncGround();

            removeBuffer.Clear();
            foreach (var pair in views)
            {
                if (game.FindObject(pair.Key) == null) removeBuffer.Add(pair.Key);
            }
            foreach (int id in removeBuffer)
            {
                Destroy(views[id].root);
                views.Remove(id);
            }

            foreach (var obj in game.State.objects)
            {
                ObjectView view;
                if (!views.TryGetValue(obj.id, out view))
                {
                    view = CreateView(obj);
                    if (view == null) continue;
                    views[obj.id] = view;
                }

                if (view.x != obj.x || view.y != obj.y)
                {
                    view.x = obj.x;
                    view.y = obj.y;
                    view.root.transform.localPosition = IsoMath.CellToWorld(obj.x, obj.y);
                    ApplySorting(view);
                }

                RefreshVisual(view);
            }
        }

        void SyncGround()
        {
            int width = game.State.landWidth;
            int height = game.State.landHeight;
            if (width == groundWidth && height == groundHeight) return;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (x < groundWidth && y < groundHeight) continue;
                    var tile = NewSprite("Tile " + x + "," + y, groundRoot, PlaceholderArt.Diamond);
                    tile.transform.localPosition = IsoMath.CellCenter(x, y);
                    tile.color = ((x + y) & 1) == 0 ? PlaceholderArt.GroundA : PlaceholderArt.GroundB;
                    tile.sortingOrder = GroundOrder;
                }
            }

            groundWidth = width;
            groundHeight = height;
        }

        static SpriteRenderer NewSprite(string name, Transform parent, Sprite sprite)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            return renderer;
        }

        ObjectView CreateView(PlacedObject obj)
        {
            var def = game.Catalog.GetPlaceable(obj.defId);
            if (def == null) return null;

            var view = new ObjectView { id = obj.id, def = def, x = int.MinValue, y = int.MinValue };
            view.root = new GameObject(def.id + " #" + obj.id);
            view.root.transform.SetParent(objectRoot, false);

            Color baseColor = def.kind == PlaceableKind.Plot ? PlaceholderArt.Soil
                : def.kind == PlaceableKind.Facility ? PlaceholderArt.FacilityBase
                : PlaceholderArt.DecorBase;

            for (int j = 0; j < def.height; j++)
            {
                for (int i = 0; i < def.width; i++)
                {
                    var cell = NewSprite("Base", view.root.transform, PlaceholderArt.Diamond);
                    cell.transform.localPosition = IsoMath.CellToWorld(i + 0.5f, j + 0.5f);
                    cell.transform.localScale = new Vector3(0.92f, 0.92f, 1f);
                    cell.color = baseColor;
                    cell.sortingOrder = BaseOrder;
                    view.bases.Add(cell);
                }
            }

            Vector3 center = IsoMath.CellToWorld(def.width * 0.5f, def.height * 0.5f);

            if (def.kind == PlaceableKind.Plot)
            {
                view.body = NewSprite("Crop", view.root.transform, PlaceholderArt.Circle);
                view.body.transform.localPosition = center + new Vector3(0f, 0.08f, 0f);
                view.body.enabled = false;
            }
            else if (def.kind == PlaceableKind.Facility)
            {
                float size = 0.62f * Mathf.Max(def.width, def.height);
                view.body = NewSprite("Body", view.root.transform, PlaceholderArt.Box);
                view.body.transform.localPosition = center + new Vector3(0f, size * 0.32f, 0f);
                view.body.transform.localScale = new Vector3(size, size, 1f);
                view.body.color = PlaceholderArt.ColorFor(def.id);
            }
            else
            {
                float size = 0.42f * Mathf.Max(def.width, def.height);
                view.body = NewSprite("Body", view.root.transform, PlaceholderArt.Circle);
                view.body.transform.localPosition = center + new Vector3(0f, size * 0.35f, 0f);
                view.body.transform.localScale = new Vector3(size, size, 1f);
                view.body.color = PlaceholderArt.ColorFor(def.id);
            }

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(view.root.transform, false);
            view.label = labelGo.AddComponent<TextMeshPro>();
            if (Ui.Font != null) view.label.font = Ui.Font;
            view.label.fontSize = 1.7f;
            view.label.alignment = TextAlignmentOptions.Center;
            view.label.color = Color.white;
            view.label.outlineWidth = 0.25f;
            view.label.outlineColor = new Color32(40, 30, 20, 255);
            view.label.rectTransform.sizeDelta = new Vector2(3f, 1f);
            view.label.sortingOrder = LabelOrder;
            float labelHeight = def.kind == PlaceableKind.Facility ? 0.62f * Mathf.Max(def.width, def.height) + 0.2f : 0.45f;
            labelGo.transform.localPosition = center + new Vector3(0f, labelHeight, 0f);
            view.label.text = "";

            return view;
        }

        void ApplySorting(ObjectView view)
        {
            // 화면 아래쪽(앞쪽)에 있는 것이 위에 그려지도록 한다.
            int order = 1000 - (view.x + view.y) * 10;
            if (view.body != null) view.body.sortingOrder = order;
        }

        void RefreshVisual(ObjectView view)
        {
            var obj = game.FindObject(view.id);
            if (obj == null) return;

            bool selected = view.id == SelectedObjectId;
            Color baseColor = view.def.kind == PlaceableKind.Plot ? PlaceholderArt.Soil
                : view.def.kind == PlaceableKind.Facility ? PlaceholderArt.FacilityBase
                : PlaceholderArt.DecorBase;
            if (selected) baseColor = Color.Lerp(baseColor, new Color(1f, 0.95f, 0.4f), 0.55f);
            foreach (var cell in view.bases) cell.color = baseColor;

            if (view.def.kind == PlaceableKind.Plot)
            {
                if (string.IsNullOrEmpty(obj.cropId))
                {
                    view.body.enabled = false;
                    view.label.text = "";
                    return;
                }

                float progress = game.GrowthProgress(obj.id);
                bool ripe = game.IsRipe(obj.id);
                float size = Mathf.Lerp(0.14f, 0.46f, progress);
                if (ripe) size += Mathf.Sin(Time.unscaledTime * 5f) * 0.03f;

                view.body.enabled = true;
                view.body.color = PlaceholderArt.ColorFor(obj.cropId);
                view.body.transform.localScale = new Vector3(size, size, 1f);
                view.label.text = ripe ? "수확!" : Texts.Duration(game.SecondsUntilRipe(obj.id));
            }
            else if (view.def.kind == PlaceableKind.Facility)
            {
                int ready = game.ReadyJobCount(obj.id);
                if (ready > 0)
                {
                    view.label.text = view.def.displayName + "\n완성 " + ready;
                }
                else if (obj.queue.Count > 0)
                {
                    long remaining = Math.Max(0L, obj.queue[0].finishAt - game.Now);
                    view.label.text = view.def.displayName + "\n" + Texts.Duration(remaining);
                }
                else
                {
                    view.label.text = view.def.displayName;
                }
            }
            else
            {
                view.label.text = "";
            }
        }

        // ---------- 배치 미리 보기 ----------

        void ShowGhost(Vector2Int cell)
        {
            string defId = placingDefId;
            int ignoreId = 0;
            if (mode == Mode.Moving)
            {
                var moving = game.FindObject(movingObjectId);
                if (moving == null)
                {
                    CancelMode();
                    return;
                }
                defId = moving.defId;
                ignoreId = moving.id;
            }

            var def = game.Catalog.GetPlaceable(defId);
            if (def == null)
            {
                HideGhost();
                return;
            }

            bool valid = game.CanPlace(defId, cell.x, cell.y, ignoreId);
            Color color = valid ? new Color(0.3f, 1f, 0.45f, 0.65f) : new Color(1f, 0.3f, 0.3f, 0.65f);

            int needed = def.width * def.height;
            while (ghostCells.Count < needed)
            {
                var ghost = NewSprite("GhostCell", ghostRoot, PlaceholderArt.Diamond);
                ghost.sortingOrder = GhostOrder;
                ghostCells.Add(ghost);
            }

            int index = 0;
            for (int j = 0; j < def.height; j++)
            {
                for (int i = 0; i < def.width; i++)
                {
                    var ghost = ghostCells[index++];
                    ghost.enabled = true;
                    ghost.color = color;
                    ghost.transform.localPosition = IsoMath.CellCenter(cell.x + i, cell.y + j);
                }
            }
            for (; index < ghostCells.Count; index++) ghostCells[index].enabled = false;
        }

        void HideGhost()
        {
            foreach (var ghost in ghostCells) ghost.enabled = false;
        }
    }
}
