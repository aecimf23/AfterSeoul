# AFTER SEOUL — 작업계획서

문서 버전: 0.1 / 작성 2026-09-11

체크박스는 진척도 기록용이다. 작업 완료 시 `- [x]` 로 바꾸고, 세션 기록은 각 Phase 끝 `진행 기록` 에 남긴다.
(본편 리포지토리 `docs/superpowers/plans/*.md` 와 동일한 관례)

---

## P0 — 프로젝트 부트스트랩

**목표: Unity 프로젝트가 열리고, Android 기기에 빈 화면이 뜨고, git 에 올라간다.**

> ⚠ P0 에서 가장 중요한 한 줄: **`Tests/EditMode/OfflineResolverTests.cs` 를 Test Runner 로 돌린다.**
> 코어 C# 은 아직 한 번도 컴파일된 적이 없다 ([ARCHITECTURE.md §10](ARCHITECTURE.md#10-검증)).

- [x] `D:\devSource\AfterSeoul` 디렉터리 생성
- [x] Unity Hub 에서 Unity 6.3 (6000.3.23f1) / **Universal 2D** 템플릿으로 신규 프로젝트 생성
  - 본편이 6.3 LTS 이므로 동일 버전으로 맞춘다 (에디터 병행 설치 회피)
- [x] `.gitignore` (본편 것 + 모바일 추가분)
- [x] `git init` (`main` 브랜치) + 첫 커밋
- [x] `.gitattributes` (본편 것 재사용)
- [ ] Player Settings
  - [ ] Company / Product Name 설정 — Product 는 `AfterSeoul`(템플릿 오타 `AfetSeoul` 수정), **Company 는 `DefaultCompany` 그대로 (결정 대기, 부록 B)**
  - [x] Scripting Backend = **IL2CPP**, Target Architecture = **ARM64** (템플릿 기본값이 이미 그렇다)
  - [x] Minimum API Level = **26** (Android 8.0)
  - [x] Orientation = Portrait 고정 (자동회전 전부 끔)
  - [x] Splash 제거
  - [x] Package Name `com.DefaultCompany.AfterSeoul` (템플릿 `urp_2d` 교체. Company 확정 시 같이 바꾼다)
- [x] Newtonsoft Json 3.2.1 을 `manifest.json` 에 추가 (Input System 은 템플릿에 이미 포함)
- [x] Unity 에서 패키지 복원 확인
- [x] 준비된 산출물 배치: `docs/` 7종, `Assets/Game/**`, `Assets/StreamingAssets/Data`, `Assets/Resources/Locales`, `Assets/Tests/`, `Tools/`
- [x] **코어 첫 컴파일 + `OfflineResolverTests` 15/15 통과**
- [ ] Android 실기기에 빈 씬 빌드 성공 확인 — **Unity 6000.3.23f1 에 Android Build Support 모듈이 설치돼 있지 않다** (Hub → Installs → Add modules)

**완료 기준:** 실기기에서 앱이 실행되고 크래시하지 않는다.

### 진행 기록
- **2026-09-11** — 에디터 1회 오픈으로 패키지 복원·첫 컴파일 성공(`error CS` 0건). batchmode Test Runner 로
  `OfflineResolverTests` 15/15 통과 → ARCHITECTURE §10 의 "컴파일된 적 없음" 해소. `Tools/sim_model.py` 도 재실행 ALL PASS
  (이 PC 는 `py` 런처가 인터프리터를 못 찾아 Blender 내장 Python 3.11 로 실행). Player Settings 정리, `git init`.
  남은 것: Android 모듈 설치 → 실기기 빌드, Company 명 결정.

---

## P1 — Prototype: 데이터 · 창고 · 공장 클리커

**목표: 탭해서 돈을 벌고, 만든 물건이 창고에 쌓이고, 앱을 껐다 켜도 남아 있다.**

### P1-A 데이터 파이프라인

- [x] `Tools/extract_mainline_data.py` 작성 — 실행 검증 완료 (458종 파싱)
  - [x] `ItemDatabase.cs` 파싱 → id/category/slot/price/rarity/maxStack
  - [x] `DummyData.cs` 의 `Maps[]`, `Traders[]` 파싱
  - [x] `Locales/*.json` 에서 `ITEM_*` `MAP_*` `TRADER_*` 키만 필터
  - [x] `transferable_items.json` 생성 (전송 허용 52종)
