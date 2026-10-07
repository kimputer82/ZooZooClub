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
    /// 마을 화면. 바닥 격자, 배치물, 돌아다니는 주민을 그리고
    /// 마우스 입력(선택, 배치, 이동, 화면 끌기, 확대)을 처리한다.
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
            public Color baseColor;
            public SpriteRenderer body;
            public TextMeshPro label;
            public Vector3 center;
            public float sortOffsetY;
        }

        sealed class ResidentView
        {
            public ResidentDef def;
            public GameObject root;
            public SpriteRenderer body;
            public Vector3 bodyRest;
            public Vector2 cell;
            public Vector2 target;
            public bool moving;
            public float waitUntil;
            public float phase;
        }

        const float DragThresholdPixels = 10f;
        const float MinZoom = 2f;
        const float MaxZoom = 12f;
        const float ResidentSpeed = 0.75f;
        const float ResidentWidth = 0.44f;
        const int GroundOrder = -5000;
        const int BaseOrder = -4000;
        const int SortBase = 5000;
        const int LabelOrder = 9000;
        const int GhostOrder = 9500;

        static readonly Color SelectedTint = new Color(1f, 0.93f, 0.35f, 1f);

        // 장식 그림의 가로 폭(차지하는 땅의 폭에 대한 비율). 적혀 있지 않으면 0.62.
        static readonly Dictionary<string, float> DecorWidths = new Dictionary<string, float>
        {
            { "fence", 0.7f },
            { "flower_bed", 0.72f },
            { "tree", 0.66f },
            { "bench", 0.62f },
            { "street_lamp", 0.34f },
            { "fountain", 0.8f }
        };

        Game game;
        Camera cam;

        Transform groundRoot;
        Transform objectRoot;
        Transform residentRoot;
        Transform ghostRoot;
        int groundWidth;
        int groundHeight;

        readonly Dictionary<int, ObjectView> views = new Dictionary<int, ObjectView>();
        readonly Dictionary<string, ResidentView> residents = new Dictionary<string, ResidentView>();
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
            residentRoot = NewChild("Residents");
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

        /// <summary>격자 칸 하나를 누른 것과 같은 동작을 한다. 자동 점검에서 쓴다.</summary>
        public void SimulateClick(int x, int y)
        {
            Click(new Vector2Int(x, y));
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

            UpdateResidents(Time.unscaledDeltaTime);
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

        // ---------- 배치물 그리기 ----------

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
                    if (view.body != null)
                    {
                        view.body.sortingOrder = SortOrderAt(view.root.transform.localPosition.y + view.sortOffsetY);
                    }
                }

                RefreshVisual(view);
            }

            SyncResidents();
        }

        static int SortOrderAt(float worldY)
        {
            // 화면 아래쪽(앞쪽)에 있는 것이 위에 그려지도록 한다.
            return SortBase - Mathf.RoundToInt(worldY * 100f);
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

        TextMeshPro NewLabel(Transform parent, Vector3 localPosition, float fontSize)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshPro>();
            if (Ui.Font != null) label.font = Ui.Font;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.outlineWidth = 0.25f;
            label.outlineColor = new Color32(40, 30, 20, 255);
            label.rectTransform.sizeDelta = new Vector2(3f, 1f);
            label.sortingOrder = LabelOrder;
            label.text = "";
            go.transform.localPosition = localPosition;
            return label;
        }

        ObjectView CreateView(PlacedObject obj)
        {
            var def = game.Catalog.GetPlaceable(obj.defId);
            if (def == null) return null;

            var view = new ObjectView { id = obj.id, def = def, x = int.MinValue, y = int.MinValue };
            view.root = new GameObject(def.id + " #" + obj.id);
            view.root.transform.SetParent(objectRoot, false);

            // 차지하는 땅(마름모)의 가로 폭과 세로 높이.
            float footprintWidth = (def.width + def.height) * 0.5f;
            float footprintHeight = footprintWidth * 0.5f;
            view.center = IsoMath.CellToWorld(def.width * 0.5f, def.height * 0.5f);

            Sprite plotArt = def.kind == PlaceableKind.Plot ? ArtLibrary.Get("plot") : null;
            if (def.kind == PlaceableKind.Plot) view.baseColor = plotArt != null ? Color.white : PlaceholderArt.Soil;
            else if (def.kind == PlaceableKind.Facility) view.baseColor = PlaceholderArt.FacilityBase;
            else view.baseColor = Color.clear;

            for (int j = 0; j < def.height; j++)
            {
                for (int i = 0; i < def.width; i++)
                {
                    var cell = NewSprite("Base", view.root.transform, PlaceholderArt.Diamond);
                    Vector3 cellCenter = IsoMath.CellToWorld(i + 0.5f, j + 0.5f);
                    if (plotArt != null)
                    {
                        // 그림의 가운데가 칸의 가운데에 오게 한다.
                        float height = ArtLibrary.FittedHeight(plotArt, 0.96f);
                        ArtLibrary.Fit(cell, plotArt, cellCenter - new Vector3(0f, height * 0.5f, 0f), 0.96f);
                    }
                    else
                    {
                        cell.transform.localPosition = cellCenter;
                        cell.transform.localScale = new Vector3(0.92f, 0.92f, 1f);
                    }
                    cell.color = view.baseColor;
                    cell.sortingOrder = BaseOrder;
                    view.bases.Add(cell);
                }
            }

            float labelHeight;

            if (def.kind == PlaceableKind.Plot)
            {
                view.body = NewSprite("Crop", view.root.transform, PlaceholderArt.Circle);
                view.body.enabled = false;
                view.sortOffsetY = view.center.y;
                labelHeight = 0.5f;
            }
            else
            {
                bool facility = def.kind == PlaceableKind.Facility;
                Sprite art = ArtLibrary.Get(def.id);
                view.body = NewSprite("Body", view.root.transform, art);

                float widthRatio = 0.7f;
                if (!facility && !DecorWidths.TryGetValue(def.id, out widthRatio)) widthRatio = 0.62f;
                float width = footprintWidth * widthRatio;
                Vector3 ground = view.center - new Vector3(0f, footprintHeight * (facility ? 0.3f : 0.2f), 0f);

                if (art != null)
                {
                    ArtLibrary.Fit(view.body, art, ground, width);
                    labelHeight = (ground.y - view.center.y) + ArtLibrary.FittedHeight(art, width) + 0.16f;
                }
                else
                {
                    // 그림이 없으면 색 도형으로 대신한다.
                    Sprite shape = facility ? PlaceholderArt.Box : PlaceholderArt.Circle;
                    ArtLibrary.Fit(view.body, shape, ground, width);
                    view.body.color = PlaceholderArt.ColorFor(def.id);
                    labelHeight = (ground.y - view.center.y) + width + 0.16f;
                }

                view.sortOffsetY = ground.y;
            }

            view.label = NewLabel(view.root.transform, view.center + new Vector3(0f, labelHeight, 0f), 1.7f);
            return view;
        }

        void RefreshVisual(ObjectView view)
        {
            var obj = game.FindObject(view.id);
            if (obj == null) return;

            bool selected = view.id == SelectedObjectId;
            Color baseColor = view.baseColor;
            if (selected)
            {
                baseColor = view.baseColor.a < 0.01f
                    ? new Color(SelectedTint.r, SelectedTint.g, SelectedTint.b, 0.7f)
                    : Color.Lerp(view.baseColor, SelectedTint, 0.5f);
            }
            foreach (var cell in view.bases) cell.color = baseColor;

            if (view.def.kind == PlaceableKind.Plot) RefreshPlot(view, obj);
            else if (view.def.kind == PlaceableKind.Facility) RefreshFacility(view, obj);
        }

        void RefreshPlot(ObjectView view, PlacedObject obj)
        {
            if (string.IsNullOrEmpty(obj.cropId))
            {
                view.body.enabled = false;
                view.label.text = "";
                return;
            }

            float progress = game.GrowthProgress(obj.id);
            bool ripe = game.IsRipe(obj.id);
            Vector3 ground = view.center - new Vector3(0f, 0.08f, 0f);

            // 앞 절반은 새싹, 뒤 절반은 작물 그림으로 자라는 모습을 보여 준다.
            Sprite art = progress < 0.5f ? ArtLibrary.Get("crop_sprout") : ArtLibrary.Get("crop_" + obj.cropId);
            if (art == null) art = ArtLibrary.Get("crop_" + obj.cropId);

            view.body.enabled = true;
            if (art != null)
            {
                float width = progress < 0.5f
                    ? Mathf.Lerp(0.22f, 0.36f, progress * 2f)
                    : Mathf.Lerp(0.36f, 0.56f, (progress - 0.5f) * 2f);
                if (ripe) width += Mathf.Sin(Time.unscaledTime * 5f) * 0.02f;
                view.body.color = Color.white;
                ArtLibrary.Fit(view.body, art, ground, width);
            }
            else
            {
                float size = Mathf.Lerp(0.14f, 0.46f, progress);
                view.body.color = PlaceholderArt.ColorFor(obj.cropId);
                ArtLibrary.Fit(view.body, PlaceholderArt.Circle, ground, size);
            }

            view.label.text = ripe ? "수확!" : Texts.Duration(game.SecondsUntilRipe(obj.id));
        }

        void RefreshFacility(ObjectView view, PlacedObject obj)
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

        // ---------- 주민 ----------

        void SyncResidents()
        {
            foreach (var def in game.UnlockedResidents())
            {
                if (residents.ContainsKey(def.id)) continue;

                var view = new ResidentView { def = def, phase = UnityEngine.Random.Range(0f, 6f) };
                view.root = new GameObject("Resident " + def.id);
                view.root.transform.SetParent(residentRoot, false);

                Sprite art = ArtLibrary.Get("resident_" + def.id);
                view.body = NewSprite("Body", view.root.transform, art);
                if (art != null)
                {
                    ArtLibrary.Fit(view.body, art, Vector3.zero, ResidentWidth);
                }
                else
                {
                    ArtLibrary.Fit(view.body, PlaceholderArt.Circle, Vector3.zero, ResidentWidth * 0.7f);
                    view.body.color = PlaceholderArt.ColorFor(def.id);
                }
                view.bodyRest = view.body.transform.localPosition;

                var label = NewLabel(view.root.transform, new Vector3(0f, ResidentWidth + 0.12f, 0f), 1.3f);
                label.text = def.displayName;

                Vector2 start;
                if (!TryPickFreeCell(out start)) start = new Vector2(0.5f, 0.5f);
                view.cell = start;
                view.target = start;
                view.waitUntil = Time.unscaledTime + UnityEngine.Random.Range(0.5f, 2.5f);

                residents[def.id] = view;
            }
        }

        bool TryPickFreeCell(out Vector2 cell)
        {
            for (int attempt = 0; attempt < 16; attempt++)
            {
                int x = UnityEngine.Random.Range(0, game.State.landWidth);
                int y = UnityEngine.Random.Range(0, game.State.landHeight);
                if (ObjectAt(x, y) != null) continue;
                cell = new Vector2(x + 0.5f, y + 0.5f);
                return true;
            }
            cell = Vector2.zero;
            return false;
        }

        void UpdateResidents(float deltaTime)
        {
            float now = Time.unscaledTime;

            foreach (var view in residents.Values)
            {
                // 시설에 배치된 주민은 그 시설 앞으로 가서 머문다. 나머지는 빈 땅을 산책한다.
                PlacedObject workplace = null;
                int assigned = game.AssignedObjectOf(view.def.id);
                if (assigned != 0) workplace = game.FindObject(assigned);

                if (workplace != null)
                {
                    view.target = new Vector2(workplace.x + 0.2f, workplace.y + 0.2f);
                }
                else if (!view.moving && now >= view.waitUntil)
                {
                    Vector2 next;
                    if (TryPickFreeCell(out next)) view.target = next;
                    else view.waitUntil = now + 2f;
                }

                Vector2 toTarget = view.target - view.cell;
                float distance = toTarget.magnitude;
                float step = ResidentSpeed * deltaTime;
                float worldDx = 0f;

                if (distance > 0.001f)
                {
                    Vector2 move = distance <= step ? toTarget : toTarget / distance * step;
                    view.cell += move;
                    worldDx = (move.x - move.y) * IsoMath.HalfWidth;
                    view.moving = distance > step;
                    if (!view.moving) view.waitUntil = now + UnityEngine.Random.Range(1f, 3.5f);
                }
                else
                {
                    view.moving = false;
                }

                Vector3 world = IsoMath.CellToWorld(view.cell.x, view.cell.y);
                view.root.transform.localPosition = world;

                float bob = view.moving
                    ? Mathf.Abs(Mathf.Sin(now * 9f + view.phase)) * 0.035f
                    : Mathf.Sin(now * 2f + view.phase) * 0.006f;
                view.body.transform.localPosition = view.bodyRest + new Vector3(0f, bob, 0f);
                if (Mathf.Abs(worldDx) > 0.0001f) view.body.flipX = worldDx < 0f;
                view.body.sortingOrder = SortOrderAt(world.y) + 1;
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
