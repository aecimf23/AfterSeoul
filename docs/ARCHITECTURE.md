# AFTER SEOUL — 코어 아키텍처

문서 버전: 0.2 / 작성 2026-09-11 (Unity 프로젝트 배치 시 수정)

> 이 문서는 **왜 이렇게 만들었는지**를 설명한다. 무엇을 만들었는지는 코드에 있다.
> 규칙을 어길 일이 생기면 먼저 여기를 고치고 나서 코드를 고친다.

---

## 0. 한 문장

> 이 게임에서 플레이어가 실제로 겪는 경험의 대부분은 **앱을 닫은 사이에 일어난 일**이다.
> 그래서 "오프라인 정산"은 부수적인 인프라가 아니라 게임의 본체다.

---

## 1. 네 가지 규칙

이 넷을 어기면 이 아키텍처는 의미가 없다.

| # | 규칙 | 어겼을 때 |
|---|---|---|
| **R1** | `AfterSeoul.Game` 어셈블리는 **UnityEngine 을 참조하지 않는다** (asmdef `noEngineReferences` 로 강제) | 테스트가 느려지고 결국 안 짜게 된다 |
| **R2** | 시간은 **`IClock` 으로만** 얻는다. `DateTime.UtcNow` 직접 호출 금지 | 오프라인 시나리오를 테스트할 방법이 없어진다 |
| **R3** | 난수는 **커밋 시점에 고정된 시드**에서만 나온다. 정산 시점에 새로 뽑지 않는다 | 리세마라가 뚫린다 |
| **R4** | 정산 진입점은 **`GameSession.Resume()` 하나**다. 화면별로 시간을 따라잡지 않는다 | 화면 전환 순서에 따라 상태가 달라진다 |

---

## 2. 계층

```
┌─────────────────────────────────────────┐
│  Unity 레이어 (MonoBehaviour, UI, 입력)    │  ← UnityEngine 여기서만
├─────────────────────────────────────────┤
│  GameSession        단일 진입점            │
├─────────────────────────────────────────┤
│  OfflineResolver    타임라인 정산 엔진      │
├─────────────────────────────────────────┤
│  Expedition / Factory / DailyQuest       │  ← 전부 stateless
├─────────────────────────────────────────┤
│  GameSave (상태)  ·  IDataRegistry (정적)  │
├─────────────────────────────────────────┤
│  IClock  ·  Rng  ·  GameTime  ·  SaveService │
└─────────────────────────────────────────┘
```

**시스템 클래스는 상태를 들고 있지 않는다.** 전부 `GameSave` 를 받아서 읽고 쓴다.
1인 개발에서 싱글턴이 각자 상태를 들고 있기 시작하면 "세이브에는 있는데 메모리에는 없는"
불일치가 반드시 생기고, 그 버그는 재현이 안 된다.

### asmdef 는 셋 (0.2 에서 넷 → 둘, 0.3 에서 Unity 레이어 추가)

```
AfterSeoul.Game    Assets/Game/**    noEngineReferences: true   ← R1 을 강제하는 장치
AfterSeoul.Unity   Assets/Unity/**   Game 참조. MonoBehaviour 는 여기만
AfterSeoul.Tests   Assets/Tests/**   Editor 전용
```

**왜 Core 와 Systems 를 나누지 않았나.** 처음엔 `Game.Core` / `Game.Systems` 로 나누려
했는데, `GameSession` 이 세 시스템을 생성·보유하는 합성 루트(composition root)라서
`Core → Systems` 참조가 생긴다. 그런데 시스템들은 `Core` 의 `GameSave` 와 `ITimelineSystem`
을 참조하므로 `Systems → Core` 도 있다. **순환 참조라 컴파일 자체가 안 된다.**

해결책은 둘이었다 — `GameSession` 을 Systems 쪽으로 내리거나, 하나로 합치거나.
합쳤다. 중요한 경계는 "Core vs Systems"가 아니라 **"시뮬레이션 코드 vs Unity 코드"**이고,
그건 어셈블리 하나로도 지켜진다. 1인 프로젝트에서 어셈블리를 늘리는 건 대부분 비용이다 (GDD §20-4).

`AfterSeoul.Unity` 는 첫 MonoBehaviour(`Bootstrap`)와 함께 추가했다. `Assets/Game/Unity/` 에 두지 않은
이유: `Assets/Game/**` 전체가 `noEngineReferences` 라는 규칙을 예외 없이 유지하려고. 폴더 경로만 보고
"여기는 Unity 를 못 쓴다"를 알 수 있어야 한다.

