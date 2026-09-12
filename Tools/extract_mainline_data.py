#!/usr/bin/env python3
"""
extract_mainline_data.py — ESCAPE FROM SEOUL(본편) → AFTER SEOUL(모바일) 데이터 추출기

본편은 아이템/지역/상인 데이터를 C# 소스에 하드코딩해 두고 있다.
이 스크립트는 그 소스를 파싱해 모바일이 쓸 JSON 으로 뽑아낸다.

원칙
  - 본편 리포지토리는 읽기만 한다. 절대 수정하지 않는다.
  - 빌드 의존성으로 걸지 않는다. 오프라인 1회 실행 → 결과물을 모바일 repo 에 커밋.
  - 본편 데이터가 바뀌면 다시 돌리고 git diff 를 사람이 리뷰한다.

사용법
    python Tools/extract_mainline_data.py \
        --mainline ../EscapeFromSeoul \
        --out Assets

출력
    Assets/StreamingAssets/Data/items.json
    Assets/StreamingAssets/Data/maps.json
    Assets/StreamingAssets/Data/npcs.json
    Assets/StreamingAssets/Data/transferable_items.json
    Assets/Resources/Locales/{ko,en,jp,zh,ru}.json   (필요한 키만 필터)
    Assets/StreamingAssets/Data/_extract_report.json (누락/경고 리포트)

건드리지 않는 것
    Assets/Resources/Locales/mobile/*.json
        모바일 전용 문구(황 상사의 의뢰 대사 등). 위 로케일 파일은 매번 통째로 덮어쓰므로
        모바일에서 쓴 말을 거기 적으면 이 스크립트를 돌리는 순간 사라진다. 그래서 따로 둔다.
        Loc.Load 가 본편 테이블 위에 덧칠한다. 여기서 지우거나 합치지 말 것.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

# ─────────────────────────────────────────────────────────────
# MVP 대상 아이템 (DATA_SCHEMA.md §3). mvp: true 로 태깅된다.
# ─────────────────────────────────────────────────────────────
MVP_ITEM_IDS = [
    # 재료
    "JUNK03", "JUNK04", "JUNK15", "JUNK16", "JUNK20", "JUNK23",
    "JUNK08", "JUNK18", "JUNK35", "JUNK29", "JUNK21", "BAT01",
    # 판매품
    "JUNK01", "JUNK02",
    # 의료
    "MED16", "MED14", "MED02", "MED05", "MED01",
    # 식량
    "FOOD01", "FOOD02", "FOOD05",
    # 탄약
    "AMO01", "AMO05", "AMO16",
    # 총기
    "WPN04", "WPN11", "WPN01",
    # 방어 / 가방 / 도구
    "AMR01", "HDW01", "BPK01", "ATT01",
]

# MVP 지역 (GDD §8)
MVP_MAP_IDS = ["MYEONGDONG", "YONGSAN_MARKET", "GURO_FACTORY", "UIJEONGBU"]

# MVP 고용주 NPC (GDD §15)
MVP_NPC_IDS = ["HWANG"]

LOCALES = ["ko", "en", "jp", "zh", "ru"]

# ─────────────────────────────────────────────────────────────
# 전송 정책 (LINK_CONTRACT.md §5-3). 수치 근거는 그 문서에 있다.
# 실질 방어선은 값 상한이다. 개수 상한은 UX 편의용이다.
# ─────────────────────────────────────────────────────────────
TRANSFER_UNIT_PRICE_CEILING = 15_000

TRANSFER_CATEGORY_CAPS = {          # 접두사: (배송당, 일일)
    "MED": (3, 6),
    "FOOD": (5, 10),
    "AMO": (60, 120),
    "JUNK": (4, 8),
}

TRANSFER_BANNED_PREFIXES = [
    "WPN", "AMR", "HDW", "BPK", "RIG", "OPT", "NVG", "ATT", "EAR", "MEL",
    "GND", "KEY", "KEYCARD", "QUEST", "CASE", "COSM", "UIJ", "TRK", "VAC",
    "XTG", "AS",
]

TRANSFER_DENY_ITEM_IDS = [
    # 고가 환금성
    "JUNK01", "JUNK02", "JUNK05", "JUNK06", "JUNK13", "JUNK14",
    "JUNK33", "JUNK39", "JUNK40",
    # 열쇠 / 진행도
    "JUNK11", "JUNK12",
    # 하이드아웃 고급 모듈 재료
    "JUNK08", "JUNK21", "JUNK24", "JUNK26", "JUNK29", "JUNK30", "JUNK31",
    # 방탄 / 특수 원단
    "JUNK37", "JUNK38",
    # 기타
    "BAT01", "DOGTAG", "INJECTOR_CASE",
]

TRANSFER_LIMITS = {
    "maxShipmentsPerDay": 2,
    "maxItemStacksPerShipment": 4,
    "maxShipmentValue": 25_000,
    "maxDailyValue": 50_000,
}

# 로케일에서 가져올 키 접두사
LOCALE_KEY_PREFIXES = ("ITEM_", "MAP_", "TRADER_")

# id 접두사 → 모바일 태그. 의뢰 조건 매칭(`tag`)에 쓴다.
ID_PREFIX_TAGS = {
    "WPN": ["총기"],
    "AMO": ["탄약"],
    "MED": ["의료", "소모품"],
    "FOOD": ["식량", "소모품"],
    "AMR": ["방탄장비"],
    "HDW": ["방탄장비"],
    "BPK": ["가방"],
    "RIG": ["가방"],
    "MEL": ["근접"],
    "OPT": ["총기부품"],
    "ATT": ["총기부품"],
    "NVG": ["전자"],
    "EAR": ["전자"],
    "GND": ["폭발물"],
    "JUNK": ["부품"],
    "KEY": ["열쇠"],
    "CASE": ["컨테이너"],
    "QUEST": ["문서"],
}

# 개별 아이템 추가 태그 (접두사만으로 부족한 것)
EXTRA_TAGS = {
    "JUNK03": ["볼트", "금속"],
    "JUNK04": ["금속"],
    "JUNK15": ["금속"],
    "JUNK16": ["금속"],
    "JUNK20": ["전자"],
    "JUNK23": ["전자"],
    "JUNK24": ["전자"],
    "JUNK25": ["전자"],
    "JUNK26": ["전자"],
    "JUNK08": ["전자", "금속"],
    "JUNK21": ["전자"],
    "BAT01": ["전자"],
    "JUNK34": ["원단"],
    "JUNK35": ["원단"],
    "JUNK36": ["원단"],
    "JUNK37": ["원단"],
    "JUNK38": ["원단"],
    "JUNK29": ["도구"],
    "JUNK28": ["도구"],
    "JUNK17": ["소모품"],
    "JUNK18": ["소모품"],
    "JUNK19": ["소모품"],
}


# ─────────────────────────────────────────────────────────────
# C# 리터럴 파싱 유틸
# ─────────────────────────────────────────────────────────────

_STRING_RE = re.compile(r'"((?:[^"\\]|\\.)*)"')


def _mask_strings(text: str) -> tuple[str, list[str]]:
    """문자열 리터럴을 \x00{n}\x00 로 치환해 쉼표 분할을 안전하게 만든다."""
    store: list[str] = []

    def repl(m: re.Match) -> str:
        store.append(m.group(1))
        return f"\x00{len(store) - 1}\x00"

    return _STRING_RE.sub(repl, text), store


def _unmask(value: str, store: list[str]) -> str:
    def repl(m: re.Match) -> str:
        return store[int(m.group(1))]

    return re.sub(r"\x00(\d+)\x00", repl, value)


def parse_object_initializer(body: str) -> dict[str, str]:
    """`Id = "X", Price = 100, Category = ItemCategory.Ammo` → dict

    중첩 `{ }` 와 `new[]{...}` 안의 쉼표는 무시한다.
    값은 전부 문자열로 돌려주고, 형변환은 호출부에서 한다.
    """
    masked, store = _mask_strings(body)

    parts: list[str] = []
    depth = 0
    current: list[str] = []
    for ch in masked:
        if ch in "{[(":
            depth += 1
        elif ch in "}])":
            depth -= 1
        if ch == "," and depth == 0:
            parts.append("".join(current))
            current = []
        else:
            current.append(ch)
    if current:
        parts.append("".join(current))

    out: dict[str, str] = {}
    for part in parts:
        if "=" not in part:
            continue
        key, _, raw = part.partition("=")
        key = key.strip()
        raw = raw.strip()
        if not key or not key.isidentifier():
            continue
        out[key] = _unmask(raw, store)
    return out


def to_int(value: str | None, default: int = 0) -> int:
    if value is None:
        return default
    m = re.match(r"^-?\d+", value.strip())
    return int(m.group()) if m else default


def enum_tail(value: str | None, default: str) -> str:
    """`ItemCategory.Medical` → `Medical`"""
    if not value:
        return default
    return value.strip().split(".")[-1].strip()


# ─────────────────────────────────────────────────────────────
# 추출기
# ─────────────────────────────────────────────────────────────

ITEMS_ADD_RE = re.compile(r"Items\.Add\(\s*new\s+Item\s*\{(.*?)\}\s*\)\s*;", re.DOTALL)
MAP_RE = re.compile(r"new\s+Map\s*\{(.*?)\}", re.DOTALL)
TRADER_RE = re.compile(r"new\s+Trader\s*\{")


# ── 장비 능력치 ──────────────────────────────────────────────
#
# AFTER SEOUL 의 장비 효과는 본편의 실제 스탯에서 나온다. 새로 지어내지 않는 이유는
# 두 게임이 같은 물건을 말하고 있어야 하기 때문이다 — 본편에서 좋은 방탄복이
# 모바일에서도 좋아야 한다.
#
#   방탄복·헬멧  ArmorClass        ItemDatabase.EnsureArmorStats 의 switch 표
#   리그·가방    GridWidth×Height  아이템 초기화식에 그대로 있다
#   헤드셋       HearingRange      EnsureEarpieceStats (미지정 기본 12)
#   총기         (없음)            본편 총기에는 등급 스탯이 없다. 데미지는 탄약이 정하고
#                                 총은 사거리·연사만 바꾸는데 그건 RaidEngine 의
#                                 switch 문에 흩어져 있다. 그래서 총기만 가격으로
#                                 등급을 매긴다 — 이 파일에서 유일한 예외다.

ARMOR_STATS_RE = re.compile(
    r'case\s+"(?P<id>[A-Z0-9_]+)"\s*:\s*item\.ArmorClass\s*=\s*(?P<cls>\d+)\s*;', re.DOTALL)
ARMOR_STATS_MULTILINE_RE = re.compile(
    r'case\s+"(?P<id>[A-Z0-9_]+)"\s*:\s*\n\s*item\.ArmorClass\s*=\s*(?P<cls>\d+)\s*;')
HEARING_RE = re.compile(
    r'case\s+"(?P<id>[A-Z0-9_]+)"\s*:\s*item\.HearingRange\s*=\s*(?P<v>\d+)\s*;')

DEFAULT_HEARING_RANGE = 12   # EnsureEarpieceStats 의 default 분기

# 총기 등급 경계 (원). 본편 가격 분포를 5구간으로 자른 값.
WEAPON_GRADE_PRICES = [30000, 60000, 100000, 160000]

# 본편 슬롯 → AFTER SEOUL 슬롯. 본편은 10칸이지만 모바일은 6칸이다 (GDD §7).
#
# 무기 3종(주무기·보조무기·근접)을 한 칸으로 합친다. 모바일에서는 교전을 직접 조작하지
# 않으니 셋을 갈라 봤자 선택이 아니라 칸 채우기가 된다. 합쳐두면 "단검 하나만 쥐여
# 보낸다"가 성립한다 — 싸게 파견은 되지만 좋은 물건은 못 가져온다.
#
# items.json 에는 본편 slot 을 그대로 두고 equipSlot 을 따로 싣는다. 본편 데이터의
# 원형을 덮어쓰면 나중에 본편과 대조할 수가 없다.
EQUIP_SLOT_MAP = {
    "MainWeapon": "Weapon",
    "SubWeapon": "Weapon",
    "Melee": "Weapon",
    "Earpiece": "Earpiece",
    "Headwear": "Headwear",
    "BodyArmor": "BodyArmor",
    "TacticalRig": "TacticalRig",
    "Backpack": "Backpack",
}

# 무기 등급 상한. 습득률의 논리는 "그 자리를 더 오래 지킬 수 있다"이므로
# 권총이 소총을, 칼이 권총을 넘어설 수는 없다. 가격만 보면 크림슨 아이스픽(950,000원)이
# MXMR 저격소총(260,000원)보다 위가 되는데 그건 그 물건이 비싼 것이지 좋은 무기라서가 아니다.
#
# 근접무기는 상한 1 — 값이 얼마든 전부 최하급이다. 칼로 자리를 지킨다는 건 성립하지
# 않으므로 근접무기끼리 우열을 매길 이유가 없다. "파견은 되지만 좋은 건 못 가져온다"의
# 자리이고, 비싼 근접무기는 장비가 아니라 팔 물건이다.
WEAPON_GRADE_CAP = {"MainWeapon": 5, "SubWeapon": 3, "Melee": 1}

# 의정부 서바이벌 모드 전용 장비. 본편에서도 서울 경제권과 분리된 인벤토리
# (UijeongbuSurvivalInventoryService) 를 쓰고, 가격도 스폰도 0 이라 거래 대상이 아니다.
# 석궁·고철 헬멧 같은 것들이 여기 속한다. AFTER SEOUL 의 장비 풀에 들어오면 안 된다.
MODE_ONLY_ITEM_PREFIX = "UIJ_"


def parse_equipment_stats(source: str, report: dict) -> tuple[dict, dict]:
    """본편 ItemDatabase 소스에서 방어등급·청취반경 표를 뽑는다."""
    armor: dict[str, int] = {}
    for rx in (ARMOR_STATS_RE, ARMOR_STATS_MULTILINE_RE):
        for m in rx.finditer(source):
            armor[m.group("id")] = int(m.group("cls"))

    hearing: dict[str, int] = {}
    for m in HEARING_RE.finditer(source):
        hearing[m.group("id")] = int(m.group("v"))

    if not armor:
        report["warnings"].append(
            "EnsureArmorStats 에서 ArmorClass 를 하나도 못 뽑았다 — 본편 포맷이 바뀌었는지 확인")
    report["armorStatsParsed"] = len(armor)
    report["hearingStatsParsed"] = len(hearing)
    return armor, hearing


def grade_from_price(price: int, thresholds: list[int]) -> int:
    """가격을 1~5 등급으로. 총기 전용 — 다른 슬롯은 본편 스탯을 쓴다."""
    grade = 1
    for t in thresholds:
        if price >= t:
            grade += 1
    return grade


def equip_stats_for(item_id: str, slot: str, price: int, fields: dict,
                    armor: dict, hearing: dict) -> dict:
    """items.json 에 실을 장비 스탯. 장비가 아니면 빈 dict."""
    equip_slot = EQUIP_SLOT_MAP.get(slot)
    if equip_slot is None:
        return {}
    if item_id.startswith(MODE_ONLY_ITEM_PREFIX):
        return {}

    if equip_slot in ("BodyArmor", "Headwear"):
        # ArmorClass 는 대부분 아이템 초기화식에 직접 들어 있고, EnsureArmorStats 는
        # 옛 세이브나 상인이 직접 채운 물건을 위한 보정이다(`if (item.ArmorClass <= 0)`).
        # 그래서 초기화식이 먼저다 — 표만 보면 AMR13 레두트-T5 같은 게 등급 1 로 떨어진다.
        cls = to_int(fields.get("ArmorClass"), 0) or armor.get(item_id, 0)
        return {"armorClass": cls} if cls > 0 else {"armorClassMissing": True}

    if equip_slot in ("Backpack", "TacticalRig"):
        w = to_int(fields.get("GridWidth"), 0)
        h = to_int(fields.get("GridHeight"), 0)
        return {"gridSlots": w * h} if w and h else {"gridSlots": 0}

    if equip_slot == "Earpiece":
        return {"hearingRange": hearing.get(item_id, DEFAULT_HEARING_RANGE)}

    if equip_slot == "Weapon":
        grade = grade_from_price(price, WEAPON_GRADE_PRICES)
        cap = WEAPON_GRADE_CAP.get(slot, 5)
        return {"weaponGrade": min(grade, cap)}

    return {}


def tags_for(item_id: str, category: str, slot: str) -> list[str]:
    tags: list[str] = []
    m = re.match(r"^([A-Z]+)", item_id)
    if m:
        tags += ID_PREFIX_TAGS.get(m.group(1), [])
    tags += EXTRA_TAGS.get(item_id, [])
    if slot and slot != "None":
        tags.append("장비")
    if category == "Junk" and "부품" not in tags:
        tags.append("부품")
    # 중복 제거, 입력 순서 유지
    return list(dict.fromkeys(tags))


def extract_items(item_db_path: Path, report: dict) -> list[dict]:
    source = item_db_path.read_text(encoding="utf-8", errors="replace")
    blocks = ITEMS_ADD_RE.findall(source)
    total_add = source.count("Items.Add")
    report["itemsAddOccurrences"] = total_add
    report["itemsParsed"] = len(blocks)

    armor_stats, hearing_stats = parse_equipment_stats(source, report)

    items: list[dict] = []
    seen: set[str] = set()
    skipped_dynamic = 0
    for block in blocks:
        # Id 가 문자열 리터럴이 아닌 경우(변수/프로퍼티 참조)는 건너뛴다.
        # 예: `Items.Add(new Item { Id = policy.BlueprintItemId, ... })`
        if not re.search(r'\bId\s*=\s*"', block):
            skipped_dynamic += 1
            continue

        fields = parse_object_initializer(block)
        item_id = fields.get("Id", "").strip()
        if not item_id:
            report["warnings"].append("Id 없는 Item 블록을 건너뜀")
            continue
        if item_id in seen:
            report["warnings"].append(f"중복 id: {item_id}")
            continue
        seen.add(item_id)

        category = enum_tail(fields.get("Category"), "Junk")
        slot = enum_tail(fields.get("Slot"), "None")
        max_stack = to_int(fields.get("MaxStackCount"), 1) or 1

        entry = {
                "id": item_id,
                "shortName": fields.get("ShortName", "").strip(),
                "category": category,
                "slot": slot,
                "basePrice": to_int(fields.get("Price")),
                "rarity": enum_tail(fields.get("Rarity"), "Common"),
                "maxStack": max_stack,
                "spawnWeight": to_int(fields.get("SpawnWeight")),
                "caliber": fields.get("Caliber", "").strip() or None,
                "iconKey": None,  # Phase 6 아이콘 작업에서 채움
                "tags": tags_for(item_id, category, slot),
                "craftable": False,  # recipes.json 에서 역산해 채움
                "mvp": item_id in MVP_ITEM_IDS,
        }
        equip = equip_stats_for(
            item_id, slot, entry["basePrice"], fields, armor_stats, hearing_stats)
        if equip.pop("armorClassMissing", False):
            # 조용히 최하 등급으로 떨어뜨리지 않는다. 값이 없으면 본편 포맷이 바뀐 것이고,
            # 그건 300,000원짜리 방탄복이 등급 1 이 되는 식으로 조용히 망가진다.
            report["warnings"].append(f"{item_id}: 방어등급을 못 찾음 — 장비 목록에서 제외")
            equip = {}
        if equip:
            entry["equippable"] = True
            entry["equipSlot"] = EQUIP_SLOT_MAP[slot]
            entry.update(equip)
        items.append(entry)

    report["itemsSkippedDynamicId"] = skipped_dynamic
    if len(blocks) < total_add - 1:  # -1: `Items.Add` 문자열이 메서드 정의에도 등장 가능
        report["warnings"].append(
            f"Items.Add 등장 {total_add}회 중 {len(blocks)}건만 파싱됨 — 포맷 확인 필요"
        )
    return items


def extract_maps(dummy_path: Path, report: dict) -> list[dict]:
    source = dummy_path.read_text(encoding="utf-8", errors="replace")
    m = re.search(r"Maps\s*=\s*new\s+Map\[\]\s*\{(.*?)\n\s*\};", source, re.DOTALL)
    if not m:
        report["warnings"].append("DummyData.cs 에서 Maps 배열을 찾지 못함")
        return []

    maps: list[dict] = []
    for block in MAP_RE.findall(m.group(1)):
        fields = parse_object_initializer(block)
        map_id = fields.get("Id", "").strip()
        if not map_id:
            continue
        maps.append(
            {
                "id": map_id,
                "mainlineDifficulty": fields.get("DifficultyLevel", "").strip(),
                "mainlineDurationMinutes": to_int(fields.get("DurationMinutes")),
                "mapX": to_int(fields.get("MapX")),
                "mapY": to_int(fields.get("MapY")),
                "mvp": map_id in MVP_MAP_IDS,
            }
        )
    report["mapsParsed"] = len(maps)
    return maps


def extract_traders(dummy_path: Path, report: dict) -> list[dict]:
    """Trader 는 Inventory 안에 또 new Item 이 중첩돼 있어서 중괄호 균형으로 자른다."""
    source = dummy_path.read_text(encoding="utf-8", errors="replace")
    start = source.find("Traders = new Trader[]")
    if start == -1:
        report["warnings"].append("DummyData.cs 에서 Traders 배열을 찾지 못함")
        return []

    traders: list[dict] = []
    for m in TRADER_RE.finditer(source, start):
        open_idx = source.index("{", m.end() - 1)
        depth = 0
        end_idx = open_idx
        masked, _ = _mask_strings(source[open_idx : open_idx + 20000])
        for i, ch in enumerate(masked):
            if ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    end_idx = open_idx + i
                    break
        body = source[open_idx + 1 : end_idx]

        # Inventory 등 중첩 블록을 제거한 뒤 최상위 필드만 읽는다
        flat = re.sub(r"new\s+\w+(\[\])?\s*\{.*?\}", "{}", body, flags=re.DOTALL)
        flat = re.sub(r"new\s+List<\w+>\s*\{.*", "", flat, flags=re.DOTALL)
        fields = parse_object_initializer(flat)

        trader_id = fields.get("Id", "").strip()
        if not trader_id:
            continue

        inventory_ids = re.findall(r'new\s+Item\s*\{\s*Id\s*=\s*"([^"]+)"', body)
        traders.append(
            {
                "id": trader_id,
                "currency": enum_tail(fields.get("Currency"), "Won"),
                "unlockedInMainline": fields.get("IsUnlocked", "false").strip() == "true",
                "mainlineInventoryItemIds": list(dict.fromkeys(inventory_ids)),
                "mvp": trader_id in MVP_NPC_IDS,
            }
        )
    report["tradersParsed"] = len(traders)
    return traders


def build_transfer_policy(items: list[dict], report: dict) -> dict:
    """본편 실측 가격으로 전송 허용 목록을 만든다.

    허용 = 허용 카테고리 && deny 목록에 없음 && 0 < basePrice <= 단가 상한.
    결과를 allowlistSnapshot 에 가격과 함께 박아 두어, 다음 실행 때 diff 로
    "새 아이템이 허용 목록에 들어왔다 / 가격이 바뀌었다"를 사람이 볼 수 있게 한다.
    """
    deny = set(TRANSFER_DENY_ITEM_IDS)
    allowed: dict[str, int] = {}
    for item in items:
        m = re.match(r"^([A-Z]+)", item["id"])
        prefix = m.group(1) if m else ""
        if prefix not in TRANSFER_CATEGORY_CAPS:
            continue
        if item["id"] in deny:
            continue
        if not (0 < item["basePrice"] <= TRANSFER_UNIT_PRICE_CEILING):
            continue
        allowed[item["id"]] = item["basePrice"]

    categories: dict[str, dict] = {}
    for prefix, (per_shipment, per_day) in TRANSFER_CATEGORY_CAPS.items():
        categories[prefix] = {
            "allowed": True,
            "maxPerShipment": per_shipment,
            "maxPerDay": per_day,
        }
    for prefix in TRANSFER_BANNED_PREFIXES:
        categories[prefix] = {"allowed": False}

    # 값 상한이 개수 상한보다 느슨하면 정책 전체가 무의미해진다.
    price_of = {i["id"]: i["basePrice"] for i in items}
    worst_stack = max(
        (price_of[i] * TRANSFER_CATEGORY_CAPS[re.match(r"^([A-Z]+)", i).group(1)][0]
         for i in allowed),
        default=0,
    )
    if worst_stack <= TRANSFER_LIMITS["maxShipmentValue"]:
        report["warnings"].append(
            "값 상한이 개수 상한보다 느슨하다 — maxShipmentValue 가 실질 제약이 아니다"
        )
    report["transferAllowedCount"] = len(allowed)
    report["transferWorstStackValue"] = worst_stack

    return {
        "schemaVersion": 1,
        "_comment": "AFTER SEOUL -> ESCAPE FROM SEOUL 전송 허용 정책. 양쪽 repo 에 동일 사본을 둔다.",
        "_generatedBy": "Tools/extract_mainline_data.py",
        "unitPriceCeiling": TRANSFER_UNIT_PRICE_CEILING,
        "categories": categories,
        "denyItemIds": sorted(deny),
        "limits": dict(TRANSFER_LIMITS),
        "allowlistSnapshot": dict(sorted(allowed.items())),
    }


def filter_locales(locale_dir: Path, keep_ids: set[str], report: dict) -> dict[str, dict]:
    out: dict[str, dict] = {}
    for code in LOCALES:
        path = locale_dir / f"{code}.json"
        if not path.exists():
            report["warnings"].append(f"로케일 없음: {code}.json")
            continue
        data = json.loads(path.read_text(encoding="utf-8"))
        if isinstance(data, dict) and "entries" in data:
            data = data["entries"]

        kept = {}
        for key, value in data.items():
            if not key.startswith(LOCALE_KEY_PREFIXES):
                continue
            m = re.match(r"^(?:ITEM|MAP|TRADER)_(.+?)_(NAME|SHORT|DESC)$", key)
            if m and m.group(1) not in keep_ids:
                continue
            kept[key] = value
        out[code] = kept
        report.setdefault("localeKeyCounts", {})[code] = len(kept)
    return out


# ─────────────────────────────────────────────────────────────

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--mainline", required=True, type=Path, help="본편 리포지토리 루트")
    ap.add_argument("--out", required=True, type=Path, help="모바일 Assets 폴더")
    args = ap.parse_args()

    logic = args.mainline / "Assets" / "Scripts" / "SeoulLogic"
    locales = args.mainline / "Assets" / "Resources" / "Locales"

    for required in (logic / "ItemDatabase.cs", logic / "DummyData.cs", locales):
        if not required.exists():
            print(f"[FATAL] 없음: {required}", file=sys.stderr)
            return 1

    report: dict = {"warnings": []}

    items = extract_items(logic / "ItemDatabase.cs", report)
    maps = extract_maps(logic / "DummyData.cs", report)
    traders = extract_traders(logic / "DummyData.cs", report)

    # 상인 재고에서 ItemDatabase 에 없는 id 를 걷어낸다.
    #
    # 환전 상인(도깨비·US_LIAISON)은 EXCHANGE_JPY / EXCHANGE_USD 를 재고로 들고 있는데
    # 그건 본편의 환전 기능이지 아이템이 아니라서 ItemDatabase 에 없다. 모바일에는 환전이
    # 없으므로 영영 생기지 않는다.
    #
    # Shop.OffersFor 가 GetItem == null 이면 조용히 건너뛰어서 게임은 안 죽는다 —
    # 그래서 더 오래 남는다. 여기서 털지 않으면 추출을 돌릴 때마다 다시 들어온다.
    item_id_set = {i["id"] for i in items}
    dropped: list[str] = []
    for t in traders:
        keep = [x for x in t["mainlineInventoryItemIds"] if x in item_id_set]
        dropped += [x for x in t["mainlineInventoryItemIds"] if x not in item_id_set]
        t["mainlineInventoryItemIds"] = keep

    if dropped:
        report["droppedTraderItems"] = sorted(set(dropped))
        report["warnings"].append(
            f"상인 재고에서 ItemDatabase 에 없는 id {len(set(dropped))}건 제거: {sorted(set(dropped))}"
        )

    keep_ids = {i["id"] for i in items} | {m["id"] for m in maps} | {t["id"] for t in traders}
    locale_data = filter_locales(locales, keep_ids, report)

    # ── 무결성 검사 ──────────────────────────────────────────
    ko = locale_data.get("ko", {})
    missing_name = [i["id"] for i in items if f"ITEM_{i['id']}_NAME" not in ko]
    if missing_name:
        report["warnings"].append(
            f"ko 로케일에 이름이 없는 아이템 {len(missing_name)}건: {missing_name[:15]}"
        )
    report["itemsMissingKoName"] = len(missing_name)

    item_ids = {i["id"] for i in items}
    missing_mvp = [i for i in MVP_ITEM_IDS if i not in item_ids]
    if missing_mvp:
        report["warnings"].append(f"MVP 아이템인데 본편에 없음: {missing_mvp}")
    report["missingMvpItems"] = missing_mvp

    map_ids = {m["id"] for m in maps}
    missing_maps = [m for m in MVP_MAP_IDS if m not in map_ids]
    if missing_maps:
        report["warnings"].append(f"MVP 지역인데 본편에 없음: {missing_maps}")
    report["missingMvpMaps"] = missing_maps

    # ── 출력 ────────────────────────────────────────────────
    data_dir = args.out / "StreamingAssets" / "Data"
    locale_dir = args.out / "Resources" / "Locales"
    data_dir.mkdir(parents=True, exist_ok=True)
    locale_dir.mkdir(parents=True, exist_ok=True)

    def dump(path: Path, payload) -> None:
        path.write_text(
            json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )

    transfer = build_transfer_policy(items, report)
    for item in items:
        item["transferable"] = item["id"] in transfer["allowlistSnapshot"]

    dump(data_dir / "items.json", {"schemaVersion": 1, "items": items})
    dump(data_dir / "transferable_items.json", transfer)
    dump(data_dir / "maps.json", {"schemaVersion": 1, "maps": maps})
    dump(data_dir / "npcs.json", {"schemaVersion": 1, "npcs": traders})
    for code, payload in locale_data.items():
        dump(locale_dir / f"{code}.json", payload)
    dump(data_dir / "_extract_report.json", report)

    print(f"items   {len(items):>4}  (MVP {sum(1 for i in items if i['mvp'])})")
    print(f"maps    {len(maps):>4}  (MVP {sum(1 for m in maps if m['mvp'])})")
    print(f"npcs    {len(traders):>4}")
    print(f"전송허용 {len(transfer['allowlistSnapshot']):>3}종 "
          f"(단가상한 {TRANSFER_UNIT_PRICE_CEILING:,}원 / 배송 {TRANSFER_LIMITS['maxShipmentValue']:,}원 "
          f"/ 일일 {TRANSFER_LIMITS['maxDailyValue']:,}원)")
    for code, count in report.get("localeKeyCounts", {}).items():
        print(f"locale  {code}: {count} keys")
    if report["warnings"]:
        print("\n경고:")
        for w in report["warnings"]:
            print(f"  - {w}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
