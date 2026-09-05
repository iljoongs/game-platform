# 공통 관리

> [메인 지시서](../CLAUDE.md)의 하위 문서. 특정 게임 항목에 속하지 않고 앱 전체에 적용되는 것들 — 저장 경로, 설정, 썸네일/드래그앤드롭 공용 인프라, 오류 처리 — 을 모은다. 게임 카드/실행/정보 창 자체는 [게임 관리](game-management.md) 참고.

**관련 파일 (예정, 아직 미구현)**: `App.xaml`/`.xaml.cs`, `AppPaths.cs`, `AppSettings.cs`, `SettingsRepository.cs`, `ImageLoadHelper.cs`, `ThumbnailPathConverter.cs`, `ThumbnailHelper.cs`, `DragDropImageHelper.cs`, `OriginalImageWindow.xaml`/`.xaml.cs`, `SingleInstanceWindow.cs`, `WindowPositionMemory.cs`, `WindowSizeMemory.cs`, `BackupService.cs`

## video-vault에서 이식하는 인프라

상위 폴더의 형제 프로젝트 `video-vault`가 이미 검증한 아래 공용 로직을 그대로 가져와 game-platform에 맞게 이름/경로만 조정해서 쓴다 (사소한 재작성이 아니라 그대로 이식 — 버그가 이미 잡혀 있는 코드이므로).

- **`ImageLoadHelper`**: 로컬 파일 경로에서 `BitmapImage`를 `BitmapCacheOption.OnLoad` + `IgnoreImageCache`로 즉시 전부 읽어들여 `Freeze()`한다. 같은 경로에 썸네일을 덮어쓸 때 "파일이 사용 중" 오류가 나거나, 덮어썼는데도 화면에 예전 이미지가 남는 문제를 막기 위한 것 — 로컬 파일 경로로 이미지를 표시하는 모든 코드는 이 헬퍼(또는 아래 컨버터)를 거친다.
- **`ThumbnailPathConverter`**: XAML에서 `Image.Source`를 문자열 경로에 바인딩할 때 `ImageLoadHelper.Load`를 거치도록 하는 `IValueConverter`.
- **`ThumbnailHelper`**: `CreateThumbnail`은 이미지를 원본과 리사이즈본(320x240 이내, 가로세로 비율 유지) 두 파일로 저장한다 — 게임 요약 스크린샷처럼 여러 장을 반복 표시하는 곳에 쓴다. `CopyOriginal`은 리사이즈 없이 원본 크기 그대로 한 파일만 저장한다 — 메인 카드 대표 썸네일처럼 한 장뿐이고 화면에서 스케일해서 보여주면 충분한 곳에 쓴다. 둘 다 `deleteSource` 매개변수를 받아, 소스로 쓰인 파일을 지울지 호출자가 명시적으로 결정한다 — 위 `DragDropImageHelper`의 `isTemporary`를 그대로 전달해, 임시 파일만 지우고 사용자가 드래그한 로컬 원본은 그대로 남겨둔다.
- **`DragDropImageHelper`**: 드래그앤드롭된 데이터에서 이미지를 꺼내 파일로 확보한다. 로컬 파일뿐 아니라 브라우저에서 드래그한 이미지(웹 URL/`data:` URI/렌더링된 비트맵)도 지원한다. `TryGetImagePath`는 반환하는 파일이 이 메서드가 직접 만든 **임시 파일**(웹 다운로드/`data:` URI/비트맵)인지, 사용자의 **로컬 파일 그대로**인지를 `isTemporary` out 매개변수로 함께 알려준다 — `ThumbnailHelper`를 호출할 때 이 값을 그대로 `deleteSource`에 넘겨서, 임시 파일만 정리하고 사용자의 원본 파일은 절대 건드리지 않는다(2026-09-05 수정 — 이전에는 로컬 파일을 드래그해도 항상 삭제했었다).
- **`OriginalImageWindow`**: 리사이즈 전 원본 이미지를 크게 보여주고, 아무 곳이나 클릭하면 닫힌다. [게임 관리](game-management.md)의 게임 요약 갤러리 항목 클릭 시 사용.
- **`SingleInstanceWindow<T>`**: 창 종류(T)별로 현재 열려 있는 인스턴스를 추적해, 같은 종류의 새 창을 열면 기존 창을 먼저 닫는다. [게임 관리](game-management.md)의 `GameInfoWindow`(한 번에 하나만 열림)에 사용.

## 저장 위치

video-vault의 `AppPaths` 패턴을 따라 `%LOCALAPPDATA%\GamePlatform\` 아래에 둔다.

```
%LOCALAPPDATA%\GamePlatform\
├── games.json          # 게임 목록 (GameItem 배열)
├── settings.json        # 카드 크기 등 전역 설정
└── images\
    └── {게임 Id}\
        ├── thumbnail.jpg / thumbnail.original.*   # 대표 썸네일
        └── screenshot-{n}.jpg / screenshot-{n}.original.*  # 게임 요약 캡처