`Bootstrap` 은 씬에 배치하지 않고 `RuntimeInitializeOnLoadMethod` 로 스스로 생긴다. StreamingAssets 읽기
(Android 는 `UnityWebRequest`), `Resources` 로케일 로드, `persistentDataPath`, 앱 수명 이벤트를 여기서 끝내고
게임 코드에는 **텍스트와 경로만** 넘긴다.

### R1 은 문서가 아니라 asmdef 가 강제한다

`AfterSeoul.Game` 의 `noEngineReferences: true` 때문에 `using UnityEngine` 을 쓰면
**컴파일 에러가 난다.** 규칙을 지키자는 약속이 아니라 지킬 수밖에 없는 구조다.

---

## 3. 핵심: 왜 시스템 순서가 아니라 시간 순서인가

이 설계에서 유일하게 비범한 부분이고, 나머지는 평범하다.

### 흔한 방식 (쓰지 않는다)

```
앱 재진입
 → 공장 전부 정산
 → 파견 전부 정산
 → 일일 의뢰 갱신
```

사흘 만에 접속했다고 하자. 실제로 일어난 일은:

```
09-11 10:35  파견 복귀
09-11 11:00  공장 완성
09-12 05:00  날짜 바뀜 (어제 의뢰 만료)
09-12 14:20  파견 복귀
09-13 05:00  날짜 바뀜
...
```

시스템별로 돌리면 이 순서가 뭉개진다. 그러면:

- 어제 복귀한 물자가 오늘 의뢰 조건에 잡히거나, 반대로 안 잡히거나
- 날짜가 두 번 넘어간 경우 중간 상태가 사라지거나
- 창고가 꽉 찬 상황에서 무엇이 먼저 들어가느냐로 결과가 달라지거나

**이 버그들은 전부 "며칠 만에 접속했을 때만" 나온다.** 개발 중에는 거의 안 보이고,
출시 후에 재현이 불가능한 문의로 돌아온다.

### 쓰는 방식

```
1. 수집 — 각 시스템에게 "이 구간에 무슨 일이 있었나" 묻는다 (상태 변경 금지)
2. 정렬 — (시각, Order, 수집순서) 로 한 줄 세운다
3. 적용 — 앞에서부터 실행한다
```

정렬 한 번으로 저 부류의 버그가 통째로 사라진다. 새 시스템이 늘어나도
`OfflineResolver` 는 손대지 않는다 — `ITimelineSystem` 을 구현하고 등록만 하면 된다.

### 동시각 tie-break

```
ExpeditionReturn (0)  <  FactoryOutput (1)  <  DayRollover (2)
```

파견이 **정확히** 날짜 경계 시각에 복귀했다면, 그 물자는 어제 의뢰가 만료되기 전에
창고에 들어와야 한다. 애매할 때는 플레이어에게 유리한 쪽으로 정한다.

그래도 같으면 **수집 순서**로 가른다. `List.Sort` 는 불안정 정렬이라,
완전 순서를 주지 않으면 같은 세이브를 두 번 정산했을 때 결과가 달라질 수 있다.

---

## 4. 결정론 — 리세마라를 설계로 막는다

### 시드는 커밋 시점에 고정

파견을 보내는 순간 `Seed` 를 확정해 세이브에 쓴다. 복귀 정산은 그 시드를 **펼쳐 보는**
것뿐이고, 새 난수를 뽑지 않는다.

결과:

- 복귀 직전에 앱을 강제 종료하고 다시 켜도 → 같은 결과
- 기기 시계를 앞으로 돌려도 → 같은 결과 (시간만 빨리 흐를 뿐)
- 세이브 파일을 백업했다가 되돌려도 → 같은 결과

**치팅을 막는 게 아니라 치팅의 이득을 없앤다.** 모바일에서 로컬 세이브 암호화는
어차피 뚫린다. 뚫어도 얻을 게 없게 만드는 쪽이 싸고 확실하다.

### System.Random 을 쓰지 않는 이유

.NET 구현체마다 알고리즘이 다르고 실제로 .NET Core 에서 한 번 바뀌었다.
같은 시드로 같은 결과가 나온다는 보장이 문서화돼 있지 않다.
"출발 시점 시드로 나중에 결과를 재현한다"가 이 게임의 핵심 규칙이므로,
난수 알고리즘이 흔들리면 세이브가 깨진다. **직접 구현한다** (xorshift32).

### 용도별 스트림 분리

```
Rng.For(seed, "loot")      전리품
Rng.For(seed, "accident")  사고 판정
Rng.For(seed, "combat")    전투
```

하나의 스트림을 공유하면, 나중에 "전투 판정을 한 번 더 굴리자" 같은 사소한 변경이
뒤따르는 **모든** 결과를 바꿔버린다. 용도를 갈라두면 한쪽을 고쳐도 다른 쪽이 유지된다.

