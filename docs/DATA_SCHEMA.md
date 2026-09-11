# AFTER SEOUL — 데이터 스키마 초안

문서 버전: 0.1 / 대상: MVP

## 설계 방침

| 항목 | 결정 | 이유 |
|---|---|---|
| 정적 데이터 저장 | **JSON** (`Assets/StreamingAssets/Data/*.json`) | 본편처럼 C# 하드코딩하면 밸런싱 때마다 빌드해야 함. 1인 개발에 치명적 |
| ScriptableObject | **쓰지 않음** | 머지 충돌이 잦고 diff 가 안 읽힌다. 텍스트 JSON 이 낫다 |
| 직렬화 | Newtonsoft.Json | 본편과 동일. 학습 비용 0 |
| 세이브 암호화 | **MVP 에서는 안 함** | 모바일은 서버 권위가 없으므로 암호화해도 의미가 약하다. 난독화는 Phase 7 에 검토 |
| 시간 | 전부 **UTC ISO-8601** | 시간대·서머타임 버그 회피 |
| 화폐 | `long` 원화. 소수점 없음 | |

---

## 1. 정적 데이터 (읽기 전용, 빌드에 포함)

### 1-1. `items.json`

본편 `ItemDatabase.cs` 에서 추출 (`Tools/extract_mainline_data.py`).

```json
{
  "schemaVersion": 1,
  "items": [
    {
      "id": "MED16",
      "category": "Medical",
      "slot": "None",
      "basePrice": 4200,
      "rarity": "Common",
      "maxStack": 5,
      "iconKey": "med_bandage",
      "tags": ["의료", "소모품"],
      "craftable": true,
      "transferable": true
    }
  ]
}
```

| 필드 | 출처 | 비고 |
|---|---|---|
| `id` `category` `slot` `basePrice` `rarity` `maxStack` | 본편 `Item` | 그대로 추출 |
| `iconKey` | **모바일 신규** | 아이콘 아틀라스 키. 아이템군 단위로 공유 가능 |
| `tags` | **모바일 신규** | 의뢰 조건 매칭용 (`"볼트 계열 아무거나"`) |
| `craftable` | **모바일 신규** | 공장 제작 대상 여부 |
| `transferable` | `transferable_items.json` 에서 파생 | 캐시용. 정본은 계약 파일 |

이름/설명은 **저장하지 않는다.** 로케일 키 `ITEM_{id}_NAME` / `_SHORT` / `_DESC` 로 조회 (본편 규칙 그대로).

### 1-2. `maps.json`

```json
{
  "schemaVersion": 1,
  "maps": [
    {
      "id": "MYEONGDONG",
      "mainlineDifficulty": "Easy",
      "tier": 1,
      "unlockCondition": { "type": "default" },
      "expedition": {
        "durationMinutes": 20,
        "baseCostWage": 20000,
        "baseCostSupply": 7000,
        "riskLevel": 1,
        "combatChance": 0.10,
        "lootTable": "LT_MYEONGDONG"
      }
    },
    {
      "id": "YONGSAN_MARKET",
      "mainlineDifficulty": "Hard",
      "tier": 2,
      "unlockCondition": { "type": "npcTrust", "npcId": "HWANG", "value": 10 },
      "expedition": {
        "durationMinutes": 45,
        "baseCostWage": 48000,
        "baseCostSupply": 16000,
        "riskLevel": 3,
        "combatChance": 0.28,
        "lootTable": "LT_YONGSAN_MARKET"
      }
    },
    {
      "id": "GURO_FACTORY",
      "mainlineDifficulty": "Hard",
      "tier": 2,
      "unlockCondition": { "type": "playerLevel", "value": 5 },
      "expedition": {
        "durationMinutes": 35,
        "baseCostWage": 60000,
        "baseCostSupply": 25000,
        "riskLevel": 3,
        "combatChance": 0.32,
        "lootTable": "LT_GURO_FACTORY"
      }
    },
    {
      "id": "UIJEONGBU",
      "mainlineDifficulty": "Normal",
      "tier": 3,
      "unlockCondition": { "type": "playerLevel", "value": 9 },
      "expedition": {
        "durationMinutes": 180,
        "baseCostWage": 110000,
        "baseCostSupply": 40000,
        "riskLevel": 2,
        "combatChance": 0.22,
        "lootTable": "LT_UIJEONGBU"
      }
    }
  ]
}
```

