using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using ZooZooClub.Core;
using UButton = UnityEngine.UI.Button;
using UCanvasScaler = UnityEngine.UI.CanvasScaler;
using UGraphicRaycaster = UnityEngine.UI.GraphicRaycaster;
using UImage = UnityEngine.UI.Image;
using ULayoutElement = UnityEngine.UI.LayoutElement;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 화면 UI. 모바일 농장 게임처럼 배치한다.
    /// - 왼쪽 위: 레벨과 경험치, 오른쪽 위: 코인·보석·창고·매력
    /// - 위 가운데: 다음 침략까지 남은 시간
    /// - 오른쪽 아래: 문제·주민·창고·주문·건설 버튼, 왼쪽 아래: 마을 확장 버튼
    /// - 가운데 창: 건설, 주문, 창고, 주민, 마을 확장, 문제 풀기, 생산 시설
    /// - 아래 시트: 밭, 장식, 방어 시설을 눌렀을 때
    /// </summary>
    public sealed class GameHud : MonoBehaviour
    {
        enum PanelKind
        {
            None,
            Build,
            Orders,
            Storage,
            Residents,
            Expand,
            Quiz,
            Selection
        }

        Game game;
        WorldView world;

        // 늘 보이는 것
        TextMeshProUGUI levelText;
        TextMeshProUGUI xpText;
        RectTransform xpFill;
        TextMeshProUGUI coinsText;
        TextMeshProUGUI gemsText;
        TextMeshProUGUI storageText;
        TextMeshProUGUI charmText;
        GameObject menuRoot;
        GameObject expandRoot;
        GameObject ordersBadge;
        TextMeshProUGUI ordersBadgeText;
        GameObject raidChipRoot;
        UImage raidChipImage;
        TextMeshProUGUI raidText;

        GameObject toastRoot;
        TextMeshProUGUI toastText;
        float toastUntil;

        GameObject hintRoot;
        TextMeshProUGUI hintText;
        GameObject hintRotateButton;

        // 가운데 창
        GameObject dimRoot;
        RectTransform windowRect;
        TextMeshProUGUI windowTitle;
        RectTransform windowHeader;
        ULayoutElement windowHeaderLayout;
        RectTransform windowContent;
        RectTransform windowFooter;

        // 아래 시트
        GameObject sheetRoot;
        RectTransform sheetInfo;
        RectTransform sheetBody;

        PanelKind panel;
        int selectedId;
        int buildTab;

        // 문제 풀기: 지금 보여 주는 문제, 보기를 섞은 순서(문제에 적힌 보기 번호, 1부터),
        // 고른 보기 번호(아직 안 골랐으면 0)와 그 결과.
        QuizDef quizCurrent;
        readonly List<int> quizOrder = new List<int>();
        int quizPicked;
        bool quizCorrect;
        int quizReward;
        string signature = "";
        bool rebuildNeeded;
        float nextTick;
        readonly List<Action> tickers = new List<Action>();

        public void Init(Game game, WorldView world)
        {
            this.game = game;
            this.world = world;

            BuildCanvas();

            game.Changed += OnGameChanged;
            game.LeveledUp += OnLeveledUp;
            game.RaidStarted += OnRaidStarted;
            game.RaidEnded += OnRaidEnded;
            world.ObjectClicked += OnObjectClicked;
            world.EmptyClicked += OnEmptyClicked;
            world.Message += Toast;

            RefreshHud();
        }

        void OnDestroy()
        {
            if (game != null)
            {
                game.Changed -= OnGameChanged;
                game.LeveledUp -= OnLeveledUp;
                game.RaidStarted -= OnRaidStarted;
                game.RaidEnded -= OnRaidEnded;
            }
            if (world != null)
            {
                world.ObjectClicked -= OnObjectClicked;
                world.EmptyClicked -= OnEmptyClicked;
                world.Message -= Toast;
            }
        }

        // ---------- 화면 구성 ----------

        void BuildCanvas()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<UCanvasScaler>();
            scaler.uiScaleMode = UCanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<UGraphicRaycaster>();

            if (EventSystem.current == null)
            {
                var eventSystem = new GameObject("EventSystem");
                eventSystem.transform.SetParent(transform, false);
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<InputSystemUIInputModule>();
            }

            // 뒤에 만든 것이 위에 그려진다.
            Transform root = canvasGo.transform;
            RaidAlert.Create(root, game);
            BuildLevelBadge(root);
            BuildCurrencies(root);
            BuildRaidChip(root);
            BuildMenus(root);
            BuildHint(root);
            BuildSheet(root);
            BuildWindow(root);
            BuildToast(root);
        }

        void BuildLevelBadge(Transform root)
        {
            var badge = Ui.Rect("LevelBadge", root);
            Ui.Put(badge, 0f, 1f, 24f, -20f, 380f, 84f);

            var bar = Ui.Box("XpBar", badge, Ui.CapsuleSprite, Ui.Pill);
            Ui.Put(bar.rectTransform, 0f, 0.5f, 40f, 0f, 330f, 46f);
            // 막대의 왼쪽 끝은 별 그림에 가려지므로 그 뒤부터 채운다.
            var fillArea = Ui.Rect("FillArea", bar.transform);
            Ui.Stretch(fillArea, 40f, 0f, 0f, 0f);
            xpFill = BarFill(fillArea, Ui.Green, 5f);

            xpText = Ui.Label("Xp", bar.transform, "", 22f, Color.white, TextAlignmentOptions.Center, true);
            Ui.Stretch(xpText.rectTransform, 46f, 0f, 12f, 0f);

            var star = Ui.Picture("Star", badge, ArtLibrary.Get("ui_icon_star"));
            Ui.Put(star.rectTransform, 0f, 0.5f, 0f, 0f, 84f, 84f);

            levelText = Ui.Label("Level", star.transform, "", 30f, Ui.Ink, TextAlignmentOptions.Center, true);
            Ui.Stretch(levelText.rectTransform, 0f, 0f, 0f, 8f);
        }

        void BuildCurrencies(Transform root)
        {
            var row = Ui.Rect("Currencies", root);
            Ui.Put(row, 1f, 1f, -24f, -32f, 900f, 60f);
            var group = Ui.Horizontal(row.gameObject, 14f, 0);
            group.childAlignment = TextAnchor.MiddleRight;
            group.childForceExpandWidth = false;

            charmText = Capsule(row, "Charm", "ui_icon_heart", 150f);
            storageText = Capsule(row, "Storage", "ui_icon_box", 190f);
            gemsText = Capsule(row, "Gems", "ui_icon_gem", 150f);
            coinsText = Capsule(row, "Coins", "ui_icon_coin", 200f);
        }

        /// <summary>왼쪽에 그림이 걸친 알약 모양 표시. 숫자를 넣을 글자를 돌려준다.</summary>
        static TextMeshProUGUI Capsule(Transform parent, string name, string iconName, float width)
        {
            var root = Ui.Rect(name, parent);
            Ui.Fixed(root.gameObject, width, 56f);

            var background = Ui.Box("Background", root, Ui.CapsuleSprite, Ui.Pill);
            Ui.Stretch(background.rectTransform, 20f, 5f, 0f, 5f);

            var icon = Ui.Picture("Icon", root, ArtLibrary.Get(iconName));
            Ui.Put(icon.rectTransform, 0f, 0.5f, 0f, 0f, 56f, 56f);

            var label = Ui.Label("Text", root, "", 26f, Color.white, TextAlignmentOptions.Center, true);
            Ui.Stretch(label.rectTransform, 58f, 0f, 16f, 0f);
            return label;
        }

        void BuildRaidChip(Transform root)
        {
            raidText = Ui.Chip("RaidChip", root, ArtLibrary.Get("ui_icon_shield"), "", Ui.Pill, 56f, 24f);
            raidChipRoot = raidText.transform.parent.gameObject;
            raidChipImage = raidChipRoot.GetComponent<UImage>();
            Ui.Put((RectTransform)raidChipRoot.transform, 0.5f, 1f, -40f, -34f, 360f, 56f);
            raidChipRoot.SetActive(false);
        }

        void BuildMenus(Transform root)
        {
            var menu = Ui.Rect("Menu", root);
            Ui.Put(menu, 1f, 0f, -24f, 18f, 800f, 170f);
            var group = Ui.Horizontal(menu.gameObject, 14f, 0);
            group.childAlignment = TextAnchor.LowerRight;
            group.childForceExpandWidth = false;
            menuRoot = menu.gameObject;

            MenuButton(menu, "QuizButton", "ui_icon_quiz", "문제", Ui.Purple, () => Toggle(PanelKind.Quiz));
            MenuButton(menu, "ResidentsButton", "ui_icon_residents", "주민", Ui.Green, () => Toggle(PanelKind.Residents));
            MenuButton(menu, "StorageButton", "ui_icon_box", "창고", Ui.Blue, () => Toggle(PanelKind.Storage));
            var orders = MenuButton(menu, "OrdersButton", "ui_icon_orders", "주문", Ui.Yellow, () => Toggle(PanelKind.Orders));
            MenuButton(menu, "BuildButton", "ui_icon_build", "건설", Ui.Orange, () => Toggle(PanelKind.Build));

            // 지금 바로 납품할 수 있는 주문 수.
            var badge = Ui.Picture("Badge", orders, ArtLibrary.Get("ui_dot"));
            badge.color = Ui.Red;
            Ui.Put(badge.rectTransform, 1f, 1f, 0f, 2f, 44f, 44f);
            ordersBadgeText = Ui.Label("Text", badge.transform, "", 24f, Color.white, TextAlignmentOptions.Center, true);
            Ui.Stretch(ordersBadgeText.rectTransform, 0f, 0f, 0f, 0f);
            ordersBadge = badge.gameObject;
            ordersBadge.SetActive(false);

            var expand = Ui.Rect("ExpandMenu", root);
            Ui.Put(expand, 0f, 0f, 24f, 18f, 140f, 170f);
            var expandGroup = Ui.Horizontal(expand.gameObject, 0f, 0);
            expandGroup.childAlignment = TextAnchor.LowerLeft;
            expandGroup.childForceExpandWidth = false;
            expandRoot = expand.gameObject;

            MenuButton(expand, "ExpandButton", "ui_icon_expand", "확장", Ui.Cream, () => Toggle(PanelKind.Expand));
        }

        /// <summary>둥근 그림 버튼과 그 아래 이름표. 버튼 오브젝트의 이름이 buttonName이 된다.</summary>
        static RectTransform MenuButton(Transform parent, string buttonName, string iconName, string text,
            Color color, Action onClick)
        {
            var slot = Ui.Rect(buttonName + "Slot", parent);
            Ui.Fixed(slot.gameObject, 136f, 164f);

            var button = Ui.ButtonBox(buttonName, slot, null, color, onClick);
            var image = (UImage)button.targetGraphic;
            image.sprite = ArtLibrary.Get("ui_circle");
            Ui.Put(image.rectTransform, 0.5f, 1f, 0f, 0f, 124f, 132f);

            var icon = Ui.Picture("Icon", button.transform, ArtLibrary.Get(iconName));
            Ui.Put(icon.rectTransform, 0.5f, 0.5f, 0f, 4f, 76f, 76f);

            var label = Ui.Chip("Label", slot, null, text, Ui.Pill, 40f, 22f);
            Ui.Put((RectTransform)label.transform.parent, 0.5f, 0f, 0f, 0f, 112f, 40f);
            return slot;
        }

        void BuildHint(Transform root)
        {
            var hint = Ui.Rect("Hint", root);
            Ui.Put(hint, 0.5f, 0f, 0f, 24f, 1120f, 64f);
            var group = Ui.Horizontal(hint.gameObject, 12f, 0);
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childForceExpandWidth = false;

            hintText = Ui.Chip("Text", hint, null, "", Ui.Pill, 60f, 22f);
            Ui.Fixed(hintText.transform.parent.gameObject, 720f, 60f);

            // 방향을 바꿀 수 있는 것을 놓을 때만 보인다.
            var rotate = Ui.Button("RotateButton", hint, "돌리기", () => world.RotatePlacement(), 60f, 24f, Ui.Blue);
            Ui.Fixed(rotate.gameObject, 160f, 60f);
            hintRotateButton = rotate.gameObject;

            var cancel = Ui.Button("CancelButton", hint, "그만두기", () => world.CancelMode(), 60f, 24f, Ui.Red);
            Ui.Fixed(cancel.gameObject, 200f, 60f);

            hintRoot = hint.gameObject;
            hintRoot.SetActive(false);
        }

        void BuildSheet(Transform root)
        {
            var sheet = Ui.Box("Sheet", root, Ui.PanelSprite, Color.white);
            sheet.raycastTarget = true;
            Ui.Put(sheet.rectTransform, 0.5f, 0f, 0f, 16f, 1210f, 300f);
            sheetRoot = sheet.gameObject;

            CloseButton(sheet.transform);

            var body = Ui.Rect("Body", sheet.transform);
            // 오른쪽 위의 닫기 버튼과 겹치지 않게 오른쪽을 더 비운다.
            Ui.Stretch(body, 32f, 38f, 52f, 28f);
            var group = Ui.Horizontal(body.gameObject, 20f, 0);
            group.childAlignment = TextAnchor.UpperLeft;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = true;

            sheetInfo = Ui.Rect("Info", body);
            Ui.Fixed(sheetInfo.gameObject, 340f, 0f).flexibleHeight = 1f;
            Ui.Vertical(sheetInfo.gameObject, 6f, 0);

            sheetBody = Ui.Rect("Options", body);
            var bodyLayout = sheetBody.gameObject.AddComponent<ULayoutElement>();
            bodyLayout.flexibleWidth = 1f;
            bodyLayout.flexibleHeight = 1f;
            Ui.Vertical(sheetBody.gameObject, 8f, 0);

            sheetRoot.SetActive(false);
        }

        void BuildWindow(Transform root)
        {
            // 창 뒤를 어둡게 가리고, 바깥을 누르면 창을 닫는다.
            var dim = Ui.Panel("Dim", root, Ui.Dim);
            Ui.Stretch(dim.rectTransform, 0f, 0f, 0f, 0f);
            var dimButton = dim.gameObject.AddComponent<UButton>();
            dimButton.transition = UnityEngine.UI.Selectable.Transition.None;
            dimButton.onClick.AddListener(ClosePanel);
            dimRoot = dim.gameObject;

            var window = Ui.Box("Window", root, Ui.PanelSprite, Color.white);
            window.raycastTarget = true;
            windowRect = window.rectTransform;
            Ui.Put(windowRect, 0.5f, 0.5f, 0f, 0f, 1240f, 760f);

            windowTitle = Ui.Label("Title", window.transform, "", 40f, Ui.Ink, TextAlignmentOptions.Center, true);
            var titleRect = windowTitle.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -22f);
            titleRect.sizeDelta = new Vector2(-180f, 56f);

            CloseButton(window.transform);

            var body = Ui.Rect("Body", window.transform);
            Ui.Stretch(body, 36f, 46f, 36f, 92f);
            Ui.Vertical(body.gameObject, 14f, 0);

            windowHeader = Ui.Rect("Header", body);
            windowHeaderLayout = Ui.Fixed(windowHeader.gameObject, 0f, 60f);
            var headerGroup = Ui.Horizontal(windowHeader.gameObject, 12f, 0);
            headerGroup.childForceExpandWidth = false;

            windowContent = Ui.ScrollView("Scroll", body, 14f);

            windowFooter = Ui.Rect("Footer", body);
            Ui.Fixed(windowFooter.gameObject, 0f, 64f);
            var footerGroup = Ui.Horizontal(windowFooter.gameObject, 12f, 0);
            footerGroup.childForceExpandWidth = false;

            dimRoot.SetActive(false);
            windowRect.gameObject.SetActive(false);
        }

        void CloseButton(Transform parent)
        {
            var close = Ui.ButtonBox("CloseButton", parent, null, Color.white, ClosePanel);
            var image = (UImage)close.targetGraphic;
            image.sprite = ArtLibrary.Get("ui_close");
            Ui.Put(image.rectTransform, 1f, 1f, 14f, 14f, 72f, 77f);
        }

        void BuildToast(Transform root)
        {
            toastText = Ui.Chip("Toast", root, null, "", Ui.Pill, 60f, 26f);
            toastRoot = toastText.transform.parent.gameObject;
            Ui.Put((RectTransform)toastRoot.transform, 0.5f, 1f, 0f, -108f, 860f, 60f);
            toastRoot.SetActive(false);
        }

        // ---------- 막대 ----------

        /// <summary>알약 모양 바탕 안에 채워지는 부분을 만든다. 얼마나 찼는지는 SetBar로 정한다.</summary>
        static RectTransform BarFill(Transform bar, Color color, float inset)
        {
            var fill = Ui.Box("Fill", bar, Ui.CapsuleSprite, color);
            var rect = fill.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            return rect;
        }

        static void SetBar(RectTransform fill, float amount)
        {
            amount = Mathf.Clamp01(amount);
            if (fill.gameObject.activeSelf != amount > 0.001f) fill.gameObject.SetActive(amount > 0.001f);
            // 너무 짧으면 알약 모양이 찌그러지므로 최소 길이를 둔다.
            fill.anchorMax = new Vector2(Mathf.Max(amount, 0.14f), 1f);
        }

        // ---------- 갱신 ----------

        void Update()
        {
            if (game == null) return;

            if (rebuildNeeded)
            {
                rebuildNeeded = false;
                RebuildPanel();
            }

            // 놓을 곳을 고르는 동안과 창이나 시트가 떠 있는 동안에는 모서리 버튼을 치운다.
            bool placing = world.InPlacementMode;
            bool showMenus = !placing && panel == PanelKind.None;
            if (hintRoot.activeSelf != placing) hintRoot.SetActive(placing);
            if (placing)
            {
                hintText.text = world.ModeHint;
                bool canRotate = world.CanRotatePlacement;
                if (hintRotateButton.activeSelf != canRotate) hintRotateButton.SetActive(canRotate);
            }
            if (menuRoot.activeSelf != showMenus) menuRoot.SetActive(showMenus);
            if (expandRoot.activeSelf != showMenus) expandRoot.SetActive(showMenus);

            if (toastRoot.activeSelf && Time.unscaledTime >= toastUntil) toastRoot.SetActive(false);

            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 0.5f;

            RefreshHud();
            foreach (var ticker in tickers) ticker();

            // 시간이 흘러서 패널의 모양이 달라져야 하면(작물이 다 자람, 새 주문 도착 등) 다시 만든다.
            if (panel != PanelKind.None && ComputeSignature() != signature) rebuildNeeded = true;
        }

        void OnGameChanged()
        {
            RefreshHud();
            rebuildNeeded = true;
        }

        void OnLeveledUp(int level)
        {
            string text = "레벨 " + level + " 달성!";
            foreach (var resident in game.Catalog.residents)
            {
                if (resident.unlockLevel == level) text += "  " + resident.displayName + "가 마을에 왔어요.";
            }
            Toast(text);
        }

        void RefreshHud()
        {
            var state = game.State;
            levelText.text = state.level.ToString();
            if (game.XpToNextLevel > 0)
            {
                xpText.text = state.xp + " / " + game.XpToNextLevel;
                SetBar(xpFill, (float)state.xp / game.XpToNextLevel);
            }
            else
            {
                xpText.text = "최고 레벨";
                SetBar(xpFill, 1f);
            }

            coinsText.text = state.coins.ToString("N0");
            gemsText.text = state.gems.ToString("N0");
            storageText.text = game.StoredTotal + "/" + state.storageCapacity;
            storageText.color = game.StoredTotal >= state.storageCapacity ? new Color(1f, 0.72f, 0.66f) : Color.white;
            charmText.text = game.TotalCharm.ToString();

            int ready = 0;
            foreach (var order in state.orders)
            {
                if (game.IsOrderAvailable(order) && game.HasItems(order.items)) ready++;
            }
            if (ordersBadge.activeSelf != ready > 0) ordersBadge.SetActive(ready > 0);
            if (ready > 0) ordersBadgeText.text = ready.ToString();

            RefreshRaidChip();
        }

        void RefreshRaidChip()
        {
            bool show = game.RaidActive || game.RaidsUnlocked;
            if (raidChipRoot.activeSelf != show) raidChipRoot.SetActive(show);
            if (!show) return;

            if (game.RaidActive)
            {
                raidChipImage.color = Ui.Red;
                raidText.text = "침략 중!  남은 적 " + game.Invaders.Count;
            }
            else if (game.RaidImminent)
            {
                raidChipImage.color = Ui.Red;
                raidText.text = "곧 침략!  " + Texts.Duration(game.SecondsUntilRaid);
            }
            else
            {
                raidChipImage.color = Ui.Pill;
                raidText.text = "다음 침략  " + Texts.Duration(game.SecondsUntilRaid);
            }
        }

        void OnRaidStarted(int wave)
        {
            RefreshRaidChip();
            Toast(wave + "번째 침략! 침략자들이 몰려와요.");
        }

        void OnRaidEnded(RaidReport report)
        {
            RefreshRaidChip();
            if (report.total == 0) return;

            if (report.escaped == 0)
            {
                Toast("침략을 모두 막아 냈어요!  +" + report.bountyCoins.ToString("N0") + " 코인");
            }
            else
            {
                Toast(report.defeated + "마리 물리치고 " + report.escaped + "마리를 놓쳤어요.  도둑맞은 코인 "
                    + report.coinsStolen.ToString("N0"));
            }
        }

        public void Toast(string text)
        {
            toastText.text = text;
            toastRoot.SetActive(true);
            toastUntil = Time.unscaledTime + 2.2f;
        }

        /// <summary>실패했으면 이유를, 성공했고 문구가 주어졌으면 그 문구를 보여 준다.</summary>
        void Report(ActionResult result, string successText)
        {
            if (result != ActionResult.Ok) Toast(Texts.Describe(result));
            else if (!string.IsNullOrEmpty(successText)) Toast(successText);
        }

        // ---------- 입력 ----------

        void OnObjectClicked(PlacedObject obj)
        {
            var def = game.Catalog.GetPlaceable(obj.defId);
            if (def == null) return;

            if (def.kind == PlaceableKind.Plot && game.IsRipe(obj.id))
            {
                var crop = game.Catalog.GetCrop(obj.cropId);
                string text = crop != null
                    ? Texts.ItemName(game.Catalog, crop.harvestItemId) + " +" + crop.harvestAmount
                    : null;
                var harvested = game.Harvest(obj.id);
                Report(harvested, text);
                if (harvested == ActionResult.Ok)
                {
                    // 다 자란 밭을 누르면 수확만 한다. 심을 작물은 빈 밭을 다시 눌렀을 때 고른다.
                    if (panel == PanelKind.Selection) ClosePanel();
                    return;
                }
            }
            else if (def.kind == PlaceableKind.Facility && game.ReadyJobCount(obj.id) > 0)
            {
                Report(game.CollectCrafted(obj.id), "완성품을 창고에 넣었어요.");
            }

            selectedId = obj.id;
            world.SelectedObjectId = obj.id;
            Open(PanelKind.Selection);
        }

        void OnEmptyClicked()
        {
            if (panel == PanelKind.Selection) ClosePanel();
        }

        void Toggle(PanelKind kind)
        {
            if (panel == kind) ClosePanel();
            else Open(kind);
        }

        void Open(PanelKind kind)
        {
            if (kind != PanelKind.Selection)
            {
                selectedId = 0;
                world.SelectedObjectId = 0;
            }
            world.CancelMode();
            panel = kind;
            windowContent.anchoredPosition = Vector2.zero;
            rebuildNeeded = true;
        }

        void ClosePanel()
        {
            panel = PanelKind.None;
            selectedId = 0;
            world.SelectedObjectId = 0;
            dimRoot.SetActive(false);
            windowRect.gameObject.SetActive(false);
            sheetRoot.SetActive(false);
            tickers.Clear();
        }

        /// <summary>격자 줄 수에 맞춘 창 높이. 너무 길면 화면에 맞게 자르고 안에서 스크롤한다.</summary>
        static float WindowHeight(int itemCount, int columns, float cellHeight, float headerHeight)
        {
            int rows = Mathf.Max(1, (itemCount + columns - 1) / columns);
            float header = headerHeight > 0f ? headerHeight + 14f : 0f;
            return Mathf.Min(800f, 148f + header + rows * cellHeight + (rows - 1) * 16f);
        }

        void ShowWindow(string title, float width, float height)
        {
            sheetRoot.SetActive(false);
            dimRoot.SetActive(true);
            windowRect.gameObject.SetActive(true);
            windowRect.sizeDelta = new Vector2(width, height);
            windowTitle.text = title;
        }

        void ShowSheet()
        {
            dimRoot.SetActive(false);
            windowRect.gameObject.SetActive(false);
            sheetRoot.SetActive(true);
        }

        void ShowHeader(float height)
        {
            windowHeaderLayout.minHeight = height;
            windowHeaderLayout.preferredHeight = height;
            windowHeader.gameObject.SetActive(true);
        }

        // ---------- 패널 ----------

        string ComputeSignature()
        {
            var builder = new StringBuilder();
            builder.Append(panel).Append('|').Append(game.State.level).Append('|');

            if (panel == PanelKind.Selection)
            {
                var obj = game.FindObject(selectedId);
                if (obj == null) return "gone";
                builder.Append(obj.cropId).Append('|').Append(game.IsRipe(obj.id)).Append('|');
                builder.Append(obj.queue.Count).Append('|').Append(game.ReadyJobCount(obj.id));
            }
            else if (panel == PanelKind.Quiz)
            {
                builder.Append(quizCurrent != null ? quizCurrent.id : "-").Append('|').Append(quizPicked).Append('|');
                builder.Append(game.QuizLockSeconds > 0).Append('|').Append(game.QuizCount);
            }
            else if (panel == PanelKind.Orders)
            {
                foreach (var order in game.State.orders)
                {
                    builder.Append(order.id).Append(game.IsOrderAvailable(order) ? 'a' : 'w')
                        .Append(game.HasItems(order.items) ? 'y' : 'n').Append(',');
                }
            }

            return builder.ToString();
        }

        void RebuildPanel()
        {
            tickers.Clear();
            Ui.Clear(windowHeader);
            Ui.Clear(windowContent);
            Ui.Clear(windowFooter);
            Ui.Clear(sheetInfo);
            Ui.Clear(sheetBody);
            windowHeader.gameObject.SetActive(false);
            windowFooter.gameObject.SetActive(false);

            if (panel == PanelKind.Selection && game.FindObject(selectedId) == null)
            {
                ClosePanel();
                return;
            }

            switch (panel)
            {
                case PanelKind.Build:
                    BuildBuildPanel();
                    break;
                case PanelKind.Orders:
                    BuildOrdersPanel();
                    break;
                case PanelKind.Storage:
                    BuildStoragePanel();
                    break;
                case PanelKind.Residents:
                    BuildResidentsPanel();
                    break;
                case PanelKind.Expand:
                    BuildExpandPanel();
                    break;
                case PanelKind.Quiz:
                    BuildQuizPanel();
                    break;
                case PanelKind.Selection:
                    BuildSelectionPanel();
                    break;
                default:
                    return;
            }

            signature = ComputeSignature();
        }

        // ---------- 작은 부품 ----------

        static Sprite Art(string name)
        {
            return ArtLibrary.Get(name);
        }

        /// <summary>물건 그림. 가공품은 item_&lt;id&gt;, 수확물은 작물 그림을 그대로 쓴다.</summary>
        static Sprite ItemIcon(string itemId)
        {
            Sprite sprite = ArtLibrary.Get("item_" + itemId);
            if (sprite == null) sprite = ArtLibrary.Get("crop_" + itemId);
            return sprite;
        }

        /// <summary>소수 첫째 자리까지만 보여 주는 숫자. 1.0은 "1", 1.6은 "1.6".</summary>
        static string Number(float value)
        {
            return value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        }

        static string ResidentTitle(ResidentDef resident)
        {
            return resident.displayName + " (" + resident.species + ")";
        }

        static TextMeshProUGUI Text(Transform parent, string text, float size, Color color, bool bold = false,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            return Ui.Label("Text", parent, text, size, color, alignment, bold);
        }

        /// <summary>가운데로 모이는 가로 줄.</summary>
        static RectTransform CenterRow(Transform parent, float spacing)
        {
            var row = Ui.Rect("Row", parent);
            var group = Ui.Horizontal(row.gameObject, spacing, 0);
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childForceExpandWidth = false;
            return row;
        }

        /// <summary>세로로 쌓이는 카드. 내용은 가운데로 모인다.</summary>
        static RectTransform CardColumn(string name, Transform parent, Color color, float spacing)
        {
            var card = Ui.Box(name, parent, Ui.CardSprite, color);
            var group = Ui.Vertical(card.gameObject, spacing, 14);
            group.padding.bottom = 20;
            group.childAlignment = TextAnchor.UpperCenter;
            return card.rectTransform;
        }

        /// <summary>"당근 3/5"처럼 물건 그림, 이름, 가진 수/필요한 수를 한 줄로 보여 준다.</summary>
        void NeedLine(Transform parent, ItemAmount need, float iconSize, float fontSize, bool withName)
        {
            var row = CenterRow(parent, 6f);
            Ui.Icon("Icon", row, ItemIcon(need.itemId), iconSize);

            int have = game.CountOf(need.itemId);
            string text = (withName ? Texts.ItemName(game.Catalog, need.itemId) + "  " : "")
                + Ui.ColorTag(have >= need.amount ? Ui.Good : Ui.Bad) + have + "</color>/" + need.amount;
            Ui.Label("Text", row, text, fontSize, Ui.Ink, TextAlignmentOptions.Left, true);
        }

        // ---------- 건설 ----------

        void BuildBuildPanel()
        {
            var shown = new List<PlaceableDef>();
            foreach (var def in game.Catalog.placeables)
            {
                int tab = def.kind == PlaceableKind.Decor ? 1 : def.kind == PlaceableKind.Defense ? 2 : 0;
                if (tab == buildTab) shown.Add(def);
            }

            bool showGrades = buildTab == 2 && game.Catalog.grades.Count > 0;
            ShowWindow("건설", 1240f, WindowHeight(shown.Count, 5, 280f, 60f) + (showGrades ? 110f : 0f));
            ShowHeader(60f);
            BuildTab("TabFacility", "밭 · 시설", 0);
            BuildTab("TabDecor", "장식", 1);
            BuildTab("TabDefense", "방어", 2);

            if (showGrades) GradeChances();

            var grid = Ui.Grid("Grid", windowContent, 220f, 280f, 16f, 5);
            foreach (var def in shown) BuildCard(grid, def);
        }

        /// <summary>방어 탭 맨 위: 살 때 등급이 무작위로 정해진다는 안내와 등급별 확률.</summary>
        void GradeChances()
        {
            var box = Ui.Box("Grades", windowContent, Ui.CardSprite, Ui.Sand);
            Ui.Fixed(box.gameObject, 0f, 96f);
            var group = Ui.Vertical(box.gameObject, 4f, 12);
            group.padding.bottom = 18;
            group.childAlignment = TextAnchor.MiddleCenter;

            Text(box.transform, "살 때마다 등급이 무작위로 정해져요. 등급이 높을수록 더 세고 멀리 공격해요.", 22f, Ui.Ink);

            var builder = new StringBuilder();
            foreach (var grade in game.Catalog.grades)
            {
                if (grade == null || grade.weight <= 0) continue;
                if (builder.Length > 0) builder.Append("      ");
                builder.Append(Texts.GradeColorTag(grade)).Append(Texts.GradeStars(game.Catalog, grade)).Append("</color> ")
                    .Append(grade.displayName).Append(' ').Append(Number(game.GradeChancePercent(grade))).Append('%');
            }
            Text(box.transform, builder.ToString(), 24f, Ui.Ink, true);
        }

        void BuildTab(string name, string text, int index)
        {
            var tab = Ui.Button(name, windowHeader, text, () =>
            {
                buildTab = index;
                windowContent.anchoredPosition = Vector2.zero;
                rebuildNeeded = true;
            }, 60f, 24f, buildTab == index ? Ui.Orange : Ui.Sand);
            Ui.Fixed(tab.gameObject, 230f, 60f);
        }

        void BuildCard(Transform grid, PlaceableDef def)
        {
            bool locked = game.State.level < def.unlockLevel;
            bool maxed = def.maxCount > 0 && game.CountPlaced(def.id) >= def.maxCount;
            string defId = def.id;

            var card = Ui.ButtonBox("Build " + def.id, grid, Ui.CardSprite, Color.white, () =>
            {
                ClosePanel();
                world.BeginPlacing(defId);
            });
            card.interactable = !locked && !maxed;
            Transform inside = card.transform;

            var icon = Ui.Picture("Icon", inside, Art(def.kind == PlaceableKind.Plot ? "plot" : def.id));
            Ui.Put(icon.rectTransform, 0.5f, 1f, 0f, -14f, 176f, 130f);

            var name = Text(inside, def.displayName, 26f, Ui.Ink, true);
            Ui.Put(name.rectTransform, 0.5f, 1f, 0f, -150f, 204f, 36f);

            string detail = def.kind == PlaceableKind.Decor ? "매력 +" + def.charm
                : def.kind == PlaceableKind.Facility ? "생산 시설"
                : def.kind == PlaceableKind.Defense ? "사거리 " + Number(def.attackRange) + " · 공격 " + def.attackDamage
                : "작물을 심는 곳";
            var detailLabel = Text(inside, detail, 20f, Ui.Muted);
            Ui.Put(detailLabel.rectTransform, 0.5f, 1f, 0f, -186f, 204f, 28f);

            TextMeshProUGUI price;
            if (locked) price = Ui.Chip("Price", inside, Art("ui_icon_lock"), "Lv " + def.unlockLevel, Ui.Gray, 46f, 22f);
            else if (maxed) price = Ui.Chip("Price", inside, null, "설치 완료", Ui.Gray, 46f, 22f);
            else price = Ui.Chip("Price", inside, Art("ui_icon_coin"), def.cost.ToString("N0"), Ui.Yellow, 46f, 24f);
            Ui.Put((RectTransform)price.transform.parent, 0.5f, 0f, 0f, 22f, 170f, 46f);
        }

        // ---------- 주문 ----------

        void BuildOrdersPanel()
        {
            ShowWindow("주문", 1240f, WindowHeight(game.State.orders.Count, 4, 480f, 0f));

            // 물건 세 가지짜리 주문까지 한 카드에 다 들어가는 높이.
            var grid = Ui.Grid("Grid", windowContent, 280f, 480f, 16f, 4);
            foreach (var order in game.State.orders) OrderCard(grid, order);
        }

        void OrderCard(Transform grid, Order order)
        {
            int orderId = order.id;

            if (!game.IsOrderAvailable(order))
            {
                var waitCard = CardColumn("Waiting", grid, Ui.Sand, 8f);
                Ui.Spacer(waitCard, 0f, 1f);
                Ui.Icon("Clock", waitCard, Art("ui_icon_clock"), 84f);
                Text(waitCard, "다음 주문까지", 22f, Ui.Muted);
                var waiting = Text(waitCard, "", 32f, Ui.Ink, true);
                Ui.Spacer(waitCard, 0f, 1f);

                long availableAt = order.availableAt;
                Action tick = () =>
                {
                    if (waiting != null) waiting.text = Texts.Duration(availableAt - game.Now);
                };
                tick();
                tickers.Add(tick);
                return;
            }

            var card = CardColumn("Order", grid, Color.white, 6f);
            var customer = game.Catalog.GetResident(order.customerId);
            Ui.Icon("Face", card, customer != null ? Art("resident_" + customer.id) : null, 96f);
            Text(card, customer != null ? customer.displayName + "의 주문" : "손님의 주문", 24f, Ui.Ink, true);

            foreach (var need in order.items) NeedLine(card, need, 38f, 22f, true);

            Ui.Spacer(card, 0f, 1f);

            var reward = CenterRow(card, 6f);
            Ui.Icon("Coin", reward, Art("ui_icon_coin"), 34f);
            Ui.Label("Coins", reward, order.rewardCoins.ToString("N0"), 24f, Ui.Ink, TextAlignmentOptions.Left, true);
            Ui.Fixed(Ui.Rect("Gap", reward).gameObject, 14f, 1f);
            Ui.Icon("Xp", reward, Art("ui_icon_star"), 34f);
            Ui.Label("XpAmount", reward, order.rewardXp.ToString(), 24f, Ui.Ink, TextAlignmentOptions.Left, true);

            var deliver = Ui.Button("Deliver", card, "납품", () =>
            {
                Report(game.DeliverOrder(orderId), "납품 완료!");
            }, 60f, 26f, Ui.Green);
            deliver.interactable = game.HasItems(order.items);

            Ui.Button("Discard", card, "거절", () =>
            {
                Report(game.DiscardOrder(orderId), null);
            }, 44f, 20f, Ui.Sand);
        }

        // ---------- 창고 ----------

        void BuildStoragePanel()
        {
            ShowWindow("창고", 1240f, WindowHeight(game.State.inventory.Count, 6, 260f, 64f));
            ShowHeader(64f);

            int stored = game.StoredTotal;
            int capacity = game.State.storageCapacity;

            var bar = Ui.Box("Capacity", windowHeader, Ui.CapsuleSprite, Ui.Sand);
            Ui.Fixed(bar.gameObject, 0f, 54f).flexibleWidth = 1f;
            var fill = BarFill(bar.transform, stored >= capacity ? Ui.Red : Ui.Blue, 5f);
            SetBar(fill, capacity > 0 ? (float)stored / capacity : 0f);
            var barText = Text(bar.transform, "보관  " + stored + " / " + capacity, 24f, Ui.Ink, true);
            Ui.Stretch(barText.rectTransform, 0f, 0f, 0f, 0f);

            var upgrade = Ui.Button("Upgrade", windowHeader,
                "창고 넓히기   " + game.StorageUpgradeCost.ToString("N0") + " 코인", () =>
            {
                Report(game.UpgradeStorage(), "창고가 넓어졌어요!");
            }, 62f, 22f, Ui.Green);
            Ui.Fixed(upgrade.gameObject, 400f, 62f);

            if (game.State.inventory.Count == 0)
            {
                Text(windowContent, "\n창고가 비어 있어요.", 26f, Ui.Muted);
                return;
            }

            var grid = Ui.Grid("Grid", windowContent, 180f, 260f, 16f, 6);
            foreach (var entry in game.State.inventory)
            {
                var def = game.Catalog.GetItem(entry.itemId);
                string itemId = entry.itemId;
                int value = def != null ? def.value : 0;

                var card = CardColumn("Item", grid, Color.white, 2f);
                Ui.Icon("Icon", card, ItemIcon(itemId), 96f);
                Text(card, Texts.ItemName(game.Catalog, itemId), 22f, Ui.Ink, true);
                Text(card, "x " + entry.amount, 24f, Ui.Muted, true);
                Ui.Spacer(card, 0f, 1f);
                Ui.Button("Sell", card, "판매 +" + value, () =>
                {
                    Report(game.SellItem(itemId, 1), null);
                }, 48f, 20f, Ui.Yellow);
            }
        }

        // ---------- 주민 ----------

        void BuildResidentsPanel()
        {
            ShowWindow("주민", 1240f, WindowHeight(game.Catalog.residents.Count, 5, 410f, 34f));
            ShowHeader(34f);
            var hint = Ui.Label("Hint", windowHeader, "시설을 눌러 주민을 배치하면 그 시설의 생산이 빨라져요.",
                22f, Ui.Muted, TextAlignmentOptions.Center);
            hint.gameObject.AddComponent<ULayoutElement>().flexibleWidth = 1f;

            var grid = Ui.Grid("Grid", windowContent, 220f, 410f, 16f, 5);
            foreach (var resident in game.Catalog.residents)
            {
                bool unlocked = game.IsResidentUnlocked(resident.id);
                string residentId = resident.id;

                var card = CardColumn("Resident", grid, unlocked ? Color.white : Ui.Sand, 4f);
                var face = Ui.Icon("Face", card, Art("resident_" + resident.id), 140f);
                Text(card, resident.displayName, 28f, unlocked ? Ui.Ink : Ui.Muted, true);
                Text(card, resident.species, 20f, Ui.Muted);

                if (!unlocked)
                {
                    face.color = new Color(1f, 1f, 1f, 0.4f);
                    Ui.Spacer(card, 0f, 1f);
                    Ui.Icon("Lock", card, Art("ui_icon_lock"), 48f);
                    Text(card, "Lv " + resident.unlockLevel + "에 마을로 와요", 20f, Ui.Muted);
                    Ui.Spacer(card, 0f, 1f);
                    continue;
                }

                Text(card, "생산 시간 -" + resident.craftSpeedBonusPercent + "%", 22f, Ui.Good, true);

                var workplace = game.FindObject(game.AssignedObjectOf(resident.id));
                var workplaceDef = workplace != null ? game.Catalog.GetPlaceable(workplace.defId) : null;
                Ui.Spacer(card, 0f, 1f);
                if (workplaceDef == null)
                {
                    Text(card, "마을을 산책하는 중", 20f, Ui.Muted);
                    continue;
                }

                Text(card, workplaceDef.displayName + "에서 일하는 중", 20f, Ui.Ink);
                Ui.Button("Rest " + resident.id, card, "쉬게 하기", () =>
                {
                    Report(game.UnassignResident(residentId), null);
                }, 50f, 20f, Ui.Yellow);
            }
        }

        // ---------- 문제 풀기 ----------

        void BuildQuizPanel()
        {
            ShowWindow("문제 풀기", 1040f, 700f);

            if (game.QuizCount == 0)
            {
                Text(windowContent, "\n아직 문제가 없어요.\nResources/quiz.json 에 문제를 넣어 주세요.", 28f, Ui.Muted);
                return;
            }

            // 보여 줄 문제가 없으면 새로 뽑는다. 틀린 뒤 기다리는 중이면 뽑히지 않는다.
            if (quizCurrent == null)
            {
                quizCurrent = game.DrawQuiz();
                quizPicked = 0;
                quizOrder.Clear();
                if (quizCurrent != null)
                {
                    for (int i = 1; i <= quizCurrent.choices.Count; i++) quizOrder.Add(i);
                    for (int i = quizOrder.Count - 1; i > 0; i--)
                    {
                        int j = UnityEngine.Random.Range(0, i + 1);
                        int swap = quizOrder[i];
                        quizOrder[i] = quizOrder[j];
                        quizOrder[j] = swap;
                    }
                }
            }

            ShowHeader(48f);
            var solved = Ui.Label("Solved", windowHeader, "맞힌 문제 " + game.State.quizSolved + "개", 24f, Ui.Muted,
                TextAlignmentOptions.Left, true);
            solved.gameObject.AddComponent<ULayoutElement>().flexibleWidth = 1f;

            if (quizCurrent == null)
            {
                var box = Ui.Rect("Wait", windowContent);
                var group = Ui.Vertical(box.gameObject, 12f, 0);
                group.childAlignment = TextAnchor.UpperCenter;
                Ui.Spacer(box, 0f, 0f).gameObject.GetComponent<ULayoutElement>().minHeight = 40f;
                Ui.Icon("Clock", box, Art("ui_icon_clock"), 110f);
                Text(box, "틀렸어요. 잠깐 쉬었다가 다시 풀어요.", 28f, Ui.Ink, true);
                var waiting = Text(box, "", 40f, Ui.Ink, true);
                Action waitTick = () =>
                {
                    if (waiting != null) waiting.text = Texts.Duration(game.QuizLockSeconds);
                };
                waitTick();
                tickers.Add(waitTick);
                return;
            }

            var quiz = quizCurrent;
            var reward = Ui.Chip("Reward", windowHeader, Art("ui_icon_coin"),
                "정답 보상  " + game.QuizRewardFor(quiz).ToString("N0"), Ui.Yellow, 48f, 24f);
            Ui.Fixed(reward.transform.parent.gameObject, 280f, 48f);

            var card = Ui.Box("Question", windowContent, Ui.CardSprite, Color.white);
            Ui.Fixed(card.gameObject, 0f, 190f);
            var question = Ui.Label("Text", card.transform, quiz.question, 36f, Ui.Ink, TextAlignmentOptions.Center, true);
            Ui.Stretch(question.rectTransform, 28f, 22f, 28f, 16f);

            bool answered = quizPicked != 0;
            var grid = Ui.Grid("Choices", windowContent, 476f, 96f, 16f, 2);
            for (int i = 0; i < quizOrder.Count; i++)
            {
                int number = quizOrder[i];
                Color color = Ui.Sand;
                if (answered && number == quiz.answer) color = Ui.Green;
                else if (answered && number == quizPicked) color = Ui.Red;

                Action onClick = null;
                if (!answered) onClick = () => AnswerQuiz(number);
                Ui.Button("Choice " + (i + 1), grid, quiz.choices[number - 1], onClick, 96f, 30f, color);
            }

            windowFooter.gameObject.SetActive(true);
            if (!answered)
            {
                var hint = Ui.Label("Hint", windowFooter, "알맞은 답을 골라 보세요.", 24f, Ui.Muted, TextAlignmentOptions.Center);
                hint.gameObject.AddComponent<ULayoutElement>().flexibleWidth = 1f;
                return;
            }

            string resultText = quizCorrect
                ? "정답이에요!  +" + quizReward.ToString("N0") + " 코인"
                : "아쉬워요. 정답은 「" + quiz.choices[quiz.answer - 1] + "」";
            var result = Ui.Label("Result", windowFooter, resultText, 28f, quizCorrect ? Ui.Good : Ui.Bad,
                TextAlignmentOptions.Left, true);
            result.gameObject.AddComponent<ULayoutElement>().flexibleWidth = 1f;

            var next = Ui.Button("NextQuiz", windowFooter, "다음 문제", () =>
            {
                quizCurrent = null;
                quizPicked = 0;
                rebuildNeeded = true;
            }, 64f, 26f, Ui.Orange);
            Ui.Fixed(next.gameObject, 300f, 64f);

            // 틀렸으면 잠깐 기다려야 다음 문제로 넘어갈 수 있다.
            Action tick = () =>
            {
                if (next == null) return;
                long wait = game.QuizLockSeconds;
                next.interactable = wait <= 0;
                Ui.SetButtonText(next, wait > 0 ? "다음 문제 (" + wait + ")" : "다음 문제");
            };
            tick();
            tickers.Add(tick);
        }

        void AnswerQuiz(int number)
        {
            if (quizCurrent == null || quizPicked != 0) return;

            bool correct;
            int reward;
            var result = game.AnswerQuiz(quizCurrent.id, number, out correct, out reward);
            if (result != ActionResult.Ok)
            {
                Toast(Texts.Describe(result));
                return;
            }

            quizPicked = number;
            quizCorrect = correct;
            quizReward = reward;
            rebuildNeeded = true;
        }

        // ---------- 마을 확장 ----------

        void BuildExpandPanel()
        {
            ShowWindow("마을 확장", 760f, 520f);

            var box = Ui.Rect("Box", windowContent);
            var group = Ui.Vertical(box.gameObject, 10f, 0);
            group.childAlignment = TextAnchor.UpperCenter;

            Ui.Icon("Icon", box, Art("ui_icon_expand"), 120f);

            if (!game.CanExpandFurther)
            {
                Text(box, "마을이 이미 가장 넓어요.", 28f, Ui.Ink, true);
                return;
            }

            var state = game.State;
            long cost = game.LandExpandCost;
            int needLevel = game.LandExpandRequiredLevel;

            Text(box, "마을 땅을 한 단계 더 넓혀요.", 28f, Ui.Ink, true);
            Text(box, "지금 크기  " + state.landWidth + " × " + state.landHeight + " 칸", 22f, Ui.Muted);

            var price = CenterRow(box, 8f);
            Ui.Icon("Coin", price, Art("ui_icon_coin"), 44f);
            Ui.Label("Cost", price, cost.ToString("N0"), 32f, state.coins >= cost ? Ui.Ink : Ui.Bad,
                TextAlignmentOptions.Left, true);

            Text(box, "필요 레벨  Lv " + needLevel, 24f, state.level >= needLevel ? Ui.Good : Ui.Bad, true);

            windowFooter.gameObject.SetActive(true);
            Ui.Button("ConfirmExpand", windowFooter, "확장하기", () =>
            {
                var result = game.ExpandLand();
                Report(result, "마을이 넓어졌어요!");
                if (result == ActionResult.Ok) ClosePanel();
            }, 64f, 28f, Ui.Green);
        }

        // ---------- 선택한 것 ----------

        void BuildSelectionPanel()
        {
            var obj = game.FindObject(selectedId);
            var def = game.Catalog.GetPlaceable(obj.defId);
            if (def == null)
            {
                ClosePanel();
                return;
            }

            if (def.kind == PlaceableKind.Facility)
            {
                BuildFacilityWindow(obj, def);
                return;
            }

            ShowSheet();
            Ui.Label("Title", sheetInfo, def.displayName, 34f, Ui.Ink, TextAlignmentOptions.Left, true);

            if (def.kind == PlaceableKind.Plot) BuildPlotSheet(obj);
            else if (def.kind == PlaceableKind.Defense) BuildDefenseSheet(obj, def);
            else BuildDecorSheet(def);

            Ui.Spacer(sheetInfo, 0f, 1f);
            var actions = Ui.Rect("Actions", sheetInfo);
            Ui.Horizontal(actions.gameObject, 8f, 0);
            MoveAndRemoveButtons(actions, obj.id, def, 54f, 20f);
        }

        void MoveAndRemoveButtons(Transform parent, int objectId, PlaceableDef def, float height, float fontSize)
        {
            if (def.rotations > 1)
            {
                Ui.Button("Rotate", parent, "돌리기", () =>
                {
                    var result = game.Rotate(objectId);
                    if (result == ActionResult.InvalidPlacement) Toast("돌릴 자리가 부족해요.");
                    else Report(result, null);
                }, height, fontSize, Ui.Yellow);
            }

            Ui.Button("Move", parent, "옮기기", () =>
            {
                ClosePanel();
                world.BeginMoving(objectId);
            }, height, fontSize, Ui.Blue);

            long refund = (long)def.cost * game.Catalog.balance.sellBackPercent / 100;
            Ui.Button("Remove", parent, "철거 +" + refund.ToString("N0"), () =>
            {
                Report(game.Remove(objectId), "철거했어요.");
            }, height, fontSize, Ui.Red);
        }

        void InfoLine(string text, Color color)
        {
            Ui.Label("Line", sheetInfo, text, 22f, color, TextAlignmentOptions.Left);
        }

        void BuildDecorSheet(PlaceableDef def)
        {
            InfoLine("마을을 꾸미는 장식이에요.", Ui.Muted);

            var row = Ui.Rect("Row", sheetBody);
            var group = Ui.Horizontal(row.gameObject, 24f, 0);
            group.childForceExpandWidth = false;
            Ui.Icon("Icon", row, Art(def.id), 170f);

            var column = Ui.Rect("Column", row);
            Ui.Vertical(column.gameObject, 6f, 0);
            var heart = CenterRow(column, 8f);
            heart.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Icon("Heart", heart, Art("ui_icon_heart"), 44f);
            Ui.Label("Charm", heart, "매력 +" + def.charm, 30f, Ui.Ink, TextAlignmentOptions.Left, true);
            Ui.Label("About", column, "매력이 높을수록 마을이 사랑받아요.", 22f, Ui.Muted, TextAlignmentOptions.Left);
        }

        void BuildDefenseSheet(PlacedObject obj, PlaceableDef def)
        {
            var grade = game.GradeOf(obj);
            if (grade != null)
            {
                Ui.Label("Grade", sheetInfo, Texts.GradeColorTag(grade) + Texts.GradeStars(game.Catalog, grade)
                    + "</color> " + grade.displayName + " 등급", 24f, Ui.Ink, TextAlignmentOptions.Left, true);
            }
            InfoLine("사거리 안에 들어온 침략자를 알아서 공격해요.", Ui.Muted);

            var row = Ui.Rect("Row", sheetBody);
            var group = Ui.Horizontal(row.gameObject, 24f, 0);
            group.childForceExpandWidth = false;
            Ui.Icon("Icon", row, Art(ArtLibrary.Variant(def.id, grade != null ? grade.id : null)), 170f);

            var column = Ui.Rect("Column", row);
            Ui.Vertical(column.gameObject, 6f, 0);
            var title = CenterRow(column, 8f);
            title.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            Ui.Icon("Shield", title, Art("ui_icon_shield"), 44f);
            Ui.Label("Damage", title, "공격력 " + game.AttackDamageOf(obj), 30f, Ui.Ink, TextAlignmentOptions.Left, true);
            Ui.Label("Range", column, "사거리 " + Number(game.AttackRangeOf(obj)) + "칸 (바닥의 밝은 원)", 22f, Ui.Ink,
                TextAlignmentOptions.Left);
            Ui.Label("Speed", column, game.AttackIntervalOf(obj).ToString("0.##",
                System.Globalization.CultureInfo.InvariantCulture) + "초마다 한 번 공격", 22f, Ui.Muted,
                TextAlignmentOptions.Left);
        }

        void BuildPlotSheet(PlacedObject plot)
        {
            int objectId = plot.id;

            if (string.IsNullOrEmpty(plot.cropId))
            {
                InfoLine("심을 작물을 고르세요.", Ui.Muted);

                var scroll = Ui.ScrollView("Scroll", sheetBody, 8f);
                var grid = Ui.Grid("Grid", scroll, 150f, 210f, 12f, 5);
                foreach (var crop in game.Catalog.crops) CropCard(grid, crop, objectId);
                return;
            }

            var planted = game.Catalog.GetCrop(plot.cropId);
            string cropName = planted != null ? planted.displayName : plot.cropId;

            var row = Ui.Rect("Row", sheetBody);
            var group = Ui.Horizontal(row.gameObject, 24f, 0);
            group.childForceExpandWidth = false;
            Ui.Icon("Icon", row, Art("crop_" + plot.cropId), 170f);

            var column = Ui.Rect("Column", row);
            column.gameObject.AddComponent<ULayoutElement>().flexibleWidth = 1f;
            Ui.Vertical(column.gameObject, 8f, 0);

            if (game.IsRipe(objectId))
            {
                InfoLine(cropName + "이(가) 다 자랐어요!", Ui.Good);
                Ui.Label("Ready", column, "수확할 수 있어요.", 28f, Ui.Ink, TextAlignmentOptions.Left, true);
                var harvest = Ui.Button("Harvest", column, "수확", () =>
                {
                    // 수확하고 나면 시트를 닫는다. 심을 작물은 빈 밭을 다시 눌렀을 때 고른다.
                    var harvested = game.Harvest(objectId);
                    Report(harvested, null);
                    if (harvested == ActionResult.Ok) ClosePanel();
                }, 66f, 28f, Ui.Green);
                harvest.GetComponent<ULayoutElement>().flexibleWidth = 0f;
                return;
            }

            InfoLine(cropName + " 자라는 중", Ui.Muted);
            Ui.Label("Until", column, "수확까지", 22f, Ui.Muted, TextAlignmentOptions.Left);
            var remaining = Ui.Label("Time", column, "", 40f, Ui.Ink, TextAlignmentOptions.Left, true);

            var bar = Ui.Box("Progress", column, Ui.CapsuleSprite, Ui.Sand);
            Ui.Fixed(bar.gameObject, 0f, 36f);
            var fill = BarFill(bar.transform, Ui.Green, 4f);

            Action tick = () =>
            {
                if (remaining == null) return;
                remaining.text = Texts.Duration(game.SecondsUntilRipe(objectId));
                SetBar(fill, game.GrowthProgress(objectId));
            };
            tick();
            tickers.Add(tick);
        }

        void CropCard(Transform grid, CropDef crop, int objectId)
        {
            bool locked = game.State.level < crop.unlockLevel;
            string cropId = crop.id;

            var card = Ui.ButtonBox("Plant " + crop.id, grid, Ui.CardSprite, Color.white, () =>
            {
                Report(game.Plant(objectId, cropId), null);
            });
            card.interactable = !locked;
            Transform inside = card.transform;

            var icon = Ui.Picture("Icon", inside, Art("crop_" + crop.id));
            Ui.Put(icon.rectTransform, 0.5f, 1f, 0f, -10f, 120f, 84f);

            var name = Text(inside, crop.displayName, 22f, Ui.Ink, true);
            Ui.Put(name.rectTransform, 0.5f, 1f, 0f, -96f, 140f, 30f);

            var time = CenterRow(inside, 4f);
            Ui.Put(time, 0.5f, 1f, 0f, -128f, 140f, 28f);
            Ui.Icon("Clock", time, Art("ui_icon_clock"), 24f);
            Ui.Label("Time", time, Texts.Duration(crop.growSeconds), 20f, Ui.Muted, TextAlignmentOptions.Left, true);

            TextMeshProUGUI price;
            if (locked) price = Ui.Chip("Price", inside, Art("ui_icon_lock"), "Lv " + crop.unlockLevel, Ui.Gray, 40f, 20f);
            else if (crop.seedCost <= 0) price = Ui.Chip("Price", inside, null, "무료", Ui.Green, 40f, 20f);
            else price = Ui.Chip("Price", inside, Art("ui_icon_coin"), crop.seedCost.ToString(), Ui.Yellow, 40f, 20f);
            Ui.Put((RectTransform)price.transform.parent, 0.5f, 0f, 0f, 18f, 118f, 40f);
        }

        // ---------- 생산 시설 ----------

        void BuildFacilityWindow(PlacedObject facility, PlaceableDef def)
        {
            int objectId = facility.id;
            int recipeCount = 0;
            foreach (var recipe in game.Catalog.recipes)
            {
                if (recipe.facilityId == def.id) recipeCount++;
            }
            // 아래쪽 버튼 줄(64 + 간격 14)만큼 더 높인다.
            ShowWindow(def.displayName, 1240f, Mathf.Min(800f, WindowHeight(recipeCount, 5, 310f, 176f) + 78f));

            ShowHeader(176f);
            WorkerCard(objectId);
            QueueCard(facility, def);

            var grid = Ui.Grid("Grid", windowContent, 220f, 310f, 16f, 5);
            foreach (var recipe in game.Catalog.recipes)
            {
                if (recipe.facilityId == def.id) RecipeCard(grid, recipe, objectId);
            }

            windowFooter.gameObject.SetActive(true);
            if (game.ReadyJobCount(objectId) > 0)
            {
                var collect = Ui.Button("Collect", windowFooter, "완성품 수거", () =>
                {
                    Report(game.CollectCrafted(objectId), "완성품을 창고에 넣었어요.");
                }, 64f, 24f, Ui.Green);
                Ui.Fixed(collect.gameObject, 300f, 64f);
            }
            Ui.Spacer(windowFooter, 1f, 0f);

            var actions = Ui.Rect("Actions", windowFooter);
            Ui.Fixed(actions.gameObject, def.rotations > 1 ? 640f : 460f, 64f);
            Ui.Horizontal(actions.gameObject, 12f, 0);
            MoveAndRemoveButtons(actions, objectId, def, 64f, 22f);
        }

        /// <summary>지금 일하는 주민과, 대신 배치할 수 있는 주민들.</summary>
        void WorkerCard(int objectId)
        {
            var card = Ui.Box("Worker", windowHeader, Ui.CardSprite, Color.white);
            Ui.Fixed(card.gameObject, 700f, 176f);
            var group = Ui.Horizontal(card.gameObject, 8f, 14);
            group.padding.bottom = 20;
            group.childForceExpandWidth = false;

            var worker = game.ResidentAt(objectId);
            var info = Ui.Rect("Info", card.transform);

            if (worker != null)
            {
                string workerId = worker.id;
                Ui.Icon("Face", card.transform, Art("resident_" + worker.id), 96f).transform.SetAsFirstSibling();
                Ui.Label("Name", info, worker.displayName + " 근무 중", 24f, Ui.Ink, TextAlignmentOptions.Left, true);
                Ui.Label("Bonus", info, "생산 시간 -" + worker.craftSpeedBonusPercent + "%", 20f, Ui.Good,
                    TextAlignmentOptions.Left, true);
                Ui.Button("Rest " + worker.id, info, "쉬게 하기", () =>
                {
                    Report(game.UnassignResident(workerId), null);
                }, 46f, 20f, Ui.Yellow);
            }
            else
            {
                var empty = Ui.Icon("Face", card.transform, Art("ui_icon_residents"), 96f);
                empty.color = new Color(1f, 1f, 1f, 0.45f);
                empty.transform.SetAsFirstSibling();
                Ui.Label("Name", info, "일꾼 없음", 24f, Ui.Ink, TextAlignmentOptions.Left, true);
                Ui.Label("Bonus", info, "주민을 배치하면\n생산이 빨라져요.", 20f, Ui.Muted, TextAlignmentOptions.Left);
            }

            Ui.Fixed(info.gameObject, 160f, 0f);
            Ui.Vertical(info.gameObject, 4f, 0);

            Ui.Spacer(card.transform, 1f, 0f);

            foreach (var resident in game.UnlockedResidents())
            {
                if (worker != null && resident.id == worker.id) continue;

                string residentId = resident.id;
                string residentName = resident.displayName;
                bool busyElsewhere = game.AssignedObjectOf(resident.id) != 0;

                var assign = Ui.ButtonBox("Assign " + resident.id, card.transform, Ui.CardSprite, Ui.Sand, () =>
                {
                    Report(game.AssignResident(residentId, objectId), residentName + "가 일을 시작했어요.");
                });
                Ui.Fixed(assign.gameObject, 70f, 124f);

                var face = Ui.Picture("Face", assign.transform, Art("resident_" + resident.id));
                Ui.Put(face.rectTransform, 0.5f, 1f, 0f, -8f, 58f, 58f);
                var bonus = Text(assign.transform, "-" + resident.craftSpeedBonusPercent + "%", 18f, Ui.Ink, true);
                Ui.Put(bonus.rectTransform, 0.5f, 0f, 0f, 32f, 68f, 24f);
                var state = Text(assign.transform, busyElsewhere ? "데려오기" : "배치", 15f, Ui.Muted);
                Ui.Put(state.rectTransform, 0.5f, 0f, 0f, 12f, 68f, 20f);
            }
        }

        void QueueCard(PlacedObject facility, PlaceableDef def)
        {
            var card = Ui.Box("Queue", windowHeader, Ui.CardSprite, Color.white);
            Ui.Fixed(card.gameObject, 0f, 176f).flexibleWidth = 1f;
            var group = Ui.Vertical(card.gameObject, 6f, 14);
            group.padding.bottom = 20;

            Ui.Label("Title", card.transform, "대기열  " + facility.queue.Count + " / " + def.queueSlots,
                22f, Ui.Ink, TextAlignmentOptions.Left, true);

            var row = Ui.Rect("Slots", card.transform);
            var rowGroup = Ui.Horizontal(row.gameObject, 8f, 0);
            rowGroup.childForceExpandWidth = false;

            for (int i = 0; i < def.queueSlots; i++)
            {
                var slot = Ui.Box("Slot", row, Ui.CardSprite, Ui.Sand);
                Ui.Fixed(slot.gameObject, 110f, 104f);

                if (i >= facility.queue.Count)
                {
                    var blank = Text(slot.transform, "빈 칸", 18f, Ui.Muted);
                    Ui.Stretch(blank.rectTransform, 0f, 6f, 0f, 0f);
                    continue;
                }

                var job = facility.queue[i];
                var recipe = game.Catalog.GetRecipe(job.recipeId);
                var icon = Ui.Picture("Icon", slot.transform, recipe != null ? ItemIcon(recipe.outputItemId) : null);
                Ui.Put(icon.rectTransform, 0.5f, 1f, 0f, -8f, 58f, 58f);

                var time = Text(slot.transform, "", 20f, Ui.Ink, true);
                Ui.Put(time.rectTransform, 0.5f, 0f, 0f, 12f, 104f, 26f);

                long finishAt = job.finishAt;
                Action tick = () =>
                {
                    if (time == null) return;
                    long remaining = finishAt - game.Now;
                    time.text = remaining <= 0 ? "완성!" : Texts.Duration(remaining);
                    time.color = remaining <= 0 ? Ui.Good : Ui.Ink;
                };
                tick();
                tickers.Add(tick);
            }
        }

        void RecipeCard(Transform grid, RecipeDef recipe, int objectId)
        {
            bool locked = game.State.level < recipe.unlockLevel;
            string recipeId = recipe.id;

            var card = Ui.ButtonBox("Craft " + recipe.id, grid, Ui.CardSprite, Color.white, () =>
            {
                Report(game.StartCraft(objectId, recipeId), null);
            });
            card.interactable = !locked;
            Transform inside = card.transform;

            var icon = Ui.Picture("Icon", inside, ItemIcon(recipe.outputItemId));
            Ui.Put(icon.rectTransform, 0.5f, 1f, 0f, -12f, 120f, 100f);

            var name = Text(inside, Texts.ItemName(game.Catalog, recipe.outputItemId), 24f, Ui.Ink, true);
            Ui.Put(name.rectTransform, 0.5f, 1f, 0f, -116f, 204f, 34f);

            float y = -154f;
            foreach (var need in recipe.inputs)
            {
                var holder = Ui.Rect("Need", inside);
                Ui.Put(holder, 0.5f, 1f, 0f, y, 204f, 32f);
                var group = Ui.Vertical(holder.gameObject, 0f, 0);
                group.childAlignment = TextAnchor.MiddleCenter;
                NeedLine(holder, need, 30f, 20f, true);
                y -= 32f;
            }

            TextMeshProUGUI chip;
            if (locked) chip = Ui.Chip("Time", inside, Art("ui_icon_lock"), "Lv " + recipe.unlockLevel, Ui.Gray, 44f, 22f);
            else chip = Ui.Chip("Time", inside, Art("ui_icon_clock"),
                Texts.Duration(game.CraftSecondsFor(objectId, recipe)), Ui.Blue, 44f, 22f);
            Ui.Put((RectTransform)chip.transform.parent, 0.5f, 0f, 0f, 22f, 160f, 44f);
        }
    }
}
