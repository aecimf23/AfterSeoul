# 모바일 시작 화면과 고용주 헤더

- 콜드 실행: 1.6초 로고 → 3.8초 ASCII 서사 → 터치 대기 타이틀.
- 연출 도중 터치하면 타이틀까지 건너뛴다. 타이틀에서 터치해야 홈으로 들어간다.
- 새 세이브는 타이틀 뒤 고용주 선택, 기존 세이브는 홈으로 이동한다.
- 부팅 정산과 대기 중 도착한 복귀 보고는 시작 화면 뒤로 보류한다. 세이브 정산 자체는 기존 Bootstrap이 계속 처리한다.
- 앱을 잠깐 나갔다 돌아오는 것만으로 오프닝을 다시 재생하지 않는다.
- 상단에는 처음 선택한 고용주(HWANG / DR_CHOI / YONGSAN_KIM)의 ASCII 초상·이름·역할, 현재 탭과 소지금을 표시한다.
- 5개 메뉴는 안전 영역 하단 고정. 상단 헤더와 하단 메뉴 높이만큼 본문 공간을 확보한다.

LaunchVerification의 시작/자동 전환/건너뛰기/명시적 진입 17개 검사 통과. Game, Unity UI 및 테스트 어셈블리 컴파일 통과. 실제 Android 화면/터치와 Unity EditMode 테스트 실행은 아직 확인하지 않았다.

APK는 자동 갱신되지 않는다. `D:/singleProject/AfterSeoul` 프로젝트를 열고 Unity 메뉴 `After Seoul > Build Android Alpha`로 다시 빌드해야 한다. 결과는 `Builds/Android/AfterSeoul-alpha.apk`다. 기존 실행 중인 Unity와 충돌할 수 있어 이 작업에서는 별도 Unity 프로세스나 APK 빌드를 실행하지 않았다.
