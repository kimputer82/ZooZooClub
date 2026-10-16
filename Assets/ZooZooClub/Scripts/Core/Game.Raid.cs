using System;
using System.Collections.Generic;

namespace ZooZooClub.Core
{
    /// <summary>
    /// 지금 마을에 들어와 있는 침략자 하나. 위치는 칸 좌표이고 칸 한가운데는 (x + 0.5, y + 0.5)다.
    /// 침략 도중의 상태는 저장하지 않는다(게임을 끄면 그 침략은 없던 일이 된다).
    /// </summary>
    public sealed class Invader
    {
        public int id;
        public InvaderDef def;
        public float x;
        public float y;
        public float hp;
        public float maxHp;

        /// <summary>마을 밖에서 차례를 기다리는 중이면 false. 나타난 뒤에만 보이고 공격받는다.</summary>
        public bool entered;

        /// <summary>마지막으로 움직인 방향(칸 좌표 기준). 그림의 방향을 정할 때 쓴다.</summary>
        public float moveX;
        public float moveY;

        /// <summary>마을 한가운데에 닿아 코인을 훔치는 중이면 true. 다 훔치기 전에 물리치면 코인을 지킨다.</summary>
        public bool stealing;

        /// <summary>훔치기가 얼마나 진행됐는지(0~1).</summary>
        public float stealProgress;

        internal float spawnDelay;
        internal int entryCell;
        internal readonly List<int> path = new List<int>();
        internal int pathIndex;
        internal bool hasTarget;
        internal float targetX;
        internal float targetY;

        internal int StepsLeft
        {
            get { return path.Count - pathIndex + (hasTarget ? 1 : 0); }
        }
    }

    /// <summary>침략 한 번의 결과.</summary>
    public sealed class RaidReport
    {
        public int wave;
        public int total;
        public int defeated;
        public int escaped;
        public long coinsStolen;
        public long bountyCoins;
        public int xp;
    }

    // 침략: 일정 시간마다 침략자들이 땅 가장자리에서 나타나 마을 한가운데로 걸어 들어온다.
    // 방어 시설은 사거리 안의 침략자를 알아서 공격하고, 끝까지 들어온 침략자는 코인을 훔쳐 달아난다.
    // 화면 쪽에서 매 프레임 TickRaid를 불러 줘야 진행된다. 게임을 꺼 둔 동안에는 침략이 오지 않는다.
    public sealed partial class Game
    {
        const float MaxRaidStep = 0.25f;
        const float SpawnOutside = 0.9f;

        readonly List<Invader> invaders = new List<Invader>();
        readonly Dictionary<int, float> defenseCooldowns = new Dictionary<int, float>();

        bool raidActive;
        RaidReport raidReport;
        int nextInvaderId = 1;
        long raidStealAllowance;

        // 물체를 놓거나 옮기거나 치우면 true가 된다. 침략자들의 길을 다시 잡아야 한다는 뜻이다.
        bool layoutChanged = true;
        bool[] raidBlocked;
        bool[] raidOpen;
        int raidGridWidth;
        int raidGridHeight;
        int raidGoal;

        /// <summary>
        /// true인 동안에는 새 침략이 시작되지 않고 다음 침략까지의 시간이 경고 시간 아래로 내려가지 않는다.
        /// 화면 쪽에서 학생이 문제를 푸는 동안 켠다. 이미 시작된 침략은 그대로 진행된다.
        /// </summary>
        public bool RaidHold { get; set; }

        /// <summary>침략이 시작될 때 몇 번째 침략인지와 함께 호출된다.</summary>
        public event Action<int> RaidStarted;

        /// <summary>침략이 끝나면 결과와 함께 호출된다.</summary>
        public event Action<RaidReport> RaidEnded;

        /// <summary>방어 시설(배치물 id)이 침략자를 공격할 때마다 호출된다.</summary>
        public event Action<int, Invader> DefenseFired;

        public event Action<Invader> InvaderDefeated;

        /// <summary>침략자가 코인을 훔쳐 달아날 때 훔친 코인과 함께 호출된다.</summary>
        public event Action<Invader, long> InvaderEscaped;

