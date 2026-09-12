#!/usr/bin/env python3
"""
check_data.py — Unity 없이 돌리는 데이터 점검

`SessionAndDataTests` 의 DataFileTests 가 하는 검사 중 **Unity 가 필요 없는 것**을
여기서도 돌린다. 에디터를 켜는 데 몇 분이 걸리는데, 데이터를 고칠 때마다 그걸 기다리면
결국 안 돌리게 된다.

여기 있는 검사는 전부 **이 프로젝트에서 실제로 한 번씩 터졌던 것**이다:

1. **파서가 안 받는 키** — `manualSteps`/`stepNames`/`stepGames` 가 JSON 에는 있는데
   `JsonDataRegistry` 가 안 옮겨서, 4단계 레시피가 3단계로 돌았다. `penalty` 도 같았다 —
   겁쟁이가 장점만 있는 특성이었다.
2. **닿을 수 없는 아이템** — 레시피 재료·의뢰 요구품이 어느 전리품 표에도, 어느 상점에도
   없으면 그 레시피와 의뢰는 영영 못 깬다.
3. **모르는 id** — 오타 하나로 전리품 표가 존재하지 않는 아이템을 가리키면 조용히 빈손이 된다.
4. **이름 없는 것** — 손에 넣을 수 있는데 로케일 키가 없으면 `ITEM_XXX_NAME` 이 그대로 화면에 뜬다.
   (전부에 이름이 있어야 하는 건 아니다 — items.json 은 본편 추출본이라 모바일이 안 쓰는 게 458종 중 대부분이다.)
5. **지배당하는 지역** — 더 어렵게 여는 곳이 더 쉬운 곳보다 1회 수익도 시간당도 낮으면
   갈 이유가 없다. 남산이 그랬다.

    python Tools/check_data.py

터진 게 있으면 종료 코드 1.
"""

from __future__ import annotations

import glob
import io
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, "Assets", "StreamingAssets", "Data")
GAME = os.path.join(ROOT, "Assets", "Game")

problems: list[str] = []


def fail(check: str, message: str) -> None:
    problems.append(f"[{check}] {message}")


def load(name: str):
    with io.open(os.path.join(DATA, name), encoding="utf-8") as f:
        return json.load(f)


# ─────────────────────────────────────────────────────────────
# 1. 파서가 안 받는 키
# ─────────────────────────────────────────────────────────────

def check_unparsed_keys() -> None:
    """JSON 에 있는데 어떤 C# 필드도 안 받는 키. manualSteps 부류를 잡는다."""
    src = ""
    for path in glob.glob(os.path.join(GAME, "**", "*.cs"), recursive=True):
        with io.open(path, encoding="utf-8") as f:
            src += f.read() + "\n"

    fields = {m.group(1).lower()
              for m in re.finditer(r"public\s+[\w<>,\[\]\.\?]+\s+(\w+)\s*(?:=|;|\{)", src)}

    # 사전의 키처럼 "필드가 아닌" 자리는 건너뛴다. 여기를 훑으면 아이템 id 가 전부 걸린다.
    skip_files = {"transferable_items.json", "_extract_report.json"}

    # 본편 추출기가 얹어 주는 참고용 열. 모바일이 안 쓰는 게 정상이다.
    extractor_only = {
        "caliber", "craftable", "iconkey", "mvp", "rarity", "shortname", "spawnweight",
        "mainlinedifficulty", "mainlinedurationminutes", "mapx", "mapy",
        "unlockedinmainline",
    }

    def walk(node, acc):
        if isinstance(node, dict):
            for k, v in node.items():
                if not k.startswith("_"):
                    acc.add(k)
                walk(v, acc)
        elif isinstance(node, list):
            for v in node:
                walk(v, acc)

    for path in sorted(glob.glob(os.path.join(DATA, "*.json"))):
        name = os.path.basename(path)
        if name in skip_files:
            continue

        acc: set[str] = set()
        walk(load(name), acc)

        for key in sorted(acc):
            low = key.lower()
            if low in fields or low in extractor_only:
                continue
            fail("미파싱", f"{name}: '{key}' 를 받는 C# 필드가 없다 — 값이 조용히 버려진다")


# ─────────────────────────────────────────────────────────────
# 2·3. id 무결성과 도달 가능성
# ─────────────────────────────────────────────────────────────

