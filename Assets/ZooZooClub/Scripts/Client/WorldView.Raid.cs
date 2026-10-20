using System.Collections.Generic;
using TMPro;
using UnityEngine;
using ZooZooClub.Core;

namespace ZooZooClub.Client
{
    // 침략을 화면에 그리는 부분: 침략자와 체력 막대, 방어 시설이 쏘는 탄, 사거리 표시.
    // 전투 규칙은 Game(Core)에 있고 여기서는 그 결과를 보여 주기만 한다.
    public sealed partial class WorldView
    {
        sealed class InvaderView
        {
            public Invader invader;
            public GameObject root;
            public SpriteRenderer body;
            public FrameSet front;
            public FrameSet back;
            public bool facingBack;
            public SpriteRenderer hpFill;
            public SpriteRenderer stealFill;
            public float hpWidth;
            public float phase;
            public float flash;
            public bool seen;
        }

        sealed class Shot
        {
            public SpriteRenderer sprite;
            public Vector3 from;
            public Vector3 to;
            public float age;
        }

        sealed class FloatingText
        {
            public TextMeshPro label;
            public Color color;
            public float age;
        }

        const float InvaderWidth = 0.5f;
        const float ShotSeconds = 0.14f;
        const float FloatSeconds = 1.1f;
        const int RangeOrder = -3000;

        static readonly Color RangeColor = new Color(1f, 1f, 1f, 0.22f);
        static readonly Color ShotColor = new Color(1f, 0.93f, 0.55f, 1f);
        static readonly Color HitColor = new Color(1f, 0.55f, 0.5f, 1f);
        static readonly Color HpBackColor = new Color(0.2f, 0.13f, 0.08f, 0.85f);
        static readonly Color HpColor = new Color(0.94f, 0.36f, 0.32f, 1f);
        static readonly Color StealColor = new Color(1f, 0.84f, 0.3f, 1f);
        static readonly Color GoalColor = new Color(1f, 0.86f, 0.35f, 0.55f);
        static readonly Color GainColor = new Color(1f, 0.9f, 0.4f, 1f);
        static readonly Color LossColor = new Color(1f, 0.5f, 0.45f, 1f);

        Transform raidRoot;
        SpriteRenderer rangeRing;

        // 침략자들이 노리는 칸을 알려 주는 표시: 바닥의 노란 칸과 그 위에 놓인 금고.
        const string SafeArt = "raid_safe";
        const float SafeWidth = 0.62f;
        const float SafeLift = 0.095f;
        const float SafeOffsetX = -0.02f;
        const float SafeSortBack = 0.06f;

        GameObject goalRoot;
        SpriteRenderer goalSafe;
        Vector3 goalSafeRest;
        TextMeshPro goalLabel;

        readonly Dictionary<int, InvaderView> invaderViews = new Dictionary<int, InvaderView>();
        readonly List<int> invaderRemoveBuffer = new List<int>();
        readonly List<Shot> shots = new List<Shot>();
        readonly List<FloatingText> floatingTexts = new List<FloatingText>();

        sealed class FloatingIcon
        {
            public SpriteRenderer sprite;
            public Vector3 start;
            public float age;
        }

        readonly List<FloatingIcon> floatingIcons = new List<FloatingIcon>();
        const float IconSeconds = 1.8f;

        void InitRaid()
        {
            raidRoot = NewChild("Raid");

            rangeRing = NewSprite("RangeRing", raidRoot, PlaceholderArt.Circle);
            rangeRing.color = RangeColor;
            rangeRing.sortingOrder = RangeOrder;
            rangeRing.enabled = false;

            goalRoot = new GameObject("RaidGoal");
            goalRoot.transform.SetParent(raidRoot, false);
            var goalTile = NewSprite("Tile", goalRoot.transform, PlaceholderArt.Diamond);
            goalTile.color = GoalColor;
            goalTile.sortingOrder = RangeOrder + 1;
            goalTile.transform.localScale = new Vector3(0.92f, 0.92f, 1f);

            float labelY = BuildGoalSafe();
            goalLabel = NewLabel(goalRoot.transform, new Vector3(0f, labelY, 0f), 1.3f);
            goalLabel.text = "지킬 곳";
            goalRoot.SetActive(false);

            game.DefenseFired += OnDefenseFired;
            game.InvaderDefeated += OnInvaderDefeated;
            game.InvaderEscaped += OnInvaderEscaped;
        }