---

## 5. 시계 조작

| 상황 | 처리 |
|---|---|
| 시계가 **앞으로** | 정상 정산. 시드 고정 때문에 이득이 없다 |
| 시계가 **뒤로** | 진행 없음. 횟수만 기록하고 **처벌하지 않는다** |

되돌림을 처벌하지 않는 이유: 타임존 변경, 기기 시각 자동 보정, 해외 출장 같은
정상적인 이유가 치팅보다 훨씬 많다. 어차피 얻을 게 없으므로 조용히 넘긴다.

---

## 6. 날짜 경계는 KST 새벽 5시

```
UTC 자정  → KST 오전 9시. 출근길에 의뢰가 바뀐다.
KST 자정  → 밤에 플레이하던 사람 눈앞에서 의뢰가 사라진다.
KST 05:00 → 거의 아무도 플레이하지 않는 시각.
```

**기기 로컬 타임존을 쓰지 않는다.** 타임존을 넘나들면 의뢰를 두 번 받을 수 있다.
게임 시간대는 UTC+9 하나로 고정하고 서머타임도 없다.

---

## 7. 멱등성

같은 세이브에 정산을 두 번 돌려도 결과가 같아야 한다. 모든 사건은 소비 표시를 갖는다.

| 사건 | 소비 표시 |
|---|---|
| 파견 복귀 | `ExpeditionState.Resolved` |
| 제작 완료 | `CraftJob.Collected` |
| 날짜 갱신 | `QuestState.ActiveGameDate` |

수집 단계에서 이미 소비된 것은 내보내지 않고, 적용 단계에서 한 번 더 확인한다(이중 안전장치).
정산 중 앱이 죽어도 중복 지급이 없다.

### `SavedAt` 은 정산기만 옮긴다

`GameSave.SavedAt` = "마지막으로 정산이 끝난 시각" = 다음 정산 구간의 시작점. **`OfflineResolver` 말고는
아무도 쓰지 않는다.** 저장(`Commit`/`Suspend`)이 이걸 now 로 밀면 그 사이의 사건 중 **날짜 경계**가
정산되지 않은 채 사라진다 (파견·제작은 구간 시작으로 당겨져 살아남지만, 날짜 경계는 구간 밖이면 생성조차 안 된다).
0.2 코드에 실제로 있던 버그다 — 앱을 켠 채 새벽 5시를 넘기면 그날 의뢰가 갱신되지 않았다.

앱이 켜져 있는 동안은 `Bootstrap` 이 몇 초마다 `GameSession.Tick()` 을 부른다. `Resume()` 과 같은 정산이고,
아무 일도 없었으면 디스크에 쓰지 않는다. 플레이어 조작(`Sell` / `Depart` …)도 먼저 `Tick()` 으로
현재까지 따라잡은 뒤에 실행한다.

---

## 8. 전부 되거나 전부 안 되거나 — 어디에 적용하는가

| 동작 | 정책 | 이유 |
|---|---|---|
| 창고에 **넣기** | 부분 허용 | 전리품이 넘칠 때 "하나도 못 받음"은 가혹하다. 넘친 건 리포트로 알린다 |
| 창고에서 **빼기** | 전부 아니면 무 | 차감은 납품·발송·제작처럼 대가가 오가는 지점에서만 일어난다. 부분 차감은 "재료는 사라졌는데 결과물은 없는" 상태를 만든다 |
| 제작 큐 등록 | 전부 아니면 무 | 위와 같음 |
| 본편 발송 | 전부 아니면 무 | 아이템 복제를 절대 막아야 한다 (LINK_CONTRACT.md) |

---

## 9. ResolveReport — UI 가 역산하지 않게

정산 결과를 리포트 객체로 돌려준다. 홈 화면의 "복귀 보고서"가 이걸 그대로 그린다.

리포트가 없으면 UI 가 세이브를 뒤져서 "뭐가 바뀌었지?"를 역산해야 하고,
그 순간 UI 가 도메인 규칙을 중복 구현하기 시작한다. 그 중복은 반드시 어긋난다.

---

## 10. 검증

### 지금 통과한 것

`Tools/sim_model.py` — C# 과 같은 알고리즘의 **Python 참조 구현**. 15개 시나리오 전부 통과.

```
결정론 / 멱등성 / 시계 되돌림 / 시계 앞당김 무이득 / 시간 순 정산 /
동시각 tie-break / KST 05:00 경계 / 일일 의뢰 안정성 / 납품 원자성 /
태그 차감 우선순위 / 창고 넘침 / 장기 미접속 / 공장 오프라인 상한 /
난수 스트림 분리 / 의뢰 설계 규칙
```