> ⚠ **구현에서는 `expedition` 블록을 `expeditions.json` 으로 분리했다** (2026-09-11).
> `maps.json` 은 `Tools/extract_mainline_data.py` 가 매번 새로 쓰는 본편 스냅샷이라, 모바일 전용 값을 넣으면
> 재추출 때 사라진다. `expeditions.json` 은 `mapId` 로 `maps.json` 을 참조하고, 본편에 없는 `mapId` 는 테스트가 막는다.
> 필드는 위 `expedition` 블록 + `mapId` `tier` `unlockCondition` 을 한 단계로 편 것이다.

> **`durationMinutes` 는 GDD §3 의 3회 접속 리듬에 맞춘 값이다.**
> 20분 = 출근 전에 보내고 점심에 회수 / 45분 = 아침에 보내고 점심 / 180분 = 점심에 보내고 퇴근.
> 장시간 파견(12시간 이상)은 Phase 2 후반에 추가.

### 1-3. `loot_tables.json`

```json
{
  "schemaVersion": 1,
  "tables": [
    {
      "id": "LT_YONGSAN_MARKET",
      "rolls": { "min": 3, "max": 6 },
      "entries": [
        { "itemId": "JUNK01", "weight": 40, "count": { "min": 1, "max": 2 } },
        { "itemId": "BAT01",  "weight": 30, "count": { "min": 1, "max": 3 } },
        { "itemId": "MED16",  "weight": 15, "count": { "min": 1, "max": 2 } },
        { "itemId": "JUNK02", "weight": 3,  "count": { "min": 1, "max": 1 } }
      ]
    }
  ]
}
```

`rolls` 횟수만큼 가중 추첨. 스캐브 `탐색` 능력치가 `rolls.max` 에 보정을 준다 (§4-2).

### 1-4. `npcs.json`

```json
{
  "schemaVersion": 1,
  "npcs": [
    {
      "id": "HWANG",
      "currency": "Won",
      "theme": "military",
      "startingMapIds": ["MYEONGDONG"],
      "dailyQuestPool": "DQP_HWANG",
      "bonuses": [
        { "type": "craftSpeed", "tags": ["장비"], "value": 0.10 },
        { "type": "sellPrice", "tags": ["탄약", "군용"], "value": 0.05 }
      ],
      "trustTiers": [0, 10, 25, 50, 90, 150]
    }
  ]
}
```

### 1-5. `daily_quests.json`

```json
{
  "schemaVersion": 1,
  "pools": [
    {
      "id": "DQP_HWANG",
      "quests": [
        {
          "id": "DQ_HWANG_BOLT_01",
          "tier": 1,
          "requires": [{ "itemId": "JUNK03", "count": 5 }],
          "reward": { "money": 95000, "trust": 3, "exp": 120 },
          "chainNextId": null,
          "touches": ["expedition", "warehouse"]
        },
        {
          "id": "DQ_HWANG_AKPART_01",
          "tier": 3,
          "requires": [{ "tag": "총기부품", "count": 3 }],
          "reward": { "money": 180000, "trust": 5, "exp": 600 },
          "chainNextId": "DQ_HWANG_AKPART_02",
          "touches": ["expedition", "craft", "warehouse"]
        }
      ]
    }
  ]
}
```

- `requires[]` 는 `itemId` 또는 `tag` 중 하나. `tag` 는 "볼트 계열 아무거나" 같은 유연한 조건용
- `touches[]` 는 **GDD §5 의 "최소 2개 시스템 경유" 규칙을 데이터로 검증하기 위한 필드다.**
  빌드 시 `touches.Length < 2` 인 의뢰가 있으면 테스트 실패시킨다
- `chainNextId` 로 연속 의뢰 체인 구성