        /// <summary>
        /// 지킬 칸 위에 금고를 놓는다. 금고 그림이 없으면 예전처럼 코인을 띄운다.
        /// 이름표를 달 높이(칸 가운데 기준)를 돌려준다.
        /// </summary>
        float BuildGoalSafe()
        {
            Sprite safe = ArtLibrary.Get(SafeArt);
            if (safe == null)
            {
                Sprite coin = ArtLibrary.Get("ui_icon_coin");
                goalSafe = NewSprite("Coin", goalRoot.transform, coin != null ? coin : PlaceholderArt.Circle);
                if (coin == null) goalSafe.color = StealColor;
                ArtLibrary.Fit(goalSafe, goalSafe.sprite, new Vector3(0f, 0.12f, 0f), 0.34f);
                goalSafeRest = goalSafe.transform.localPosition;
                return 0.62f;
            }

            // 다른 그림과 같은 방식으로 놓는다: 칸의 맨 앞 모서리에서 lift만큼 위, offsetX만큼 옆.
            var layout = ArtLibrary.Layout(SafeArt);
            float width = layout != null ? layout.width : SafeWidth;
            float lift = layout != null ? layout.lift : SafeLift;
            float offsetX = layout != null ? layout.offsetX : SafeOffsetX;

            goalSafe = NewSprite("Safe", goalRoot.transform, safe);
            ArtLibrary.Fit(goalSafe, safe, new Vector3(offsetX, lift - IsoMath.HalfHeight, 0f), width);
            goalSafeRest = goalSafe.transform.localPosition;

            float height = width * safe.bounds.size.y / Mathf.Max(0.0001f, safe.bounds.size.x);
            return lift - IsoMath.HalfHeight + height + 0.12f;
        }

        void DisposeRaid()
        {
            game.DefenseFired -= OnDefenseFired;
            game.InvaderDefeated -= OnInvaderDefeated;
            game.InvaderEscaped -= OnInvaderEscaped;
        }

        void UpdateRaid(float deltaTime)
        {
            UpdateInvaders(deltaTime);
            UpdateShots(deltaTime);
            UpdateFloatingTexts(deltaTime);
            UpdateFloatingIcons(deltaTime);
            bool planningDefense = UpdateRangeRing();
            UpdateGoalMarker(planningDefense);
        }

        // ---------- 침략자 ----------

        static Vector3 InvaderWorld(Invader invader)
        {
            return IsoMath.CellToWorld(invader.x, invader.y);
        }

        InvaderView CreateInvaderView(Invader invader)
        {
            var view = new InvaderView { invader = invader, phase = Random.Range(0f, 6f) };
            view.root = new GameObject("Invader " + invader.def.id + " #" + invader.id);
            view.root.transform.SetParent(raidRoot, false);

            // 그림 이름은 invader_<id>. 주민처럼 _idle, _walk_1~4, _back_idle, _back_walk_1~4 를 넣으면 걷는 모습이 된다.
            string artName = "invader_" + invader.def.id;
            view.body = NewSprite("Body", view.root.transform, null);
            view.back = LoadFrames(view.body, artName + "_back", null);
            view.front = LoadFrames(view.body, artName, artName);
            if (view.front == null)
            {
                float size = InvaderWidth * 0.7f;
                ArtLibrary.Fit(view.body, PlaceholderArt.Circle, Vector3.zero, size);
                view.body.color = PlaceholderArt.ColorFor(artName);
                view.front = new FrameSet
                {
                    scale = view.body.transform.localScale,
                    rest = view.body.transform.localPosition,
                    top = size
                };
            }

            // 머리 위의 체력 막대.
            view.hpWidth = 0.46f;
            float barY = view.front.top + 0.1f;
            var back = NewSprite("HpBack", view.root.transform, PlaceholderArt.Box);
            back.color = HpBackColor;
            back.sortingOrder = LabelOrder;
            back.transform.localPosition = new Vector3(0f, barY, 0f);
            back.transform.localScale = new Vector3(view.hpWidth + 0.04f, 0.1f, 1f);

            view.hpFill = NewSprite("HpFill", view.root.transform, PlaceholderArt.Box);
            view.hpFill.color = HpColor;
            view.hpFill.sortingOrder = LabelOrder + 1;
            view.hpFill.transform.localPosition = new Vector3(0f, barY, 0f);

            // 코인을 훔치는 동안에만 보이는 노란 막대. 다 차면 훔쳐 달아난다.
            view.stealFill = NewSprite("StealFill", view.root.transform, PlaceholderArt.Box);
            view.stealFill.color = StealColor;
            view.stealFill.sortingOrder = LabelOrder + 1;
            view.stealFill.transform.localPosition = new Vector3(0f, barY + 0.1f, 0f);
            view.stealFill.enabled = false;

            return view;
        }

