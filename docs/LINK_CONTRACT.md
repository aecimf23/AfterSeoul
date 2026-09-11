# 본편 ↔ 모바일 연동 데이터 계약

문서 버전: 0.1 / 상태: **설계 확정 대기** (Phase 5 착수 전 리뷰 필수)

이 문서는 `AFTER SEOUL`(모바일) 과 `ESCAPE FROM SEOUL`(PC) 사이에 오가는 **데이터의 형태와 규칙**만 정의한다.
실제 Cloud Save 연결 구현은 Phase 5. 지금 단계의 목적은 **양쪽이 서로를 기다리지 않고 개발할 수 있게 계약을 먼저 못 박는 것**이다.

---

## 0. 설계 대전제

| # | 원칙 |
|---|---|
| 1 | **모바일은 본편 세이브 파일을 절대 읽거나 쓰지 않는다.** 본편 세이브는 AES+HMAC 로 봉인돼 있고, 반영은 본편 클라이언트만 한다 |
| 2 | 자체 WAS/DB 없음. Unity Authentication + Cloud Save 만 사용 |
| 3 | Cloud Save 는 **전체 세이브 저장소가 아니라 우체통**이다 |
| 4 | **화폐는 절대 이전되지 않는다.** 아이템만 오간다 |
| 5 | 양쪽 클라이언트가 **각각 독립적으로** validation 한다. 한쪽을 믿지 않는다 |
| 6 | 과금 여부는 전송 한도에 **아무 영향이 없다** |
| 7 | 연동이 아예 없어도 양쪽 게임은 완전히 동작해야 한다 |

---

## 1. Cloud Save 키 레이아웃

플레이어 1인당 Cloud Save 아래 키만 사용한다.

| 키 | 소유(쓰기) | 읽기 | 용도 |
|---|---|---|---|
| `link_profile` | 양쪽 | 양쪽 | 연결 메타데이터, 스키마 버전 |
| `box_m2p` | 모바일 | 양쪽 | 모바일 → PC 배송함 |
| `box_p2m` | PC | 양쪽 | PC → 모바일 배송함 |
| `quota` | 양쪽 | 양쪽 | 전송 한도 카운터 |
| `shared_flags` | 양쪽 | 양쪽 | 월드 플래그 / 해금 상태 (Phase 6+) |

> 쓰기 소유자가 아닌 쪽은 해당 키를 **수정하지 않는다.**
> 예외: 수령 확인(ack)은 수령자가 자기 쪽 `*_ack` 배열에만 append 한다 (§4).

---

## 2. `link_profile`

```json
{
  "schemaVersion": 1,
  "linkedAt": "2026-09-11T04:12:00Z",
  "mobilePlayerId": "as_9f3c21",
  "pcPlayerId": "efs_4a77b0",
  "mobileEmployerNpcId": "HWANG",
  "lastMobileSyncAt": "2026-09-11T04:12:00Z",
  "lastPcSyncAt": "2026-09-10T22:40:11Z"
}
```

`schemaVersion` 불일치 시: **연동 기능만 비활성화하고 양쪽 게임은 정상 진행한다.** 절대 실패로 게임을 막지 않는다.

---

## 3. `box_m2p` — 모바일 → PC 배송함

```json
{
  "schemaVersion": 1,
  "shipments": [
    {
      "txId": "m2p_20260911_a7f3e9c1",
      "createdAt": "2026-09-11T04:12:00Z",
      "senderLabel": "서울 회수팀 R-04",
      "sourceMapId": "GURO_FACTORY",
      "items": [
        { "itemId": "MED16", "count": 2 },
        { "itemId": "BAT01", "count": 1 }
      ],
      "checksum": "sha256:...",
      "status": "pending"
    }
  ],
  "ackedTxIds": ["m2p_20260908_31bd0a44"]
}
```

### 필드 규칙

