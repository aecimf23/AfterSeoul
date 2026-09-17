# AFTER SEOUL

2026-09-16: 공장을 생산/설비로 구분하고 용산킴 총기 제작 클리커를 추가했다. 본편 총기 25종 이미지, 시제품 의뢰 납품에 따른 정식 제작 해금, 제작 장비 강화, 대기 스캐브 배치와 오프라인 작업비를 지원한다. 보유금 조건으로 시제품 의뢰를 받고 목표 수량을 납품해야 보수와 제작 승인을 받는다. 부품 공급·재료 선별·동력 공구·추가 작업대에 투자하여 공급량·재료 등급·자동 생산·배치 한도를 높인다. 중앙 구간의 부품 이미지을 타이밍에 맞춰 조립하며 연타로는 진행할 수 없다. 플레이어와 스캐브 최대 3명의 일러스트 작업대·이름·망치 동작과 타격음, 자동 생산률·완성 예정 시간을 표시한다. 공장 전용 본편 배경음과 고용주별 메인 화면 인사·대사음을 지원한다. 첫 총기 납품으로 첫 동료 계약금 지원이 열린다. 기존 부품 미니게임은 설비 공장 안에 유지한다. 상단 NPC 터치 대사와 45초 방치 대사, 확인 후 튜토리얼로 돌아가는 진행 초기화를 지원한다. [생산 공장 설계·검증](docs/FACTORY_PRODUCTION_DESIGN.md).

2026-09-16: 무료 회수 실습 1회 → 고철 1개 판매 → 첫 1티어 스캐브 계약금 전액 지원을 추가했다. 미니게임 시작 버튼·선택 직후 미리보기·배송망 첫 누름을 수정했다. 기존 초반 저장 지원과 검증 근거는 [STARTER_SUPPORT.md](docs/STARTER_SUPPORT.md).

2026-09-16: 같은 계정 로그인 기반 본편 우편함 연동 코드를 추가했다. 실제 서비스는 Unity 설정/Cloud Code 배포/알파 계정 초기화 전까지 비활성이다. 본편 `docs/mobile-link/SETUP.md`에 절차와 검증 한계를 기록했다. 새 APK는 아직 생성하지 않았다.

`ESCAPE FROM SEOUL` (PC, Unity 6) 세계관을 공유하는 **비동기 탐색 / 경영 / 클리커 모바일 게임**.

- 엔진: Unity 6 (본편과 동일 LTS 라인 — 6.3)
- 우선 플랫폼: Android, 이후 iOS
- 개발 규모: 1인
- 리포지토리: `D:\singleProject\AfterSeoul` (본편 `D:\singleProject\EscapeFromSeoul` 와 분리)

한 문장 요약:

> ESCAPE FROM SEOUL의 주요 NPC 밑에서 말단 팀장으로 시작해 직접 노동으로 돈을 벌고, 스캐브 조직을 키워 서울 곳곳을 수색하며, 회수한 물자를 조직 성장에 쓸지 본편의 자신에게 보낼지를 선택하는 비동기 모바일 경영·탐색 게임.

---

## 문서

| 문서 | 내용 |
|---|---|
| [docs/GDD.md](docs/GDD.md) | 게임 기획서 (본편 실제 데이터로 보강한 정본) |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | **코어 아키텍처 — 오프라인 정산 엔진. 코드 쓰기 전에 읽을 것** |
| [docs/WORK_PLAN.md](docs/WORK_PLAN.md) | Phase 1~7 작업계획서 + Vertical Slice 태스크 분해 |
| [docs/DATA_SCHEMA.md](docs/DATA_SCHEMA.md) | MVP 데이터 스키마 (아이템/지역/스캐브/탐색/세이브) |
| [docs/LINK_CONTRACT.md](docs/LINK_CONTRACT.md) | 본편 ↔ 모바일 연동 데이터 계약 + validation 규칙 |
| [docs/MAINLINE_REFERENCE.md](docs/MAINLINE_REFERENCE.md) | 본편 코드 조사 결과 (재활용 가능한 자산 목록) |

## 지금 상태

**모바일 내부 알파 — 핵심 루프 구현 및 UI·오디오 보강** (2026-09-15).

- Unity 6000.3.24f1 프로젝트. 최신 검증 결과는 [알파 검증 기록](docs/ALPHA_VALIDATION.md) 참조.
- `Assets/Game/**` — 코어 정산 엔진 + 공장/파견/일일의뢰/창고/판매 + JSON 데이터 로더 + 로케일 (UnityEngine 비의존)
- `Assets/Unity/Bootstrap.cs` — 세션 부팅·앱 수명 처리 (씬 배치 불필요)
- `Assets/StreamingAssets/Data/*.json` — 본편 추출 4종 + 직접 설계 6종 (파견/전리품/레시피/의뢰/스캐브/밸런스)
- `Tools/sim_model.py` (참조 구현) / `Tools/check_data.py` (데이터 검산) / `Tools/extract_mainline_data.py` (본편 추출)

