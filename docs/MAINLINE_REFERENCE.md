# 본편 레퍼런스 — ESCAPE FROM SEOUL 코드 조사 결과

조사일: 2026-09-11 / 대상: `D:\devSource\EscapeFromSeoul`

AFTER SEOUL 이 재활용할 수 있는 본편 자산을 정리한 문서.
**이 문서의 목적은 "모바일에서 새로 만들지 않아도 되는 것"을 확정하는 것이다.**

---

## 1. 본편 구조 요약

- Unity 프로젝트지만 실질은 **C# 콘솔(ASCII) 게임 로직을 Unity 안에서 구동**하는 구조.
- 씬은 `Assets/EscapeFromSeoul.unity` **단 하나**. 하이드아웃/레이드는 별도 씬이 아니라 `GameState` 상태 전환.
- 게임 로직 전부: `Assets/Scripts/SeoulLogic/` (약 360개 .cs, 네임스페이스 `EscapeFromConsole`).
- 렌더링: `VirtualConsole.cs` + `RaidRenderer.cs` (ASCII 콘솔 에뮬레이션).
- **데이터는 ScriptableObject / JSON 이 아니라 C# 하드코딩.**
  - 예외: 로케일 `Assets/Resources/Locales/{ko,en,jp,zh,ru}.json`, 맵 타일 `Assets/StreamingAssets/Maps/*.txt`.
- `Assets/Scripts/{Core,Gameplay,Mechanics,Model,Network,UI,View}` 는 Unity 플랫포머 튜토리얼 잔재로 보임 (게임 로직과 무관, 추정).

> 함의: 모바일이 본편 데이터를 **런타임에 공유할 방법은 없다**. 빌드 타임 추출(§5) 로 간다.

---

## 2. 지역 (재활용 확정)

정의: `SeoulLogic/DummyData.cs` 2112~2130 (`Maps = new Map[] { ... }`), 모델 `Map` — `Models.cs:990`.
식별자는 enum 이 아니라 **string `Id`**. 표시명은 `Loc.Get($"MAP_{Id}_NAME")`.

| Id | 한글명 | 본편 난이도 |
|---|---|---|
| `HAN_RIVER` | 한강 세관 | Easy |
| `MYEONGDONG` | 명동 | Easy |
| `NAMSAN_WOODS` | 남산 숲 | Normal |
| `GANGNAM_STREETS` | 강남 시내 | Normal |
| `UIJEONGBU` | 의정부 | Normal |
| `YONGSAN_BASE` | 용산 미군기지 | Hard |
| `GURO_FACTORY` | 구로 폐공단 | Hard |
| `YONGSAN_MARKET` | 용산 전자상가 | Hard |
| `SUYU_DONG` | 강북구 수유동 | Hard |
| `SUSPICIOUS_TUNNEL` | 수상한 땅굴 | Nightmare |
| `INCHEON_PORT` | 인천 여객선 | Nightmare |
| `INCHEON_AIRPORT` | 인천공항 | Nightmare |
| `SOKCHO_RESORT` | 뉴로닉스 리조트 | Nightmare |
| `NEURONIX_LAB` | 뉴로닉스 연구소 | Insane |
| `SUWON_CORRIDOR` | 수원 방면 피난로 | (히든 / 뉴로닉스 제어 데이터 확보 시) |

의정부 오픈월드 하위 지역 (`UijeongbuSurvivalRegionCatalog.cs`) — 후반 세분화 탐색 루트로 재활용 가능:
`UIJ_HOME`, `UIJ_NOGYANG`, `UIJ_GANEUNG`, `UIJ_OLD_TOWN`, `UIJ_HOWON`, `UIJ_HOERYONG`, `UIJ_DOBONG_GATE`, `UIJ_SEWAGE`

---

## 3. 아이템 (재활용 확정)

- 정의: `SeoulLogic/ItemDatabase.cs` (107KB, `Items.Add(new Item{...})` **약 469건 하드코딩**). 조회: `ItemDatabase.GetItemById(string id)`.
- 모델 `Item` — `Models.cs:98`. 주요 필드: `Id, ShortName, Price, Rarity, Category, Slot, SpawnWeight, Caliber, StackCount/MaxStackCount, CurrentAmmo/MaxAmmo, Durability, GridWidth/GridHeight/InnerGrid, WeaponOptic, LoadedAmmoId`.
- **이름/설명은 코드에 없고 로케일 키**: `ITEM_{Id}_NAME`, `ITEM_{Id}_SHORT`, `ITEM_{Id}_DESC` → `ko.json` (5,196 키).

