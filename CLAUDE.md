# game-platform - 메인 지시서

이 문서는 프로젝트의 메인 지시서입니다. 작업 시작 전 아래 보조 지시서를 모두 읽고 진행하세요.

이 문서는 상위 폴더의 형제 프로젝트(`image-readers`, `text-readers`, `english-training`, `video-vault`)들이 공통으로 따르는 지시서 구조와 작업 원칙을 뽑아 정리한 것입니다. 프로젝트별 세부 내용(프로젝트 한 줄 요약, 기술 스택 상세, doc/ 목록 등)은 아래 표를 채워 넣어 확장하세요.

## 프로젝트 한 줄 요약
썸네일 카드로 게임을 관리하고, 카드에서 바로 정보를 확인하거나 지정한 실행 파일을 실행하는 WPF 데스크톱 런처.

## 기술 스택
형제 프로젝트들의 공통 스택(참고용, 이 프로젝트 성격에 맞게 조정):
- .NET 8
- WPF (Windows Presentation Foundation) — C#
- MVVM 패턴 권장 (Repository → Service → ViewModel → View 계층 분리)
- 데이터 저장: JSON 파일 (`data/` 또는 `.data/` 폴더)

## 폴더 구조 (공통 패턴)

```
game-platform/
├── CLAUDE.md          # 메인 지시서 (이 파일)
├── game-platform.sln
├── .gitignore
├── game-platform.png  # 앱 아이콘 원본 PNG (512x512, 코드 아님, 루트 유지) — 아래 "앱 아이콘" 참고
├── doc/               # 기능별 상세 보조 지시서 (게임 목록/썸네일/설정 데이터는 %LOCALAPPDATA%\GamePlatform\ 사용 — doc/common-management.md 참고)
└── src/
    └── GamePlatform/  # 앱 본체 소스 (.NET 8 WPF)
```

## 앱 아이콘

exe 아이콘(탐색기/작업 표시줄)과 `MainWindow` 타이틀바 아이콘 모두 `Assets/AppIcon.ico`를 사용한다.

- **원본**: 저장소 루트의 `game-platform.png`(512x512, 알파 채널 포함)를 소스로 쓴다. `System.Drawing`으로 16/32/48/256px 각 크기로 고품질 리샘플링(`InterpolationMode.HighQualityBicubic`, 알파 유지)한 뒤 PNG로 인코딩하고, 이 PNG들을 담은 `.ico`(PNG-압축 아이콘 항목, Vista 이상에서 지원)를 직접 조립하는 1회성 PowerShell 스크립트로 변환했다(video-vault와 동일한 방식, 프로젝트에는 스크립트 자체를 포함하지 않음).
- **적용 위치**: `src/GamePlatform/GamePlatform.csproj`의 `<ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>`(exe 자체 아이콘) + `<Resource Include="Assets\AppIcon.ico" />`, `MainWindow.xaml`의 `Icon="Assets/AppIcon.ico"`(타이틀바/Alt+Tab).
- **검증**: 빌드된 exe에서 `System.Drawing.Icon.ExtractAssociatedIcon`으로 아이콘이 정상 임베드되었음을 확인함.

## 보조 지시서 목록 (doc/ 폴더)

작업 성격에 맞는 문서를 참조하세요.

| 문서 | 내용 |
|---|---|
| [doc/game-management.md](doc/game-management.md) | 게임 카드 목록(메인 화면), 카드 추가/삭제, 대표 썸네일, 실행, 정보 창(`GameInfoWindow`), `GameItem` 데이터 모델 |
| [doc/common-management.md](doc/common-management.md) | 저장 경로(`AppPaths`), 설정 저장, video-vault에서 이식하는 썸네일/드래그앤드롭 공용 인프라, 오류 처리 |
| [doc/release-folder-naming.md](doc/release-folder-naming.md) | `D:\game\.latest` 등 아직 앱에 등록하지 않은 게임 릴리즈 폴더의 이름 정리 규칙 (대소문자, 구분자, 버전 표기, 태그 단어 제거, 시즌/에피소드/파트 축약형) |

## 작업 원칙

형제 프로젝트들에 공통으로 적용되는 규칙:

1. 각 기능을 구현/수정하기 전, 해당 작업과 관련된 보조 지시서(doc/ 폴더)를 먼저 확인한다.
2. 보조 지시서 간 내용이 충돌하거나, 아직 결정되지 않은 항목을 다뤄야 할 때는 임의로 확정하지 말고 합리적인 기본값을 제안한 뒤 사용자 확인을 받는다.
3. 코드는 MVVM 패턴을 따르며, View(XAML)와 로직(ViewModel/Service/Repository)을 분리한다.
4. 기능/구조/결정 사항이 변경되면 관련된 보조 지시서(doc/ 폴더 및 이 메인 지시서)를 함께 업데이트한다. 문서와 코드가 어긋난 상태로 두지 않는다.
5. 문서를 고칠 때 별도의 백업(history 폴더 등)은 두지 않는다 — git 커밋 이력이 변경 기록 역할을 한다. 의미 있는 단위로 커밋한다.
6. 사용자 명령으로 파일이 수정되고 작업이 성공적으로 끝나면, 별도 요청/확인 없이 git commit(커밋 메시지는 영어로 직접 작성)과 push까지 수행한다.
7. 명령 수행 후, 이번 작업에서 참조하거나 수정한 보조 지시서(doc/ 폴더) 목록을 사용자에게 알려준다.
8. **실제 사용자 데이터(기본 폴더, `AppPaths.GamesBaseDir` — 기본값 `D:\game`과 그 밑의 `GamePlatform\images`/`archives`/`games.json` 등)를 건드리는 코드를 직접 실행해서 검증할 때는, 절대 그 실제 경로를 대상으로 테스트하지 않는다.** 항상 `AppPaths.Initialize(new AppConfig { GamesBaseDir = <임시 폴더> })`로 완전히 격리된 임시 폴더를 먼저 지정한 뒤 그 안에서만 만들고 지운다 — 테스트용 `GameItem`이 새 GUID를 써서 실제 게임 폴더와 이름이 안 겹치는 것만으로는 충분하지 않다(실제로 겪은 사고: 테스트 게임의 이미지가 실제 `images\` 폴더에 함께 저장됐고, 뒷정리용 `rm -f .../images/*/cover.original.*` 명령의 와일드카드가 테스트 폴더뿐 아니라 실제 게임 17개의 대표 썸네일까지 전부 지워버렸다 — 복구 불가). 부수적으로: 테스트가 끝난 뒤 무언가를 지우는 명령을 쓸 때는, 와일드카드(`*`)가 조금이라도 실제 데이터 폴더와 겹칠 가능성이 있으면 실행 전에 반드시 `ls`/`echo` 등으로 정확히 무엇이 매칭되는지 먼저 확인한다.
