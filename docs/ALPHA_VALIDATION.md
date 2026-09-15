# 모바일 알파 검증 — 2026-09-15

## 적용 범위

- Unity 6000.3.24f1, 기존 코드 기반 uGUI와 GameSession 유지.
- 원작의 D2Coding, UI 효과음 6개와 기지 음악을 복사해 사용. 원작 프로젝트 파일 수정 없음.
- 검은 단말기 바탕, 청록 구획 제목, 밝은 버튼의 어두운 글자, 인사기록 카드, 서울 개략도.
- 소리 설정에서 효과음/음악을 별도로 조절하고 저장. 빈 씬의 리스너 보완, 생성 오디오 정리.
- 첫 고용주 선택과 5탭 진입, 에디터 모달 레이아웃, 한글 글리프 회귀 테스트.
- 팀·숙련자의 회수량 보정. [측정 조건과 결과](economy-validation.md).

## 확인한 문제와 해결

1. 첫 화면이 고용주 선택 전에 홈을 갱신해 null ID로 신뢰도 조회: 선택 전 탭 진입 차단.
2. 알림 패키지 어셈블리 참조 누락: 명시 참조 추가.
3. Android 전용 권한 요청 API 오류: 설치된 패키지의 PermissionRequest 사용, 중복 요청 차단.
4. 모달의 가로 레이아웃이 세로 여유 공간을 요구해 제목 영역이 화면 절반을 차지: 헤더 flexibleHeight=0.
5. 고용주 선택 당일 의뢰가 비어 있는 문제: 실제 화면에서 발견. 선택 즉시 의뢰를 발급하고 기존 빈 보드도 복구한다. 고용주 3종·날짜 경과·재접속·완료 보존을 9개 회귀 사례로 검증.
6. 기존 테스트의 무기 미지급, 잘못된 난수 표본과 티어 조건 정리. 실제 데이터의 누락된 의뢰 대사 56개와 탄약 의뢰 보상 수정.

## 설정·테마 후속 업데이트

테마 3종과 설정 화면 추가 후 최신 결과는 **395/395 테스트 통과, Android APK 빌드 오류 0·경고 0**이다. 아래 기록은 이전 알파 검증이며, APK는 최신 설정 버전으로 갱신했다. [설정·테마 검증 기록](SETTINGS_THEMES.md).

## 실행 기록

- 초기 전체 테스트: 371개 중 343 통과, 28 실패. 새 오디오 실패 3개 포함.
- UI/오디오/경제 변경 후: 379/379 통과 (`Logs/alpha-final.xml`).
- 추가 첫날 의뢰 회귀: 386개 중 380 통과, 신규 6개 모두 예상대로 실패 (`Logs/alpha-quests-red.xml`).
- 최종 전체 EditMode 테스트: **389/389 통과, 실패 0** (`Logs/alpha-release-tests.xml`).
- Android IL2CPP 개발용 APK 빌드: **성공, 오류 0·경고 0**, 7분 53초 (`Logs/alpha-android.log`, `Builds/Android/build-result.txt`). APK는 `Builds/Android/AfterSeoul-alpha.apk`, 56,708,694바이트(약 57MB). BuildReport의 전체 출력 크기는 디버그 심볼 등도 포함하므로 APK 파일 크기와 다르다.
- 실제 uGUI 캡처: `Logs/alpha-preview/00-employer.png` ~ `06-audio.png`. 신규 세이브를 메모리에서 생성하며 사용자 세이브에 접근하지 않는다.
- 최종 캡처에서 당일 의뢰, 시계, 모달 제목 배치를 확인했다. Unity 서비스 설정 조회의 네트워크 오류가 로그에 남았으나 7개 화면 생성은 완료됐고 게임 코드 예외는 없었다.
- Android 조건부 코드 컴파일: 권한 API 오류를 재현한 뒤 수정본 종료 코드 0.
- `adb devices`: 연결된 기기 없음. 물리 기기에서의 재생·터치·알림은 아직 확인하지 못했다.

## 재실행

Unity를 닫고 PowerShell에서 다음 명령을 실행한다.

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.3.24f1/Editor/Unity.exe' -batchmode -nographics -projectPath 'D:/singleProject/AfterSeoul' -runTests -testPlatform EditMode -testResults 'D:/singleProject/AfterSeoul/Logs/test-results.xml' -logFile 'D:/singleProject/AfterSeoul/Logs/test-run.log'
```

실제 UI 렌더링은 `-executeMethod AfterSeoul.Unity.Editor.AlphaPreview.Capture`와 `-force-d3d11`로 실행한다. 렌더링 때는 `-nographics`와 `-quit`을 사용하지 않는다. 캡처 완료 후 도구가 종료한다. 이 도구는 배치 검증용이며, 열려 있는 사용자 씬을 대신 편집하는 도구가 아니다.

Android 개발용 APK는 `After Seoul → Build Android Alpha`, 또는 `-buildTarget Android -executeMethod AfterSeoul.Unity.Editor.AlphaBuild.Android`로 빌드한다.

## 남은 확인

- Android 실기기에서 설치, 한글/스크롤/터치, 음량 저장, 백그라운드 후 복귀, 알림 허용/거부.
- 첫 30분 동안 직접 노동→고용→장비→파견→복귀→납품의 재미와 대기 시간. 자동 테스트 통과는 재미 검증을 대신하지 않는다.
- PC 계정 연결·Cloud Save·실제 수령과 중복 방지의 양쪽 통합, 실제 결제·광고 SDK 및 스토어 등록은 미완료.
- 팀 보정은 이미 출발한 파견에도 적용된다. 저장 형식과 시드 재현성은 유지되지만 업데이트 전후 보상은 달라질 수 있다.

## 자산 출처

- D2Coding 1.3.2: 원작 `Assets/Fonts/D2Coding/D2Coding`에서 복사. [공식 글꼴 저장소](https://github.com/naver/d2-coding-font), OFL 전문은 `Assets/Resources/Fonts/OFL.txt`에 포함.
- 효과음·음악: [오디오 자산 기록](audio-assets.md).
