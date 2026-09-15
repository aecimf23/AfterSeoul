# 설정과 테마 — 2026-09-15

## 사용 방법

화면 오른쪽 위 **설정**을 누른다.

- **서울의 밤**: 새 기본 테마. 남색 패널, 호박색 포인트, 비 내리는 서울 실루엣.
- **군용 단말기**: 녹색 작전도와 검은 장비 색.
- **낡은 피난처**: 갈색 패널, 따뜻한 조명, 창문 너머 서울.
- 카드의 그림과 색으로 미리 확인하고 선택하면 즉시 적용된다. 위험/성공 색의 의미는 유지한다.
- 배경 음악/효과음 각각 슬라이더와 음소거를 제공한다. 음소거 해제 시 이전 음량을 유지한다.
- 연출 줄이기는 화면 이동, 확대, 페이드, 장식 점멸을 줄인다. 미니게임 조작 타이밍은 유지한다.
- 설정 초기화는 두 번 눌러 확인하며 테마·음량·음소거·연출만 기본값으로 되돌린다. 게임 저장은 건드리지 않는다.

## 구현 범위

Theme 팔레트를 교체하고 기존 그래픽을 다시 칠한다. 화면 객체를 새로 만들거나 게임 세션을 재시작하지 않아 제작/선택 상태를 보존한다. 설정을 보는 동안 활성 화면의 수동 미니게임 타이머는 멈춘다. 서버/현실 시각 기준 자동 제작·파견은 기존 정산 규칙대로 진행된다.

설정은 AfterSeoul.UI.* 및 AfterSeoul.Audio.* PlayerPrefs에 저장한다. 게임 저장 파일과 별개다. 알 수 없는 테마 ID는 서울의 밤으로 표시한다.

ThemeScene은 경량 벡터 그림이며 게임 난수를 소비하지 않는다. 실제 지역 경로나 해금 정보를 표시하지 않는다. 이번 범위는 설정과 테마 3종이다. 고용주 초상과 지역별 전용 원화는 후속 작업이다.

## 검증

- 테마 저장/복구 테스트 2개: 구현 전 실패 확인.
- 리뷰 회귀 2개: 설정 뒤 제작 Tick, 실제 SlideIn 누락을 재현하고 수정.
- 실제 버튼/슬라이더/음소거/초기화와 화면 객체·게임 저장 보존을 검증.
- 최종 전체 EditMode 테스트: **395/395 통과** (Logs/theme-tests-final.xml).
- 세 테마의 홈·설정 총 6개 실제 uGUI 캡처를 확인했다 (Logs/theme-preview). 선택 표시·한글·슬라이더 배치 정상.
- Android IL2CPP 개발용 APK 갱신 성공: 오류 0·경고 0, 1분 16초 (Logs/theme-android.log). 파일: Builds/Android/AfterSeoul-alpha.apk, 80,906,722바이트(약 81MB). 실기기 터치·음성 출력 검증은 별도로 필요하다.

## 캡처 재실행

Unity가 AfterSeoul을 열고 있지 않은 상태에서:
`-batchmode -force-d3d11 -projectPath D:/singleProject/AfterSeoul -executeMethod AfterSeoul.Unity.Editor.AlphaPreview.CaptureThemes -logFile D:/singleProject/AfterSeoul/Logs/theme-preview.log`

`-nographics`, `-quit`을 붙이지 않는다. 캡처 도구가 완료 후 종료한다. 사용자 게임 저장을 사용하지 않고 기존 테마 선택도 복원한다. 결과: `Logs/theme-preview`.