        public IReadOnlyList<Invader> Invaders
        {
            get { return invaders; }
        }

        public bool RaidActive
        {
            get { return raidActive; }
        }

        public bool RaidsUnlocked
        {
            get { return State.level >= Catalog.balance.raidUnlockLevel && Catalog.invaders.Count > 0; }
        }

        /// <summary>지금 진행 중이거나 다음에 올 침략이 몇 번째인지.</summary>
        public int RaidWave
        {
            get { return raidActive && raidReport != null ? raidReport.wave : State.raidWave + 1; }
        }

        /// <summary>다음 침략까지 남은 시간(초). 침략이 아직 열리지 않았거나 진행 중이면 -1.</summary>
        public long SecondsUntilRaid
        {
            get
            {
                if (!RaidsUnlocked || raidActive) return -1;
                if (State.nextRaidAt <= 0) return Math.Max(1, Catalog.balance.raidIntervalSeconds);
                return Math.Max(0, State.nextRaidAt - Now);
            }
        }

        /// <summary>침략이 곧 온다고 경고할 때인지.</summary>
        public bool RaidImminent
        {
            get
            {
                long seconds = SecondsUntilRaid;
                return seconds >= 0 && seconds <= Catalog.balance.raidWarningSeconds;
            }
        }

        /// <summary>침략자들이 노리는 칸(마을 한가운데에서 가장 가까운 빈칸).</summary>
        public void GetRaidGoal(out int x, out int y)
        {
            if (layoutChanged || raidBlocked == null
                || raidGridWidth != State.landWidth || raidGridHeight != State.landHeight)
            {
                if (raidActive) ReplanInvaders();
                else RebuildRaidGrid();
            }
            x = raidGoal % raidGridWidth;
            y = raidGoal / raidGridWidth;
        }

        /// <summary>금고(지킬 곳)를 그 칸으로 옮길 수 있는지. 침략자가 설 수 있는 칸(빈칸, 바닥, 장식)이어야 하고 침략 중에는 안 된다.</summary>
        public bool CanMoveSafeTo(int x, int y)
        {
            if (raidActive) return false;
            if (!IsInsideLand(x, y, 1, 1)) return false;
            foreach (var obj in State.objects)
            {
                if (!PlaceableKind.BlocksInvaders(Catalog.GetPlaceable(obj.defId))) continue;
                int width, height;
                GetFootprint(obj, out width, out height);
                if (x >= obj.x && x < obj.x + width && y >= obj.y && y < obj.y + height) return false;
            }
            return true;
        }

        /// <summary>침략자들이 노리는 금고를 그 칸으로 옮긴다.</summary>
        public ActionResult MoveSafe(int x, int y)
        {
            if (raidActive) return ActionResult.Busy;
            if (!CanMoveSafeTo(x, y)) return ActionResult.InvalidPlacement;
            State.safeX = x;
            State.safeY = y;
            layoutChanged = true;
            NotifyChanged();
            return ActionResult.Ok;
        }

        /// <summary>
        /// 저장 파일을 읽은 직후에 부른다. 꺼 둔 동안 지나간 침략은 건너뛰고,
        /// 돌아오자마자 침략당하지 않도록 다음 침략까지 한 주기를 새로 준다.
        /// </summary>
        public void ResumeRaidClock()
        {
            if (!RaidsUnlocked) return;
            var balance = Catalog.balance;
            if (State.nextRaidAt < Now + balance.raidWarningSeconds)
            {
                State.nextRaidAt = Now + Math.Max(1, balance.raidIntervalSeconds);
                NotifyChanged();
            }
        }

        /// <summary>기다리지 않고 지금 침략을 시작한다. 점검할 때 쓴다.</summary>
        public void StartRaidNow()
        {
            if (!raidActive && Catalog.invaders.Count > 0) StartRaid();
        }