        void UpdateInvaders(float deltaTime)
        {
            float now = Time.unscaledTime;
            foreach (var view in invaderViews.Values) view.seen = false;

            foreach (var invader in game.Invaders)
            {
                if (!invader.entered) continue;

                InvaderView view;
                if (!invaderViews.TryGetValue(invader.id, out view))
                {
                    view = CreateInvaderView(invader);
                    invaderViews[invader.id] = view;
                }
                view.seen = true;

                Vector3 world = InvaderWorld(invader);
                view.root.transform.localPosition = world;

                float worldDx = (invader.moveX - invader.moveY) * IsoMath.HalfWidth;
                float worldDy = (invader.moveX + invader.moveY) * IsoMath.HalfHeight;
                if (view.back != null && Mathf.Abs(worldDy) > 0.0001f) view.facingBack = worldDy > 0f;
                FrameSet set = view.facingBack && view.back != null ? view.back : view.front;

                float bob;
                if (set.walk != null)
                {
                    int frame = (int)((now + view.phase) * WalkFramesPerSecond) % set.walk.Length;
                    view.body.sprite = set.walk[frame];
                    bob = 0f;
                }
                else
                {
                    if (set.idle != null) view.body.sprite = set.idle;
                    bob = Mathf.Abs(Mathf.Sin(now * 9f + view.phase)) * 0.035f;
                }
                view.body.transform.localScale = set.scale;
                view.body.transform.localPosition = set.rest + new Vector3(0f, bob, 0f);
                if (Mathf.Abs(worldDx) > 0.0001f) view.body.flipX = view.facingBack ? worldDx > 0f : worldDx < 0f;
                view.body.sortingOrder = SortOrderAt(world.y) + 1;

                // 맞으면 잠깐 붉어진다. 색 동그라미로 대신 그린 침략자는 제 색을 지킨다.
                if (set.idle != null)
                {
                    view.flash = Mathf.Max(0f, view.flash - deltaTime);
                    view.body.color = view.flash > 0f ? HitColor : Color.white;
                }

                float ratio = invader.maxHp > 0f ? Mathf.Clamp01(invader.hp / invader.maxHp) : 0f;
                float barY = view.hpFill.transform.localPosition.y;
                view.hpFill.transform.localScale = new Vector3(Mathf.Max(0.001f, view.hpWidth * ratio), 0.06f, 1f);
                view.hpFill.transform.localPosition = new Vector3(-view.hpWidth * (1f - ratio) * 0.5f, barY, 0f);

                view.stealFill.enabled = invader.stealing;
                if (invader.stealing)
                {
                    float progress = Mathf.Clamp01(invader.stealProgress);
                    view.stealFill.transform.localScale = new Vector3(Mathf.Max(0.001f, view.hpWidth * progress), 0.06f, 1f);
                    view.stealFill.transform.localPosition =
                        new Vector3(-view.hpWidth * (1f - progress) * 0.5f, barY + 0.1f, 0f);
                }
            }

            invaderRemoveBuffer.Clear();
            foreach (var pair in invaderViews)
            {
                if (!pair.Value.seen) invaderRemoveBuffer.Add(pair.Key);
            }
            foreach (int id in invaderRemoveBuffer)
            {
                Destroy(invaderViews[id].root);
                invaderViews.Remove(id);
            }
        }

        // ---------- 공격 ----------

        void OnDefenseFired(int objectId, Invader invader)
        {
            InvaderView target;
            if (invaderViews.TryGetValue(invader.id, out target)) target.flash = 0.12f;

            ObjectView source;
            if (!views.TryGetValue(objectId, out source)) return;

            var sprite = NewSprite("Shot", raidRoot, PlaceholderArt.Circle);
            sprite.color = ShotColor;
            sprite.sortingOrder = LabelOrder - 1;
            sprite.transform.localScale = new Vector3(0.13f, 0.13f, 1f);

            var shot = new Shot
            {
                sprite = sprite,
                from = source.root.transform.localPosition
                    + new Vector3(source.center.x, Mathf.Max(0.3f, source.labelY - 0.35f), 0f),
                to = InvaderWorld(invader) + new Vector3(0f, 0.3f, 0f)
            };
            sprite.transform.localPosition = shot.from;
            shots.Add(shot);
        }