> ✅ `JUNK03` = 본편의 "볼트" (2026-09-11 `ko.json` 대조 확인). 전체 `JUNK` 목록은 [MAINLINE_REFERENCE.md §3](MAINLINE_REFERENCE.md#3-아이템-재활용-확정) 참조.

### 1-6. `recipes.json`

```json
{
  "schemaVersion": 1,
  "recipes": [
    {
      "id": "RCP_BOLT",
      "stationLevel": 1,
      "inputs": [{ "itemId": "JUNK16", "count": 2 }],
      "output": { "itemId": "JUNK03", "count": 1 },
      "workSeconds": 8,
      "minigame": "assemble",
      "qualityAffects": ["sellPrice", "questSatisfaction"]
    }
  ]
}
```

`minigame` 후보: `assemble`(배치) / `timing`(타이밍 입력) / `inspect`(검수). MVP 는 `assemble` 1종만.

> ⚠ **구현값은 `JUNK16 ×1 → JUNK03 ×1`** (2026-09-11). 위 예시(×2 → ×1)는 본편 가격으로 30,000원을
> 15,000원으로 바꾸는 손해 레시피다. 초반 돈은 **직접 노동**(`balance.laborPayByQuality`)으로 벌고,
> 제작은 의뢰에 필요한 볼트로 바꾸는 경로로 둔다.

### 1-8. `balance.json`

코드가 실제로 읽는 상수만 둔다. 안 쓰는 상수를 미리 넣으면 "고쳤는데 왜 안 바뀌지"가 된다.

| 키 | 값 | 쓰는 곳 |
|---|---|---|
| `startingMoney` | 0 | 신규 게임 (`GameSession`) |
| `sellPriceRatio` | 1.0 | 판매가 = basePrice × 이 값 (`Market`) |
| `expeditionCostRatio` | 0.55 | 파견비 검증 (`DataFileTests`, `check_data.py`) |
| `questRewardMultiplierByTier` | 1.25 / 1.35 / 1.5 | 의뢰 보상 검증 |
| `laborPayByQuality` | 0 / 8,000 / 11,000 / 15,000 | 직접 노동 보수 (`FactorySystem`) |
| `laborGradeThresholds` | 0.3 / 0.6 / 0.85 | 미니게임 점수 → 품질 |

### 1-7. `scav_pool.json`

고용 시장에 등장할 스캐브 템플릿.

```json
{
  "schemaVersion": 1,
  "names": ["김철수", "박영호", "이순자", "정대만", "최기웅"],
  "traits": [
    { "id": "TR_YONGSAN_NATIVE", "effect": { "type": "mapBonus", "mapId": "YONGSAN_MARKET", "value": 0.20 } },
    { "id": "TR_COWARD",         "effect": { "type": "fleeChance", "value": 0.35 }, "penalty": { "type": "lootMultiplier", "value": -0.15 } },
    { "id": "TR_MEDIC",          "effect": { "type": "injurySurvival", "value": 0.25 } }
  ],
  "tiers": [
    { "tier": 1, "statTotal": { "min": 9,  "max": 14 }, "hireCost": 250000,   "wagePerHour": 40000,  "traitCount": 1 },
    { "tier": 2, "statTotal": { "min": 15, "max": 21 }, "hireCost": 900000,   "wagePerHour": 95000, "traitCount": 2 },
    { "tier": 3, "statTotal": { "min": 22, "max": 27 }, "hireCost": 2800000,  "wagePerHour": 220000, "traitCount": 2 }
  ]
}
```

---

## 2. 세이브 데이터 (런타임, 쓰기 가능)

`Application.persistentDataPath/save.json`

```json
{
  "schemaVersion": 1,
  "savedAt": "2026-09-11T04:12:00Z",
  "player": {
    "level": 7,
    "exp": 4820,
    "money": 1840000,
    "employerNpcId": "HWANG",
    "createdAt": "2026-09-01T09:00:00Z"
  },
  "warehouse": {
    "capacity": 120,
    "stacks": [
      { "itemId": "JUNK03", "count": 12 },
      { "itemId": "MED16", "count": 3 }
    ]
  },
  "factory": {
    "stationLevel": 2,
    "autoLevel": 0,
    "queue": [
      { "recipeId": "RCP_BOLT", "startedAt": "2026-09-11T03:50:00Z", "completesAt": "2026-09-11T04:20:00Z", "quality": null }
    ],
    "lastCollectedAt": "2026-09-11T03:50:00Z"
  },
  "scavs": [
    {
      "uid": "sc_0001",
      "name": "김철수",
      "level": 7,
      "stats": { "search": 8, "combat": 5, "survival": 9 },
      "traitIds": ["TR_YONGSAN_NATIVE", "TR_COWARD"],
      "state": "onExpedition",
      "equipment": { "weapon": null, "armor": null, "backpack": null, "medical": null, "light": null, "tool": null },
      "history": { "expeditions": 23, "totalLootValue": 1240000, "hiredAt": "2026-09-03T08:00:00Z" }
    }
  ],
  "expeditions": [
    {
      "uid": "ex_0042",
      "mapId": "YONGSAN_MARKET",
      "scavUids": ["sc_0001"],
      "departedAt": "2026-09-11T03:40:00Z",
      "returnsAt": "2026-09-11T04:25:00Z",
      "costPaid": 19000,
      "rngSeed": 918273645,
      "resolved": false
    }
  ],
  "quests": {
    "activeDay": "2026-09-11",
    "active": [
      { "questId": "DQ_HWANG_BOLT_01", "progress": [{ "key": "JUNK03", "current": 3, "target": 5 }] }
    ],
    "completedIds": ["DQ_HWANG_INTRO_01"],
    "chainState": {}
  },
  "npcTrust": { "HWANG": 34 },
  "mail": {
    "outboxTxIds": ["m2p_20260911_a7f3e9c1"],
    "dailyQuotaDate": "2026-09-11",
    "dailyQuotaUsedValue": 42000,
    "dailyShipmentsUsed": 1
  },
  "meta": {
    "lastSeenAt": "2026-09-11T04:12:00Z",
    "tutorialStep": "done"
  }
}
```

### 핵심 설계 결정

| 결정 | 이유 |
|---|---|
| **`expeditions[].rngSeed` 를 출발 시점에 확정해 저장** | 결과를 복귀 시점이 아니라 출발 시점에 결정론적으로 고정. 앱을 껐다 켜도, 시계를 돌려도 결과가 같다. **리세마라 방지의 핵심** |
| `resolved: false` 플래그 | 결과 수령 중 크래시가 나도 중복 지급되지 않음 |
| `warehouse.stacks` 는 **그리드가 아니라 스택 리스트** | 본편은 그리드 인벤토리지만 모바일에서 테트리스 인벤토리는 최악의 UX. 용량은 스택 수로만 제한 |
| `factory.lastCollectedAt` | 오프라인 생산 계산 기준점 |
| `mail.dailyQuota*` | 전송 한도 로컬 카운터. Cloud Save `quota` 와 이중 관리 |

### 오프라인 진행 처리

앱 재진입 시:

```
1. now = UTC now
2. if now < save.savedAt  → 시계 되돌림 감지. 진행 없음 + 로그 (처벌은 하지 않음)
3. 공장: lastCollectedAt ~ now 구간의 완료 개수 계산 (오프라인 상한 적용)
4. 탐색: returnsAt <= now 이고 resolved == false 인 건을 rngSeed 로 결과 산출
5. 일일 의뢰: activeDay != today 면 갱신
6. savedAt = now 로 저장
```

**시계 되돌림은 막되, 시계 앞당김은 결과가 seed 로 고정돼 있으므로 아이템 이득이 없다.**
(시간만 빨리 흐를 뿐 같은 결과를 받는다 → 치팅 동기 자체가 사라진다)

---

## 3. MVP 아이템 목록 (20~30개)

본편 실제 id 로 구성 (2026-09-11 `ko.json` 대조 확인 완료).
**모바일 전용 신규 아이템은 하나도 필요 없다** — 본편 `JUNK` 계열이 제작 재료를 전부 커버한다.

| 구분 | 아이템 | id | 용도 |
|---|---|---|---|
| 재료 | **볼트** | `JUNK03` | **초반 의뢰 핵심** |
| 재료 | 나사못 | `JUNK04` | 기초 제작 재료 |
| 재료 | 너트 | `JUNK15` | 기초 제작 재료 |
| 재료 | 금속 여분 부품 | `JUNK16` | 볼트/나사 제작 원료 (= 스크랩 역할) |
| 재료 | 전선 | `JUNK20` | 전자 제작 |
| 재료 | 인쇄 회로 기판 | `JUNK23` | 중급 전자 제작 (= 전자부품) |
| 재료 | 전기 모터 | `JUNK08` | 중급 제작 |
| 재료 | 덕트 테이프 | `JUNK18` | 범용 제작 |
| 재료 | 플리스 원단 | `JUNK35` | 붕대 제작 (= 천 조각) |
| 재료 | 공구 세트 | `JUNK29` | 제작 성공률 / 스캐브 특수도구 |
| 재료 | 자동차 배터리 | `JUNK21` | 제작 + 의뢰 |
| 재료 | NVG 배터리 | `BAT01` | 고급 장비용 (전송 금지) |
| 판매 | 그래픽 카드 | `JUNK01` | 고가 판매품 |
| 판매 | LedX 피부 이식경 | `JUNK02` | 최고가 판매품 (전송 금지) |
| 의료 | 군용 붕대 | `MED16` | 의뢰 + 스캐브 치료 |
| 의료 | 이부프로펜 진통제 | `MED14` | 의뢰 |
| 의료 | IFAK | `MED02` | 스캐브 장비 |
| 의료 | AI-2 | `MED05` | 저가 치료 |
| 의료 | 살레와 구급상자 | `MED01` | 고급 치료 |
| 식량 | 투숑카 통조림 | `FOOD01` | 판매/보급 |
| 식량 | 타르콜라 | `FOOD02` | 판매/보급 |
| 식량 | 정수 | `FOOD05` | 보급비 절감 |
| 탄약 | 5.45x39 PS | `AMO01` | 의뢰 + 스캐브 장비 |
| 탄약 | 9x19 PST gzh | `AMO05` | 의뢰 |
| 탄약 | 12g AP-20 | `AMO16` | 의뢰 |
| 총기 | P17 (권총) | `WPN04` | 저급 스캐브 장비 |
| 총기 | SC-45 | `WPN11` | 중급 스캐브 장비 |
| 총기 | AV-74M | `WPN01` | **후반 의뢰 목표** |
| 방어 | PACA 방탄복 | `AMR01` | 저급 스캐브 장비 |
| 방어 | SSh-68 철모 | `HDW01` | 저급 스캐브 장비 |
| 가방 | MBSS 백팩 | `BPK01` | 회수량 증가 |
| 도구 | X400U 라이트/레이저 | `ATT01` | 손전등 슬롯, 탐색 보너스 |

---

## 3-1. ⚠ 본편 경제 스케일 — 기획서 수치 전면 재조정 필요

2026-09-11 `ItemDatabase.cs` 실측. **기획서 0.1 의 금액 예시는 전부 본편 스케일과 맞지 않는다.**

### 카테고리별 가격 분포 (본편 `Item.Price`, 원)

| 접두사 | 종수 | 중앙값 | 최소 | 최대 |
|---|---:|---:|---:|---:|
| `WPN` 총기 | 25 | 80,000 | 22,000 | 260,000 |
| `AMO` 탄약 | 26 | 5,500 | 900 | 268,000 |
| `MED` 의약 | 20 | 21,500 | 3,500 | 85,000 |
| `FOOD` 식량 | 13 | 6,000 | 3,000 | 250,000 |
| `AMR` 방탄복 | 13 | 140,000 | 16,000 | 560,000 |
| `HDW` 헬멧 | 11 | 80,000 | 12,000 | 640,000 |
| `BPK` 가방 | 6 | 82,500 | 12,000 | 250,000 |
| `NVG` 야시경 | 3 | 480,000 | 145,000 | 480,000 |
| `MEL` 근접 | 5 | 180,000 | 5,000 | 950,000 |
| **`JUNK` 부품** | 45 | **30,000** | 1,500 | 4,000,000 |

### 여기서 나오는 두 가지 사실

**1. 정크가 비싸다.** 볼트 `JUNK03` 이 **15,000원**, 나사못 12,000원, 전선 12,000원,
전기 모터 45,000원, 공구 세트 45,000원. 정크 중앙값 30,000원은 탄약(5,500)의 5배이고
총기 중앙값(80,000)의 3분의 1이다. **AV-74M 소총 한 정 = 볼트 3개.**

이건 버그가 아니라 본편의 의도로 보인다 — 하이드아웃 제작 경제가 총기 경제만큼 무겁다.

**2. 기획서 §5 의 "볼트 ×5 납품 → ₩8,000" 은 성립하지 않는다.**
본편 가격을 그대로 쓰면 볼트 5개 = **75,000원**어치다. 그걸 8,000원에 넘기면
플레이어가 의뢰를 받을 이유가 없다. 의뢰 시스템 전체가 손해 보는 장치가 된다.

### 결정: 본편 `basePrice` 를 그대로 쓴다

모바일 전용 가격표를 따로 만들지 않는다. 세계관 일관성이 깨지고, 본편 아이템이
바뀔 때마다 두 표를 맞춰야 하며, 전송 한도 계산이 두 가격 사이에서 흔들린다.

대신 **기획서의 금액 예시를 본편 스케일로 다시 잡는다.**

| 항목 | 기획서 0.1 | 재조정 | 산출 근거 |
|---|---:|---:|---|
| 볼트 ×5 납품 보상 | ₩8,000 | **₩95,000** | 아이템 가치 75,000 × 1.25 |
| 구로 폐공단 인건비 | ₩18,000 | ₩60,000 | 아래 파견 마진 규칙 |
| 구로 폐공단 보급비 | ₩7,000 | ₩25,000 | |
| 총 파견비 | ₩25,000 | **₩85,000** | |

### 일일 의뢰 보상 공식

```
보상금 = Σ(요구 아이템 basePrice × 수량) × 배수
배수: tier1 1.25 / tier2 1.35 / tier3 1.5
```

배수가 1보다 큰 이유는 **납품이 판매보다 항상 이득이어야** 하기 때문이다.
같거나 낮으면 플레이어는 의뢰를 무시하고 전부 팔아버린다.
1.5를 넘기면 반대로 판매 선택지가 죽는다 — GDD §9 의 3갈래 선택이 무너진다.

### 파견비 공식

```
파견비 = 예상 회수 가치 × 0.55
예상 회수 가치 = lootTable 기대값 (rolls 중앙값 × entry 가중평균 가치)
```

기대 마진 45%. 실패·사고 확률을 감안하면 실질 마진은 25~35% 가 된다.
**마진이 이보다 크면 파견이 무뇌 반복이 되고, 작으면 파견을 안 보낸다.**

`balance.json` 에 `questRewardMultiplier`, `expeditionCostRatio` 로 노출해
코드에 박지 않는다. Phase 2 이후 실측으로 조정한다.

### 모바일 시작 자금

첫 스캐브 고용 비용(`scav_pool.json` tier1 30,000)도 이 스케일에서 다시 봐야 한다.
볼트 2개 값이다. **tier1 고용비를 ₩250,000 정도로 올리고, 공장 클리커 1회 수익을
₩8,000~15,000 선으로 잡으면 "20~30회 작업 → 첫 고용"** 이 되어 GDD §16 의
첫 30분 흐름과 맞는다.

---

## 4. 탐색 결과 산출 공식 (초안)

### 4-1. 성공/사고 판정

```
riskScore   = map.riskLevel × 10
defenseScore = Σ(scav.survival) + 장비보정 + 특성보정
accidentChance = clamp(0.05, 0.60, (riskScore - defenseScore × 0.8) / 100)
```

사고 발생 시 결과: `부상`(60%) / `실종`(30%) / `사망`(10%)
`TR_MEDIC` 보유 시 `사망` → `부상` 으로 1단계 완화.

### 4-2. 회수량

```
rolls = lootTable.rolls.min
      + floor((Σ scav.search / 팀원수) / 4)
      + 가방보정
rolls = min(rolls, lootTable.rolls.max + 2)
```

### 4-3. 전투 발생

```
combatChance 판정 → 발생 시
  전투력 = Σ(scav.combat) + 총기보정
  승리   → 추가 전리품 1~2건
  패배   → 사고 판정 1회 추가 + 장비 손실 확률
  TR_COWARD 보유 시 fleeChance 만큼 도주 → 전투 회피, 회수량 -15%
```

> **모든 수치는 초안이다.** Phase 2 완료 후 실제 플레이로 조정한다.
> 조정 대상 상수는 전부 `Assets/StreamingAssets/Data/balance.json` 한 곳에 모아 코드에 박지 않는다.

---

## 5. 폴더 구조

```
AfterSeoul/
├─ Assets/
│  ├─ Game/
│  │  ├─ Core/          # 세이브, 시간, 이벤트버스, 데이터 로더
│  │  ├─ Factory/
│  │  ├─ Scav/
│  │  ├─ Expedition/
│  │  ├─ Inventory/
│  │  ├─ Quest/
│  │  ├─ Mail/
│  │  └─ UI/
│  ├─ Services/
│  │  ├─ Authentication/  CloudSave/  IAP/  Notification/
│  ├─ Platform/
│  │  ├─ Android/  iOS/
│  ├─ StreamingAssets/Data/   # items, maps, loot_tables, npcs, daily_quests, recipes, scav_pool, balance
│  └─ Resources/Locales/      # ko, en, jp, zh, ru (본편에서 추출)
├─ Tests/
│  ├─ EditMode/        # 밸런스·세이브·validation 테스트
│  └─ PlayMode/
├─ Tools/
│  └─ extract_mainline_data.py
└─ docs/
```

`asmdef` 는 `Game.Core` / `Game.Systems` / `Services` / `Platform` / `Tests` 정도로만 나눈다.
**1인 개발이므로 어셈블리를 잘게 쪼개지 않는다** (GDD §20-4).