        /// <summary>침략을 dt초만큼 진행한다. 때가 되면 침략을 시작하고, 침략자와 방어 시설을 움직인다.</summary>
        public void TickRaid(float dt)
        {
            if (!raidActive)
            {
                if (!RaidsUnlocked) return;
                if (State.nextRaidAt <= 0)
                {
                    State.nextRaidAt = Now + Math.Max(1, Catalog.balance.raidIntervalSeconds);
                    NotifyChanged();
                    return;
                }
                if (RaidHold)
                {
                    long earliest = Now + Math.Max(0, Catalog.balance.raidWarningSeconds);
                    if (State.nextRaidAt < earliest)
                    {
                        State.nextRaidAt = earliest;
                        NotifyChanged();
                    }
                    return;
                }
                if (Now < State.nextRaidAt) return;
                StartRaid();
            }

            if (dt <= 0f) return;
            if (dt > MaxRaidStep) dt = MaxRaidStep;

            if (layoutChanged) ReplanInvaders();

            bool changed = false;
            changed |= MoveInvaders(dt);
            changed |= FireDefenses(dt);

            if (invaders.Count == 0)
            {
                EndRaid();
                return;
            }

            if (changed) NotifyChanged();
        }

        // ---------- 시작과 끝 ----------

        void StartRaid()
        {
            var balance = Catalog.balance;
            raidActive = true;
            raidReport = new RaidReport { wave = State.raidWave + 1 };
            invaders.Clear();
            defenseCooldowns.Clear();
            RebuildRaidGrid();

            int wave = raidReport.wave;
            var candidates = new List<InvaderDef>();
            foreach (var def in Catalog.invaders)
            {
                if (def != null && def.firstLevel <= State.level) candidates.Add(def);
            }
            if (candidates.Count == 0)
            {
                foreach (var def in Catalog.invaders)
                {
                    if (def != null) candidates.Add(def);
                }
            }

            // 세기는 레벨을 따른다. 침략이 열린 레벨에서는 가장 약하고, 레벨이 오를수록 수와 체력이 는다.
            int tier = Math.Max(0, State.level - balance.raidUnlockLevel);
            int extra = tier / Math.Max(1, balance.raidLevelsPerExtra);
            int count = Math.Min(Math.Max(1, balance.raidBaseCount + extra), Math.Max(1, balance.raidMaxCount));
            if (candidates.Count == 0) count = 0;

            int hpPercent = Math.Min(Math.Max(100, balance.raidHpMaxPercent),
                100 + balance.raidHpGrowthPercent * tier);

            int stealPercent = Math.Max(0, Math.Min(100, balance.raidMaxStealPercent));
            raidStealAllowance = State.coins * stealPercent / 100;

            for (int i = 0; i < count; i++)
            {
                var def = candidates[NextRandom(candidates.Count)];
                float hp = Math.Max(1f, def.hp * hpPercent / 100f);
                var invader = new Invader
                {
                    id = nextInvaderId++,
                    def = def,
                    hp = hp,
                    maxHp = hp,
                    spawnDelay = i * Math.Max(0f, balance.raidSpawnGapSeconds),
                    entryCell = PickEntryCell()
                };
                PlaceOutside(invader);
                PlanPath(invader, true);
                invaders.Add(invader);
            }

            raidReport.total = count;

            var started = RaidStarted;
            if (started != null) started(wave);
            NotifyChanged();
        }

        void EndRaid()
        {
            var report = raidReport ?? new RaidReport { wave = State.raidWave + 1 };
            raidActive = false;
            raidReport = null;
            invaders.Clear();
            defenseCooldowns.Clear();

            State.raidWave = report.wave;
            State.nextRaidAt = Now + Math.Max(1, Catalog.balance.raidIntervalSeconds);
            NotifyChanged();

            var ended = RaidEnded;
            if (ended != null) ended(report);
        }

        // ---------- 길 ----------