### 카테고리 체계

`ItemCategory` (`Models.cs:50`) — 6개뿐: `Equipment, Medical, Food, Junk, Ammo, Container`
`EquipmentSlot` (`Models.cs:75`): `None, Headwear, Earpiece, BodyArmor, MainWeapon, SubWeapon, Melee, Backpack, TacticalRig, Pockets, NightVision, WeaponAttachment, WeaponOptic`
`ItemRarity`: `Common, Rare, Unique, Legendary`

### ID 접두사 = 사실상의 카테고리

`WPN`(총기 01~27) `AMO`(탄약 01~26) `MED`(의약 01~20) `FOOD`(01~13) `AMR`(방탄복 01~13) `HDW`(헬멧 01~11) `BPK`(가방 01~06) `RIG`(리그 01~03) `MEL`(근접 01~05) `OPT`(조준경 01~04) `ATT`(부착물 01~03) `NVG`(01~03) `EAR`(헤드셋 01~03) `JUNK`(부품·잡템 01~40) `GND`(수류탄 01~02)
단품: `BAT01`, `TRK01`, `VAC01`, `XTG12`, `DOGTAG`, `INJECTOR_CASE`
특수군: `KEY_*`, `KEYCARD_*`(BLUE/RED/YELLOW/PURPLE/BLACK/NEURONIX_ENTRY), `QUEST_*`(30여 종), `CASE_*`, `COSM_*`(엔딩 코스메틱), `UIJ_*`(의정부 생존모드 전용 200+)

### 실제 id 예시

`WPN01`(AV-74M) `WPN04`(P17) `WPN05`(AR-416) `WPN11`(SC-45) `WPN18`(R700)
`AMO01`(5.45x39 PS) `AMO05`(9x19 PST gzh) `AMO09`(7.62x51 M80) `AMO16`(12g AP-20)
`MED01`(살레와) `MED02`(IFAK) `MED05`(AI-2) `MED16`(군용 붕대)
`FOOD01`(투숑카) `FOOD02`(타르콜라) `FOOD05`(정수)
`AMR01`(PACA) `AMR11`(MF-UNTAR) `HDW01`(SSh-68) `BPK01`(MBSS) `BPK06`(Vydra-8)
`JUNK01`(그래픽 카드) `JUNK02`(LedX) `OPT01`(VUDU 1-6) `NVG01`(PNV-10T) `EAR01`(GSSH-01)
`UIJ_IRON_ORE`(철 광석) `UIJ_WOOD`(목재) `UIJ_BANDAGE`(붕대)

### `JUNK01~40` 전체 목록 (제작 재료 설계 근거 — 확정)

| id | 이름 | id | 이름 | id | 이름 | id | 이름 |
|---|---|---|---|---|---|---|---|
| JUNK01 | 그래픽 카드 | JUNK11 | 검정색 연구 키카드 | JUNK21 | 자동차 배터리 | JUNK31 | 가이거 계수기 |
| JUNK02 | LedX 피부 이식경 | JUNK12 | 마크드 방 열쇠 | JUNK22 | 스파크 플러그 | JUNK32 | 군용 케이블 |
| **JUNK03** | **볼트** | JUNK13 | 금 해골 반지 | JUNK23 | 인쇄 회로 기판(PCB) | JUNK33 | 위상 배열 레이더(AESA) |
| JUNK04 | 나사못 | JUNK14 | 롤러 서브마리너 시계 | JUNK24 | 컴퓨터 CPU | JUNK34 | 립스탑 원단 |
| JUNK05 | 테트리즈 게임기 | JUNK15 | 너트 | JUNK25 | CPU 쿨링 팬 | JUNK35 | 플리스 원단 |
| JUNK06 | 실물 비트코인 | JUNK16 | 금속 여분 부품 | JUNK26 | 파워 서플라이 | JUNK36 | 코듀라 원단 |
| JUNK07 | 정수 필터 | JUNK17 | KEK 테이프 | JUNK27 | 위상 제어 릴레이 | JUNK37 | 아라미드 원단 |
| JUNK08 | 전기 모터 | JUNK18 | 덕트 테이프 | JUNK28 | WD-40 (100ml) | JUNK38 | 방탄 원단 |
| JUNK09 | 주름진 호스 | JUNK19 | 절연 테이프 | JUNK29 | 공구 세트 | JUNK39 | 은 목걸이 |
| JUNK10 | 파라코드 | JUNK20 | 전선 | JUNK30 | 가스 분석기 | JUNK40 | 말 동상 |