| 필드 | 규칙 |
|---|---|
| `txId` | `m2p_{YYYYMMDD}_{8자리 hex}`. **전역 고유. 재사용 절대 금지** |
| `senderLabel` | 표시용 문자열. 본편에서 우편 발신자로 노출 |
| `sourceMapId` | 본편 `Map.Id` 중 하나. 연출용 |
| `items[].itemId` | 본편 `ItemDatabase` 에 **실존하는** id 여야 함 |
| `items[].count` | 1 이상, 아이템별 상한 이하 (§5) |
| `checksum` | `sha256(txId + 정렬된 items 직렬화 + schemaVersion)` |
| `status` | `pending` → `claimed` → (보관) |
| `ackedTxIds` | PC 가 수령 완료한 txId. **PC 만 append** |

### 배송함 크기 제한

- `shipments` 최대 **20건**. 초과 시 모바일에서 신규 발송 차단 (본편에서 수령하라고 안내)
- `ackedTxIds` 최대 **200건**. 초과 시 오래된 것부터 제거 (단, `shipments` 에 남아 있는 txId 는 제거 금지)

---

## 4. 수령(ack) 프로토콜

**단순함이 최우선이다. 분산 트랜잭션을 흉내내지 않는다.**

```
[PC 본편]
1. box_m2p 읽기
2. status == "pending" 이고 txId ∉ ackedTxIds 인 shipment 목록 추출
3. 각 shipment 를 validation (§5)
4. 통과분만 하이드아웃 우편함 UI 에 표시
5. 플레이어가 [모두 수령] 클릭
6. 스태시 빈칸 확보 확인 → 아이템 삽입 → SaveManager.SaveProfile()   ← 로컬 확정 먼저
7. 로컬 저장 성공 후에만 ackedTxIds 에 txId append + status = "claimed" 로 Cloud Save 쓰기
8. 7번 쓰기가 실패하면? → 다음 실행 때 재시도. 6번은 이미 끝났으므로 로컬에 중복 수령 방지 기록을 별도로 남긴다
```

### 중복 수령 방지 — 이중 장치

| 장치 | 위치 | 역할 |
|---|---|---|
| `ackedTxIds` | Cloud Save | 1차 방어. 기기 간 공유 |
| `PlayerProfile.ClaimedMailTxIds` | **본편 로컬 세이브** | 최종 방어. 클라우드 쓰기 실패 시에도 중복 수령 차단 |

> **`ClaimedMailTxIds` 는 본편 `PlayerProfile` 에 신규 추가해야 하는 필드다.** → §6 변경 요청 목록 참조.
> 두 장치 중 하나라도 해당 txId 를 알고 있으면 수령하지 않는다.

### 스태시 공간 부족

빈칸이 모자라면 **일부만 수령하지 않는다.** shipment 단위로 전부 받거나 전부 남긴다.
UI 메시지: `"창고 공간이 부족합니다. (필요 N칸)"` — shipment 는 `pending` 유지.

---

## 5. Validation 규칙

### 5-1. 모바일 발송 시 (보내기 전)

| # | 검사 | 실패 시 |
|---|---|---|
| V1 | `itemId` 가 전송 허용 목록(`transferable_items.json`)에 있는가 | 발송 UI 에 노출하지 않음 |
| V2 | `itemId` 가 `AS_` 접두사가 아닌가 | 차단 |
| V3 | 플레이어 창고에 해당 수량이 실제로 있는가 | 차단 |
| V4 | 아이템별 1회 상한 이하인가 | 수량 제한 |
| V5 | 일일 전송 한도 잔량이 충분한가 | 차단 + 잔량 표시 |
| V6 | `shipments` 가 20건 미만인가 | 차단 + 안내 |
| V7 | 발송 즉시 모바일 창고에서 아이템을 **차감**했는가 | 차감 없으면 발송 취소 |

### 5-2. PC 수령 시 (받기 전) — 모바일을 믿지 않는다