- [x] **미확인 id 확정** — 볼트=`JUNK03`, 진통제=`MED14`, `JUNK01~40` 전체 목록 확보 (2026-09-11)
- [x] `DATA_SCHEMA.md` §3 의 MVP 아이템 32개를 실제 id 로 확정
- [x] 확정된 id 들의 `Item.Price` / `maxStack` 실측값을 `items.json` 에 반영
- [x] `Assets/StreamingAssets/Data/` 에 `items.json` `maps.json` `npcs.json` 출력
- [x] `Assets/Resources/Locales/{ko,en,jp,zh,ru}.json` 배치 (각 855키)
- [x] `loot_tables.json` / `recipes.json` / `daily_quests.json` / `scav_pool.json` / `balance.json` 작성
      (추출 대상이 아니라 직접 설계해야 하는 데이터)
  - [x] + `expeditions.json` — 지역별 파견 파라미터. `maps.json` 은 추출기가 덮어쓰므로 분리 (DATA_SCHEMA §1-2)
  - [x] `Tools/check_data.py` — Unity 없이 데이터 검산 (파견비 비율 / 의뢰 보상 공식 / id 참조)

### P1-B 코어

**설계와 스켈레톤은 이미 있다** → [ARCHITECTURE.md](ARCHITECTURE.md). 남은 건 Unity 에 얹고 돌리는 것.

- [x] `Game/Core/IClock.cs` — 시간의 유일한 통로
- [x] `Game/Core/GameTime.cs` — KST 05:00 날짜 경계
- [x] `Game/Core/Rng.cs` — 결정론적 난수 + 용도별 스트림 분리
- [x] `Game/Core/GameSave.cs` — 상태 루트
- [x] `Game/Core/SaveService.cs` — 원자적 저장, 깨진 세이브 복구
- [x] `Game/Core/Timeline.cs` + `OfflineResolver.cs` — 정산 엔진
- [x] `Game/Core/ResolveReport.cs` — 정산 결과
- [x] `Game/Core/GameSession.cs` — 단일 진입점
- [x] `Game/Core/JsonDataRegistry.cs` — `IDataRegistry` 의 JSON 구현
- [x] `Game/Core/NewtonsoftJsonCodec.cs` — `IJsonCodec` 구현 (enum 문자열, DateTimeOffset 직접 파싱)
- [x] `Game/Core/Loc.cs` — 로케일 조회 (본편 `Loc.Get(key, args)` 과 동일 인터페이스, en 폴백)
- [x] `Unity/Bootstrap.cs` — MonoBehaviour 하나가 GameSession 을 생성·보유. 씬 배치 없이 `RuntimeInitializeOnLoadMethod` 로 생성
      (`Assets/Game/**` 는 `noEngineReferences` 라 거기 둘 수 없어 `Assets/Unity/` + `AfterSeoul.Unity` asmdef)
- [x] asmdef 3개 (`AfterSeoul.Game` / `AfterSeoul.Unity` / `AfterSeoul.Tests`) — [ARCHITECTURE.md §2](ARCHITECTURE.md)
- [x] **`Assets/Tests/EditMode/OfflineResolverTests.cs` 를 Test Runner 로 처음 돌린다** ← 15/15 통과
- [x] `GameSession` 플레이어 조작 진입점 (`DoManualWork` / `Sell` / `EnqueueCraft` / `Depart` / `Deliver`) + 앱 켜진 동안의 `Tick()`
- [x] **버그 수정: `Commit()`/`Suspend()` 가 `SavedAt` 을 now 로 밀어 그 사이 날짜 경계가 사라지던 문제** (회귀 테스트 포함)
- [x] `Assets/link.xml` — IL2CPP 가 Newtonsoft 전용 DTO 를 스트리핑하지 않게

