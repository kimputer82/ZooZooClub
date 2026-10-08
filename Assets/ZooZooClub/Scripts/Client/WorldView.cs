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
    public sealed partial class WorldView : MonoBehaviour
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
            public int rotation;
            public PlaceableDef def;
            public GameObject root;
            public readonly List<SpriteRenderer> bases = new List<SpriteRenderer>();
            public Color baseColor;
            public SpriteRenderer body;
            public TextMeshPro label;
            public Vector3 center;
            public float sortOffsetY;
            public float labelY;
        }

        /// <summary>한 방향에서 본 주민 그림 한 벌(서 있는 그림 + 걷는 그림)과 그 그림을 놓는 크기·위치.</summary>
        sealed class FrameSet
        {
            public Sprite idle;
            public Sprite[] walk;
            public Vector3 scale;
            public Vector3 rest;
            public float top;
        }

        sealed class ResidentView
        {
            public ResidentDef def;
            public GameObject root;
            public SpriteRenderer body;
            public FrameSet front;
            public FrameSet back;
            public bool facingBack;
            public Vector2 cell;
            public Vector2 target;
            public readonly List<int> path = new List<int>();
            public int pathIndex;
            public bool moving;
            public bool working;
            public float waitUntil;
            public float phase;
        }

        const float DragThresholdPixels = 10f;
        const float MinZoom = 2f;
        const float MaxZoom = 12f;
        const float ResidentSpeed = 0.75f;
        const float ResidentWidth = 0.44f;
        const float WalkFramesPerSecond = 7f;
        const int MaxStrollSteps = 10;

        // 바닥 그림(Art/ground) 한 장이 덮는 칸 수(가로·세로 각각). 그림은 이 간격으로 되풀이된다.
        const float GroundCellsPerImage = 6f;
        const int GroundOrder = -5000;
        const int BaseOrder = -4000;
        const int SortBase = 5000;
        const int LabelOrder = 9000;
        const int GhostOrder = 9500;

        static readonly Color SelectedTint = new Color(1f, 0.93f, 0.35f, 1f);

        Game game;
        Camera cam;

        Transform groundRoot;
        Transform objectRoot;
        Transform residentRoot;
        Transform ghostRoot;
        int groundWidth;
        int groundHeight;
        SpriteRenderer groundBacking;
        SpriteRenderer groundArt;
        Sprite groundSprite;

        readonly Dictionary<int, ObjectView> views = new Dictionary<int, ObjectView>();
        readonly Dictionary<string, ResidentView> residents = new Dictionary<string, ResidentView>();
        readonly List<int> removeBuffer = new List<int>();
        readonly List<SpriteRenderer> ghostCells = new List<SpriteRenderer>();
        readonly List<int> reachableBuffer = new List<int>();

        // 주민이 지나갈 수 없는 칸(무언가 놓인 칸). 배치가 바뀔 때마다 다시 만든다.
        bool[] blockedCells;
        int blockedWidth;
        int blockedHeight;

        bool syncNeeded = true;
        float nextVisualRefresh;

        // 지금 미리 보기로 보여 주는 배치물과 그 위치. 보여 주지 않을 때는 ghostDef가 null.
        PlaceableDef ghostDef;
        Vector2Int ghostCell;
        SpriteRenderer ghostBody;

        // 놓거나 옮기는 중인 배치물의 방향. R 키나 돌리기 버튼으로 바꾼다.
        int placingRotation;

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
                string keys = CanRotatePlacement ? "R: 돌리기, 우클릭·Esc: 그만두기" : "우클릭·Esc: 그만두기";
                if (mode == Mode.Placing)
                {
                    var def = game.Catalog.GetPlaceable(placingDefId);
                    string name = def != null ? def.displayName : placingDefId;
                    return name + " 놓을 곳을 클릭하세요  (" + keys + ")";
                }
                if (mode == Mode.Moving) return "옮길 곳을 클릭하세요  (" + keys + ")";
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
            InitRaid();

            SetUpCamera();
            Sync();
        }

        void OnDestroy()
        {
            if (game != null)
            {
                game.Changed -= OnGameChanged;
                DisposeRaid();
            }
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
            placingRotation = 0;
        }

        public void BeginMoving(int objectId)
        {
            var obj = game.FindObject(objectId);
            if (obj == null) return;
            mode = Mode.Moving;
            movingObjectId = objectId;
            placingDefId = null;
            placingRotation = obj.rotation;
        }

        /// <summary>지금 놓거나 옮기는 중인 배치물. 없으면 null.</summary>
        PlaceableDef PlacementDef
        {
            get
            {
                if (mode == Mode.Placing) return game.Catalog.GetPlaceable(placingDefId);
                if (mode == Mode.Moving)
                {
                    var moving = game.FindObject(movingObjectId);
                    return moving != null ? game.Catalog.GetPlaceable(moving.defId) : null;
                }
                return null;
            }
        }

        /// <summary>지금 놓거나 옮기는 중인 것이 방향을 바꿀 수 있는 것인지.</summary>
        public bool CanRotatePlacement
        {
            get
            {
                var def = PlacementDef;
                return def != null && def.rotations > 1;
            }
        }

        /// <summary>놓거나 옮기는 중인 배치물을 한 번 돌린다.</summary>
        public void RotatePlacement()
        {
            var def = PlacementDef;
            if (def != null) placingRotation = Rotation.Next(def, placingRotation);
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

        /// <summary>
        /// 주민마다 "이름:x,y:상태"를 한 줄로 적어 돌려준다. 상태는 work(시설에서 일하는 중),
        /// walk, stand 가운데 하나이고, 물체가 놓인 칸 위에 있으면 끝에 !가 붙는다. 자동 점검에서 쓴다.
        /// </summary>
        public string DescribeResidents()
        {
            var text = new System.Text.StringBuilder();
            foreach (var view in residents.Values)
            {
                if (text.Length > 0) text.Append(' ');
                bool onObject = !view.working && blockedCells != null
                    && IsBlockedCell(Mathf.FloorToInt(view.cell.x), Mathf.FloorToInt(view.cell.y));
                text.Append(view.def.id).Append(':')
                    .Append(view.cell.x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(view.cell.y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
                    .Append(view.working ? "work" : view.moving ? "walk" : "stand");
                if (!view.working && view.facingBack && view.back != null) text.Append("-back");
                if (onObject) text.Append('!');
            }
            return text.ToString();
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
            UpdateRaid(Time.unscaledDeltaTime);
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

            if (mode != Mode.None && keyboard != null && keyboard.rKey.wasPressedThisFrame) RotatePlacement();

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
                var result = game.BuyAndPlace(placingDefId, cell.x, cell.y, out newId, placingRotation);
                if (result != ActionResult.Ok)
                {
                    Notify(Texts.Describe(result));
                    if (result != ActionResult.InvalidPlacement) CancelMode();
                    return;
                }

                // 밭과 장식은 이어서 여러 개 놓을 수 있게 배치 상태를 유지한다.
                var def = game.Catalog.GetPlaceable(placingDefId);

                // 방어 시설은 살 때 등급이 무작위로 정해진다. 무엇이 나왔는지 알려 준다.
                var grade = game.GradeOf(game.FindObject(newId));
                if (def != null && grade != null)
                {
                    Notify(Texts.GradeStars(game.Catalog, grade) + "  " + grade.displayName + " " + def.displayName + " 등장!");
                }
                bool keepPlacing = def != null
                    && def.kind != PlaceableKind.Facility
                    && game.State.coins >= def.cost
                    && (def.maxCount <= 0 || game.CountPlaced(def.id) < def.maxCount);
                if (!keepPlacing) CancelMode();
                return;
            }

            if (mode == Mode.Moving)
            {
                var result = game.Move(movingObjectId, cell.x, cell.y, placingRotation);
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
                int width, height;
                game.GetFootprint(obj, out width, out height);
                if (x >= obj.x && x < obj.x + width && y >= obj.y && y < obj.y + height) return obj;
            }
            return null;
        }

        // ---------- 배치물 그리기 ----------

        void Sync()
        {
            SyncGround();
            RebuildBlockedCells();

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
                if (views.TryGetValue(obj.id, out view) && view.rotation != obj.rotation)
                {
                    // 방향이 바뀌면 그림과 차지하는 칸이 달라지므로 새로 만든다.
                    Destroy(view.root);
                    views.Remove(obj.id);
                    view = null;
                }

                if (view == null)
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
                    float rootY = view.root.transform.localPosition.y;
                    if (view.body != null) view.body.sortingOrder = SortOrderAt(rootY + view.sortOffsetY);

                    // 밭처럼 두께가 있는 바닥 그림은 앞쪽 것이 뒤쪽 것을 가리도록 칸마다 순서를 준다.
                    foreach (var cell in view.bases)
                    {
                        cell.sortingOrder = BaseOrder - Mathf.RoundToInt((rootY + cell.transform.localPosition.y) * 20f);
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

            // 바닥 그림은 칸 방향으로 펼친 정사각형 그림이다. 처음 한 번 스프라이트로 만들어 둔다.
            if (groundSprite == null)
            {
                Sprite source = ArtLibrary.Get("ground");
                if (source != null)
                {
                    var texture = source.texture;
                    groundSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                        Vector2.zero, texture.width / GroundCellsPerImage, 0, SpriteMeshType.FullRect);
                    groundSprite.hideFlags = HideFlags.DontSave;
                }
            }

            // 땅 가장자리에서 하늘색이 비치지 않게 땅 전체 크기의 바탕을 한 장 깐다.
            if (groundBacking == null)
            {
                groundBacking = NewSprite("Backing", groundRoot, PlaceholderArt.Diamond);
                groundBacking.color = groundSprite != null ? PlaceholderArt.Grass : PlaceholderArt.GroundSeam;
                groundBacking.sortingOrder = GroundOrder - 1;
            }
            float span = (width + height) * 0.5f;
            groundBacking.transform.localPosition = IsoMath.CellToWorld(width * 0.5f, height * 0.5f);
            groundBacking.transform.localScale = new Vector3(span + 0.06f, span + 0.06f, 1f);

            if (groundSprite != null)
            {
                // 바닥 그림 한 장을 땅 전체에 이어 붙여 그린다. 칸마다 따로 그리지 않으므로 칸 사이에 틈이 없다.
                // 정사각형을 45도 돌리고 세로로 절반 눌러 마름모 격자에 맞춘다:
                // 그림의 가로축이 칸의 x축(오른쪽 위), 세로축이 칸의 y축(왼쪽 위)이 된다.
                if (groundArt == null)
                {
                    var tilt = NewChild("GroundTilt");
                    tilt.SetParent(groundRoot, false);
                    tilt.localScale = new Vector3(1f, 0.5f, 1f);

                    groundArt = NewSprite("GroundArt", tilt, groundSprite);
                    float shrink = Mathf.Sqrt(0.5f);
                    groundArt.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    groundArt.transform.localScale = new Vector3(shrink, shrink, 1f);
                    groundArt.drawMode = SpriteDrawMode.Tiled;
                    groundArt.tileMode = SpriteTileMode.Continuous;
                    groundArt.sortingOrder = GroundOrder;
                }
                groundArt.size = new Vector2(width, height);
            }
            else
            {
                // 바닥 그림이 없으면 칸마다 색 마름모를 깐다.
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

            var view = new ObjectView
            {
                id = obj.id, def = def, x = int.MinValue, y = int.MinValue, rotation = obj.rotation
            };
            view.root = new GameObject(def.id + " #" + obj.id);
            view.root.transform.SetParent(objectRoot, false);

            // 지금 방향에서 차지하는 칸 수. 그림은 이 땅의 앞쪽 꼭짓점(root의 원점)을 기준으로 놓는다.
            int cellsWide = Rotation.Width(def, obj.rotation);
            int cellsDeep = Rotation.Height(def, obj.rotation);
            float footprintWidth = (cellsWide + cellsDeep) * 0.5f;
            view.center = IsoMath.CellToWorld(cellsWide * 0.5f, cellsDeep * 0.5f);

            bool isPlot = def.kind == PlaceableKind.Plot;
            bool isFacility = def.kind == PlaceableKind.Facility;

            Sprite plotArt = isPlot ? ArtLibrary.Get("plot") : null;
            var plotLayout = ArtLibrary.Layout("plot");
            if (isPlot) view.baseColor = plotArt != null ? Color.white : PlaceholderArt.Soil;
            else view.baseColor = Color.clear;

            for (int j = 0; j < cellsDeep; j++)
            {
                for (int i = 0; i < cellsWide; i++)
                {
                    var cell = NewSprite("Base", view.root.transform, PlaceholderArt.Diamond);
                    Vector3 corner = IsoMath.CellToWorld(i, j);
                    if (plotArt != null)
                    {
                        float width = plotLayout != null ? plotLayout.width : 0.96f;
                        float lift = plotLayout != null ? plotLayout.lift : 0f;
                        float offsetX = plotLayout != null ? plotLayout.offsetX : 0f;
                        ArtLibrary.Fit(cell, plotArt, corner + new Vector3(offsetX, lift, 0f), width);
                    }
                    else
                    {
                        cell.transform.localPosition = IsoMath.CellToWorld(i + 0.5f, j + 0.5f);
                        cell.transform.localScale = new Vector3(0.92f, 0.92f, 1f);
                    }
                    cell.color = view.baseColor;
                    view.bases.Add(cell);
                }
            }

            float labelY;

            if (isPlot)
            {
                view.body = NewSprite("Crop", view.root.transform, PlaceholderArt.Circle);
                view.body.enabled = false;
                view.sortOffsetY = view.center.y - 0.1f;
                labelY = view.center.y + 0.5f;
            }
            else
            {
                view.body = NewSprite("Body", view.root.transform, null);

                var grade = game.GradeOf(obj);
                float lift;
                float top;
                if (FitObjectArt(view.body, def, grade != null ? grade.id : null, obj.rotation, out lift, out top))
                {
                    labelY = top + 0.16f;
                }
                else
                {
                    // 그림이 없으면 색 도형으로 대신한다.
                    Sprite shape = isFacility ? PlaceholderArt.Box : PlaceholderArt.Circle;
                    float size = footprintWidth * (isFacility ? 0.6f : 0.4f);
                    ArtLibrary.Fit(view.body, shape, new Vector3(view.center.x, view.center.y - size * 0.3f, 0f), size);
                    view.body.color = PlaceholderArt.ColorFor(def.id);
                    labelY = view.center.y + size + 0.16f;
                }

                view.sortOffsetY = lift;
            }

            view.labelY = labelY;
            view.label = NewLabel(view.root.transform, new Vector3(view.center.x, labelY, 0f), 1.35f);
            return view;
        }

        /// <summary>
        /// 배치물의 그림을 골라 renderer에 놓는다(부모의 원점이 차지하는 땅의 앞쪽 꼭짓점일 때).
        /// 방어 시설은 등급마다(&lt;id&gt;_&lt;등급&gt;), 뒤를 보는 방향은 뒷모습 그림(…_back)을 쓰고, 없으면 기본 그림으로 대신한다.
        /// 옆을 보는 방향은 그림을 좌우로 뒤집는다. 그림이 없으면 false.
        /// lift는 그림의 맨 아래 높이, top은 맨 위 높이.
        /// </summary>
        bool FitObjectArt(SpriteRenderer renderer, PlaceableDef def, string gradeId, int rotation,
            out float lift, out float top)
        {
            string artName = ArtLibrary.Variant(def.id, gradeId);
            if (Rotation.ShowsBack(rotation)) artName = ArtLibrary.Variant(artName, "back");
            Sprite art = ArtLibrary.Get(artName);
            var layout = ArtLibrary.LayoutFor(artName, def.id);

            bool isFacility = def.kind == PlaceableKind.Facility;
            float footprintWidth = (def.width + def.height) * 0.5f;
            float width = layout != null ? layout.width : footprintWidth * (isFacility ? 0.92f : 0.7f);
            float offsetX = layout != null ? layout.offsetX : 0f;
            lift = layout != null ? layout.lift : (isFacility ? 0.04f : 0.1f);
            top = lift;

            // 그림은 돌리기 전 모양(가로 def.width, 세로 def.height) 기준으로 그려져 있다.
            // 옆 방향은 앞쪽 꼭짓점을 지나는 세로선을 기준으로 좌우를 뒤집은 것이다.
            float x = (def.width - def.height) * 0.25f + offsetX;
            bool mirrored = Rotation.Mirrored(rotation);
            if (mirrored) x = -x;

            renderer.flipX = mirrored;
            renderer.color = Color.white;
            if (art == null) return false;

            ArtLibrary.Fit(renderer, art, new Vector3(x, lift, 0f), width);
            top = lift + ArtLibrary.FittedHeight(art, width);
            return true;
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
            else if (view.def.kind == PlaceableKind.Defense) RefreshDefense(view, obj);
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

            // 앞 절반은 새싹, 뒤 절반은 작물 그림으로 자라는 모습을 보여 준다.
            string artName = progress < 0.5f ? "crop_sprout" : "crop_" + obj.cropId;
            Sprite art = ArtLibrary.Get(artName);
            if (art == null)
            {
                artName = "crop_" + obj.cropId;
                art = ArtLibrary.Get(artName);
            }

            // 0~1: 지금 보여 주는 그림 안에서 얼마나 자랐는지.
            float stage = progress < 0.5f ? progress * 2f : (progress - 0.5f) * 2f;

            view.body.enabled = true;
            if (art != null)
            {
                var layout = ArtLibrary.Layout(artName);
                float fullWidth = layout != null ? layout.width : 0.56f;
                float lift = layout != null ? layout.lift : 0.18f;
                float offsetX = layout != null ? layout.offsetX : 0f;

                float width = fullWidth * Mathf.Lerp(0.6f, 1f, stage);
                if (ripe) width += Mathf.Sin(Time.unscaledTime * 5f) * 0.02f;
                view.body.color = Color.white;
                ArtLibrary.Fit(view.body, art, new Vector3(view.center.x + offsetX, lift, 0f), width);
            }
            else
            {
                float size = Mathf.Lerp(0.14f, 0.46f, progress);
                view.body.color = PlaceholderArt.ColorFor(obj.cropId);
                ArtLibrary.Fit(view.body, PlaceholderArt.Circle, new Vector3(view.center.x, 0.17f, 0f), size);
            }

            view.label.text = ripe ? "수확!" : Texts.Duration(game.SecondsUntilRipe(obj.id));
        }

        /// <summary>방어 시설 위에 등급을 별로 보여 준다.</summary>
        void RefreshDefense(ObjectView view, PlacedObject obj)
        {
            var grade = game.GradeOf(obj);
            view.label.text = grade != null
                ? Texts.GradeColorTag(grade) + Texts.GradeStars(game.Catalog, grade) + "</color>"
                : "";
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

                // 앞모습: resident_<id>_idle 과 resident_<id>_walk_1~4 (없으면 resident_<id> 한 장만 쓴다).
                // 뒷모습: resident_<id>_back_idle 과 resident_<id>_back_walk_1~4. 화면 위쪽으로 걸을 때 쓰고,
                // 없으면 앞모습으로 대신한다. 한 벌 안의 그림들은 캔버스 크기가 모두 같아야 한다.
                string artName = "resident_" + def.id;
                view.body = NewSprite("Body", view.root.transform, null);
                view.back = LoadFrames(view.body, artName + "_back", null);
                view.front = LoadFrames(view.body, artName, artName);
                if (view.front == null)
                {
                    // 그림이 없으면 색 동그라미로 대신한다.
                    float size = ResidentWidth * 0.7f;
                    ArtLibrary.Fit(view.body, PlaceholderArt.Circle, Vector3.zero, size);
                    view.body.color = PlaceholderArt.ColorFor(def.id);
                    view.front = new FrameSet
                    {
                        scale = view.body.transform.localScale,
                        rest = view.body.transform.localPosition,
                        top = size
                    };
                }

                var label = NewLabel(view.root.transform, new Vector3(0f, view.front.top + 0.1f, 0f), 1.15f);
                label.text = def.displayName;

                Vector2 start;
                if (!TryPickFreeCell(out start)) start = new Vector2(0.5f, 0.5f);
                view.cell = start;
                view.target = start;
                view.waitUntil = Time.unscaledTime + UnityEngine.Random.Range(0.5f, 2.5f);

                residents[def.id] = view;
            }
        }

        /// <summary>
        /// 이름이 name인 그림 한 벌을 읽고, renderer에 놓았을 때의 크기와 위치를 함께 적어 돌려준다.
        /// 서 있는 그림(name_idle, 없으면 singleName)이 없으면 null.
        /// </summary>
        static FrameSet LoadFrames(SpriteRenderer renderer, string name, string singleName)
        {
            Sprite idle = ArtLibrary.Get(name + "_idle");
            if (idle == null && singleName != null) idle = ArtLibrary.Get(singleName);
            if (idle == null) return null;

            var layout = ArtLibrary.Layout(name);
            float width = layout != null && layout.width > 0f ? layout.width : ResidentWidth;
            Vector3 ground = layout != null ? new Vector3(layout.offsetX, layout.lift, 0f) : Vector3.zero;

            ArtLibrary.Fit(renderer, idle, ground, width);
            var set = new FrameSet
            {
                idle = idle,
                scale = renderer.transform.localScale,
                rest = renderer.transform.localPosition,
                top = ground.y + ArtLibrary.FittedHeight(idle, width)
            };

            var frames = new Sprite[4];
            bool complete = true;
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] = ArtLibrary.Get(name + "_walk_" + (i + 1));
                if (frames[i] == null) complete = false;
            }
            if (complete) set.walk = frames;
            return set;
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

        void RebuildBlockedCells()
        {
            int width = game.State.landWidth;
            int height = game.State.landHeight;
            if (blockedCells == null || blockedWidth != width || blockedHeight != height)
            {
                blockedCells = new bool[width * height];
                blockedWidth = width;
                blockedHeight = height;

                // 땅 크기가 바뀌면 칸 번호가 달라지므로, 가던 길은 버리고 다음에 새로 잡게 한다.
                foreach (var resident in residents.Values)
                {
                    resident.path.Clear();
                    resident.pathIndex = 0;
                }
            }
            else
            {
                Array.Clear(blockedCells, 0, blockedCells.Length);
            }

            foreach (var obj in game.State.objects)
            {
                int objectWidth, objectHeight;
                game.GetFootprint(obj, out objectWidth, out objectHeight);
                for (int j = 0; j < objectHeight; j++)
                {
                    for (int i = 0; i < objectWidth; i++)
                    {
                        int x = obj.x + i;
                        int y = obj.y + j;
                        if (x >= 0 && y >= 0 && x < width && y < height) blockedCells[x + y * width] = true;
                    }
                }
            }
        }

        bool IsBlockedCell(int x, int y)
        {
            if (x < 0 || y < 0 || x >= blockedWidth || y >= blockedHeight) return true;
            return blockedCells[x + y * blockedWidth];
        }

        Vector2 CellCenter(int index)
        {
            return new Vector2(index % blockedWidth + 0.5f, index / blockedWidth + 0.5f);
        }

        /// <summary>
        /// 주민이 지금 목표 지점에 서 있을 때 다음에 갈 곳을 정한다. 새 목표가 생기면 true.
        /// 물체가 놓인 칸은 피해서, 칸에서 이웃 칸으로만 걷는다.
        /// </summary>
        bool ChooseNextTarget(ResidentView view, float now)
        {
            int hereX = Mathf.FloorToInt(view.cell.x);
            int hereY = Mathf.FloorToInt(view.cell.y);

            // 가던 길이 남아 있으면 다음 칸으로. 그 사이 무언가 놓여 막혔으면 길을 버리고 잠깐 선다.
            if (view.pathIndex < view.path.Count)
            {
                int next = view.path[view.pathIndex];
                if (!blockedCells[next])
                {
                    view.pathIndex++;
                    view.target = CellCenter(next);
                    return true;
                }
                view.path.Clear();
                view.pathIndex = 0;
                view.waitUntil = now + 0.6f;
                return false;
            }

            if (view.path.Count > 0)
            {
                // 도착했다. 잠깐 쉬었다가 다시 걷는다.
                view.path.Clear();
                view.pathIndex = 0;
                view.waitUntil = now + UnityEngine.Random.Range(1f, 3.5f);
                return false;
            }

            if (IsBlockedCell(hereX, hereY))
            {
                // 서 있던 자리에 무언가 놓였다. 가장 가까운 빈칸으로 비켜선다.
                int free = GridPath.NearestFree(blockedCells, blockedWidth, blockedHeight,
                    Mathf.Clamp(hereX, 0, blockedWidth - 1), Mathf.Clamp(hereY, 0, blockedHeight - 1));
                if (free < 0) return false;
                view.target = CellCenter(free);
                view.waitUntil = now + 1f;
                return (view.target - view.cell).sqrMagnitude > 0.000001f;
            }

            if (now < view.waitUntil) return false;

            // 걸어서 닿을 수 있는 빈칸 가운데 하나를 골라 그곳까지의 길을 잡는다.
            GridPath.Reachable(blockedCells, blockedWidth, blockedHeight, hereX, hereY, MaxStrollSteps, reachableBuffer);
            if (reachableBuffer.Count == 0)
            {
                view.waitUntil = now + 2f;
                return false;
            }

            int goal = reachableBuffer[UnityEngine.Random.Range(0, reachableBuffer.Count)];
            if (!GridPath.Find(blockedCells, blockedWidth, blockedHeight, hereX, hereY,
                    goal % blockedWidth, goal / blockedWidth, view.path) || view.path.Count == 0)
            {
                view.path.Clear();
                view.waitUntil = now + 2f;
                return false;
            }

            // 칸 한가운데에 서 있지 않았다면 먼저 지금 칸의 가운데로 간다(칸 모서리를 가로지르지 않도록).
            view.pathIndex = 0;
            Vector2 hereCenter = new Vector2(hereX + 0.5f, hereY + 0.5f);
            if ((hereCenter - view.cell).sqrMagnitude > 0.000001f)
            {
                view.target = hereCenter;
                return true;
            }

            view.target = CellCenter(view.path[0]);
            view.pathIndex = 1;
            return true;
        }

        void UpdateResidents(float deltaTime)
        {
            float now = Time.unscaledTime;
            if (blockedCells == null) RebuildBlockedCells();

            foreach (var view in residents.Values)
            {
                // 시설에 배치된 주민은 그 시설 위에 얼굴을 작게 띄워 "안에서 일하는 중"임을 보여 준다.
                // 나머지는 빈 땅을 산책한다.
                int assigned = game.AssignedObjectOf(view.def.id);
                ObjectView workplace;
                if (assigned != 0 && views.TryGetValue(assigned, out workplace))
                {
                    view.working = true;
                    view.moving = false;
                    view.root.transform.localScale = new Vector3(0.7f, 0.7f, 1f);
                    view.root.transform.localPosition = workplace.root.transform.localPosition
                        + new Vector3(workplace.center.x, workplace.labelY + 0.3f, 0f);
                    view.facingBack = false;
                    view.body.transform.localScale = view.front.scale;
                    view.body.transform.localPosition = view.front.rest
                        + new Vector3(0f, Mathf.Sin(now * 3f + view.phase) * 0.02f, 0f);
                    view.body.flipX = false;
                    view.body.sortingOrder = LabelOrder;
                    if (view.front.idle != null) view.body.sprite = view.front.idle;
                    continue;
                }

                if (view.working)
                {
                    // 일을 마치고 나오면 원래 있던 자리에서 다시 산책을 시작한다.
                    view.working = false;
                    view.root.transform.localScale = Vector3.one;
                    view.target = view.cell;
                    view.path.Clear();
                    view.pathIndex = 0;
                    view.waitUntil = now + 0.5f;
                }

                // 이번 프레임에 걸을 수 있는 거리만큼, 목표에 닿으면 다음 목표를 받아 가며 걷는다.
                float remaining = ResidentSpeed * deltaTime;
                float worldDx = 0f;
                float worldDy = 0f;
                bool moved = false;

                for (int guard = 0; guard < 4 && remaining > 0f; guard++)
                {
                    Vector2 toTarget = view.target - view.cell;
                    float distance = toTarget.magnitude;
                    if (distance <= 0.0005f)
                    {
                        view.cell = view.target;
                        if (!ChooseNextTarget(view, now)) break;
                        continue;
                    }

                    float stride = Mathf.Min(distance, remaining);
                    Vector2 move = toTarget / distance * stride;
                    view.cell = stride >= distance ? view.target : view.cell + move;
                    worldDx += (move.x - move.y) * IsoMath.HalfWidth;
                    worldDy += (move.x + move.y) * IsoMath.HalfHeight;
                    remaining -= stride;
                    moved = true;
                }
                view.moving = moved;

                Vector3 world = IsoMath.CellToWorld(view.cell.x, view.cell.y);
                view.root.transform.localPosition = world;

                // 화면 위쪽(뒤쪽)으로 걸으면 뒷모습, 아래쪽으로 걸으면 앞모습. 멈추면 마지막 모습 그대로 선다.
                if (view.back != null && Mathf.Abs(worldDy) > 0.0001f) view.facingBack = worldDy > 0f;
                FrameSet set = view.facingBack && view.back != null ? view.back : view.front;

                float bob;
                if (set.walk != null)
                {
                    // 걷는 그림이 있으면 그림을 바꿔 가며 보여 주고, 통통 튀는 움직임은 뺀다.
                    if (view.moving)
                    {
                        int frame = (int)((now + view.phase) * WalkFramesPerSecond) % set.walk.Length;
                        view.body.sprite = set.walk[frame];
                    }
                    else
                    {
                        view.body.sprite = set.idle;
                    }
                    bob = view.moving ? 0f : Mathf.Sin(now * 2f + view.phase) * 0.006f;
                }
                else
                {
                    if (set.idle != null) view.body.sprite = set.idle;
                    bob = view.moving
                        ? Mathf.Abs(Mathf.Sin(now * 9f + view.phase)) * 0.035f
                        : Mathf.Sin(now * 2f + view.phase) * 0.006f;
                }
                view.body.transform.localScale = set.scale;
                view.body.transform.localPosition = set.rest + new Vector3(0f, bob, 0f);

                // 앞모습 그림은 오른쪽 아래를, 뒷모습 그림은 왼쪽 위를 보고 있다. 반대쪽으로 갈 때 좌우를 뒤집는다.
                if (Mathf.Abs(worldDx) > 0.0001f) view.body.flipX = view.facingBack ? worldDx > 0f : worldDx < 0f;
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

            ghostDef = def;
            ghostCell = cell;

            int rotation = Rotation.Normalize(def, placingRotation);
            int cellsWide = Rotation.Width(def, rotation);
            int cellsDeep = Rotation.Height(def, rotation);

            bool valid = game.CanPlace(defId, cell.x, cell.y, ignoreId, rotation);
            Color color = valid ? new Color(0.3f, 1f, 0.45f, 0.65f) : new Color(1f, 0.3f, 0.3f, 0.65f);

            // 놓일 모습을 반투명하게 미리 보여 준다. 방향을 돌리면 그림도 따라 바뀐다.
            if (ghostBody == null)
            {
                ghostBody = NewSprite("GhostBody", ghostRoot, null);
                ghostBody.sortingOrder = GhostOrder + 1;
            }
            string gradeId = null;
            if (mode == Mode.Moving)
            {
                var movingGrade = game.GradeOf(game.FindObject(movingObjectId));
                if (movingGrade != null) gradeId = movingGrade.id;
            }
            float ghostLift;
            float ghostTop;
            ghostBody.transform.localPosition = Vector3.zero;
            ghostBody.enabled = def.kind != PlaceableKind.Plot
                && FitObjectArt(ghostBody, def, gradeId, rotation, out ghostLift, out ghostTop);
            if (ghostBody.enabled)
            {
                ghostBody.transform.localPosition += IsoMath.CellToWorld(cell.x, cell.y);
                ghostBody.color = valid ? new Color(1f, 1f, 1f, 0.8f) : new Color(1f, 0.6f, 0.6f, 0.7f);
            }

            int needed = cellsWide * cellsDeep;
            while (ghostCells.Count < needed)
            {
                var ghost = NewSprite("GhostCell", ghostRoot, PlaceholderArt.Diamond);
                ghost.sortingOrder = GhostOrder;
                ghostCells.Add(ghost);
            }

            int index = 0;
            for (int j = 0; j < cellsDeep; j++)
            {
                for (int i = 0; i < cellsWide; i++)
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
            ghostDef = null;
            if (ghostBody != null) ghostBody.enabled = false;
            foreach (var ghost in ghostCells) ghost.enabled = false;
        }
    }
}