        void UpdateShots(float deltaTime)
        {
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var shot = shots[i];
                shot.age += deltaTime;
                float t = shot.age / ShotSeconds;
                if (t >= 1f)
                {
                    Destroy(shot.sprite.gameObject);
                    shots.RemoveAt(i);
                    continue;
                }
                shot.sprite.transform.localPosition = Vector3.Lerp(shot.from, shot.to, t);
            }
        }

        void OnInvaderDefeated(Invader invader)
        {
            if (invader.def.bounty > 0)
                ShowFloatingText(InvaderWorld(invader), "+" + invader.def.bounty, GainColor);
        }

        void OnInvaderEscaped(Invader invader, long stolen)
        {
            if (stolen > 0) ShowFloatingText(InvaderWorld(invader), "-" + stolen, LossColor);
        }

        // ---------- 떠오르는 숫자 ----------

        void ShowFloatingText(Vector3 world, string text, Color color)
        {
            var label = NewLabel(raidRoot, world + new Vector3(0f, 0.5f, 0f), 2.4f);
            label.text = text;
            label.color = color;
            label.fontStyle = FontStyles.Bold;
            floatingTexts.Add(new FloatingText { label = label, color = color });
        }

        /// <summary>
        /// 그 배치물 위로 물건 그림이 톡 튀어 올랐다가 사라지게 한다(수확하다 보따리를 찾았을 때). 이름표도 함께 띄운다.
        /// </summary>
        public void ShowFoundItem(int objectId, string artName, string text)
        {
            var obj = game.FindObject(objectId);
            if (obj == null) return;
            int width, height;
            game.GetFootprint(obj, out width, out height);
            Vector3 world = IsoMath.CellToWorld(obj.x + width * 0.5f, obj.y + height * 0.5f);

            var sprite = NewSprite("Found", raidRoot, ArtLibrary.Get(artName) ?? PlaceholderArt.Circle);
            ArtLibrary.Fit(sprite, sprite.sprite, world + new Vector3(0f, 0.15f, 0f), 0.42f);
            sprite.sortingOrder = LabelOrder - 2;
            floatingIcons.Add(new FloatingIcon { sprite = sprite, start = sprite.transform.localPosition });
            if (!string.IsNullOrEmpty(text)) ShowFloatingText(world + new Vector3(0f, 0.35f, 0f), text, GainColor);
        }

        void UpdateFloatingIcons(float deltaTime)
        {
            for (int i = floatingIcons.Count - 1; i >= 0; i--)
            {
                var item = floatingIcons[i];
                item.age += deltaTime;
                float t = item.age / IconSeconds;
                if (t >= 1f || item.sprite == null)
                {
                    if (item.sprite != null) Destroy(item.sprite.gameObject);
                    floatingIcons.RemoveAt(i);
                    continue;
                }
                // 처음에는 통통 튀어 오르고, 끝 무렵에 흐려진다.
                float hop = Mathf.Sin(Mathf.Min(1f, t * 2.2f) * Mathf.PI) * 0.25f + t * 0.35f;
                item.sprite.transform.localPosition = item.start + new Vector3(0f, hop, 0f);
                item.sprite.color = new Color(1f, 1f, 1f, t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f);
            }
        }

        void UpdateFloatingTexts(float deltaTime)
        {
            for (int i = floatingTexts.Count - 1; i >= 0; i--)
            {
                var item = floatingTexts[i];
                item.age += deltaTime;
                float t = item.age / FloatSeconds;
                if (t >= 1f)
                {
                    Destroy(item.label.gameObject);
                    floatingTexts.RemoveAt(i);
                    continue;
                }

                item.label.transform.localPosition += new Vector3(0f, deltaTime * 0.5f, 0f);
                var color = item.color;
                color.a = 1f - t * t;
                item.label.color = color;
            }
        }

        // ---------- 사거리 ----------