        void RebuildRaidGrid()
        {
            int width = State.landWidth;
            int height = State.landHeight;
            if (raidBlocked == null || raidGridWidth != width || raidGridHeight != height)
            {
                raidBlocked = new bool[width * height];
                raidOpen = new bool[width * height];
                raidGridWidth = width;
                raidGridHeight = height;
            }
            else
            {
                Array.Clear(raidBlocked, 0, raidBlocked.Length);
            }

            foreach (var obj in State.objects)
            {
                if (!PlaceableKind.BlocksInvaders(Catalog.GetPlaceable(obj.defId))) continue;
                int objectWidth, objectHeight;
                GetFootprint(obj, out objectWidth, out objectHeight);
                for (int j = 0; j < objectHeight; j++)
                {
                    for (int i = 0; i < objectWidth; i++)
                    {
                        int x = obj.x + i;
                        int y = obj.y + j;
                        if (x >= 0 && y >= 0 && x < width && y < height) raidBlocked[x + y * width] = true;
                    }
                }
            }

            // 침략자들이 노리는 곳: 금고를 옮겨 둔 칸(없으면 마을 한가운데)에서 가장 가까운 빈칸.
            int wantX = width / 2;
            int wantY = height / 2;
            if (State.safeX >= 0 && State.safeY >= 0 && State.safeX < width && State.safeY < height)
            {
                wantX = State.safeX;
                wantY = State.safeY;
            }
            raidGoal = GridPath.NearestFree(raidBlocked, width, height, wantX, wantY);
            if (raidGoal < 0) raidGoal = wantX + wantY * width;

            layoutChanged = false;
        }

        /// <summary>땅 가장자리 칸 가운데 하나를 고른다. 비어 있는 칸을 먼저 고른다.</summary>
        int PickEntryCell()
        {
            int width = raidGridWidth;
            int height = raidGridHeight;
            var free = new List<int>();
            var any = new List<int>();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (x != 0 && y != 0 && x != width - 1 && y != height - 1) continue;
                    int index = x + y * width;
                    any.Add(index);
                    if (!raidBlocked[index]) free.Add(index);
                }
            }