| # | 검사 | 실패 시 |
|---|---|---|
| P1 | `schemaVersion` 이 지원 범위인가 | shipment 무시 (에러 아님) |
| P2 | `txId` 형식이 올바른가 | 폐기 |
| P3 | `txId` 가 `ackedTxIds` 또는 `ClaimedMailTxIds` 에 없는가 | 폐기 (중복) |
| P4 | `checksum` 이 일치하는가 | 폐기 + 로그 |
| P5 | 모든 `itemId` 가 `ItemDatabase.GetItemById` 로 조회되는가 | 해당 아이템만 제외 |
| P6 | 모든 `itemId` 가 전송 허용 목록에 있는가 | 해당 아이템만 제외 |
| P7 | 각 `count` 가 1 이상, 아이템별 상한 이하인가 | 상한으로 clamp |
| P8 | shipment 총 가치(`Item.Price` 합)가 상한 이하인가 | 폐기 + 로그 |
| P9 | `createdAt` 이 미래가 아니고 90일 이내인가 | 폐기 |
| P10 | 오늘 수령한 총량이 일일 한도 이하인가 | 다음 날로 보류 |

> **P4~P8 실패는 조용히 폐기하고 로그만 남긴다.** 플레이어에게 "변조가 감지되었습니다" 같은 메시지를 띄우지 않는다 — 대부분은 변조가 아니라 버전 불일치다.

### 5-3. 전송 허용 목록

정본 파일: **`Assets/StreamingAssets/Data/transferable_items.json`**
(`Tools/extract_mainline_data.py` 로 뽑은 본편 실측 가격 기반. 양쪽 리포지토리에 **동일 사본**을 둔다)

2026-09-11 본편 `ItemDatabase.cs` 실측으로 확정한 수치:

| 항목 | 값 | 근거 |
|---|---|---|
| `unitPriceCeiling` | **15,000원** | 이 위로는 고가 소모품(살레와 15,000 통과 / GoldStar 85,000·문샤인 250,000·.50AE 268,000 차단) |
| `maxShipmentValue` | **25,000원** | 본편 퀘스트 보상 중앙값 40,000원의 약 0.6배 |
| `maxDailyValue` | **50,000원** | 하루치가 본편 퀘스트 1.25건 수준. 보급품이지 수입원이 아니다 |
| `maxShipmentsPerDay` | 2 | |
| `maxItemStacksPerShipment` | 4 | |

허용 카테고리와 개수 상한: `MED` 3/6, `FOOD` 5/10, `AMO` 60/120, `JUNK` 4/8 (배송당/일일).
그 외 접두사(`WPN` `AMR` `HDW` `BPK` `RIG` `OPT` `NVG` `ATT` `EAR` `MEL` `GND` `KEY` `KEYCARD` `QUEST` `CASE` `COSM` `UIJ` `TRK` `VAC` `XTG` `AS`)는 전부 `allowed: false`.

**결과: 52종이 전송 가능.** 예시 배송 —
`볼트 ×1 (15,000) + 군용 붕대 ×2 (9,000) = 24,000원` / `5.45x39 PS ×16 = 24,000원`

### 5-3-1. ⚠ 개수 상한은 방어선이 아니다

실측으로 드러난 사실: 카테고리 개수 상한만으로는 **한 스택이 900,000원까지 나온다.**
탄약 단가가 900원(12게이지 벅샷)에서 268,000원(.50 AE FMJ)까지 **300배** 차이 나기 때문이다.
`AMO` 60발 상한은 싼 탄에는 넉넉하고 비싼 탄에는 무의미하다.

따라서 **값 상한(`maxShipmentValue` / `maxDailyValue`)이 유일한 실질 방어선이다.**
개수 상한과 단가 상한은 UX 편의와 이상치 차단용 보조 장치로만 취급한다.

구현 시: 값 상한 검사를 건너뛰는 경로가 하나라도 있으면 정책 전체가 무력화된다.
`P8`(shipment 총 가치)과 `P10`(일일 누적)은 **생략 가능한 검사가 아니다.**

### 5-3-2. 새 아이템이 추가될 때

본편에 비싼 소모품이 추가되면 `unitPriceCeiling` 덕분에 자동으로 차단된다.
단, **추출 스크립트를 다시 돌려 `allowlistSnapshot` 을 갱신하고 diff 를 리뷰해야 한다.**
스냅샷에 없던 id 가 들어오면 CI 에서 경고를 띄운다.

### 5-4. 허용 정책의 근거

**보내도 되는 것 = 본편에서 "사면 그만"인 소모품.**
**보내면 안 되는 것 = 본편에서 획득 자체가 도전인 것.**