        /// <summary>
        /// 방어 시설을 골랐거나 놓을 곳을 정하는 중이면 그 사거리를 바닥에 보여 준다.
        /// 칸 좌표의 원은 화면에서 가로로 긴 타원이 된다. 보여 주고 있으면 true.
        /// </summary>
        bool UpdateRangeRing()
        {
            PlaceableDef def = null;
            float range = 0f;
            float cellX = 0f;
            float cellY = 0f;
            int rotation = 0;

            if (ghostDef != null && ghostDef.kind == PlaceableKind.Defense)
            {
                def = ghostDef;
                cellX = ghostCell.x;
                cellY = ghostCell.y;
                rotation = Rotation.Normalize(def, placingRotation);

                // 옮기는 중이면 그 시설의 등급이 반영된 사거리, 새로 사는 중이면 기본 사거리.
                var moving = mode == Mode.Moving ? game.FindObject(movingObjectId) : null;
                range = moving != null ? game.AttackRangeOf(moving) : def.attackRange;
            }
            else if (mode == Mode.None && SelectedObjectId != 0)
            {
                var selected = game.FindObject(SelectedObjectId);
                var selectedDef = selected != null ? game.Catalog.GetPlaceable(selected.defId) : null;
                if (selectedDef != null && selectedDef.kind == PlaceableKind.Defense)
                {
                    def = selectedDef;
                    range = game.AttackRangeOf(selected);
                    cellX = selected.x;
                    cellY = selected.y;
                    rotation = selected.rotation;
                }
            }

            if (def == null || range <= 0f)
            {
                if (rangeRing.enabled) rangeRing.enabled = false;
                return false;
            }

            float diameter = range * Mathf.Sqrt(2f);
            rangeRing.enabled = true;
            rangeRing.transform.localPosition = IsoMath.CellToWorld(
                cellX + Rotation.Width(def, rotation) * 0.5f, cellY + Rotation.Height(def, rotation) * 0.5f);
            rangeRing.transform.localScale = new Vector3(diameter, diameter * 0.5f, 1f);
            return true;
        }

        /// <summary>
        /// 침략자들이 노리는 칸을 표시한다. 침략이 가깝거나 진행 중일 때, 그리고 방어 시설을 놓거나 골랐을 때 보인다.
        /// </summary>
        void UpdateGoalMarker(bool planningDefense)
        {
            bool movingSafe = mode == Mode.MovingSafe;
            bool show = game.RaidsUnlocked && (planningDefense || movingSafe || game.RaidActive || game.RaidImminent);
            if (goalRoot.activeSelf != show) goalRoot.SetActive(show);
            if (!show) return;

            int goalX, goalY;
            game.GetRaidGoal(out goalX, out goalY);
            // 옮기는 중이면 가리키는 칸에 금고를 띄워 보여 준다. 놓을 수 없는 칸이면 붉게.
            Color tint = Color.white;
            if (movingSafe && safeHoverActive)
            {
                goalX = safeHover.x;
                goalY = safeHover.y;
                tint = game.CanMoveSafeTo(goalX, goalY) ? new Color(1f, 1f, 1f, 0.85f) : new Color(1f, 0.55f, 0.55f, 0.75f);
            }
            goalSafe.color = tint;
            goalLabel.text = movingSafe ? "여기로 옮길까요?" : "지킬 곳";
            Vector3 world = IsoMath.CellCenter(goalX, goalY);
            goalRoot.transform.localPosition = world;

            // 침략 중에는 침략자의 체력 막대와 겹치므로 이름표를 감춘다.
            bool showLabel = !game.RaidActive;
            if (goalLabel.gameObject.activeSelf != showLabel) goalLabel.gameObject.SetActive(showLabel);

            // 침략자가 금고를 털고 있는 동안에는 금고가 덜컹거린다.
            bool beingRobbed = false;
            foreach (var invader in game.Invaders)
            {
                if (invader.stealing) { beingRobbed = true; break; }
            }
            float shake = beingRobbed ? Mathf.Sin(Time.unscaledTime * 45f) * 0.015f : 0f;
            goalSafe.transform.localPosition = goalSafeRest + new Vector3(shake, 0f, 0f);
            // 금고를 터는 침략자는 칸 한가운데에 선다. 금고에 가려지지 않게 금고를 살짝 뒤쪽 순서로 그린다.
            goalSafe.sortingOrder = SortOrderAt(world.y + SafeSortBack);
        }
    }
}