이 모델은 밸런스 샌드박스로도 계속 쓴다 — Unity 를 켜지 않고 파견 수천 회를 돌려
손익 분포를 본다 (`--balance`).

### 컴파일 전에 잡은 것

실제로 Unity 에 얹기 전에 두 가지가 걸렸다. 둘 다 컴파일을 막았을 문제다.

1. **`DateOnly` 사용** — .NET 6 에서 추가된 타입이라 Unity 의 .NET Standard 2.1 API 레벨에 없다.
   `GameDate` 구조체로 교체했다. 파이썬 모델의 `date.toordinal()` 과 같은 일련번호를 내도록 맞췄다.
2. **어셈블리 순환 참조** — 위 §2 참조. 4개 분할 계획을 2개로 접었다.

### Unity 에서 통과한 것 (2026-09-11)

첫 컴파일에서 `error CS` 0건. `OfflineResolverTests` 15/15 — Python 시나리오와 같은 답을 낸다.
이후 `SessionTests`(세이브 왕복·세션) 와 `DataFileTests`(실제 JSON 정합성·밸런스 공식·로케일) 를 더해 **31/31**.

> 두 구현 중 하나를 고치면 반드시 다른 쪽도 고친다.
> 어긋나는 순간 Python 모델의 검증이 통째로 무의미해진다.

---

## 11. 이 아키텍처가 하지 않는 것

과한 추상화를 피하기 위해 **일부러 안 넣은 것들**이다. 나중에 "왜 없지?" 할 때 보라.

| 안 넣은 것 | 이유 |
|---|---|
| `DateOnly` | .NET 6 타입이라 Unity 의 .NET Standard 2.1 에 없다. 작은 `GameDate` 구조체를 직접 만들었다 |
| 범용 EventBus | 문자열 키 이벤트는 타입 안전성이 없고 추적이 안 된다. 필요하면 구체적인 C# `event` 를 추가한다 |
| DI 컨테이너 | 생성자 주입으로 충분하다. 객체가 열 개도 안 된다 |
| ECS / 상태 머신 프레임워크 | 이 게임은 프레임마다 도는 시뮬레이션이 아니다. 사건이 드물고 크다 |
| Repository 패턴 | `SaveService` 하나면 된다 |
| 세이브 암호화 | 시드 고정으로 치팅 이득을 없앴다. 난독화는 Phase 7 |
| 커맨드 패턴 / undo | 되돌릴 일이 없다 |

새 추상화를 넣고 싶을 때의 기준: **지금 당장 두 번째 구현체가 있는가?**
`IClock`(실제/테스트)과 `IDataRegistry`(JSON/테스트)만 통과했다.

---

## 12. 파일 위치

```
Assets/Game/AfterSeoul.Game.asmdef   ← noEngineReferences: true

Assets/Game/Core/
  IClock.cs          시간의 유일한 통로
  GameTime.cs        KST 05:00 날짜 경계
  Rng.cs             결정론적 난수 + 스트림 분리
  GameSave.cs        상태 루트 (여기 밖에 변경 가능 상태 없음)
  SaveService.cs     원자적 저장, 깨진 세이브 복구
  Timeline.cs        TimedEvent / EventOrder / ITimelineSystem
  OfflineResolver.cs 수집 → 정렬 → 적용
  ResolveReport.cs   정산 결과 (UI 가 그린다)
  IDataRegistry.cs   정적 데이터 조회 + BalanceDef
  JsonDataRegistry.cs  IDataRegistry 의 JSON 구현 (텍스트만 받는다)
  NewtonsoftJsonCodec.cs  세이브 직렬화
  Loc.cs             로케일 조회 (본편 Loc.Get 과 같은 모양)
  GameSession.cs     단일 진입점 + 플레이어 조작

Assets/Game/Inventory/Warehouse.cs
Assets/Game/Inventory/Market.cs
Assets/Game/Expedition/ExpeditionSystem.cs
Assets/Game/Factory/FactorySystem.cs      제작 큐 + 직접 노동
Assets/Game/Quest/DailyQuestSystem.cs

Assets/Unity/AfterSeoul.Unity.asmdef
Assets/Unity/Bootstrap.cs                 세션 부팅, 앱 수명, Unity I/O
Assets/link.xml                           IL2CPP 스트리핑 방지

Assets/Tests/EditMode/OfflineResolverTests.cs   정산 엔진 (sim_model.py 와 1:1)
Assets/Tests/EditMode/SessionAndDataTests.cs    세이브·세션·실제 데이터
Assets/Tests/EditMode/AfterSeoul.Tests.asmdef
Tools/sim_model.py
Tools/check_data.py                       데이터 검산 (DataFileTests 와 같은 규칙)
```
