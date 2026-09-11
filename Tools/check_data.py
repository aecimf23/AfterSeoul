import json, pathlib, sys

D = pathlib.Path(r"D:\devSource\AfterSeoul\Assets\StreamingAssets\Data")
load = lambda n: json.loads((D / n).read_text(encoding="utf-8"))

items = {i["id"]: i for i in load("items.json")["items"]}
tables = {t["id"]: t for t in load("loot_tables.json")["tables"]}
exps = load("expeditions.json")["expeditions"]
quests = load("daily_quests.json")["pools"][0]["quests"]
recipes = load("recipes.json")["recipes"]
bal = load("balance.json")
maps = {m["id"] for m in load("maps.json")["maps"]}

bad = []
# 1. 참조 정합성
for t in tables.values():
    if sum(e["weight"] for e in t["entries"]) != 100:
        bad.append(f"{t['id']} weight sum={sum(e['weight'] for e in t['entries'])}")
    for e in t["entries"]:
        if e["itemId"] not in items:
            bad.append(f"{t['id']} unknown item {e['itemId']}")
obtainable = {e["itemId"] for t in tables.values() for e in t["entries"]}
for r in recipes:
    for i in r["inputs"] + [r["output"]]:
        if i["itemId"] not in items:
            bad.append(f"{r['id']} unknown item {i['itemId']}")
    obtainable.add(r["output"]["itemId"])

# 2. 파견비 비율
print(f"{'map':<16}{'cost':>9}{'EV/roll':>9}{'E[rolls]':>9}{'EV':>10}{'ratio':>7}")
for e in exps:
    if e["mapId"] not in maps:
        bad.append(f"unknown map {e['mapId']}")
    t = tables[e["lootTable"]]
    ev_roll = sum(
        it["weight"] * items[it["itemId"]]["basePrice"]
        * (it["count"]["min"] + it["count"]["max"]) / 2
        for it in t["entries"]) / 100
    rolls = t["rolls"]["min"] + 1          # 티어1 스캐브 탐색 보정 +1 가정
    cost = e["baseCostWage"] + e["baseCostSupply"]
    ratio = cost / (ev_roll * rolls)
    print(f"{e['mapId']:<16}{cost:>9,}{ev_roll:>9,.0f}{rolls:>9}{ev_roll*rolls:>10,.0f}{ratio:>7.2f}")
    if not 0.45 <= ratio <= 0.65:
        bad.append(f"{e['mapId']} cost ratio {ratio:.2f} (목표 0.55, 허용 0.45~0.65)")

# 3. 의뢰 보상 공식
print(f"\n{'quest':<22}{'tier':>5}{'items':>10}{'formula':>10}{'actual':>10}{'diff':>7}")
for q in quests:
    total = 0
    for req in q["requires"]:
        if "itemId" in req and req["itemId"]:
            if req["itemId"] not in items:
                bad.append(f"{q['id']} unknown item {req['itemId']}")
                continue
            if req["itemId"] not in obtainable:
                bad.append(f"{q['id']} 입수 불가 아이템 {req['itemId']}")
            total += items[req["itemId"]]["basePrice"] * req["count"]
        else:
            tagged = [i for i in items.values()
                      if req["tag"] in (i.get("tags") or []) and i["id"] in obtainable]
            if not tagged:
                bad.append(f"{q['id']} 태그 '{req['tag']}' 입수 가능 아이템 없음")
                continue
            total += min(i["basePrice"] for i in tagged) * req["count"]
    mult = bal["questRewardMultiplierByTier"][q["tier"] - 1]
    formula, actual = total * mult, q["reward"]["money"]
    diff = (actual - formula) / formula if formula else 0
    print(f"{q['id']:<22}{q['tier']:>5}{total:>10,}{formula:>10,.0f}{actual:>10,}{diff:>7.1%}")
    if abs(diff) > 0.05:
        bad.append(f"{q['id']} 보상 {actual:,} vs 공식 {formula:,.0f} ({diff:+.1%})")
    if len(q["touches"]) < 2:
        bad.append(f"{q['id']} touches < 2")

print()
if bad:
    print("문제:")
    for b in bad:
        print("  -", b)
    sys.exit(1)
print("모든 검사 통과")
