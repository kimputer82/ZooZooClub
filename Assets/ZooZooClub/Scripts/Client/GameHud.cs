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
using ULayoutElement = UnityEngine.UI.LayoutElement;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 화면 UI. 위쪽 상태 표시줄, 아래쪽 메뉴, 오른쪽 패널(건설·주문·창고·선택한 대상)로 이루어진다.
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
            Selection
        }

        Game game;
        WorldView world;

        TextMeshProUGUI levelText;
        TextMeshProUGUI coinsText;
        TextMeshProUGUI gemsText;
        TextMeshProUGUI storageText;
        TextMeshProUGUI charmText;
        UButton expandButton;

        GameObject toastRoot;
        TextMeshProUGUI toastText;
        float toastUntil;

        GameObject hintRoot;
        TextMeshProUGUI hintText;

        GameObject panelRoot;
        TextMeshProUGUI panelTitle;
        RectTransform panelContent;

        PanelKind panel;
        int selectedId;
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
            world.ObjectClicked += OnObjectClicked;
            world.EmptyClicked += OnEmptyClicked;
            world.Message += Toast;

            RefreshTopBar();
        }

        void OnDestroy()
        {
            if (game != null)
            {
                game.Changed -= OnGameChanged;
                game.LeveledUp -= OnLeveledUp;
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

            Transform root = canvasGo.transform;
            BuildTopBar(root);
            BuildBottomBar(root);
            BuildSidePanel(root);
            BuildToast(root);
            BuildHint(root);
        }

        void BuildTopBar(Transform root)
        {
            var bar = Ui.Panel("TopBar", root, Ui.BarColor);
            var rect = bar.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 72f);
            Ui.Horizontal(bar.gameObject, 16f, 18);

            levelText = Ui.Label("Level", bar.transform, "", 28f, Ui.TextColor);
            coinsText = Ui.Label("Coins", bar.transform, "", 28f, Ui.TextColor);
            gemsText = Ui.Label("Gems", bar.transform, "", 28f, Ui.TextColor);
            storageText = Ui.Label("Storage", bar.transform, "", 28f, Ui.TextColor);
            charmText = Ui.Label("Charm", bar.transform, "", 28f, Ui.TextColor);
        }

        void BuildBottomBar(Transform root)
        {
            var bar = Ui.Panel("BottomBar", root, Ui.BarColor);
            var rect = bar.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 96f);
            Ui.Horizontal(bar.gameObject, 16f, 18);

            Ui.Button("BuildButton", bar.transform, "건설", () => Toggle(PanelKind.Build), 60f, 28f);
            Ui.Button("OrdersButton", bar.transform, "주문", () => Toggle(PanelKind.Orders), 60f, 28f);
            Ui.Button("StorageButton", bar.transform, "창고", () => Toggle(PanelKind.Storage), 60f, 28f);
            Ui.Button("ResidentsButton", bar.transform, "주민", () => Toggle(PanelKind.Residents), 60f, 28f);
            expandButton = Ui.Button("ExpandButton", bar.transform, "마을 확장", OnExpandClicked, 60f, 28f);
        }

        void BuildSidePanel(Transform root)
        {
            var image = Ui.Panel("SidePanel", root, Ui.PanelColor);
            var rect = image.rectTransform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(-556f, 112f);
            rect.offsetMax = new Vector2(-16f, -88f);
            Ui.Vertical(image.gameObject, 12f, 18);
            panelRoot = image.gameObject;

            var header = Ui.Rect("Header", image.transform);
            var headerGroup = Ui.Horizontal(header.gameObject, 8f, 0);
            headerGroup.childForceExpandWidth = false;

            panelTitle = Ui.Label("Title", header, "", 32f, Ui.TextColor);
            panelTitle.gameObject.AddComponent<ULayoutElement>().flexibleWidth = 1f;

            var close = Ui.Button("CloseButton", header, "닫기", ClosePanel, 48f, 22f);
            var closeLayout = close.GetComponent<ULayoutElement>();
            closeLayout.flexibleWidth = 0f;
            closeLayout.minWidth = 96f;
            closeLayout.preferredWidth = 96f;

            panelContent = Ui.ScrollView("Scroll", image.transform, 8f);

            panelRoot.SetActive(false);
        }

        void BuildToast(Transform root)
        {
            var image = Ui.Panel("Toast", root, new Color(0.1f, 0.1f, 0.12f, 0.9f));
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -88f);
            rect.sizeDelta = new Vector2(760f, 56f);

            toastText = Ui.Label("Text", image.transform, "", 28f, Ui.TextColor, TextAlignmentOptions.Center);
            Ui.Stretch(toastText.rectTransform, 12f, 4f, 12f, 4f);
            toastRoot = image.gameObject;
            toastRoot.SetActive(false);
        }

        void BuildHint(Transform root)
        {
            var image = Ui.Panel("Hint", root, new Color(0.1f, 0.1f, 0.12f, 0.85f));
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 112f);
            rect.sizeDelta = new Vector2(900f, 52f);

            hintText = Ui.Label("Text", image.transform, "", 26f, Ui.TextColor, TextAlignmentOptions.Center);
            Ui.Stretch(hintText.rectTransform, 12f, 4f, 12f, 4f);
            hintRoot = image.gameObject;
            hintRoot.SetActive(false);
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

            bool hinting = world.InPlacementMode;
            if (hintRoot.activeSelf != hinting) hintRoot.SetActive(hinting);
            if (hinting) hintText.text = world.ModeHint;

            if (toastRoot.activeSelf && Time.unscaledTime >= toastUntil) toastRoot.SetActive(false);

            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 0.5f;

            RefreshTopBar();
            foreach (var ticker in tickers) ticker();

            // 시간이 흘러서 패널의 모양이 달라져야 하면(작물이 다 자람, 새 주문 도착 등) 다시 만든다.
            if (panel != PanelKind.None && ComputeSignature() != signature) rebuildNeeded = true;
        }

        void OnGameChanged()
        {
            RefreshTopBar();
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

        void RefreshTopBar()
        {
            var state = game.State;
            levelText.text = game.XpToNextLevel > 0
                ? "Lv " + state.level + "  (" + state.xp + "/" + game.XpToNextLevel + ")"
                : "Lv " + state.level + "  (최고)";
            coinsText.text = "코인 " + state.coins;
            gemsText.text = "보석 " + state.gems;
            storageText.text = "창고 " + game.StoredTotal + "/" + state.storageCapacity;
            charmText.text = "매력 " + game.TotalCharm;

            if (!game.CanExpandFurther)
            {
                Ui.SetButtonText(expandButton, "마을 확장 (최대)");
                expandButton.interactable = false;
            }
            else
            {
                Ui.SetButtonText(expandButton,
                    "마을 확장  " + game.LandExpandCost + "코인 · Lv " + game.LandExpandRequiredLevel);
                expandButton.interactable = true;
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

        void OnExpandClicked()
        {
            Report(game.ExpandLand(), "마을이 넓어졌어요!");
        }

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
                Report(game.Harvest(obj.id), text);
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
            panelRoot.SetActive(true);
            rebuildNeeded = true;
        }

        void ClosePanel()
        {
            panel = PanelKind.None;
            selectedId = 0;
            world.SelectedObjectId = 0;
            panelRoot.SetActive(false);
            tickers.Clear();
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
            Ui.Clear(panelContent);
            tickers.Clear();

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
                case PanelKind.Selection:
                    BuildSelectionPanel();
                    break;
                default:
                    return;
            }

            signature = ComputeSignature();
        }

        TextMeshProUGUI Line(string text, bool muted = false)
        {
            return Ui.Label("Line", panelContent, text, 24f, muted ? Ui.MutedColor : Ui.TextColor);
        }

        RectTransform Card()
        {
            var card = Ui.Panel("Card", panelContent, Ui.CardColor);
            Ui.Vertical(card.gameObject, 6f, 12);
            return card.rectTransform;
        }

        static RectTransform Row(Transform parent)
        {
            var row = Ui.Rect("Row", parent);
            Ui.Horizontal(row.gameObject, 8f, 0);
            return row;
        }

        /// <summary>왼쪽에 그림, 오른쪽에 글 여러 줄이 들어가는 줄을 만들고 글을 넣을 곳을 돌려준다.</summary>
        static RectTransform IconRow(Transform parent, Sprite icon, float iconSize)
        {
            var row = Ui.Rect("IconRow", parent);
            var group = Ui.Horizontal(row.gameObject, 12f, 0);
            group.childForceExpandWidth = false;

            if (icon != null) Ui.Icon("Icon", row, icon, iconSize);

            var column = Ui.Rect("Column", row);
            Ui.Vertical(column.gameObject, 4f, 0);
            column.gameObject.AddComponent<ULayoutElement>().flexibleWidth = 1f;
            return column;
        }

        static string ResidentTitle(ResidentDef resident)
        {
            return resident.displayName + " (" + resident.species + ")";
        }

        void BuildBuildPanel()
        {
            panelTitle.text = "건설";
            Line("놓을 것을 고른 뒤 땅을 클릭하세요.", true);

            foreach (var def in game.Catalog.placeables)
            {
                bool locked = game.State.level < def.unlockLevel;
                bool maxed = def.maxCount > 0 && game.CountPlaced(def.id) >= def.maxCount;

                string detail = locked ? "Lv " + def.unlockLevel + " 필요"
                    : maxed ? "설치 완료"
                    : def.cost + " 코인";
                if (def.charm > 0 && !locked) detail += " · 매력 +" + def.charm;

                string defId = def.id;
                var button = Ui.Button("Build " + def.id, panelContent, def.displayName + "   " + detail, () =>
                {
                    ClosePanel();
                    world.BeginPlacing(defId);
                }, 52f, 24f);
                button.interactable = !locked && !maxed;
            }
        }

        void BuildOrdersPanel()
        {
            panelTitle.text = "주문";

            foreach (var order in game.State.orders)
            {
                var card = Card();
                int orderId = order.id;

                if (!game.IsOrderAvailable(order))
                {
                    var waiting = Ui.Label("Waiting", card, "", 24f, Ui.MutedColor);
                    long availableAt = order.availableAt;
                    Action tick = () =>
                    {
                        if (waiting != null) waiting.text = "다음 주문까지 " + Texts.Duration(availableAt - game.Now);
                    };
                    tick();
                    tickers.Add(tick);
                    continue;
                }

                var customer = game.Catalog.GetResident(order.customerId);
                string who = customer != null ? ResidentTitle(customer) : "손님";
                Sprite face = customer != null ? ArtLibrary.Get("resident_" + customer.id) : null;
                var info = IconRow(card, face, 84f);
                Ui.Label("Customer", info, who + "의 주문", 26f, Ui.TextColor);
                Ui.Label("Items", info, Texts.ItemList(game, order.items, true), 24f, Ui.TextColor);
                Ui.Label("Reward", info, "보상  코인 " + order.rewardCoins + " · 경험치 " + order.rewardXp, 22f, Ui.MutedColor);

                var row = Row(card);
                var deliver = Ui.Button("Deliver", row, "납품", () =>
                {
                    Report(game.DeliverOrder(orderId), "납품 완료!");
                }, 48f, 24f);
                deliver.interactable = game.HasItems(order.items);

                Ui.Button("Discard", row, "거절", () =>
                {
                    Report(game.DiscardOrder(orderId), null);
                }, 48f, 24f);
            }
        }

        void BuildStoragePanel()
        {
            panelTitle.text = "창고";
            Line("보관 " + game.StoredTotal + " / " + game.State.storageCapacity);
            Ui.Button("Upgrade", panelContent, "창고 넓히기   " + game.StorageUpgradeCost + " 코인", () =>
            {
                Report(game.UpgradeStorage(), "창고가 넓어졌어요!");
            }, 52f, 24f);

            if (game.State.inventory.Count == 0)
            {
                Line("창고가 비어 있어요.", true);
                return;
            }

            foreach (var entry in game.State.inventory)
            {
                var def = game.Catalog.GetItem(entry.itemId);
                string itemId = entry.itemId;
                int value = def != null ? def.value : 0;

                var row = Row(panelContent);
                var label = Ui.Label("Item", row, Texts.ItemName(game.Catalog, itemId) + "  x" + entry.amount, 24f, Ui.TextColor);
                label.gameObject.AddComponent<ULayoutElement>().flexibleWidth = 2f;
                Ui.Button("Sell", row, "1개 판매 (" + value + ")", () =>
                {
                    Report(game.SellItem(itemId, 1), null);
                }, 44f, 20f);
            }
        }

        void BuildResidentsPanel()
        {
            panelTitle.text = "주민";
            Line("시설에 배치하면 그 시설의 생산이 빨라져요. 배치는 시설을 눌러서 해요.", true);

            foreach (var resident in game.Catalog.residents)
            {
                var card = Card();
                bool unlocked = game.IsResidentUnlocked(resident.id);
                string residentId = resident.id;

                var info = IconRow(card, ArtLibrary.Get("resident_" + resident.id), 84f);
                Ui.Label("Name", info, ResidentTitle(resident), 26f, unlocked ? Ui.TextColor : Ui.MutedColor);

                if (!unlocked)
                {
                    Ui.Label("Locked", info, "Lv " + resident.unlockLevel + "에 마을로 와요.", 22f, Ui.MutedColor);
                    continue;
                }

                Ui.Label("Bonus", info, "생산 시간 -" + resident.craftSpeedBonusPercent + "%", 22f, Ui.MutedColor);

                var workplace = game.FindObject(game.AssignedObjectOf(resident.id));
                var workplaceDef = workplace != null ? game.Catalog.GetPlaceable(workplace.defId) : null;
                if (workplaceDef == null)
                {
                    Ui.Label("Status", info, "마을을 산책하는 중", 22f, Ui.TextColor);
                    continue;
                }

                Ui.Label("Status", info, workplaceDef.displayName + "에서 일하는 중", 22f, Ui.TextColor);
                Ui.Button("Rest " + resident.id, card, "쉬게 하기", () =>
                {
                    Report(game.UnassignResident(residentId), null);
                }, 44f, 22f);
            }
        }

        void BuildSelectionPanel()
        {
            var obj = game.FindObject(selectedId);
            var def = game.Catalog.GetPlaceable(obj.defId);
            int objectId = obj.id;
            panelTitle.text = def != null ? def.displayName : obj.defId;
            if (def == null) return;

            if (def.kind == PlaceableKind.Plot) BuildPlotSection(obj);
            else if (def.kind == PlaceableKind.Facility) BuildFacilitySection(obj, def);
            else Line("매력 +" + def.charm, true);

            var row = Row(panelContent);
            Ui.Button("Move", row, "옮기기", () =>
            {
                ClosePanel();
                world.BeginMoving(objectId);
            }, 48f, 24f);

            long refund = (long)def.cost * game.Catalog.balance.sellBackPercent / 100;
            Ui.Button("Remove", row, "철거 (+" + refund + " 코인)", () =>
            {
                Report(game.Remove(objectId), "철거했어요.");
            }, 48f, 24f);
        }

        void BuildPlotSection(PlacedObject plot)
        {
            int objectId = plot.id;

            if (!string.IsNullOrEmpty(plot.cropId))
            {
                var crop = game.Catalog.GetCrop(plot.cropId);
                string cropName = crop != null ? crop.displayName : plot.cropId;

                if (game.IsRipe(objectId))
                {
                    Line(cropName + " — 다 자랐어요!");
                    Ui.Button("Harvest", panelContent, "수확", () =>
                    {
                        Report(game.Harvest(objectId), null);
                    }, 52f, 24f);
                }
                else
                {
                    var growing = Line("");
                    Action tick = () =>
                    {
                        if (growing != null)
                            growing.text = cropName + " 자라는 중 — " + Texts.Duration(game.SecondsUntilRipe(objectId));
                    };
                    tick();
                    tickers.Add(tick);
                }
                return;
            }

            Line("심을 작물을 고르세요.", true);
            foreach (var crop in game.Catalog.crops)
            {
                bool locked = game.State.level < crop.unlockLevel;
                string detail = locked
                    ? "Lv " + crop.unlockLevel + " 필요"
                    : Texts.Duration(crop.growSeconds) + " · 씨앗 " + crop.seedCost + "코인";
                string cropId = crop.id;
                var button = Ui.Button("Plant " + crop.id, panelContent, crop.displayName + "   " + detail, () =>
                {
                    Report(game.Plant(objectId, cropId), null);
                }, 52f, 24f);
                button.interactable = !locked;
            }
        }

        void BuildFacilitySection(PlacedObject facility, PlaceableDef def)
        {
            int objectId = facility.id;

            BuildWorkerSection(objectId);

            Line("대기열 " + facility.queue.Count + " / " + def.queueSlots, true);
            foreach (var job in facility.queue)
            {
                var recipe = game.Catalog.GetRecipe(job.recipeId);
                string name = recipe != null ? Texts.ItemName(game.Catalog, recipe.outputItemId) : job.recipeId;
                long finishAt = job.finishAt;
                var line = Line("");
                Action tick = () =>
                {
                    if (line == null) return;
                    long remaining = finishAt - game.Now;
                    line.text = remaining <= 0 ? name + " — 완성" : name + " — " + Texts.Duration(remaining);
                };
                tick();
                tickers.Add(tick);
            }

            if (game.ReadyJobCount(objectId) > 0)
            {
                Ui.Button("Collect", panelContent, "완성품 수거", () =>
                {
                    Report(game.CollectCrafted(objectId), "완성품을 창고에 넣었어요.");
                }, 52f, 24f);
            }

            Line("만들 것을 고르세요.", true);
            foreach (var recipe in game.Catalog.recipes)
            {
                if (recipe.facilityId != def.id) continue;

                bool locked = game.State.level < recipe.unlockLevel;
                string outputName = Texts.ItemName(game.Catalog, recipe.outputItemId);
                string detail = locked
                    ? "Lv " + recipe.unlockLevel + " 필요"
                    : Texts.ItemList(game, recipe.inputs, true) + " · " + Texts.Duration(game.CraftSecondsFor(objectId, recipe));
                string recipeId = recipe.id;
                var button = Ui.Button("Craft " + recipe.id, panelContent, outputName + "   " + detail, () =>
                {
                    Report(game.StartCraft(objectId, recipeId), null);
                }, 52f, 22f);
                button.interactable = !locked;
            }
        }

        /// <summary>시설 패널 맨 위의 일꾼 영역: 지금 일하는 주민과, 배치할 수 있는 주민 목록.</summary>
        void BuildWorkerSection(int objectId)
        {
            var worker = game.ResidentAt(objectId);
            if (worker != null)
            {
                var card = Card();
                string workerId = worker.id;
                var info = IconRow(card, ArtLibrary.Get("resident_" + worker.id), 72f);
                Ui.Label("Worker", info, ResidentTitle(worker) + " 근무 중", 24f, Ui.TextColor);
                Ui.Label("Bonus", info, "생산 시간 -" + worker.craftSpeedBonusPercent + "%", 22f, Ui.MutedColor);
                Ui.Button("Rest " + worker.id, card, "쉬게 하기", () =>
                {
                    Report(game.UnassignResident(workerId), null);
                }, 44f, 22f);
            }
            else
            {
                Line("일꾼이 없어요. 주민을 배치하면 생산이 빨라져요.", true);
            }

            foreach (var resident in game.UnlockedResidents())
            {
                if (worker != null && resident.id == worker.id) continue;

                string residentId = resident.id;
                bool busyElsewhere = game.AssignedObjectOf(resident.id) != 0;
                string text = resident.displayName + (busyElsewhere ? " 데려오기" : " 배치")
                    + "   -" + resident.craftSpeedBonusPercent + "%";
                Ui.Button("Assign " + resident.id, panelContent, text, () =>
                {
                    Report(game.AssignResident(residentId, objectId), resident.displayName + "가 일을 시작했어요.");
                }, 44f, 22f);
            }
        }
    }
}