> ✅ 기획서의 **"볼트" = `JUNK03`** 확정. "천 조각" ≈ `JUNK35`(플리스 원단), "전자부품" ≈ `JUNK23`(PCB),
> "금속/스크랩" ≈ `JUNK16`, "정비도구" = `JUNK29`(공구 세트) — **모바일 전용 id 를 새로 만들 필요 없다.**
> "진통제" = **`MED14`**(이부프로펜 진통제).
> `BAT01` 은 NVG 배터리, 일반 배터리는 `JUNK21`(자동차 배터리).

---

## 4. NPC / 상인 (재활용 확정)

정의: `DummyData.cs:1921` `Traders = new Trader[]`, 모델 `Trader` — `Models.cs:970`.
이름/대사는 `TRADER_{Id}_NAME` / `_DESC` 로케일 키. 얼굴은 ASCII 배열(`Ascii`, `HappyEyeLine`, `OpenMouthLine`).

| Id | 한글명 | 통화 | 본편 초기 해금 |
|---|---|---|---|
| `HWANG` | **황 상사** | Won | ✗ |
| `DR_CHOI` | 보건의 최씨 | Won | ✓ |
| `BROKER` | 장물아비 | Dollar | ✓ |
| `DOKKAEBI` | 도깨비 | Won | ✗ |
| `US_LIAISON` | 미군 연락책 | Won | ✗ |
| `YONGSAN_KIM` | 용산 킴 | Dollar | ✗ |
| `DONGDAEMUN_CHOI` | 동대문 최씨 | Won | ✗ |
| `WILDMAN` | 와일드맨 | Yen | ✗ |

> 모바일의 "고용주 NPC" 후보는 이 8명. MVP 는 `HWANG` 1명 (GDD §38).
> 모바일 UI 에는 본편 ASCII 얼굴을 그대로 못 쓰므로, 같은 캐릭터를 모바일용 아이콘/포트레이트로 재해석 필요.

---

## 5. 로케일 재활용 — 핵심 결정

본편 로케일 키 규칙이 이미 `PREFIX_{Id}_FIELD` 로 일관적이다:

```
ITEM_{Id}_NAME / _SHORT / _DESC
MAP_{Id}_NAME
TRADER_{Id}_NAME / _DESC
QUEST_{Id}_NAME / _DESC
ENTITY_{Id}_NAME
```

**결정: AFTER SEOUL 은 동일한 키 규칙을 그대로 채택한다.**
→ 본편 `ko.json` / `en.json` 에서 `ITEM_*`, `MAP_*`, `TRADER_*` 만 추출하면 모바일 아이템·지역·NPC 텍스트가 5개 국어로 즉시 확보된다. 번역 재작업 0.

### 추출 파이프라인 (Phase 1 에서 구축)

`Tools/extract_mainline_data.py` (모바일 repo 에 둔다) 가 하는 일:

1. `EscapeFromSeoul/Assets/Scripts/SeoulLogic/ItemDatabase.cs` 파싱 → `Id, Category, Slot, Price, Rarity, StackCount, Caliber` 추출
2. `DummyData.cs` 의 `Maps[]`, `Traders[]` 파싱 → 지역·NPC 목록
3. `Assets/Resources/Locales/*.json` 에서 해당 키만 필터
4. 출력: `AfterSeoul/Assets/StreamingAssets/Data/{items,maps,npcs}.json` + `Assets/Resources/Locales/*.json`

> 본편 저장소를 빌드 의존성으로 걸지 않는다. **오프라인 1회 추출 + 결과물 커밋** 방식.
> 본편 아이템이 바뀌면 스크립트를 다시 돌리고 diff 를 리뷰한다.

### 신규 id 발급 규칙

모바일 전용 아이템/개념이 필요하면 **`AS_` 접두사**를 쓴다 (예: `AS_TOOLKIT01`).
본편 id 네임스페이스를 침범하지 않으며, 전송 validation(`LINK_CONTRACT.md`)에서 `AS_*` 는 **전송 불가**로 자동 분류된다.

---

## 6. 하이드아웃 / 창고 — 우편함을 붙일 자리

