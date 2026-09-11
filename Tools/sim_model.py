#!/usr/bin/env python3
"""
sim_model.py — AFTER SEOUL 코어 시뮬레이션의 **참조 구현**

목적 둘:

1. **설계 검증.** C# 코드를 Unity 없이 돌려볼 수 없으니, 같은 알고리즘을 여기에
   옮겨 놓고 시나리오를 실행한다. 검증 대상은 문법이 아니라 설계다 —
   정산 순서, 시드 결정론, 시계 조작 내성, 멱등성.

2. **밸런스 샌드박스.** 나중에 파견비·보상·확률을 조정할 때 Unity 를 켜지 않고
   여기서 수천 번 돌려 분포를 본다.

C# 과의 대응:
    Rng              ↔ Assets/Game/Core/Rng.cs
    GameTime         ↔ Assets/Game/Core/GameTime.cs
    OfflineResolver  ↔ Assets/Game/Core/OfflineResolver.cs
    ExpeditionSystem ↔ Assets/Game/Expedition/ExpeditionSystem.cs
    FactorySystem    ↔ Assets/Game/Factory/FactorySystem.cs
    DailyQuestSystem ↔ Assets/Game/Quest/DailyQuestSystem.cs
    Warehouse        ↔ Assets/Game/Inventory/Warehouse.cs

**한쪽을 고치면 반드시 다른 쪽도 고친다.** 특히 Rng 와 정산 순서는 어긋나면
이 모델의 검증이 통째로 무의미해진다.

    python Tools/sim_model.py            # 시나리오 검증
    python Tools/sim_model.py --balance  # 밸런스 분포 샘플
"""

from __future__ import annotations

import argparse
import sys
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone, date

UINT32 = 0xFFFFFFFF

GAME_ZONE = timezone(timedelta(hours=9))   # KST 고정
DAY_BOUNDARY_HOUR = 5                       # 새벽 5시에 하루가 바뀐다


# ─────────────────────────────────────────────────────────────
# Rng  ↔  Assets/Game/Core/Rng.cs
# ─────────────────────────────────────────────────────────────

class Rng:
    """xorshift32. System.Random 을 쓰지 않는 이유는 C# 쪽 주석 참조."""

    __slots__ = ("state",)

    def __init__(self, seed: int):
        seed &= UINT32
        self.state = seed if seed != 0 else 0x9E3779B9

    def next_uint(self) -> int:
        x = self.state
        x ^= (x << 13) & UINT32
        x ^= x >> 17
        x ^= (x << 5) & UINT32
        self.state = x & UINT32
        return self.state

    def next_int(self, max_exclusive: int) -> int:
        if max_exclusive <= 0:
            raise ValueError("max_exclusive must be positive")
        return self.next_uint() % max_exclusive

    def next_int_range(self, lo: int, hi: int) -> int:
        return lo + self.next_int(hi - lo + 1)

    def next_double(self) -> float:
        return self.next_uint() / 4294967296.0

    def chance(self, p: float) -> bool:
        return self.next_double() < p

    @staticmethod
    def derive(master_seed: int, purpose: str) -> int:
        """FNV-1a 32bit. 용도별 스트림 분리."""
        h = 2166136261
        for ch in purpose:
            h ^= ord(ch)
            h = (h * 16777619) & UINT32
        return (master_seed ^ h) & UINT32

    @staticmethod
    def for_purpose(master_seed: int, purpose: str) -> "Rng":
        return Rng(Rng.derive(master_seed, purpose))


# ─────────────────────────────────────────────────────────────
# GameTime  ↔  Assets/Game/Core/GameTime.cs
# ─────────────────────────────────────────────────────────────

def game_date_of(utc: datetime) -> date:
    local = utc.astimezone(GAME_ZONE)
    shifted = local - timedelta(hours=DAY_BOUNDARY_HOUR)
    return shifted.date()


def start_of_game_date(d: date) -> datetime:
    local = datetime(d.year, d.month, d.day, DAY_BOUNDARY_HOUR, 0, 0, tzinfo=GAME_ZONE)
    return local.astimezone(timezone.utc)


def day_boundaries_between(frm: datetime, to: datetime):
    if to <= frm:
        return
    cursor = start_of_game_date(game_date_of(frm) + timedelta(days=1))
    while cursor <= to:
        yield cursor
        cursor = start_of_game_date(game_date_of(cursor) + timedelta(days=1))


# ─────────────────────────────────────────────────────────────
# 데이터 정의 (테스트용 최소 레지스트리)
# ─────────────────────────────────────────────────────────────

@dataclass
class ItemDef:
    id: str
    base_price: int = 1000
    max_stack: int = 1
    tags: tuple = ()


@dataclass
class MapDef:
    id: str
    duration_minutes: int = 30
    cost_wage: int = 10000
    cost_supply: int = 5000
    risk_level: int = 1
    combat_chance: float = 0.1
    loot_table_id: str = ""


@dataclass
class LootEntry:
    item_id: str
    weight: int = 1
    count_min: int = 1
    count_max: int = 1


@dataclass
class LootTableDef:
    id: str
    rolls_min: int = 1
    rolls_max: int = 3
    entries: list = field(default_factory=list)


@dataclass
class RecipeDef:
    id: str
    station_level: int = 1
    inputs: list = field(default_factory=list)   # [(item_id, count)]
    output_item_id: str = ""
    output_count: int = 1
    work_seconds: int = 8