def check_ids_and_reachability() -> None:
    items = {i["id"] for i in load("items.json")["items"]}
    tables = load("loot_tables.json")["tables"]
    recipes = load("recipes.json")["recipes"]
    shop = load("shop.json")
    quests = load("daily_quests.json")

    # 모르는 id
    for t in tables:
        for e in t["entries"]:
            if e["itemId"] not in items:
                fail("모르는id", f"{t['id']} 가 없는 아이템 {e['itemId']} 를 가리킨다")

    for r in recipes:
        for i in r.get("inputs", []):
            if i["itemId"] not in items:
                fail("모르는id", f"{r['id']} 의 재료 {i['itemId']} 가 없다")
        if r["output"]["itemId"] not in items:
            fail("모르는id", f"{r['id']} 의 산출물 {r['output']['itemId']} 가 없다")

    # 상인 재고가 없는 아이템을 가리키는 경우.
    #
    # Shop.OffersFor 는 GetItem 이 null 이면 조용히 건너뛰므로 게임은 안 죽는다 —
    # 그래서 더 오래 남는다. 본편 추출본을 그대로 들여올 때 생긴 찌꺼기다.
    for item_id in set(_shop_offers(shop)):
        if item_id not in items:
            fail("모르는id", f"상인 재고에 없는 아이템 {item_id} 가 있다 — 상점에서 조용히 빠진다")

    # 손에 넣을 수 있는 것 = 전리품 + 상점 + 레시피 산출물
    obtainable = {e["itemId"] for t in tables for e in t["entries"]}
    obtainable |= {r["output"]["itemId"] for r in recipes}
    obtainable |= {i for i in _shop_offers(shop) if i in items}

    for r in recipes:
        for i in r.get("inputs", []):
            if i["itemId"] not in obtainable:
                fail("도달불가", f"{r['id']} 의 재료 {i['itemId']} 는 어디서도 구할 수 없다")

    for pool_id, pool in _quest_pools(quests):
        for q in pool:
            for req in q.get("requires", []):
                item = req.get("itemId")
                if item and item not in obtainable:
                    fail("도달불가", f"{pool_id}/{q['id']} 가 구할 수 없는 {item} 를 요구한다")


def check_names() -> None:
    """
    손에 넣을 수 있는 것에는 이름이 있어야 한다.

    <b>전부에 이름이 있어야 하는 건 아니다.</b> `items.json` 은 본편 추출본이라 458종이 들어 있고
    모바일이 실제로 보여주는 건 그중 일부다 — 안 나오는 물건의 이름까지 채우는 건 낭비다.
    문제가 되는 건 **화면에 뜨는데 이름이 없는 것**뿐이고, 그때는 `ITEM_XXX_NAME` 이 그대로 뜬다.
    """
    locales = ["ko.json", os.path.join("mobile", "ko.json")]
    keys: set[str] = set()

    for rel in locales:
        path = os.path.join(ROOT, "Assets", "Resources", "Locales", rel)
        if not os.path.exists(path):
            fail("로케일", f"{rel} 이 없다")
            continue
        with io.open(path, encoding="utf-8") as f:
            keys |= set(json.load(f))

    tables = load("loot_tables.json")["tables"]
    recipes = load("recipes.json")["recipes"]
    maps = load("expeditions.json")["expeditions"]
    items = {i["id"] for i in load("items.json")["items"]}

    obtainable = {e["itemId"] for t in tables for e in t["entries"]}
    obtainable |= {r["output"]["itemId"] for r in recipes}
    obtainable |= {i["itemId"] for r in recipes for i in r.get("inputs", [])}
    obtainable |= set(_shop_offers(load("shop.json")))

    # 존재하지 않는 id 는 위 '모르는id' 가 이미 잡았다. 여기서 또 세면
    # 같은 한 가지 잘못이 두 줄로 보고돼서 어느 쪽을 고쳐야 하는지가 흐려진다.
    obtainable &= items

    for item in sorted(obtainable):
        if f"ITEM_{item}_NAME" not in keys:
            fail("이름없음", f"{item} 는 손에 넣을 수 있는데 이름이 없다 — 화면에 키가 그대로 뜬다")

    for m in maps:
        if f"MAP_{m['mapId']}_NAME" not in keys:
            fail("이름없음", f"{m['mapId']} 지역 이름이 없다")

    for e in load("expedition_events.json")["events"]:
        for suffix in ("PASS", "FAIL"):
            if f"{e['id']}_{suffix}" not in keys:
                fail("이름없음", f"사건 {e['id']} 의 {suffix} 문장이 없다 — 보고서가 비어 보인다")

    for e in load("employers.json")["employers"]:
        for suffix in ("NAME", "DESC"):
            if f"TRADER_{e['npcId']}_{suffix}" not in keys:
                fail("이름없음", f"고용주 {e['npcId']} 의 {suffix} 가 없다 — 선택 화면이 키를 보여준다")