- 별도 씬 없음. `GameState` enum (`Models.cs:6`) 의 화면 상태: `HideoutScreen`, `HideoutWorkout`, `HideoutShootingRange`.
- 시스템: `SeoulLogic/HideoutSystem.cs` (`public static class HideoutSystem`, 61KB) + `HideoutRecipe`.
- 화면/입력 루프: `Program.Runtime.Hideout.cs` → `RunHideoutState()`
- 뷰: `Program.Views.Hideout.cs`
- 모듈: `Generator`, `Medstation`, `Workbench`, `BitcoinFarm`(max5), `NutritionUnit`, `Storage`(max5), `ShootingRange`(max1), `PersonalRoom`(max5)

### 창고(스태시)

전용 클래스 없음. **`PlayerProfile.StashGrid` (`Item[]`) 가 곧 창고.**
`Storage` 모듈 레벨당 50칸(10x5) 1페이지, 최대 5페이지 = 250칸.
용량 보정: `HideoutSystem.EnsureStashCapacity()` / `GetStashPageCount()`.

### 아이템 추가 진입점 (통일 API 없음 — 중요)

| 용도 | 위치 |
|---|---|
| 은신처 제작물 투입 | `HideoutSystem.TryAddToStash(PlayerProfile, Item)` — `HideoutSystem.cs:1049` (**private**) |
| 퀘스트 보상 지급 | `Program.TryGrantQuestItemReward(Quest, out string)` — `Program.Progression.cs:275` |
| 레이드 종료 가방→창고 | `MoveBackpackToStash(PlayerProfile)` — `Program.Inventory.cs:63` |
| 레이드 중 루팅 | `Program.TryStoreRaidLoot(Item, out string)` — `Program.Inventory.cs:1234` |
| 사망 복구/보험 | `Program.Inventory.cs:170 / :214`, `PendingInsuranceReturns` |
| 의정부 생존모드 (유일한 제대로 된 public API) | `UijeongbuSurvivalInventoryService.TryAddStack/TryAddCarriedStack/CanAddCarriedStack` |

> **우편함 구현 시 결정 필요**: `HideoutSystem.TryAddToStash` 를 `internal`/`public` 으로 승격해 재사용할지,
> 아니면 `MobileMailboxService.cs` 를 신설해 자체 삽입 로직을 둘지.
> **권장: 승격 + 재사용.** 스태시 빈칸 탐색 로직을 두 벌 두면 반드시 어긋난다.
> 자세한 것은 `LINK_CONTRACT.md` §6.

---

## 7. 세이브 시스템