### 설정과 테마

오른쪽 위 **설정**에서 서울의 밤·군용 단말기·낡은 피난처를 선택한다. 효과음/음악 슬라이더, 개별 음소거, 연출 줄이기와 설정 초기화를 지원한다. 선택은 재실행 후 유지되며 게임 진행은 보존한다. [구현·검증 기록](docs/SETTINGS_THEMES.md).

### 이번 변경

- 원작 D2Coding 한글 폰트 포함, 청록·군용 녹색의 단말기 UI, 서울 개략도와 고용주 인사기록 카드.
- 원작 버튼·확인·판매 효과음, 기지 배경 음악, 개별 음량 저장과 오디오 리스너 처리.
- 고용주 선택 전 탭 진입 차단, 모든 고용주·5탭에 대한 첫 실행 회귀 검증.
- 팀 인원과 탐색 능력이 회수량에 반영되도록 경제 보정. 측정 조건과 한계는 [경제 검증](docs/economy-validation.md) 참조.
- 데이터·테스트의 누락된 무기, 의뢰 대사, 보상 공식과 진행 조건 정리.

### 다음 검증

- Android 실기기에서 터치·한글·음량·알림·백그라운드 복귀 확인.
- 첫 30분 실제 플레이에서 노동 횟수, 첫 고용·복귀 시간, 납품 흐름 확인.
- 장기 경제는 창고 포화와 장비 손실까지 포함해 계속 조정.
- PC 실제 연동과 결제·광고 SDK는 미연결. 개발용 연결은 실제 배송·결제가 아니다.
- 회사명/패키지명 확정 및 스토어 등록은 별도 단계.

Android 빌드: Unity 메뉴 `After Seoul → Build Android Alpha`.
출력: `Builds/Android/AfterSeoul-alpha.apk` (개발용, 스토어 업로드용 아님).

### 테스트 실행

```
"C:\Program Files\Unity\Hub\Editor\6000.3.24f1\Editor\Unity.exe" -batchmode -projectPath . ^
  -runTests -testPlatform EditMode -testResults Logs\test_results.xml -logFile Logs\test_run.log
```

에디터가 이 프로젝트를 열고 있으면 batchmode 가 실패한다. 에디터 안에서는 Window → General → Test Runner.

## AI 작업자 필독

`docs/GDD.md` §20 (AI 개발 작업 시 필수 지침) 과 `docs/LINK_CONTRACT.md` 를 먼저 읽을 것.
특히 **모바일 → PC 아이템 전송은 반드시 별도 validation 계층을 거친다**.

## 초도 보급 개선 (2026-09-15)
첫 고용 이후 3분 무료 안전 파견 → 꾸러미 납품 50,000원 → 다음 장비 준비 흐름을 추가했다.
기존 진행 저장에는 보호를 소급 적용하지 않는다. 상세 규칙과 백그라운드 검증 결과: [FIRST_EXPEDITION.md](docs/FIRST_EXPEDITION.md).
이번 변경 뒤 새 APK와 화면 렌더링 검증은 아직 수행하지 않았다.

## 첫 임무 이후 성장 안내 (2026-09-15)
기지에 장비 준비 → 일반 파견 → 의뢰 납품 → 다음 지역 해금 안내를 연결했다.

탐색은 지역 지도 → 상세 정보 → 인원 편성·파견 순서로 진행한다. 잠긴 지역은 지도에서 숨기고, 파견 중인 지역 아래에는 가장 빠른 복귀 시각과 남은 시간을 표시한다. 해당 지역 상세에서는 팀별 인원·진행률을 확인한다. [탐색 화면 개편](docs/EXPEDITION_NAVIGATION.md) 참조.
Unity 테스트 403개와 실제 UI 화면 9장 검증 완료. 상세 내용: [GROWTH_GUIDANCE.md](docs/GROWTH_GUIDANCE.md).

### 최신 APK 갱신
성장 안내까지 포함한 Android 개발 APK 빌드 완료 (오류 0, 경고 0, 약 56.7 MB).
Builds/Android/AfterSeoul-alpha.apk에서 확인할 수 있다. 위 초도 보급 작업 당시의 APK 미생성 기록은 [후속 검증](docs/GROWTH_GUIDANCE.md)으로 대체한다.