def _shop_offers(shop):
    """
    상점이 파는 물건.

    <b>shop.json 에는 품목이 없다.</b> 상인 목록(`traders`)만 있고 실제 재고는
    `npcs.json` 의 `mainlineInventoryItemIds` 에서 온다 — 모바일에서 품목을 따로 지어내면
    본편과 다른 물건을 파는 상인이 되기 때문이다.

    <para>처음 이 함수는 `offers`/`items`/`stock` 을 찾았고 셋 다 없어서 <b>말없이 빈 목록</b>을
    돌려주고 있었다. 그러면 "상점에서만 구할 수 있는 재료"가 도달 불가로 오판되거나, 반대로
    검사가 통째로 헛돈다. 검사 도구가 조용히 아무것도 안 하는 게 제일 나쁜 고장이라,
    아래 `_sanity` 가 결과가 비면 그 자체를 실패로 잡는다.</para>
    """
    npcs = {n["id"]: n for n in load("npcs.json")["npcs"]}

    for trader in shop.get("traders", []) or []:
        npc = npcs.get(trader.get("npcId"))
        if npc is None:
            fail("모르는id", f"shop.json 의 상인 {trader.get('npcId')} 가 npcs.json 에 없다")
            continue

        for item_id in npc.get("mainlineInventoryItemIds") or []:
            yield item_id


def _quest_pools(quests):
    pools = quests.get("pools") or quests
    if isinstance(pools, dict):
        for pool_id, pool in pools.items():
            if isinstance(pool, list):
                yield pool_id, pool
    elif isinstance(pools, list):
        for p in pools:
            if isinstance(p, dict) and "quests" in p:
                yield p.get("id", "?"), p["quests"]


# ─────────────────────────────────────────────────────────────
# 4. 지배당하는 지역
# ─────────────────────────────────────────────────────────────

