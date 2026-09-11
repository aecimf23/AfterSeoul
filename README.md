# AFTER SEOUL

`ESCAPE FROM SEOUL` (PC, Unity 6) 세계관을 공유하는 **비동기 탐색 / 경영 / 클리커 모바일 게임**.

- 엔진: Unity 6 (본편과 동일 LTS 라인 — 6.3)
- 우선 플랫폼: Android, 이후 iOS
- 개발 규모: 1인
- 리포지토리: `D:\devSource\AfterSeoul` (본편 `D:\devSource\EscapeFromSeoul` 와 분리)

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

**P0 거의 완료 / P1 은 UI 를 뺀 전부 완료** (2026-09-11). 상세는 [WORK_PLAN.md](docs/WORK_PLAN.md) 진행 기록.

- Unity 6000.3.23f1 프로젝트, 코어 컴파일 통과, **EditMode 테스트 31/31 통과**
- `Assets/Game/**` — 코어 정산 엔진 + 공장/파견/일일의뢰/창고/판매 + JSON 데이터 로더 + 로케일 (UnityEngine 비의존)
- `Assets/Unity/Bootstrap.cs` — 세션 부팅·앱 수명 처리 (씬 배치 불필요)
- `Assets/StreamingAssets/Data/*.json` — 본편 추출 4종 + 직접 설계 6종 (파견/전리품/레시피/의뢰/스캐브/밸런스)
- `Tools/sim_model.py` (참조 구현) / `Tools/check_data.py` (데이터 검산) / `Tools/extract_mainline_data.py` (본편 추출)

남은 P0: Android Build Support 모듈 설치 → 실기기 빌드, Company 명 결정.
다음 작업: **P1 UI** — 하단 5탭, 창고 목록·판매, 공장 `assemble` 미니게임.

### 테스트 실행

```
"C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe" -batchmode -projectPath . ^
  -runTests -testPlatform EditMode -testResults Logs\test_results.xml -logFile Logs\test_run.log
```

에디터가 이 프로젝트를 열고 있으면 batchmode 가 실패한다. 에디터 안에서는 Window → General → Test Runner.

## AI 작업자 필독

`docs/GDD.md` §45 (AI 개발 작업 시 필수 지침) 과 `docs/LINK_CONTRACT.md` 를 먼저 읽을 것.
특히 **모바일 → PC 아이템 전송은 반드시 별도 validation 계층을 거친다**.