```

게임의 실행 파일(exe)은 사용자가 이미 설치해 둔 임의의 위치(다른 드라이브, 읽기 전용 폴더 등)를 가리킬 수 있으므로, video-vault처럼 원본 파일과 같은 폴더에 썸네일을 저장하지 않고 앱 데이터 폴더 안에 게임 Id별로 모아 저장한다.

## 설정 관리 (`settings.json`)

- 카드 크기 프리셋(320x240 / 160x120) — 마지막 선택값을 기억했다가 다음 실행 시 복원한다.
- 아래 "백업"의 마지막 일간/주간 백업 수행 시각(`LastDailyBackupUtc`/`LastWeeklyBackupUtc`)도 함께 저장한다.
- 아래 "창 위치/크기 기억"의 값들도 함께 저장한다.

## 창 위치/크기 기억

`MainWindow`와 `GameInfoWindow`([게임 관리](game-management.md) 참고) 모두 닫을 때의 화면 위치(Left/Top)와 크기(Width/Height)를 기억했다가, 다음에 열 때 그대로 복원한다. video-vault의 방식을 그대로 따른다.

- **`MainWindow`**: 앱을 대표하는 창이라 `AppSettings.MainWindowWidth/Height/Left/Top` 전용 필드로 별도 저장한다. 창이 최대화된 상태로 닫히면 `RestoreBounds`(최대화 이전의 "정상" 크기/위치)를 저장해, 다음 실행이 항상 전체화면으로 시작되지 않게 한다. 저장된 값이 없거나(최초 실행) 현재 화면 구성에서 화면 밖으로 벗어난 좌표면 XAML 기본값으로 시작한다.
- **`GameInfoWindow`**: `WindowPositionMemory`/`WindowSizeMemory`(창 클래스 이름을 키로 위치/크기를 기억하는 공용 저장소, video-vault에서 이식)로 관리한다. 창 종류가 하나뿐이라도 나중에 창이 추가될 걸 대비해 범용 저장소를 그대로 쓴다.
- 두 창 모두 값이 바뀔 때마다 즉시 저장하지 않고(위치 이동/크기 조절마다 저장하기엔 너무 잦음), **창을 닫는 시점에 그때의 최종 상태를 저장**한다.

## 백업 (`games.json`)

`games.json`(게임 목록 데이터)을 주기적으로 백업한다. 일간/주간 각각 **파일 하나씩만** 유지한다 (날짜별로 계속 쌓지 않고, 같은 파일에 덮어쓴다).

```
%LOCALAPPDATA%\GamePlatform\backup\
├── games.daily.json    # 최근 1일 주기 백업 (덮어씀)
└── games.weekly.json   # 최근 1주 주기 백업 (덮어씀)
```

- `settings.json`에 저장해 둔 `LastDailyBackupUtc`/`LastWeeklyBackupUtc`를 기준으로, 마지막 백업 이후 하루/일주일이 지났으면 그 시점의 `games.json`을 각각의 백업 파일로 복사하고 타임스탬프를 갱신한다. 앱 시작 시 한 번, 그리고 `games.json` 저장 시점마다 이 조건을 확인한다 — 앱을 매일 켜지 않아도 다음 실행 시 지난 기간만큼 자동으로 따라잡는다.
- 복구는 아직 UI로 제공하지 않는다 (필요 시 `%LOCALAPPDATA%\GamePlatform\backup\`의 파일을 `games.json`에 수동으로 덮어쓰는 방식). 향후 "백업에서 복원" 메뉴를 추가할 수 있다.

## 오류 처리

예외를 catch해 `MessageBox`로 알리고, 프로그램은 종료하지 않고 계속 동작 가능한 상태를 유지한다 (video-vault와 동일한 정책).

- 실행하려는 파일이 존재하지 않는 경우 → [게임 관리](game-management.md)에 따라 애초에 [실행] 버튼이 비활성화되어 있으므로 이 경로로는 발생하지 않는다.
- 이미지 파일이 손상된 경우 → 기본 아이콘으로 대체 표시
- **`games.json`이 손상되어 읽을 수 없는 경우 → 빈 목록으로 시작한다.** 위 백업 파일이 있다는 오류 메시지와 함께, 사용자가 원하면 백업에서 수동으로 복구할 수 있음을 안내한다 (자동 복구는 하지 않음 — 손상 원인을 사용자가 먼저 확인할 수 있도록).

## 확인/결정이 더 필요한 사항

- 앱 시작 시 모든 게임의 `ExecutablePath` 존재 여부를 매번 다시 검사할지, 아니면 캐시된 값을 쓰다가 [실행] 클릭 시점에만 재확인할지 (전자가 기본값에 가깝다 — video-vault의 `IsExist` 갱신과 동일한 방식).