| 분류 | 판단 |
|---|---|
| `MED` `FOOD` `AMO` | 허용. 소모품이고 본편 상점에서 살 수 있다. 편의성만 준다 |
| `JUNK` | 제한적 허용. 하이드아웃 제작 재료. 단 아래 표의 23종은 개별 차단 |
| `WPN` `AMR` `HDW` `BPK` `RIG` `OPT` `NVG` `ATT` `EAR` `MEL` | **전면 금지.** 장비는 본편 성장의 핵심이다. 모바일로 우회하면 게임이 망가진다 |
| `KEY` `KEYCARD` `QUEST` | **전면 금지.** 진행도 스킵이 된다 |
| `GND` `CASE` `COSM` `TRK` `VAC` `XTG` | 금지. 밸런스·의미 훼손 |
| `UIJ` `AS` | 금지. 컨텍스트가 다른 아이템 |

`JUNK` 개별 차단 근거 (전체 목록은 [MAINLINE_REFERENCE.md §3](MAINLINE_REFERENCE.md#3-아이템-재활용-확정)):

| 차단군 | id | 이유 |
|---|---|---|
| 고가 환금성 | `JUNK01`(그래픽 카드) `JUNK02`(LedX) `JUNK05`(테트리즈) `JUNK06`(실물 비트코인) `JUNK13`(금 해골 반지) `JUNK14`(롤러 시계) `JUNK33`(AESA) `JUNK39`(은 목걸이) `JUNK40`(말 동상) | 사실상 현금. 본편 경제를 직접 깬다 |
| 열쇠/진행도 | `JUNK11`(검정 연구 키카드) `JUNK12`(마크드 방 열쇠) | 진행도 스킵 |
| 하이드아웃 고급 모듈 재료 | `JUNK08`(전기 모터) `JUNK21`(자동차 배터리) `JUNK24`(CPU) `JUNK26`(파워 서플라이) `JUNK29`(공구 세트) `JUNK30`(가스 분석기) `JUNK31`(가이거 계수기) | 하이드아웃 업그레이드가 본편 중기 목표다. 우회 금지 |
| 방탄/특수 원단 | `JUNK37`(아라미드) `JUNK38`(방탄 원단) | 장비 제작 재료 = 사실상 장비 |
| 기타 | `BAT01`(NVG 배터리) `DOGTAG` `INJECTOR_CASE` | 야시경 전용 / 전리품 증표 / 특수 케이스 |

허용되는 `JUNK` 15종: `JUNK03`(볼트) `JUNK04`(나사못) `JUNK15`(너트) `JUNK16`(금속 여분 부품)
`JUNK17~19`(테이프류) `JUNK20`(전선) `JUNK22`(스파크 플러그) `JUNK23`(PCB) `JUNK25`(CPU 쿨링 팬)
`JUNK28`(WD-40) `JUNK34`(립스탑) `JUNK35`(플리스) `JUNK_CIG` `JUNK_LIGHTER`

## 6. 본편 변경 요청 목록

본편 리포지토리를 건드려야 하는 항목. **AFTER SEOUL 작업 중 임의로 수정하지 말고 여기에 기록 후 별도 작업으로 처리한다.**

| # | 대상 | 변경 | 사유 | 상태 |
|---|---|---|---|---|
| M1 | `Models.cs` `PlayerProfile` | `List<string> ClaimedMailTxIds` 필드 추가 | 중복 수령 최종 방어 | 대기 |
| M2 | `HideoutSystem.cs:1049` `TryAddToStash` | `private` → `internal` 승격 | 우편 수령이 스태시 삽입 로직을 재사용해야 함. 두 벌 두면 반드시 어긋난다 | 대기 |
| M3 | `SeoulLogic/` 신규 | `MobileLinkService.cs` — Cloud Save 읽기/검증/수령 | 우편함 본체 | 대기 |
| M4 | `Program.Runtime.Hideout.cs` | 하이드아웃 메뉴에 `[우편함]` 항목 추가 | 진입점 | 대기 |
| M5 | `Program.Views.Hideout.cs` | 우편함 화면 렌더 | UI | 대기 |
| M6 | `Models.cs` `GameState` | `HideoutMailbox` 상태 추가 | 화면 전환 | 대기 |
| M7 | `Resources/Locales/*.json` | `MAIL_*` 키 5개 국어 추가 | 텍스트 | 대기 |
| M8 | `Packages/manifest.json` | UGS Authentication + Cloud Save 패키지 | 의존성 | 대기 |
| M9 | 타이틀/로비 | `"하이드아웃에 도착한 우편이 있습니다!"` 알림 | 연출 | 대기 |

> `SaveEnvelope.Version` 은 **2 를 유지한다.** `ClaimedMailTxIds` 는 추가 필드이므로 Newtonsoft 역직렬화에서 구 세이브는 null → 빈 리스트로 초기화하면 된다. 버전을 올릴 필요 없음.

---

## 7. `box_p2m` — PC → 모바일 (Phase 6 이후)

MVP 범위 밖. 계약만 미리 잡아둔다.

```json
{
  "schemaVersion": 1,
  "shipments": [
    {
      "txId": "p2m_20260911_5c81d0a2",
      "createdAt": "2026-09-11T05:00:00Z",
      "kind": "unlock",
      "payload": { "unlockMapId": "SUYU_DONG" },
      "status": "pending"
    }
  ],
  "ackedTxIds": []
}
```

`kind` 후보: `unlock`(모바일 지역 해금) / `intel`(탐색 정보) / `item`(아이템 — **당분간 사용 안 함**)

> **PC → 모바일은 아이템을 보내지 않는다.** 모바일 경제가 본편 부자에게 종속되면 안 된다.
> 보내는 것은 **해금과 정보**뿐.

---

## 8. 실패 모드와 대응

| 상황 | 대응 |
|---|---|
| Cloud Save 접속 불가 | 양쪽 게임 정상 진행. 연동 UI 만 "오프라인" 표시 |
| 연동 미설정 | 모바일: 창고에 `[본편 발송]` 버튼 자체를 숨김. PC: 우편함 메뉴 숨김 |
| `schemaVersion` 상위 | 연동만 비활성 + "앱을 업데이트하세요" 안내 |
| 모바일 발송 후 창고 차감 실패 | 발송 롤백. **아이템 복제는 무슨 일이 있어도 막는다** |
| PC 수령 중 크래시 | 로컬 저장이 끝났으면 `ClaimedMailTxIds` 에 있으므로 재수령 없음. 끝나기 전이면 shipment 는 `pending` 이므로 재시도 가능 |
| 동일 계정 다중 기기 | Cloud Save 낙관적 동시성(버전 충돌 시 재읽기 후 재시도). 충돌 3회 초과 시 포기하고 다음 실행에 재시도 |

---

## 9. Phase 5 착수 전 체크리스트

- [x] 본편 `Packages/manifest.json` UGS 확인 — `com.unity.services.authentication` **3.7.4 설치됨**, **`cloudsave` 없음** (M8 로 추가)
- [x] 본편 `ProjectSettings` 회사명/제품명 — `DefaultCompany` / `EscapeFromSeoul`
- [x] `transferable_items.json` 을 본편 실제 `JUNK01~40` 목록으로 재검토
- [x] `denyItemIds` 에 넣을 고가 JUNK 목록 확정
- [x] `JUNK` 허용분 각각의 `Item.Price` 실측 → 허용 52종 확정, `transferable_items.json` 생성
- [x] `maxShipmentValue` 25,000 / `maxDailyValue` 50,000 확정 (퀘스트 보상 중앙값 40,000원 기준)
- [ ] 본편 **실제 플레이**로 레이드 1회 평균 수익 측정 → 위 수치 재검토
      (현재는 퀘스트 보상 분포를 대리 지표로 썼다. 레이드 수익이 훨씬 크면 한도를 올려도 된다)
- [ ] M1~M9 변경 요청을 본편 작업 브랜치로 분리
- [ ] 아이템 복제 시나리오 테스트 케이스 작성 (발송 중 앱 강제종료, 네트워크 끊김, 동시 발송)
