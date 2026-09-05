# 게임 관리

> [메인 지시서](../CLAUDE.md)의 하위 문서. 게임 카드 목록(메인 화면), 실행, 정보 창을 다룬다. 썸네일 저장·드래그앤드롭 공용 인프라는 [공통 관리](common-management.md) 참고.

**관련 파일 (예정, 아직 미구현)**: `GameItem.cs`, `GameLibraryRepository.cs`, `MainWindow.xaml`/`.xaml.cs`, `GameInfoWindow.xaml`/`.xaml.cs`, `GameCardSizeSettings.cs`

## 데이터 모델 (`GameItem`)

관리 리스트(`games.json`) 항목 하나. video-vault의 `ManagedVideoItem`과 같은 역할.

| 필드 | 설명 |
|---|---|
| `Id` | 고유 식별자(GUID). 이미지 저장 폴더명 등에 사용 |
| `Name` | 게임 이름. exe 드래그드롭으로 카드를 만들 때 파일명(확장자 제외)으로 자동 채워지며, 정보 창에서 수정 가능 |
| `Version` | 게임 버전 (자유 텍스트) |
| `Description` | 게임 내용 (여러 줄 텍스트) |
| `ExecutablePath` | 실행 파일 경로. 정보 창의 "찾아보기"로 지정/변경 |
| `ThumbnailPath` / `ThumbnailOriginalPath` | 메인 카드 대표 썸네일(리사이즈본/원본). 카드에 이미지를 직접 드래그드롭해서 지정 |
| `Screenshots` | 게임 요약 갤러리용 캡처 이미지 경로 목록 (각각 리사이즈본/원본 쌍) |

`ExecutablePath`가 실제로 존재하는지 여부(`IsExecutableValid` 등, JSON에는 저장하지 않는 런타임 계산 값)는 앱 시작 시와 정보 창에서 경로를 바꿀 때마다 `File.Exists`로 다시 판단한다 — 아래 "실행(Run)" 절 참고.

## 메인 화면 (카드 목록)

- 카드 그리드에 게임 카드를 나열한다. 각 카드: 대표 썸네일 + 이름 + 하단 [정보][실행] 버튼 두 개.
- **새 게임 추가**: 메인 화면 빈 영역에 실행 파일(exe)을 드래그드롭하면 새 카드가 생성된다. `Name`은 파일명(확장자 제외)으로 자동 채워지고, `ExecutablePath`는 드롭한 파일 경로로 설정된다. 이름/버전/설명 등 나머지 정보는 이후 정보 창에서 채운다.
- **대표 썸네일 지정**: 이미 만들어진 카드 위에 이미지 파일을 드래그드롭하면 그 카드의 대표 썸네일로 지정된다 ([공통 관리](common-management.md)의 `DragDropImageHelper`/`ThumbnailHelper` 재사용).
- **삭제**: 카드 우클릭 → 컨텍스트 메뉴 "삭제". `games.json` 목록에서 제거함과 동시에, 이 게임에 연결된 이미지 파일(대표 썸네일 원본/리사이즈본, 게임 요약 스크린샷 원본/리사이즈본 전부 — [공통 관리](common-management.md)의 `images\{게임 Id}\` 폴더)도 함께 삭제한다. 게임 Id별로 폴더가 분리되어 있으므로 그 폴더 전체를 지우면 된다. 실행 파일(exe) 자체는 사용자의 게임 설치 파일이므로 지우지 않는다.
- **카드 크기**: 320x240(기본) / 160x120 두 프리셋을 전역 설정으로 전환한다 (`GameCardSizeSettings`, video-vault의 `IconSizeSettings`와 같은 역할이지만 프리셋이 2개뿐이라는 점이 다르다). 전환하면 화면의 모든 카드가 동시에 바뀐다.

## 실행 (Run)

- [실행] 버튼을 누르면 `Process.Start(ExecutablePath)`로 실행하고, `WorkingDirectory`는 실행 파일이 있는 폴더로 지정한다 (상대 경로로 리소스를 찾는 게임을 위해).
- `ExecutablePath`가 비어 있거나 파일이 더 이상 존재하지 않으면 카드의 [실행] 버튼 자체를 비활성화한다 (클릭 시 오류 메시지를 띄우는 대신, 애초에 누를 수 없게 한다). 존재 여부는 앱 시작 시 전체 항목에 대해, 그리고 정보 창에서 실행 파일 경로를 바꿔 저장할 때 다시 확인한다.

## 정보 창 (`GameInfoWindow`)

카드의 [정보] 버튼을 누르면 열리는 창. 표시/편집 항목:

- **게임 이름**, **게임 버전** — 한 줄 텍스트박스
- **게임 내용** — 여러 줄 텍스트박스 (`Description`)
- **실행 파일 경로** — 텍스트박스 + "찾아보기"(`OpenFileDialog`) 버튼으로 `ExecutablePath` 지정/변경
- **게임 요약** — 캡처 이미지 갤러리 (`Screenshots`). 창 안에 이미지 파일을 드래그드롭하면 목록에 추가되고, 각 항목은 320x240 썸네일로 표시된다. 개별 삭제는 항목 우클릭으로 처리한다 (메인 화면 카드 삭제와 동일한 상호작용 규칙). **항목을 클릭하면 원본 이미지를 크게 보여주는 뷰어가 열린다** (video-vault의 `OriginalImageWindow`와 동일한 방식 — 아무 곳이나 클릭하면 닫힘).
- **창은 한 번에 하나만 연다**: 이미 `GameInfoWindow`가 열려 있는 상태에서 다른 카드의 [정보]를 누르면, 열려 있던 창을 닫고 새 게임 정보로 새 창을 연다 (video-vault의 `SingleInstanceWindow<T>` 패턴을 그대로 사용 — [공통 관리](common-management.md) 참고). 게임별로 별개 창을 유지하지 않는다.

## 확인/결정이 더 필요한 사항

- 정보 창의 저장 시점: 필드 변경 즉시 자동 저장(debounce)할지, "저장" 버튼을 눌러야 반영할지.