            var pool = free.Count > 0 ? free : any;
            return pool[NextRandom(pool.Count)];
        }

        /// <summary>침략자를 들어올 칸의 바로 바깥에 세운다.</summary>
        void PlaceOutside(Invader invader)
        {
            int width = raidGridWidth;
            int height = raidGridHeight;
            int cx = invader.entryCell % width;
            int cy = invader.entryCell / width;

            invader.x = cx + 0.5f;
            invader.y = cy + 0.5f;
            if (cx == 0) invader.x = -SpawnOutside;
            else if (cx == width - 1) invader.x = width + SpawnOutside;
            else if (cy == 0) invader.y = -SpawnOutside;
            else invader.y = height + SpawnOutside;
        }

        /// <summary>
        /// 침략자가 목표까지 갈 길을 잡는다. 물체를 피해 가는 길이 없으면(울타리로 꽉 막았을 때)
        /// 물체를 넘어서 곧장 간다. fromEntry면 들어올 칸부터, 아니면 지금 서 있는 칸부터 잡는다.
        /// </summary>
        void PlanPath(Invader invader, bool fromEntry)
        {
            int width = raidGridWidth;
            int height = raidGridHeight;

            int startX;
            int startY;
            if (fromEntry)
            {
                startX = invader.entryCell % width;
                startY = invader.entryCell / width;
            }
            else
            {
                startX = Math.Max(0, Math.Min(width - 1, (int)Math.Floor(invader.x)));
                startY = Math.Max(0, Math.Min(height - 1, (int)Math.Floor(invader.y)));
            }

            int goalX = raidGoal % width;
            int goalY = raidGoal / width;

            invader.pathIndex = 0;
            invader.hasTarget = false;
            invader.stealing = false;
            invader.stealProgress = 0f;
            if (!GridPath.Find(raidBlocked, width, height, startX, startY, goalX, goalY, invader.path))
            {
                GridPath.Find(raidOpen, width, height, startX, startY, goalX, goalY, invader.path);
            }

            if (fromEntry) invader.path.Insert(0, startX + startY * width);
        }

        void ReplanInvaders()
        {
            int oldWidth = raidGridWidth;
            RebuildRaidGrid();

            foreach (var invader in invaders)
            {
                // 땅 크기가 바뀌면 칸 번호가 달라지므로 들어올 칸도 다시 고른다.
                if (oldWidth != raidGridWidth && !InsideLand(invader))
                {
                    invader.entryCell = PickEntryCell();
                    if (!invader.entered) PlaceOutside(invader);
                }
                PlanPath(invader, !InsideLand(invader));
            }
        }

        bool InsideLand(Invader invader)
        {
            return invader.x >= 0f && invader.y >= 0f
                && invader.x < raidGridWidth && invader.y < raidGridHeight;
        }

        // ---------- 침략자 ----------

        bool MoveInvaders(float dt)
        {
            bool changed = false;

            for (int i = invaders.Count - 1; i >= 0; i--)
            {
                var invader = invaders[i];

                if (!invader.entered)
                {
                    invader.spawnDelay -= dt;
                    if (invader.spawnDelay > 0f) continue;
                    invader.entered = true;
                }

                if (!invader.stealing)
                {
                    if (Advance(invader, invader.def.speed * dt)) continue;
                    invader.stealing = true;
                    invader.stealProgress = 0f;
                }

                // 마을 한가운데에 닿았다. 잠깐 코인을 뒤지고, 그동안 물리치지 못하면 훔쳐 달아난다.
                float stealSeconds = Catalog.balance.raidStealSeconds;
                invader.stealProgress = stealSeconds > 0f
                    ? Math.Min(1f, invader.stealProgress + dt / stealSeconds)
                    : 1f;
                if (invader.stealProgress < 1f) continue;

                // 금고가 지키는 몫은 못 가져간다: 한 번의 침략에 잃는 코인은 시작할 때 가진 코인의 일정 비율까지다.
                long stolen = Math.Min(State.coins, Math.Max(0, invader.def.stealCoins));
                stolen = Math.Min(stolen, Math.Max(0, raidStealAllowance));
                raidStealAllowance -= stolen;
                State.coins -= stolen;
                raidReport.escaped++;
                raidReport.coinsStolen += stolen;
                invaders.RemoveAt(i);
                changed = true;

                var escaped = InvaderEscaped;
                if (escaped != null) escaped(invader, stolen);
            }

            return changed;
        }

        /// <summary>침략자를 길을 따라 distance만큼 옮긴다. 길 끝에 닿았으면 false.</summary>
        bool Advance(Invader invader, float distance)
        {
            for (int guard = 0; guard < 16; guard++)
            {
                if (!invader.hasTarget)
                {
                    if (invader.pathIndex >= invader.path.Count) return false;
                    int cell = invader.path[invader.pathIndex++];
                    invader.targetX = cell % raidGridWidth + 0.5f;
                    invader.targetY = cell / raidGridWidth + 0.5f;
                    invader.hasTarget = true;
                }

                float dx = invader.targetX - invader.x;
                float dy = invader.targetY - invader.y;
                float length = (float)Math.Sqrt(dx * dx + dy * dy);

                if (length > 0.0001f)
                {
                    invader.moveX = dx / length;
                    invader.moveY = dy / length;
                }

                if (length <= distance)
                {
                    invader.x = invader.targetX;
                    invader.y = invader.targetY;
                    invader.hasTarget = false;
                    distance -= length;
                    if (distance <= 0f) return invader.pathIndex < invader.path.Count;
                    continue;
                }

                invader.x += dx / length * distance;
                invader.y += dy / length * distance;
                return true;
            }

            return true;
        }

        // ---------- 등급 ----------

        /// <summary>등급 하나를 weight에 비례하는 확률로 뽑는다. 등급이 정의되어 있지 않으면 빈 문자열.</summary>
        string RollGrade()
        {
            int total = 0;
            foreach (var grade in Catalog.grades)
            {
                if (grade != null && grade.weight > 0) total += grade.weight;
            }
            if (total <= 0) return "";

            int roll = NextRandom(total);
            foreach (var grade in Catalog.grades)
            {
                if (grade == null || grade.weight <= 0) continue;
                if (roll < grade.weight) return grade.id;
                roll -= grade.weight;
            }
            return "";
        }

        /// <summary>이 등급이 뽑힐 확률(%). 소수 첫째 자리까지.</summary>
        public float GradeChancePercent(GradeDef grade)
        {
            if (grade == null || grade.weight <= 0) return 0f;
            int total = 0;
            foreach (var other in Catalog.grades)
            {
                if (other != null && other.weight > 0) total += other.weight;
            }
            return total > 0 ? (float)Math.Round(grade.weight * 100.0 / total, 1) : 0f;
        }

        /// <summary>방어 시설의 등급. 등급이 적혀 있지 않으면 가장 낮은 등급, 등급 정의가 없으면 null.</summary>
        public GradeDef GradeOf(PlacedObject obj)
        {
            if (obj == null) return null;
            var def = Catalog.GetPlaceable(obj.defId);
            if (def == null || def.kind != PlaceableKind.Defense) return null;

            var grade = Catalog.GetGrade(obj.gradeId);
            if (grade == null && Catalog.grades.Count > 0) grade = Catalog.grades[0];
            return grade;
        }

        public int AttackDamageOf(PlacedObject obj)
        {
            var def = obj != null ? Catalog.GetPlaceable(obj.defId) : null;
            if (def == null) return 0;
            var grade = GradeOf(obj);
            if (grade == null) return def.attackDamage;
            return Math.Max(1, (int)Math.Round(def.attackDamage * grade.damagePercent / 100.0, MidpointRounding.AwayFromZero));
        }

        public float AttackRangeOf(PlacedObject obj)
        {
            var def = obj != null ? Catalog.GetPlaceable(obj.defId) : null;
            if (def == null) return 0f;
            var grade = GradeOf(obj);
            return grade == null ? def.attackRange : def.attackRange * grade.rangePercent / 100f;
        }

        /// <summary>공격 사이 간격(초). 등급의 빠르기가 150%면 간격은 1/1.5가 된다.</summary>
        public float AttackIntervalOf(PlacedObject obj)
        {
            var def = obj != null ? Catalog.GetPlaceable(obj.defId) : null;
            if (def == null) return 0f;
            var grade = GradeOf(obj);
            if (grade == null || grade.speedPercent <= 0) return def.attackInterval;
            return def.attackInterval * 100f / grade.speedPercent;
        }

        // ---------- 방어 시설 ----------

        bool FireDefenses(float dt)
        {
            bool changed = false;

            foreach (var obj in State.objects)
            {
                var def = Catalog.GetPlaceable(obj.defId);
                if (def == null || def.kind != PlaceableKind.Defense) continue;

                float cooldown;
                defenseCooldowns.TryGetValue(obj.id, out cooldown);
                cooldown -= dt;

                if (cooldown <= 0f)
                {
                    var target = FindTarget(obj.x + Rotation.Width(def, obj.rotation) * 0.5f,
                        obj.y + Rotation.Height(def, obj.rotation) * 0.5f, AttackRangeOf(obj));
                    if (target == null)
                    {
                        cooldown = 0f;
                    }
                    else
                    {
                        cooldown = Math.Max(0.05f, AttackIntervalOf(obj));
                        target.hp -= AttackDamageOf(obj);

                        var fired = DefenseFired;
                        if (fired != null) fired(obj.id, target);

                        if (target.hp <= 0f)
                        {
                            invaders.Remove(target);
                            raidReport.defeated++;
                            raidReport.bountyCoins += Math.Max(0, target.def.bounty);
                            raidReport.xp += Math.Max(0, target.def.xp);
                            State.coins += Math.Max(0, target.def.bounty);
                            AddXp(target.def.xp);
                            changed = true;

                            var defeated = InvaderDefeated;
                            if (defeated != null) defeated(target);
                        }
                    }
                }

                defenseCooldowns[obj.id] = cooldown;
            }

            return changed;
        }

        /// <summary>사거리 안의 침략자 가운데 목표에 가장 가까이 간 것을 고른다.</summary>
        Invader FindTarget(float centerX, float centerY, float range)
        {
            Invader best = null;
            float rangeSquared = range * range;

            foreach (var invader in invaders)
            {
                if (!invader.entered) continue;
                float dx = invader.x - centerX;
                float dy = invader.y - centerY;
                if (dx * dx + dy * dy > rangeSquared) continue;

                if (best == null
                    || invader.StepsLeft < best.StepsLeft
                    || (invader.StepsLeft == best.StepsLeft && invader.hp < best.hp))
                {
                    best = invader;
                }
            }

            return best;
        }
    }
}