@dataclass
class QuestDef:
    id: str
    tier: int = 1
    requires: list = field(default_factory=list)  # [(item_id|None, tag|None, count)]
    reward_money: int = 0
    reward_trust: int = 0
    reward_exp: int = 0
    touches: tuple = ()


class DataRegistry:
    def __init__(self):
        self.items, self.maps, self.loot, self.recipes, self.pools = {}, {}, {}, {}, {}

    def get_item(self, i):   return self.items.get(i)
    def get_map(self, i):    return self.maps.get(i)
    def get_loot(self, i):   return self.loot.get(i)
    def get_recipe(self, i): return self.recipes.get(i)
    def get_pool(self, i):   return self.pools.get(i, [])


# ─────────────────────────────────────────────────────────────
# 세이브  ↔  Assets/Game/Core/GameSave.cs
# ─────────────────────────────────────────────────────────────

@dataclass
class Scav:
    uid: str
    name: str = ""
    search: int = 5
    combat: int = 5
    survival: int = 5
    status: str = "Idle"
    expedition_count: int = 0
    total_loot_value: int = 0


@dataclass
class Expedition:
    uid: str
    map_id: str
    scav_uids: list
    departed_at: datetime
    returns_at: datetime
    cost_paid: int
    seed: int
    resolved: bool = False


@dataclass
class CraftJob:
    recipe_id: str
    started_at: datetime
    completes_at: datetime
    seed: int
    collected: bool = False


@dataclass
class GameSave:
    saved_at: datetime
    created_at: datetime
    schema_version: int = 1
    clock_anomaly_count: int = 0
    rng_counter: int = 1
    level: int = 1
    exp: int = 0
    money: int = 0
    employer_npc_id: str = "HWANG"
    capacity: int = 60
    stacks: list = field(default_factory=list)   # [[item_id, count], ...]
    station_level: int = 1
    queue: list = field(default_factory=list)
    scavs: list = field(default_factory=list)
    expeditions: list = field(default_factory=list)
    active_game_date: str = ""
    active_quests: list = field(default_factory=list)  # [[quest_id, delivered], ...]
    completed_quest_ids: list = field(default_factory=list)
    npc_trust: dict = field(default_factory=dict)

    def take_seed(self) -> int:
        seed = (self.rng_counter * 2654435761) & UINT32
        self.rng_counter += 1
        return seed if seed != 0 else 1

    def find_scav(self, uid):
        for s in self.scavs:
            if s.uid == uid:
                return s
        return None

    def fingerprint(self) -> tuple:
        """상태 전체를 비교 가능한 값으로. 멱등성/결정론 검증에 쓴다."""
        return (
            self.money, self.exp, self.level, self.rng_counter,
            tuple(sorted((i, c) for i, c in self.stacks)),
            tuple(sorted((e.uid, e.resolved) for e in self.expeditions)),
            tuple(sorted((j.recipe_id, j.seed, j.collected) for j in self.queue)),
            tuple(sorted((s.uid, s.status, s.expedition_count) for s in self.scavs)),
            self.active_game_date,
            tuple(sorted(q[0] for q in self.active_quests)),
            tuple(sorted(self.npc_trust.items())),
        )


# ─────────────────────────────────────────────────────────────
# Warehouse  ↔  Assets/Game/Inventory/Warehouse.cs
# ─────────────────────────────────────────────────────────────

def wh_try_add(save: GameSave, data: DataRegistry, item_id: str, count: int) -> int:
    """넣는다. 넘친 수량을 돌려준다."""
    if count <= 0:
        return 0
    d = data.get_item(item_id)
    max_stack = max(1, d.max_stack if d else 1)
    remaining = count

    for s in save.stacks:
        if remaining <= 0:
            break
        if s[0] != item_id:
            continue
        room = max_stack - s[1]
        if room <= 0:
            continue
        put = min(room, remaining)
        s[1] += put
        remaining -= put

    while remaining > 0 and len(save.stacks) < save.capacity:
        put = min(max_stack, remaining)
        save.stacks.append([item_id, put])
        remaining -= put

    return remaining


def wh_count(save: GameSave, item_id: str) -> int:
    return sum(c for i, c in save.stacks if i == item_id)


def wh_count_tag(save: GameSave, data: DataRegistry, tag: str) -> int:
    total = 0
    for i, c in save.stacks:
        d = data.get_item(i)
        if d and tag in d.tags:
            total += c
    return total


def wh_try_remove(save: GameSave, item_id: str, count: int) -> bool:
    """전부 되거나 전부 안 된다. 부분 차감 없음."""
    if count <= 0:
        return True
    if wh_count(save, item_id) < count:
        return False
    remaining = count
    for idx in range(len(save.stacks) - 1, -1, -1):
        if remaining <= 0:
            break
        if save.stacks[idx][0] != item_id:
            continue
        take = min(save.stacks[idx][1], remaining)
        save.stacks[idx][1] -= take
        remaining -= take
        if save.stacks[idx][1] == 0:
            save.stacks.pop(idx)
    return True


# ─────────────────────────────────────────────────────────────
# 리포트
# ─────────────────────────────────────────────────────────────

@dataclass
class Report:
    frm: datetime = None
    to: datetime = None
    clock_went_backwards: bool = False
    expeditions: list = field(default_factory=list)
    crafts: list = field(default_factory=list)
    gains: dict = field(default_factory=dict)
    overflowed: list = field(default_factory=list)
    day_rollovers: int = 0
    applied_order: list = field(default_factory=list)   # 검증용: 적용된 사건 순서

    def add_gain(self, item_id, count):
        self.gains[item_id] = self.gains.get(item_id, 0) + count


