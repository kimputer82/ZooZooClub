using System;
using System.Collections.Generic;

namespace ZooZooClub.Core
{
    // 주민: 레벨이 오르면 마을에 합류한다. 생산 시설에 한 마리씩 배치할 수 있고,
    // 배치된 시설은 그 주민의 보너스만큼 생산 시간이 줄어든다.
    public sealed partial class Game
    {
        public bool IsResidentUnlocked(string residentId)
        {
            var def = Catalog.GetResident(residentId);
            return def != null && State.level >= def.unlockLevel;
        }

        /// <summary>마을에 합류한 주민 목록.</summary>
        public List<ResidentDef> UnlockedResidents()
        {
            var result = new List<ResidentDef>();
            foreach (var def in Catalog.residents)
            {
                if (State.level >= def.unlockLevel) result.Add(def);
            }
            return result;
        }

        /// <summary>주민이 배치된 시설의 id. 배치되지 않았으면 0.</summary>
        public int AssignedObjectOf(string residentId)
        {
            foreach (var assignment in State.assignments)
            {
                if (assignment.residentId == residentId) return assignment.objectId;
            }
            return 0;
        }

        /// <summary>시설에 배치된 주민. 없으면 null.</summary>
        public ResidentDef ResidentAt(int objectId)
        {
            foreach (var assignment in State.assignments)
            {
                if (assignment.objectId == objectId) return Catalog.GetResident(assignment.residentId);
            }
            return null;
        }

        /// <summary>
        /// 주민을 시설에 배치한다. 주민이 다른 시설에 있었으면 옮겨 오고,
        /// 그 시설에 다른 주민이 있었으면 그 주민은 자리에서 물러난다.
        /// </summary>
        public ActionResult AssignResident(string residentId, int objectId)
        {
            var resident = Catalog.GetResident(residentId);
            if (resident == null) return ActionResult.NotFound;
            if (State.level < resident.unlockLevel) return ActionResult.LockedByLevel;

            PlaceableDef def;
            if (FindFacility(objectId, out def) == null) return ActionResult.NotFound;

            for (int i = State.assignments.Count - 1; i >= 0; i--)
            {
                var existing = State.assignments[i];
                if (existing.residentId == residentId || existing.objectId == objectId)
                {
                    State.assignments.RemoveAt(i);
                }
            }

            State.assignments.Add(new ResidentAssignment { residentId = residentId, objectId = objectId });
            NotifyChanged();
            return ActionResult.Ok;
        }

        public ActionResult UnassignResident(string residentId)
        {
            for (int i = 0; i < State.assignments.Count; i++)
            {
                if (State.assignments[i].residentId != residentId) continue;
                State.assignments.RemoveAt(i);
                NotifyChanged();
                return ActionResult.Ok;
            }
            return ActionResult.NotFound;
        }

        void ClearAssignmentsFor(int objectId)
        {
            for (int i = State.assignments.Count - 1; i >= 0; i--)
            {
                if (State.assignments[i].objectId == objectId) State.assignments.RemoveAt(i);
            }
        }

        /// <summary>이 시설에서 지금 레시피를 시작하면 걸리는 시간(초). 배치된 주민의 보너스가 반영된다.</summary>
        public int CraftSecondsFor(int objectId, RecipeDef recipe)
        {
            if (recipe == null) return 0;

            int bonus = 0;
            var resident = ResidentAt(objectId);
            if (resident != null) bonus = Math.Max(0, Math.Min(99, resident.craftSpeedBonusPercent));

            long seconds = (long)recipe.craftSeconds * (100 - bonus) / 100;
            return (int)Math.Max(1L, seconds);
        }
    }
}