- `SeoulLogic/SaveManager.cs` (`public static class SaveManager`)
- API: `SaveProfile(PlayerProfile)`, `LoadProfile()` (테스트 훅 `SaveProfileOverrideForTests`)
- 위치: `Application.persistentDataPath + "/SaveData.dat"` (+ `.tmp`, `.bak`)
  - Windows 실경로: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\EscapeFromSeoul\SaveData.dat`
    (`ProjectSettings.asset`: `companyName: DefaultCompany`, `productName: EscapeFromSeoul`, `bundleVersion: 4.0.4`)
- 포맷: Newtonsoft.Json 으로 `SaveEnvelope { int Version = 2; PlayerProfile Profile; }` 직렬화 → **AES 암호화**
  - 레이아웃: `"EFS2"(4B) + IV(16B) + ciphertext + HMACSHA256(32B)`
  - HMAC 키 = `SHA256("EscapeFromSeoul.Save.Authentication.v2")`
- 쓰기: tmp → `File.Replace` 원자적 교체. 에디터 배치모드에선 저장 스킵.
- 루트 객체 `PlayerProfile` (`Models.cs:413`) 에 스태시/장비/하이드아웃/퀘스트/스킬/의정부 월드/플리마켓/엔딩 기록이 **전부** 들어감.

> 함의: **모바일이 본편 세이브를 직접 읽거나 쓰는 일은 절대 없다.**
> 연동은 Cloud Save 상의 TransferBox 문서만 주고받고, 본편 세이브 반영은 본편 클라이언트가 한다.

---

## 8. 퀘스트 시스템 (일일 의뢰 설계 참고용)

- 모델: `Quest`(`Models.cs:920`), `QuestObjective`(:908), `QuestStatus{Locked, Available, Active, Completed}`(:906)
- `Quest` 필드: `Id, TraderId, Name/Description(로케일 키), Objectives, Status, RewardExp, RewardRoubles, RewardItemId/Count, RequiredLevel, PrerequisiteQuestId, NextQuestId, UnlockTraderId, StoryTextOnAccept/OnComplete`
- `QuestObjective`: `Type`(예 `"KillScav"`, `"Extract"`), `TargetId`, `TargetMapId`, `CurrentProgress`, `TargetProgress`
- 정의: `DummyData.cs:74` 부터 `Player.Quests.Add(new Quest{...})` **164건**
- 네이밍: `Q_{TRADER}_{주제}_{번호}`, 후속은 `_FOLLOWUP`
  - 예: `Q_HWANG_01~06`, `Q_HWANG_ESCAPE_SUPPLY_01~08`, `Q_HWANG_SUYU_01/02`, `Q_DR_CHOI_FOOD_01~07`, `Q_YONGSAN_01~20`, `Q_WILDMAN_BOSS_01~07`
- 루트 `quests_analysis.txt` 는 UTF-16LE, `Id/Exp/Roubles` 3열 덤프 (스냅샷 — **코드가 정본**)

> 모바일 일일 의뢰는 `PrerequisiteQuestId`/`NextQuestId` 체인 구조를 그대로 차용한다 (GDD §8 연속 의뢰).
> 다만 모바일 퀘스트 id 는 `DQ_` 접두사를 써서 본편 `Q_` 와 분리한다.

---

## 9. 확인 완료 / 미확인 항목

### 확인 완료 (2026-09-11)

- [x] 회사명/제품명 → `DefaultCompany` / `EscapeFromSeoul` (§7)
- [x] "볼트" = `JUNK03`, "진통제" = `MED14` (§3)
- [x] `JUNK01~40` 전체 목록 확보 (§3)
- [x] **UGS 패키지 현황** — `Packages/manifest.json`:
  - `com.unity.services.authentication` **3.7.4 이미 설치됨** ✅
  - `com.unity.services.lobby` 1.3.0, `com.unity.services.relay` 1.2.0 (멀티플레이용)
  - ⚠ **`com.unity.services.cloudsave` 는 없음** → Phase 5 에서 추가 필요 (변경 요청 M8)
  - 함의: Unity 프로젝트 연결과 익명 인증 기반은 이미 있으므로 **Code-Link 는 인증 위에 얹으면 된다**

### 추출 실행 결과 (2026-09-11)

`Tools/extract_mainline_data.py` 1회 실행. **파싱은 문제없이 동작한다.**

| 항목 | 결과 |
|---|---|
| `Items.Add` 등장 | 470회 |
| 파싱 성공 | **458종** |
| 건너뜀 — Id 가 변수/프로퍼티 | 10건 (`policy.BlueprintItemId`, `CultPoisonPolicy.KnifeItemId`, `SiegeStartingWeapons.KnifeId/TacticalSpearId` 등) |
| 건너뜀 — 중복 id | 1건 (`UIJ_BONE`) |
| 지역 | 14개 |
| 상인 | 8명 |
| 로케일 키 | ko/en/jp/zh/ru 각 855개 |

### 추출 중 발견한 본편 이슈 (모바일과 무관 — 본편 쪽 참고용)

- **`UIJ_BONE` 이 `ItemDatabase.cs` 에 두 번 정의돼 있다.** `GetItemById` 는 `Find` 라 앞의 것만 쓰이므로
  뒤의 정의는 죽은 코드다. 두 정의의 스탯이 다르면 의도와 다르게 동작할 수 있다.
- **로케일에 이름이 없는 아이템 13종**: `AMO24`, `AMO26`, `KEY_DORM_206/208/306`,
  `QUEST_CULT_MARKED_DOCUMENTS`, `QUEST_CULT_SUYU_RITUAL`, `QUEST_NEURONIX_CREATURE_DNA`,
  `QUEST_SCA008_REAGENT`, `QUEST_HAN_RIVER_DORM_SURVEY`, `QUEST_HAN_RIVER_206_MANIFEST`,
  `QUEST_HAN_RIVER_208_INSPECTION`, `QUEST_HAN_RIVER_306_LEDGER`.
  `AMO24`/`AMO26` 은 탄약이라 플레이 중 실제로 노출될 가능성이 있다 — **본편 로컬라이제이션 누락으로 보임.**

### 미확인 (Phase 1~5 에서 확정)

- [ ] 우편함 UI 를 넣을 구체적 메뉴 위치 (`Program.Views.Hideout.cs` / `Program.Runtime.Hideout.cs` 실측)
- [ ] 본편 초반 평균 레이드 수익 (전송 한도 산정 기준)
- [ ] `applicationIdentifier` 가 `com.unity.template.platformer` 로 남아 있음 — 본편 출시 전 교체 대상 (모바일과 무관하나 기록)