def check_region_ladder() -> None:
    """
    더 어렵게 여는 지역이 더 쉬운 곳보다 **1회 수익도 시간당도** 낮으면 갈 이유가 없다.

    C# 쪽 `EveryUnlockedRegion_IsTheBestChoiceForSomething` 과 같은 판단이다.
    실제 파견을 돌리는 대신 기대값을 **해석적으로** 계산한다 — 표본을 뽑지 않으니
    결과가 흔들리지 않고, 데이터를 고칠 때마다 즉시 답이 나온다.

    <b>풀장비 기준이어야 한다.</b> 맨손 기준으로 재면 순위가 뒤집힌다: 장비는 회수 횟수를
    +5 올리는데, 그 +5 가 잭팟이 있는 표(의정부 800,000원)에서는 훨씬 크게 작용한다.
    처음에 맨손으로 쟀다가 의정부와 한강이 억울하게 걸렸다.
    """
    bal = load("balance.json")
    maps = load("expeditions.json")["expeditions"]
    items = {i["id"]: i for i in load("items.json")["items"]}
    tables = {t["id"]: t for t in load("loot_tables.json")["tables"]}
    tiers = {t["tier"]: t for t in load("scav_pool.json")["tiers"]}

    ratio = bal["sellPriceRatio"]
    base_wage = bal["baseWagePerHour"]
    eq = bal["equipment"]

    # 풀장비 한 명: 최고 등급 무기 + 헤드셋 3단계, 큰 리그(+2) + 큰 가방(+3).
    luck = min(eq["luckCap"], 5 * eq["weaponLuckPerGrade"] + 3 * eq["headsetLuckPerStep"])
    extra_rolls = 5
    search = 4   # 티어 1 평균

    def expected(m):
        t = tables[m["lootTable"]]
        entries = t["entries"]
        total_w = sum(e["weight"] for e in entries)

        def unit(e):
            return (items[e["itemId"]]["basePrice"]
                    * (e["count"]["min"] + e["count"]["max"]) / 2)

        mean_all = sum(e["weight"] * unit(e) for e in entries) / total_w

        # 운은 "재추첨"이 아니라 기대 단가 이상인 항목만 모아둔 표에서 뽑는 것이다
        # (ExpeditionSystem 의 PickGoodEntry 와 같은 정의).
        good = [e for e in entries if unit(e) >= mean_all]
        good_w = sum(e["weight"] for e in good)
        if good_w >= total_w:
            good_w = 0   # 전부 같은 값인 표에서는 운이 아무것도 안 바꾼다

        if good_w > 0:
            mean_good = sum(e["weight"] * unit(e) for e in good) / good_w
            per_roll = (1 - luck) * mean_all + luck * mean_good
        else:
            per_roll = mean_all

        rolls = min(t["rolls"]["min"] + search // 4 + extra_rolls,
                    t["rolls"]["max"] + 2 + extra_rolls)

        gross = per_roll * rolls * ratio
        cost = m["baseCostWage"] * tiers[1]["wagePerHour"] / base_wage + m["baseCostSupply"]
        return gross - cost

    def gate(m):
        u = m["unlockCondition"]
        if u["type"] == "default":
            return ("default", None, 0)
        return (u["type"], u.get("npcId"), u["value"])

    rows = []
    for m in maps:
        net = expected(m)
        hours = m["durationMinutes"] / 60.0
        rows.append((m["mapId"], gate(m), net, net / hours if hours else net))

    for later in rows:
        for earlier in rows:
            if later is earlier:
                continue
            if not _is_easier(earlier[1], later[1]):
                continue
            if later[2] < earlier[2] * 0.95 and later[3] < earlier[3] * 0.95:
                fail("지배당함",
                     f"{later[0]} 는 더 쉽게 여는 {earlier[0]} 보다 1회 수익도 시간당도 낮다 "
                     f"({later[2]:,.0f} vs {earlier[2]:,.0f} / "
                     f"{later[3]:,.0f} vs {earlier[3]:,.0f}) — 갈 이유가 없다")


def _is_easier(a, b) -> bool:
    """a 가 b 보다 확실히 쉽게 열리는가. 축이 다르면 순서를 말할 수 없다."""
    if a[0] == "default":
        return b[0] != "default"
    if b[0] == "default":
        return False
    if a[0] != b[0]:
        return False
    if a[0] == "npcTrust" and a[1] != b[1]:
        return False
    return a[2] < b[2]


# ─────────────────────────────────────────────────────────────

def _sanity() -> None:
    """
    <b>검사가 실제로 무언가를 봤는가.</b>

    데이터 모양을 잘못 짚으면 이 도구는 아무것도 못 찾고 "통과"라고 말한다 — 실제로
    `_shop_offers` 가 그랬다. 없는 키를 찾다가 말없이 빈 목록을 돌려주고 있었고,
    그동안 상점 재고는 한 번도 검사되지 않았다.
    <b>조용히 아무것도 안 하는 검사는 없는 검사보다 나쁘다.</b> 자기가 뭘 봤는지 세게 한다.
    """
    counts = {
        "아이템": len(load("items.json")["items"]),
        "전리품 표": len(load("loot_tables.json")["tables"]),
        "레시피": len(load("recipes.json")["recipes"]),
        "지역": len(load("expeditions.json")["expeditions"]),
        "사건": len(load("expedition_events.json")["events"]),
        "고용주": len(load("employers.json")["employers"]),
        "상점 품목": len(set(_shop_offers(load("shop.json")))),
        "의뢰": sum(len(p) for _, p in _quest_pools(load("daily_quests.json"))),
    }

    for label, n in counts.items():
        if n == 0:
            fail("헛검사", f"{label} 를 하나도 못 읽었다 — 데이터 모양이 바뀌었거나 도구가 틀렸다")

    print("  " + "  ".join(f"{k} {v}" for k, v in counts.items()))


def main() -> int:
    _sanity()

    check_unparsed_keys()
    check_ids_and_reachability()
    check_names()
    check_region_ladder()

    if not problems:
        print("데이터 점검 통과")
        return 0

    for p in problems:
        print(p)
    print(f"\n{len(problems)}건")
    return 1


if __name__ == "__main__":
    sys.exit(main())
