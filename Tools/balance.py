"""catalog.json의 밸런스를 숫자로 살펴보는 도구.

사용법: python3 balance.py <catalog.json 경로>
작물과 레시피의 분당 수익, 주문으로 팔 때의 분당 코인·경험치,
그리고 단순한 플레이 방식으로 각 레벨에 도달하는 데 걸리는 시간을 출력한다.
"""
import json
import sys


def load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def report(c):
    value = {i["id"]: i["value"] for i in c["items"]}
    b = c["balance"]
    order_coin = b["orderRewardPercent"] / 100
    order_xp = b["orderXpPercent"] / 100

    print("작물 (밭 한 칸 기준, 주문으로 팔 때)")
    print(f"{'이름':<12}{'Lv':>3}{'시간':>6}{'코인/분':>9}{'경험치/분':>10}")
    crops = {}
    for cr in c["crops"]:
        gross = cr["harvestAmount"] * value[cr["harvestItemId"]]
        minutes = cr["growSeconds"] / 60
        coin = (gross * order_coin - cr["seedCost"]) / minutes
        xp = (cr["harvestXp"] + gross * order_xp) / minutes
        crops[cr["id"]] = dict(coin=coin, xp=xp, level=cr["unlockLevel"], seconds=cr["growSeconds"],
                               amount=cr["harvestAmount"], seed=cr["seedCost"], hxp=cr["harvestXp"])
        print(f"{cr['id']:<12}{cr['unlockLevel']:>3}{cr['growSeconds']:>6}{coin:>9.1f}{xp:>10.1f}")

    print("\n레시피 (시설 한 곳 기준, 재료값을 뺀 추가 가치)")
    print(f"{'이름':<18}{'Lv':>3}{'시간':>6}{'재료':>6}{'완성':>6}{'배수':>6}{'추가코인/분':>12}{'경험치/분':>10}")
    for r in c["recipes"]:
        cost = sum(value[i["itemId"]] * i["amount"] for i in r["inputs"])
        out = value[r["outputItemId"]] * r["outputAmount"]
        minutes = r["craftSeconds"] / 60
        added = (out - cost) * order_coin / minutes
        xp = (r["xp"] + (out - cost) * order_xp) / minutes
        print(f"{r['id']:<18}{r['unlockLevel']:>3}{r['craftSeconds']:>6}{cost:>6}{out:>6}{out / cost:>6.2f}{added:>12.1f}{xp:>10.1f}")

    # 단순 플레이: 밭 N칸에 그 레벨에서 경험치가 가장 좋은 작물만 계속 심고 전부 주문으로 판다.
    print("\n레벨별 소요 시간 (쉬지 않고 플레이, 밭 수는 레벨에 따라 4→12칸으로 가정)")
    total = 0.0
    for level, lv in enumerate(c["levels"], start=1):
        plots = min(12, 4 + (level - 1) * 1)
        best = max((v for v in crops.values() if v["level"] <= level), key=lambda v: v["xp"])
        xp_per_min = best["xp"] * plots
        minutes = lv["xpToNext"] / xp_per_min
        total += minutes
        print(f"Lv {level:>2} → {level + 1:<2}  필요 {lv['xpToNext']:>5}  밭 {plots:>2}칸  약 {minutes:5.1f}분  (누적 {total:5.1f}분)")


if __name__ == "__main__":
    report(load(sys.argv[1]))