> EventBus 는 만들지 않는다. 이유는 [ARCHITECTURE.md §11](ARCHITECTURE.md#11-이-아키텍처가-하지-않는-것).

### P1-C 창고

- [x] `Game/Inventory/Warehouse.cs` — 스택 리스트, 용량, Add/Remove/CountByTag
- [ ] 창고 UI 화면 (목록, 수량, 정렬)
- [x] 아이템 판매 로직 (`Inventory/Market.cs`, `basePrice × sellPriceRatio`, 전부 아니면 무) — UI 는 창고 화면과 함께

### P1-D 공장 클리커

- [ ] `assemble` 미니게임 1종 (재료 배치 → 조립, 5~15초) — UI 작업
- [x] 품질 4단계 판정 (`실패` `보통` `양호` `우수`) — `FactorySystem.GradeManualWork(점수 0~1)`, 경계는 `balance.json`
- [x] 품질 → 보수 반영 — 직접 노동 1회 보수 `laborPayByQuality` 0/8,000/11,000/15,000
      (창고 스택에 품질을 싣지 않는다. 스택마다 품질을 들면 스택이 쪼개지고 창고 용량 규칙이 무너진다)
- [x] `recipes.json` 로딩 + `RCP_BOLT` 1개 동작 — 원안 `JUNK16×2→JUNK03×1` 은 30,000원→15,000원 손해라 **1:1 로 조정**
- [x] 제작 큐 (1슬롯) — `QueueCapacity = StationLevel`

### P1-E UI 골격

- [ ] 하단 5탭 내비게이션 `[기지] [공장] [탐색] [인원] [창고]`
- [ ] Canvas Scaler + Safe Area 컴포넌트
- [ ] 색 팔레트 상수화 (어두운 회색 / 청회색 / 군용 녹색 / 상태 4색)

### P1-F 검증

- [x] EditMode 테스트: 세이브 왕복(직렬화→역직렬화 동일성) + 깨진 세이브 복구
- [x] EditMode 테스트: 창고 용량 초과 시 거부
- [x] EditMode 테스트: `items.json` 의 모든 id 가 로케일 키를 가지는가 (5개 국어. 본편 누락 13종은 예외 목록)
- [x] EditMode 테스트: 실제 데이터 정합성 — id 참조 / 의뢰 입수 가능성·touches / 보상 공식 / 파견비 비율
- [x] EditMode 테스트: 세션 — 신규 게임 부팅·재실행 동일성 / 앱 켜진 채 파견 복귀(`Tick`) / 조작 즉시 저장
- [ ] 실기기: 탭 → 제작 → 판매 → 앱 종료 → 재실행 → 데이터 유지 확인

**완료 기준:** 실기기에서 5분간 탭해서 돈을 벌고, 껐다 켜도 그대로다.

### 진행 기록
- **2026-09-11** — P1 의 **UI 를 뺀 전부** 완료. EditMode **31/31 통과** (`OfflineResolverTests` 15 + `SessionTests` 9 + `DataFileTests` 7).
  - 데이터: 전리품 표 4개는 파견비 공식(기대 회수가치 × 0.55)에서 역산 — 파견비/기대가치 명동 0.51 / 용산 0.59 / 구로 0.54 / 의정부 0.60.
    의뢰 8개는 `Σ가격×수량×티어배수` 공식 ±1.3% 이내. 둘 다 테스트가 강제한다.
  - 문서와 다르게 간 것: `expeditions.json` 분리, `RCP_BOLT` 1:1, `Bootstrap` 위치(`Assets/Unity/`).
  - 발견: 본편 아이템 대부분이 `maxStack 1` 이라(의약·식량·부품) 창고 60스택이 파견 10회 안팎이면 찬다. P2 밸런스 때 용량 재검토.
  - 다음: P1-C/D/E UI (하단 5탭, 창고 목록·판매, 공장 `assemble` 미니게임). 세션 API 는 준비돼 있다.

---

## P2 — Scav: 고용 · 장비 · 파견

**목표: 스캐브를 고용해 파견하고, 시간이 지나면 물건을 들고 돌아온다.**

- [ ] `Game/Scav/Scav.cs` — 능력치 3종, 특성, 상태(`대기` `탐색중` `부상` `치료중` `실종`)
- [ ] `scav_pool.json` + 고용 시장 UI (티어별 후보 3명 롤링, 일 1회 갱신)
- [ ] 고용 비용 / 시간당 급여 처리
- [ ] 장비 슬롯 6종 (총기/방탄복/가방/의료품/손전등/특수도구)
- [ ] `Game/Expedition/ExpeditionService.cs`
  - [ ] 파견비 계산 (인건비 + 보급비) 및 즉시 차감
  - [ ] **출발 시점에 `rngSeed` 확정 저장** ← 리세마라 방지 핵심
  - [ ] `returnsAt` 계산
- [ ] 결과 산출기 (`DATA_SCHEMA.md` §4 공식)
  - [ ] 전리품 추첨 (`loot_tables.json`)
  - [ ] 사고 판정 (부상/실종/사망)
  - [ ] 전투 판정 + 도주
  - [ ] 장비 손실
- [ ] `resolved` 플래그로 중복 지급 차단
- [ ] 오프라인 복귀 처리 (앱 재진입 시 일괄 정산)
- [ ] 탐색 화면 (지도 → 지역 카드 → 위험도/시간/발견 가능 품목 → 팀 배치 → 파견)
- [ ] 인원 화면 (스캐브 목록, 상세, 누적 이력)
- [ ] MVP 지역 4개 데이터 (`MYEONGDONG` `YONGSAN_MARKET` `GURO_FACTORY` `UIJEONGBU`)
- [ ] 로컬 푸시 알림: 복귀 시각에 알림 (`Services/Notification`)

### 검증

- [ ] EditMode: 동일 seed → 동일 결과 (결정론 보장)
- [ ] EditMode: `resolved=true` 인 탐색은 재정산되지 않는다
- [ ] EditMode: 시계를 앞당겨도 전리품이 달라지지 않는다
- [ ] EditMode: 시계를 되돌리면 진행이 발생하지 않는다
- [ ] 실기기: 파견 → 앱 종료 → 시간 경과 → 재실행 → 정상 복귀

**완료 기준:** 파견을 보내고 앱을 닫았다가 나중에 열면 결과가 기다리고 있다.

### 진행 기록
(비어 있음 = 미착수)

---

## P3 — Quest: NPC · 일일 의뢰 · 신뢰도

**목표: 황 상사가 매일 다른 걸 요구하고, 납품하면 신뢰도가 오르고 새 지역이 열린다.**

- [ ] `Game/Quest/DailyQuestService.cs`
  - [ ] 일일 갱신 (UTC 날짜 기준, 접속 시 판정)
  - [ ] 티어별 풀에서 추첨 (플레이어 레벨/신뢰도로 티어 결정)
  - [ ] 진행도 추적 (`itemId` 조건 / `tag` 조건)
- [ ] 납품 처리 (창고에서 차감 → 보상 지급)
- [ ] NPC 신뢰도 + 티어 해금 (`trustTiers`)
- [ ] 연속 의뢰 체인 (`chainNextId`)
- [ ] `daily_quests.json` — `DQP_HWANG` 풀 8개 작성
- [ ] NPC 대사 (로케일 키 `DQ_{id}_ACCEPT` / `_COMPLETE`)
- [ ] 홈 화면 "오늘의 지시" 위젯

### 검증

- [ ] **빌드 테스트: 모든 의뢰의 `touches.Length >= 2`** (GDD §5 규칙 강제)
- [ ] EditMode: 날짜 경과 시 의뢰 갱신, 미완료 의뢰 소멸
- [ ] EditMode: 납품 시 창고 차감과 보상 지급이 원자적인가
- [ ] EditMode: 신뢰도 티어 도달 시 지역 해금

**완료 기준:** 이틀 연속 플레이했을 때 어제와 다른 의뢰가 나온다.

### 진행 기록
(비어 있음 = 미착수)

---

## 🎯 마일스톤: Vertical Slice

P1~P3 완료 시점에 **GDD §17 의 전체 사이클**이 동작해야 한다.
아래는 그 체크리스트다. **하나라도 막히면 P4 로 넘어가지 않는다.**

- [ ] 황 상사에게 볼트 5개 의뢰 수령
- [ ] 공장에서 클리커로 돈 벌기
- [ ] 첫 스캐브 고용
- [ ] 용산 전자상가 탐색 파견
- [ ] 볼트 및 잡동사니 회수
- [ ] 회수품 중 일부 판매
- [ ] 볼트 5개 황 상사에게 납품
- [ ] 보상 수령
- [ ] 남은 아이템 하나를 PC 본편 발송함에 **등록** (실제 전송은 P5, 여기선 로컬 큐에만 쌓임)

### 재미 검증 (코드가 아니라 판단)

- [ ] 첫 30분 안에 위 흐름을 전부 경험하는가
- [ ] 파견 대기 시간이 답답한가, 기대되는가
- [ ] 다시 켜고 싶은가
- [ ] 클리커가 노동인가, 놀이인가

> **이 4개 질문에 만족스럽게 답하지 못하면 P4 로 가지 말고 P1~P3 을 조정한다.**
> 기능 수보다 핵심 루프 완성도가 우선이다 (GDD §17).

---

## P4 — Factory: 제작 · 생산 큐 · 자동화

**목표: 플레이어의 조작이 "직접 탭"에서 "생산 계획"으로 옮겨간다.**

- [ ] 작업대 레벨 업그레이드 (레벨당 제작 속도/해금 레시피)
- [ ] 제작 큐 다중 슬롯 (레벨당 +1)
- [ ] 오프라인 생산 (상한 적용 — 무제한이면 접속 동기가 사라진다)
- [ ] 보조 인력 고용 (반자동)
- [ ] 자동 생산 (완전 자동, 후반 해금)
- [ ] 중급 레시피 (전자부품 조립, 의료키트, 탄창 정비, 총기부품 가공)
- [ ] 고급 레시피 기본형 (총기 수리 / 소총 조립)
- [ ] `timing` `inspect` 미니게임 추가
- [ ] 품질 → 제작 성공률 반영

### 검증

- [ ] EditMode: 오프라인 생산 상한이 정확히 걸리는가
- [ ] 밸런스: GDD §2 의 조작 비중 표(1개월차 클리커 10%)에 근접하는가

### 진행 기록
(비어 있음 = 미착수)

---

## P5 — PC Integration: 연동

**목표: 모바일에서 보낸 붕대 2개가 본편 하이드아웃 우편함에 도착한다.**

> ⚠ 착수 전 [LINK_CONTRACT.md §9 체크리스트](LINK_CONTRACT.md#9-phase-5-착수-전-체크리스트) 를 전부 처리할 것.

### P5-A 모바일 쪽

- [ ] `Services/Authentication` — Unity Authentication 익명 로그인
- [ ] Code-Link 연결 플로우 (본편에서 코드 발급 → 모바일에 입력)
- [ ] `Services/CloudSave` — `link_profile` / `box_m2p` / `quota` 읽기·쓰기
- [ ] `Game/Mail/OutboxService.cs`
  - [ ] `transferable_items.json` 로딩
  - [ ] V1~V7 validation (LINK_CONTRACT §5-1)
  - [ ] `txId` 생성, `checksum` 계산
  - [ ] **창고 차감 → 발송 순서 보장, 실패 시 롤백**
- [ ] 창고 UI 에 `[본편 발송]` 액션 (연동 미설정 시 버튼 자체를 숨김)
- [ ] 발송함 화면 (대기 중 / 수령됨 상태 표시)
- [ ] 일일 한도 UI 표시

### P5-B 본편 쪽 (별도 브랜치)

- [ ] M8 — `Packages/manifest.json` 에 `com.unity.services.cloudsave` 추가
      (`com.unity.services.authentication` 3.7.4 는 **이미 설치돼 있음** — 인증 기반 재사용)
- [ ] M1 — `PlayerProfile.ClaimedMailTxIds` 필드 추가
- [ ] M2 — `HideoutSystem.TryAddToStash` `internal` 승격
- [ ] M3 — `MobileLinkService.cs` (Cloud Save 읽기 + P1~P10 validation)
- [ ] M6 — `GameState.HideoutMailbox` 추가
- [ ] M4 — 하이드아웃 메뉴에 `[우편함]`
- [ ] M5 — 우편함 화면 렌더
- [ ] M7 — `MAIL_*` 로케일 키 5개 국어
- [ ] M9 — 타이틀/로비 도착 알림

### P5-C 검증 (가장 중요)

- [ ] **아이템 복제 테스트**
  - [ ] 발송 직후 앱 강제 종료 → 재실행 시 창고에서 차감돼 있는가
  - [ ] 네트워크 끊김 상태에서 발송 시도 → 창고가 그대로인가
  - [ ] 두 기기에서 동시 발송 → 총량이 한도를 넘지 않는가
- [ ] **중복 수령 테스트**
  - [ ] 수령 직후 Cloud Save 쓰기 실패 시뮬레이션 → 재실행 시 재수령되지 않는가
  - [ ] 같은 `txId` 를 수동으로 `pending` 으로 되돌려도 수령되지 않는가
- [ ] **변조 테스트**
  - [ ] `count` 를 999 로 변조 → clamp 되는가
  - [ ] 존재하지 않는 `itemId` → 조용히 제외되는가
  - [ ] `WPN01` 을 넣어봄 → 전송 허용 목록에서 걸리는가
  - [ ] `checksum` 불일치 → 폐기되는가
- [ ] **연동 없음 테스트**
  - [ ] 모바일만 설치 → 전 구간 정상 플레이
  - [ ] PC 만 설치 → 전 구간 정상 플레이
  - [ ] Cloud Save 장애 → 양쪽 정상 진행, 연동 UI 만 오프라인

**완료 기준:** 위 검증 항목 전부 통과. **하나라도 실패하면 연동 기능을 출시하지 않는다.**

### 진행 기록
(비어 있음 = 미착수)

---

## P6 — Polish

- [ ] 아이콘 세트 제작 (12개 아이템군, **세트 단위 일괄 생성** — GDD §13)
- [ ] UI 리터치 (현장 단말기 감성 강화)
- [ ] 사운드 (클리커 피드백, 복귀 알림, 납품 확인)
- [ ] 최소한의 애니메이션 (제작 진행, 복귀 연출)
- [ ] 로컬 푸시 알림 정교화 (묶어서 보내기, 과다 알림 방지)
- [ ] 튜토리얼 (첫 30분 흐름 — GDD §16)
- [ ] NPC 추가 (`DR_CHOI`, `YONGSAN_KIM` 순)
- [ ] 지역 추가 (`HAN_RIVER`, `GANGNAM_STREETS`, `NAMSAN_WOODS`)
- [ ] 탐색 이벤트 (매복, 생존자 발견, 잠긴 창고)
- [ ] 스캐브 구조 임무 (실종자 무전 재포착)
- [ ] iOS 빌드 확인

### 진행 기록
(비어 있음 = 미착수)

---

## P7 — Monetization

> **게임의 기본 재미와 경제 밸런스 검증 후에만 착수한다.**

- [ ] 선택형 보상 광고 (강제 광고 없음)
- [ ] 월간 지원계약 (편의 기능 — GDD §11)
- [ ] `Services/IAP`
- [ ] **전송 한도가 과금과 무관함을 코드로 보장** (테스트 포함)
- [ ] 스토어 등록 준비

### 진행 기록
(비어 있음 = 미착수)

---

## 부록 A — 위험 요소

| 위험 | 영향 | 대응 |
|---|---|---|
| **아이템 복제 버그** | 본편 경제 파괴. 되돌릴 수 없음 | P5-C 검증 전원 통과 전까지 연동 미출시. 발송은 반드시 차감 후 |
| **파견 대기가 재미없다** | 게임의 근본이 무너짐 | Vertical Slice 마일스톤에서 판단. 안 되면 파견 시간 단축 + 중간 무전 이벤트 |
| **클리커가 노동으로 느껴짐** | 이탈 | 공정형 미니게임 5~15초 유지. 반복 연타 금지 |
| **본편 id 변경** | 모바일 데이터 깨짐 | 추출 스크립트 재실행 + diff 리뷰. 빌드 테스트로 미존재 id 검출 |
| **1인 개발 범위 초과** | 미완성 | MVP 제외 목록(GDD §15) 엄수. 새 시스템은 핵심 루프 완성 후 |
| **Cloud Save 비용/쿼터** | 서비스 중단 | 우체통 용도로만 사용. 문서 크기 상한(shipments 20, ack 200) 준수 |
| **본편 리포 오염** | 본편 개발 방해 | 본편 변경은 LINK_CONTRACT §6 에 기록 후 별도 브랜치 |

## 부록 B — 결정 대기 항목

- [x] Unity 버전을 본편 6.3 LTS 에 맞출지, 최신으로 갈지 → **6000.3.23f1 로 맞춤**
- [x] Android Minimum API Level → **26**
- [ ] Company Name (`DefaultCompany` 는 본편과 같다. 스토어 등록 전 확정 필요 — 패키지명·세이브 경로가 따라 바뀐다)
- [ ] 세이브 암호화 여부 (권장: MVP 는 평문, P7 에 난독화 검토)
- [ ] `maxDailyValue` 등 전송 한도 실수치 (본편 레이드 수익 실측 후)
- [ ] 스캐브 사망 시 완전 소멸인지, 일정 확률 구조 가능인지 (애착 시스템 방향)
- [ ] 앱 이름 최종 확정 (`AFTER SEOUL` 가안)