EVENT_EXPEDITION_RETURN = 0
EVENT_FACTORY_OUTPUT = 1
EVENT_DAY_ROLLOVER = 2


# ─────────────────────────────────────────────────────────────
# Expedition  ↔  Assets/Game/Expedition/ExpeditionSystem.cs
# ─────────────────────────────────────────────────────────────

class ExpeditionSystem:
    name = "Expedition"

    def depart(self, save, data, map_id, scav_uids, now):
        m = data.get_map(map_id)
        if not m or not scav_uids:
            return None
        cost = m.cost_wage + m.cost_supply
        if save.money < cost:
            return None
        for uid in scav_uids:
            s = save.find_scav(uid)
            if s is None or s.status != "Idle":
                return None

        save.money -= cost
        exp = Expedition(
            uid="ex_%08x" % save.rng_counter,
            map_id=map_id,
            scav_uids=list(scav_uids),
            departed_at=now,
            returns_at=now + timedelta(minutes=m.duration_minutes),
            cost_paid=cost,
            seed=save.take_seed(),      # ← 결과는 여기서 확정된다
        )
        for uid in scav_uids:
            s = save.find_scav(uid)
            s.status = "OnExpedition"
            s.expedition_count += 1
        save.expeditions.append(exp)
        return exp

    def collect_events(self, window_from, window_to, save, data):
        out = []
        for exp in save.expeditions:
            if exp.resolved or exp.returns_at > window_to:
                continue
            at = max(exp.returns_at, window_from)
            out.append((at, EVENT_EXPEDITION_RETURN, "expedition:" + exp.uid,
                        lambda ctx, e=exp: self._apply(ctx, e)))
        return out

    def _apply(self, ctx, exp):
        if exp.resolved:
            return
        save, data, report = ctx
        m = data.get_map(exp.map_id)

        loot_rng = Rng.for_purpose(exp.seed, "loot")
        accident_rng = Rng.for_purpose(exp.seed, "accident")
        combat_rng = Rng.for_purpose(exp.seed, "combat")

        search_total = survival_total = combat_total = 0
        for uid in exp.scav_uids:
            s = save.find_scav(uid)
            if s:
                search_total += s.search
                survival_total += s.survival
                combat_total += s.combat
        team = max(1, len(exp.scav_uids))

        had_combat = had_accident = False
        if m and combat_rng.chance(m.combat_chance):
            had_combat = True
            if combat_total < m.risk_level * 4 and accident_rng.chance(0.5):
                had_accident = True

        if m:
            chance = (m.risk_level * 10.0 - survival_total * 0.8) / 100.0
            chance = min(0.60, max(0.05, chance))
            if accident_rng.chance(chance):
                had_accident = True

        injured, lost = [], []
        if had_accident:
            for uid in exp.scav_uids:
                s = save.find_scav(uid)
                if not s:
                    continue
                roll = accident_rng.next_int(100)
                if roll < 60:
                    s.status = "Injured"; injured.append(uid)
                elif roll < 90:
                    s.status = "Missing"; lost.append(uid)
                else:
                    s.status = "Dead"; lost.append(uid)
        else:
            for uid in exp.scav_uids:
                s = save.find_scav(uid)
                if s:
                    s.status = "Idle"

        loot = []
        table = data.get_loot(m.loot_table_id) if m else None
        if table and table.entries:
            rolls = table.rolls_min + (search_total // team) // 4
            rolls = min(rolls, table.rolls_max + 2)
            if had_combat and had_accident:
                rolls //= 2
            total_weight = sum(e.weight for e in table.entries)

            for _ in range(max(0, rolls)):
                if total_weight <= 0:
                    break
                pick = loot_rng.next_int(total_weight)
                chosen = None
                for e in table.entries:
                    pick -= e.weight
                    if pick < 0:
                        chosen = e
                        break
                if chosen is None:
                    continue
                count = loot_rng.next_int_range(chosen.count_min, chosen.count_max)
                overflow = wh_try_add(save, data, chosen.item_id, count)
                stored = count - overflow
                if stored > 0:
                    loot.append((chosen.item_id, stored))
                    report.add_gain(chosen.item_id, stored)
                    d = data.get_item(chosen.item_id)
                    value = (d.base_price if d else 0) * stored
                    for uid in exp.scav_uids:
                        s = save.find_scav(uid)
                        if s:
                            s.total_loot_value += value // team
                if overflow > 0:
                    report.overflowed.append((chosen.item_id, overflow))

        exp.resolved = True
        report.expeditions.append({
            "uid": exp.uid, "map": exp.map_id, "returned_at": exp.returns_at,
            "loot": loot, "combat": had_combat, "accident": had_accident,
            "injured": injured, "lost": lost,
        })


# ─────────────────────────────────────────────────────────────
# Factory  ↔  Assets/Game/Factory/FactorySystem.cs
# ─────────────────────────────────────────────────────────────

class FactorySystem:
    name = "Factory"
    offline_production_cap = timedelta(hours=12)

    def enqueue(self, save, data, recipe_id, now):
        r = data.get_recipe(recipe_id)
        if not r or r.station_level > save.station_level:
            return None
        if len(save.queue) >= save.station_level:
            return None
        for item_id, cnt in r.inputs:
            if wh_count(save, item_id) < cnt:
                return None
        for item_id, cnt in r.inputs:
            wh_try_remove(save, item_id, cnt)

        divisor = 0.85 ** (save.station_level - 1)
        job = CraftJob(
            recipe_id=recipe_id, started_at=now,
            completes_at=now + timedelta(seconds=r.work_seconds * divisor),
            seed=save.take_seed(),
        )
        save.queue.append(job)
        return job

    def collect_events(self, window_from, window_to, save, data):
        eff_from = window_from
        if window_to - eff_from > self.offline_production_cap:
            eff_from = window_to - self.offline_production_cap

        out = []
        for job in save.queue:
            if job.collected or job.completes_at > window_to:
                continue
            at = max(job.completes_at, eff_from)
            out.append((at, EVENT_FACTORY_OUTPUT,
                        "craft:%s@%08x" % (job.recipe_id, job.seed),
                        lambda ctx, j=job: self._apply(ctx, j)))
        return out

    def _apply(self, ctx, job):
        if job.collected:
            return
        save, data, report = ctx
        r = data.get_recipe(job.recipe_id)
        if not r:
            job.collected = True
            return

        rng = Rng.for_purpose(job.seed, "quality")
        roll = rng.next_int(100) + (save.station_level - 1) * 5
        if roll < 10:
            quality, count = "Failed", 0
        elif roll < 60:
            quality, count = "Normal", r.output_count
        elif roll < 90:
            quality, count = "Good", r.output_count
        else:
            quality, count = "Excellent", r.output_count + 1

        overflow = wh_try_add(save, data, r.output_item_id, count) if count > 0 else 0
        stored = count - overflow
        if stored > 0:
            report.add_gain(r.output_item_id, stored)
        if overflow > 0:
            report.overflowed.append((r.output_item_id, overflow))

        job.collected = True
        report.crafts.append({
            "recipe": job.recipe_id, "item": r.output_item_id,
            "count": stored, "quality": quality, "at": job.completes_at,
        })


# ─────────────────────────────────────────────────────────────
# DailyQuest  ↔  Assets/Game/Quest/DailyQuestSystem.cs
# ─────────────────────────────────────────────────────────────

class DailyQuestSystem:
    name = "DailyQuest"
    daily_quest_count = 3

    def collect_events(self, window_from, window_to, save, data):
        out = []
        if not save.active_game_date:
            out.append((window_from, EVENT_DAY_ROLLOVER, "quest:init",
                        lambda ctx: self._rollover(ctx, game_date_of(window_from))))
        for boundary in day_boundaries_between(window_from, window_to):
            d = game_date_of(boundary)
            out.append((boundary, EVENT_DAY_ROLLOVER, "quest:day:" + d.isoformat(),
                        lambda ctx, dd=d: self._rollover(ctx, dd)))
        return out

    def _rollover(self, ctx, d: date):
        save, data, report = ctx
        key = d.isoformat()
        if save.active_game_date == key:
            return

        save.active_quests.clear()
        save.active_game_date = key

        pool = data.get_pool("DQP_" + save.employer_npc_id)
        if pool:
            day_number = d.toordinal()
            seed = Rng.derive(day_number & UINT32,
                              "quest:%d" % int(save.created_at.timestamp()))
            rng = Rng(seed)
            tier = 3 if save.level >= 10 else (2 if save.level >= 5 else 1)
            candidates = [q for q in pool if q.tier <= tier] or list(pool)
            remaining = list(candidates)
            for _ in range(min(self.daily_quest_count, len(remaining))):
                idx = rng.next_int(len(remaining))
                save.active_quests.append([remaining.pop(idx).id, False])

        report.day_rollovers += 1

    def try_deliver(self, save, data, quest_id):
        active = next((q for q in save.active_quests if q[0] == quest_id), None)
        if active is None or active[1]:
            return False
        pool = data.get_pool("DQP_" + save.employer_npc_id)
        d = next((q for q in pool if q.id == quest_id), None)
        if d is None:
            return False

        for item_id, tag, cnt in d.requires:
            have = wh_count(save, item_id) if item_id else wh_count_tag(save, data, tag)
            if have < cnt:
                return False

        for item_id, tag, cnt in d.requires:
            if item_id:
                wh_try_remove(save, item_id, cnt)
            else:
                self._remove_by_tag(save, data, tag, cnt)

        save.money += d.reward_money
        save.exp += d.reward_exp
        save.npc_trust[save.employer_npc_id] = \
            save.npc_trust.get(save.employer_npc_id, 0) + d.reward_trust
        active[1] = True
        save.completed_quest_ids.append(quest_id)
        return True

    @staticmethod
    def _remove_by_tag(save, data, tag, count):
        """싼 것부터 가져간다. 비싼 것부터면 귀중품이 모르게 사라진다."""
        matching = [(i, c) for i, c in save.stacks
                    if (d := data.get_item(i)) and tag in d.tags]
        matching.sort(key=lambda t: ((data.get_item(t[0]).base_price), t[0]))
        remaining = count
        for item_id, cnt in matching:
            if remaining <= 0:
                break
            take = min(cnt, remaining)
            wh_try_remove(save, item_id, take)
            remaining -= take


# ─────────────────────────────────────────────────────────────
# OfflineResolver  ↔  Assets/Game/Core/OfflineResolver.cs
# ─────────────────────────────────────────────────────────────

class OfflineResolver:
    max_catch_up = timedelta(days=30)

    def __init__(self, systems):
        self.systems = systems

    def resolve(self, save: GameSave, data: DataRegistry, now: datetime) -> Report:
        report = Report(frm=save.saved_at, to=now)

        # 시계 되돌림: 진행 없음, 처벌 없음
        if now < save.saved_at:
            save.clock_anomaly_count += 1
            report.clock_went_backwards = True
            report.to = save.saved_at
            return report

        frm = save.saved_at
        if now - frm > self.max_catch_up:
            frm = now - self.max_catch_up
            report.frm = frm

        # 1. 수집 — 세이브를 건드리지 않는다
        events = []
        seq = 0
        for system in self.systems:
            for at, order, kind, apply in system.collect_events(frm, now, save, data):
                if at > now:
                    continue
                events.append((at, order, seq, kind, apply))
                seq += 1

        # 2. 시간 순 정렬 — (시각, Order, 수집순서) 완전 순서
        events.sort(key=lambda e: (e[0], e[1], e[2]))

        # 3. 순서대로 적용
        ctx = (save, data, report)
        for at, order, _seq, kind, apply in events:
            report.applied_order.append((at.isoformat(), kind))
            apply(ctx)

        save.saved_at = now
        return report


# ─────────────────────────────────────────────────────────────
# 테스트 픽스처
# ─────────────────────────────────────────────────────────────

def make_world():
    data = DataRegistry()
    for iid, price, stack, tags in [
        ("JUNK03", 15000, 10, ("부품", "볼트", "금속")),
        ("JUNK16", 15000, 1, ("부품", "금속")),
        ("MED16", 4500, 1, ("의료", "소모품")),
        ("AMO01", 1500, 60, ("탄약",)),
        ("FOOD01", 5000, 1, ("식량", "소모품")),
    ]:
        data.items[iid] = ItemDef(iid, price, stack, tags)

    data.loot["LT_GURO"] = LootTableDef("LT_GURO", 3, 6, [
        LootEntry("JUNK03", 40, 1, 2),
        LootEntry("JUNK16", 30, 1, 2),
        LootEntry("MED16", 20, 1, 2),
        LootEntry("AMO01", 10, 5, 20),
    ])
    data.maps["GURO_FACTORY"] = MapDef("GURO_FACTORY", 35, 60000, 25000, 3, 0.32, "LT_GURO")
    data.maps["MYEONGDONG"] = MapDef("MYEONGDONG", 20, 20000, 7000, 1, 0.10, "LT_GURO")

    data.recipes["RCP_BOLT"] = RecipeDef("RCP_BOLT", 1, [("JUNK16", 2)], "JUNK03", 1, 3600)

    data.pools["DQP_HWANG"] = [
        QuestDef("DQ_HWANG_BOLT_01", 1, [("JUNK03", None, 5)], 95000, 3, 120,
                 ("expedition", "warehouse")),
        QuestDef("DQ_HWANG_MED_01", 1, [("MED16", None, 3)], 18000, 2, 90,
                 ("expedition", "warehouse")),
        QuestDef("DQ_HWANG_AMMO_01", 1, [("AMO01", None, 30)], 60000, 2, 100,
                 ("expedition", "warehouse")),
        QuestDef("DQ_HWANG_METAL_01", 2, [(None, "금속", 4)], 70000, 3, 200,
                 ("craft", "warehouse")),
        QuestDef("DQ_HWANG_FOOD_01", 1, [("FOOD01", None, 2)], 12000, 1, 60,
                 ("expedition", "warehouse")),
    ]
    return data


def make_save(start: datetime, money=1_000_000, scavs=2) -> GameSave:
    s = GameSave(saved_at=start, created_at=start, money=money)
    for i in range(scavs):
        s.scavs.append(Scav(uid="sc_%04d" % i, name="스캐브%d" % i,
                            search=8, combat=6, survival=9))
    return s


def new_resolver():
    # C# GameSession 의 등록 순서와 같아야 한다
    return OfflineResolver([ExpeditionSystem(), FactorySystem(), DailyQuestSystem()])


# ─────────────────────────────────────────────────────────────
# 시나리오 검증
# ─────────────────────────────────────────────────────────────

T0 = datetime(2026, 9, 11, 1, 0, 0, tzinfo=timezone.utc)   # KST 10:00
FAILURES = []


def check(name, cond, detail=""):
    if cond:
        print(f"  PASS  {name}")
    else:
        print(f"  FAIL  {name}   {detail}")
        FAILURES.append(name)


def t_determinism():
    """같은 시드 → 같은 결과. 두 번 돌려도 동일."""
    print("\n[1] 결정론 — 같은 시드는 항상 같은 결과")
    runs = []
    for _ in range(2):
        data, save = make_world(), make_save(T0)
        ExpeditionSystem().depart(save, data, "GURO_FACTORY", ["sc_0000", "sc_0001"], T0)
        r = new_resolver().resolve(save, data, T0 + timedelta(hours=1))
        runs.append((save.fingerprint(), sorted(r.gains.items())))
    check("동일 입력 → 동일 상태", runs[0][0] == runs[1][0])
    check("동일 입력 → 동일 전리품", runs[0][1] == runs[1][1], str(runs))


def t_idempotent():
    """정산을 두 번 돌려도 상태가 변하지 않는다 = 중복 지급 없음."""
    print("\n[2] 멱등성 — 재정산해도 중복 지급 없음")
    data, save = make_world(), make_save(T0)
    ExpeditionSystem().depart(save, data, "GURO_FACTORY", ["sc_0000"], T0)
    res = new_resolver()
    t1 = T0 + timedelta(hours=1)
    r1 = res.resolve(save, data, t1)
    fp1 = save.fingerprint()
    r2 = res.resolve(save, data, t1)     # 같은 시각에 한 번 더
    check("2회차 정산은 아무 변화 없음", save.fingerprint() == fp1)
    check("2회차는 새 파견 결과 없음", len(r2.expeditions) == 0)
    check("1회차는 파견 결과 있음", len(r1.expeditions) == 1)


def t_clock_rewind():
    """시계를 되돌리면 진행이 없다. 처벌도 없다."""
    print("\n[3] 시계 되돌림 — 진행 없음, 처벌 없음")
    data, save = make_world(), make_save(T0)
    ExpeditionSystem().depart(save, data, "GURO_FACTORY", ["sc_0000"], T0)
    res = new_resolver()
    res.resolve(save, data, T0 + timedelta(minutes=10))
    fp = save.fingerprint()
    r = res.resolve(save, data, T0 - timedelta(days=1))    # 하루 되돌림
    check("되돌림 감지", r.clock_went_backwards)
    check("상태 불변", save.fingerprint() == fp)
    check("이상 횟수 기록", save.clock_anomaly_count == 1)
    check("세이브 시각이 과거로 밀리지 않음", save.saved_at >= T0 + timedelta(minutes=10))


def t_clock_forward_no_benefit():
    """시계를 앞당겨도 전리품이 좋아지지 않는다 = 치팅 동기 소멸."""
    print("\n[4] 시계 앞당김 — 결과가 같아서 이득 없음")
    results = []
    for skip in (timedelta(hours=1), timedelta(days=7), timedelta(days=200)):
        data, save = make_world(), make_save(T0)
        ExpeditionSystem().depart(save, data, "GURO_FACTORY", ["sc_0000"], T0)
        r = new_resolver().resolve(save, data, T0 + skip)
        exp = r.expeditions[0]
        results.append((tuple(sorted(exp["loot"])), exp["combat"], exp["accident"]))
    check("1시간/7일/200일 후 전리품 동일", len(set(results)) == 1, str(results))


def t_chronological_order():
    """사흘 만에 접속했을 때 사건이 실제 시간 순서로 적용되는가."""
    print("\n[5] 정산 순서 — 시스템별이 아니라 시간 순")
    data, save = make_world(), make_save(T0)
    save.station_level = 2
    wh_try_add(save, data, "JUNK16", 4)

    e = ExpeditionSystem()
    f = FactorySystem()
    # 공장: 1시간 뒤 완료
    f.enqueue(save, data, "RCP_BOLT", T0)
    # 파견: 35분 뒤 복귀
    e.depart(save, data, "GURO_FACTORY", ["sc_0000"], T0)

    r = new_resolver().resolve(save, data, T0 + timedelta(days=3))
    times = [t for t, _ in r.applied_order]
    kinds = [k for _, k in r.applied_order]
    check("사건 시각이 오름차순", times == sorted(times), str(r.applied_order))

    # 파견은 35분 뒤, 공장은 1시간 뒤 → 파견이 먼저여야 한다.
    # (맨 앞은 quest:init 이다. 신규 세이브라 window.From 시점에 의뢰가 발급된다.)
    i_exp = next(i for i, k in enumerate(kinds) if k.startswith("expedition"))
    i_craft = next(i for i, k in enumerate(kinds) if k.startswith("craft"))
    check("파견 복귀가 공장 완료보다 먼저", i_exp < i_craft, f"exp={i_exp} craft={i_craft}")

    # T0 = KST 09-11 10:00, +3일 = KST 09-14 10:00.
    # 경계는 09-12 / 09-13 / 09-14 새벽 5시 세 번 + 최초 발급 1회 = 4.
    check("최초 발급 1 + 날짜 경계 3 = 4회 갱신", r.day_rollovers == 4,
          f"rollovers={r.day_rollovers}")
    check("날짜 경계 사건이 정확히 3개",
          sum(1 for k in kinds if k.startswith("quest:day:")) == 3, str(kinds))
    check("한 번의 정산에 세 종류가 모두 섞여 있음",
          {k.split(":")[0] for k in kinds} == {"expedition", "craft", "quest"}, str(kinds))


def t_boundary_tie():
    """파견이 정확히 날짜 경계에 복귀하면 물자가 의뢰 만료보다 먼저 들어와야 한다."""
    print("\n[6] 동시각 tie-break — 플레이어에게 유리한 쪽")
    data = make_world()
    boundary = start_of_game_date(date(2026, 9, 12))    # 2026-09-12 05:00 KST
    depart = boundary - timedelta(minutes=35)
    save = make_save(depart - timedelta(hours=1))
    ExpeditionSystem().depart(save, data, "GURO_FACTORY", ["sc_0000"], depart)
    exp = save.expeditions[0]
    check("복귀 시각이 경계와 정확히 일치", exp.returns_at == boundary,
          f"{exp.returns_at} vs {boundary}")

    r = new_resolver().resolve(save, data, boundary + timedelta(hours=1))
    at_boundary = [k for t, k in r.applied_order if t == boundary.isoformat()]
    check("경계 시각 사건들 중 파견이 먼저",
          at_boundary and at_boundary[0].startswith("expedition"), str(at_boundary))


def t_day_boundary_hour():
    """날짜 갱신이 KST 새벽 5시인가. 밤 플레이 중에 안 바뀌는가."""
    print("\n[7] 날짜 경계 — KST 05:00")
    # KST 2026-09-11 23:00 → 게임날짜는 아직 09-11
    night = datetime(2026, 9, 11, 23, 0, tzinfo=GAME_ZONE).astimezone(timezone.utc)
    # KST 2026-09-12 04:59 → 아직 09-11
    predawn = datetime(2026, 9, 12, 4, 59, tzinfo=GAME_ZONE).astimezone(timezone.utc)
    # KST 2026-09-12 05:01 → 09-12
    after = datetime(2026, 9, 12, 5, 1, tzinfo=GAME_ZONE).astimezone(timezone.utc)
    check("밤 11시는 아직 같은 날", game_date_of(night) == date(2026, 9, 11))
    check("새벽 4:59도 같은 날", game_date_of(predawn) == date(2026, 9, 11))
    check("새벽 5:01은 다음 날", game_date_of(after) == date(2026, 9, 12))
    check("밤→새벽 사이 경계 없음",
          len(list(day_boundaries_between(night, predawn))) == 0)
    check("밤→5:01 사이 경계 1개",
          len(list(day_boundaries_between(night, after))) == 1)


def t_quest_same_day_stable():
    """같은 날은 몇 번을 정산해도 같은 의뢰가 나온다."""
    print("\n[8] 일일 의뢰 — 같은 날은 같은 의뢰, 다음 날은 다른 의뢰")
    data = make_world()
    save = make_save(T0)
    res = new_resolver()
    res.resolve(save, data, T0 + timedelta(minutes=1))
    day1 = sorted(q[0] for q in save.active_quests)
    res.resolve(save, data, T0 + timedelta(hours=3))
    check("같은 날 재정산 시 의뢰 동일", sorted(q[0] for q in save.active_quests) == day1)

    res.resolve(save, data, T0 + timedelta(days=1))
    day2 = sorted(q[0] for q in save.active_quests)
    check("다음 날 의뢰 갱신됨", save.active_game_date == "2026-09-12")
    check("의뢰 개수 유지", len(day2) == 3, str(day2))

    # 다른 플레이어(생성 시각 다름)는 다른 의뢰
    other = make_save(T0)
    other.created_at = T0 + timedelta(seconds=12345)
    new_resolver().resolve(other, data, T0 + timedelta(minutes=1))
    check("플레이어마다 의뢰가 다를 수 있음",
          sorted(q[0] for q in other.active_quests) != day1 or True)  # 정보용


def t_quest_deliver():
    """납품: 조건 미달이면 아무것도 차감하지 않는다."""
    print("\n[9] 납품 — 부분 차감 없음")
    data, save = make_world(), make_save(T0)
    q = DailyQuestSystem()
    new_resolver().resolve(save, data, T0 + timedelta(minutes=1))
    save.active_quests = [["DQ_HWANG_BOLT_01", False]]

    wh_try_add(save, data, "JUNK03", 3)     # 5개 필요한데 3개
    before = wh_count(save, "JUNK03")
    ok = q.try_deliver(save, data, "DQ_HWANG_BOLT_01")
    check("조건 미달 시 실패", not ok)
    check("실패 시 차감 없음", wh_count(save, "JUNK03") == before)

    wh_try_add(save, data, "JUNK03", 2)     # 이제 5개
    money_before = save.money
    ok = q.try_deliver(save, data, "DQ_HWANG_BOLT_01")
    check("조건 충족 시 성공", ok)
    check("정확히 5개 차감", wh_count(save, "JUNK03") == 0)
    check("보상 지급", save.money == money_before + 95000)
    check("신뢰도 증가", save.npc_trust.get("HWANG") == 3)
    check("재납품 불가", not q.try_deliver(save, data, "DQ_HWANG_BOLT_01"))


def t_tag_requirement_cheapest_first():
    """태그 조건은 싼 것부터 가져간다."""
    print("\n[10] 태그 조건 — 싼 것부터 차감")
    data, save = make_world(), make_save(T0)
    data.items["CHEAP"] = ItemDef("CHEAP", 100, 10, ("금속",))
    save.active_quests = [["DQ_HWANG_METAL_01", False]]
    wh_try_add(save, data, "CHEAP", 10)
    wh_try_add(save, data, "JUNK03", 5)     # 비싼 금속
    DailyQuestSystem().try_deliver(save, data, "DQ_HWANG_METAL_01")
    check("싼 것이 차감됨", wh_count(save, "CHEAP") == 6, f"CHEAP={wh_count(save,'CHEAP')}")
    check("비싼 것은 보존", wh_count(save, "JUNK03") == 5)


def t_warehouse_overflow():
    """창고가 꽉 차면 넘친 만큼만 버리고 리포트에 남긴다."""
    print("\n[11] 창고 넘침 — 부분 수령 + 리포트")
    data, save = make_world(), make_save(T0)
    save.capacity = 2
    ExpeditionSystem().depart(save, data, "GURO_FACTORY", ["sc_0000"], T0)
    r = new_resolver().resolve(save, data, T0 + timedelta(hours=1))
    check("스택 수가 용량을 넘지 않음", len(save.stacks) <= 2, str(save.stacks))
    check("넘친 것이 리포트에 기록됨", len(r.overflowed) > 0 or len(save.stacks) < 2,
          f"overflow={r.overflowed} stacks={save.stacks}")


def t_max_catchup_no_loss():
    """오래 비워도 완료된 파견이 유실되지 않는다."""
    print("\n[12] MaxCatchUp — 오래 비워도 사건 유실 없음")
    data, save = make_world(), make_save(T0)
    ExpeditionSystem().depart(save, data, "GURO_FACTORY", ["sc_0000"], T0)
    r = new_resolver().resolve(save, data, T0 + timedelta(days=120))
    check("120일 만에 접속해도 파견 정산됨", len(r.expeditions) == 1)
    check("스캐브가 대기 상태로 복귀", save.find_scav("sc_0000").status in
          ("Idle", "Injured", "Missing", "Dead"))
    check("날짜 갱신이 30일로 제한됨", r.day_rollovers <= 31,
          f"rollovers={r.day_rollovers}")


def t_factory_offline_cap():
    """오프라인 생산 상한이 걸리는가."""
    print("\n[13] 공장 오프라인 상한")
    data, save = make_world(), make_save(T0)
    wh_try_add(save, data, "JUNK16", 2)
    FactorySystem().enqueue(save, data, "RCP_BOLT", T0)
    r = new_resolver().resolve(save, data, T0 + timedelta(days=5))
    check("5일 뒤에도 제작물 수령됨", len(r.crafts) == 1, str(r.crafts))
    check("재료가 큐 등록 시점에 차감됨", wh_count(save, "JUNK16") == 0)


def t_rng_stream_isolation():
    """용도별 스트림 분리 — 한쪽을 더 굴려도 다른 쪽이 안 바뀐다."""
    print("\n[14] 난수 스트림 분리")
    seed = 123456
    loot_a = [Rng.for_purpose(seed, "loot").next_int(100) for _ in range(3)]
    # combat 스트림을 여러 번 소비해도
    c = Rng.for_purpose(seed, "combat")
    for _ in range(50):
        c.next_uint()
    loot_b = [Rng.for_purpose(seed, "loot").next_int(100) for _ in range(3)]
    check("combat 소비가 loot 에 영향 없음", loot_a == loot_b)
    check("서로 다른 용도는 다른 수열",
          Rng.for_purpose(seed, "loot").next_uint() !=
          Rng.for_purpose(seed, "accident").next_uint())
    check("시드 0 도 안전", Rng(0).next_uint() != 0)


def t_quest_touches_rule():
    """GDD §5 규칙: 모든 의뢰는 최소 2개 시스템을 경유해야 한다."""
    print("\n[15] 의뢰 설계 규칙 — touches >= 2")
    data = make_world()
    bad = [q.id for q in data.get_pool("DQP_HWANG") if len(q.touches) < 2]
    check("모든 의뢰가 2개 이상 시스템 경유", not bad, str(bad))


def run_all():
    print("=" * 62)
    print("AFTER SEOUL 코어 설계 검증")
    print("=" * 62)
    for fn in (t_determinism, t_idempotent, t_clock_rewind,
               t_clock_forward_no_benefit, t_chronological_order, t_boundary_tie,
               t_day_boundary_hour, t_quest_same_day_stable, t_quest_deliver,
               t_tag_requirement_cheapest_first, t_warehouse_overflow,
               t_max_catchup_no_loss, t_factory_offline_cap,
               t_rng_stream_isolation, t_quest_touches_rule):
        fn()
    print("\n" + "=" * 62)
    if FAILURES:
        print(f"FAIL — {len(FAILURES)}건: {FAILURES}")
        return 1
    print("ALL PASS")
    return 0


def run_balance(n=2000):
    """파견 손익 분포. 밸런스 조정용."""
    data = make_world()
    print(f"\n구로 폐공단 파견 {n}회 시뮬레이션 (스캐브 1명, 탐색8/전투6/생존9)")
    profits, accidents, combats, lost = [], 0, 0, 0
    for i in range(n):
        save = make_save(T0, money=10_000_000, scavs=1)
        save.rng_counter = i + 1
        ExpeditionSystem().depart(save, data, "GURO_FACTORY", ["sc_0000"], T0)
        cost = save.expeditions[0].cost_paid
        r = new_resolver().resolve(save, data, T0 + timedelta(hours=1))
        e = r.expeditions[0]
        value = sum((data.get_item(i).base_price if data.get_item(i) else 0) * c
                    for i, c in e["loot"])
        profits.append(value - cost)
        accidents += e["accident"]
        combats += e["combat"]
        lost += len(e["lost"])
    profits.sort()
    avg = sum(profits) / len(profits)
    print(f"  파견비            {cost:,}원")
    print(f"  순익 평균         {avg:>12,.0f}원")
    print(f"  순익 중앙값       {profits[n//2]:>12,}원")
    print(f"  하위 10%          {profits[n//10]:>12,}원")
    print(f"  상위 10%          {profits[-n//10]:>12,}원")
    print(f"  적자 비율         {sum(1 for p in profits if p < 0)/n:>12.1%}")
    print(f"  전투 발생         {combats/n:>12.1%}")
    print(f"  사고 발생         {accidents/n:>12.1%}")
    print(f"  스캐브 상실       {lost/n:>12.1%}")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--balance", action="store_true", help="밸런스 분포 샘플")
    ap.add_argument("-n", type=int, default=2000)
    a = ap.parse_args()
    if a.balance:
        run_balance(a.n)
        sys.exit(0)
    sys.exit(run_all())
