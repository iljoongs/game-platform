# 공통 관리

> [메인 지시서](../CLAUDE.md)의 하위 문서. 특정 게임 항목에 속하지 않고 앱 전체에 적용되는 것들 — 저장 경로, 설정, 썸네일/드래그앤드롭 공용 인프라, 오류 처리 — 을 모은다. 게임 카드/실행/정보 창 자체는 [게임 관리](game-management.md) 참고.

**관련 파일 (예정, 아직 미구현)**: `App.xaml`/`.xaml.cs`, `AppPaths.cs`, `AppSettings.cs`, `SettingsRepository.cs`, `ImageLoadHelper.cs`, `ThumbnailPathConverter.cs`, `ThumbnailHelper.cs`, `DragDropImageHelper.cs`

## video-vault에서 이식하는 인프라

상위 폴더의 형제 프로젝트 `video-vault`가 이미 검증한 아래 공용 로직을 그대로 가져와 game-platform에 맞게 이름/경로만 조정해서 쓴다 (사소한 재작성이 아니라 그대로 이식 — 버그가 이미 잡혀 있는 코드이므로).

- **`ImageLoadHelper`**: 로컬 파일 경로에서 `BitmapImage`를 `BitmapCacheOption.OnLoad` + `IgnoreImageCache`로 즉시 전부 읽어들여 `Freeze()`한다. 같은 경로에 썸네일을 덮어쓸 때 "파일이 사용 중" 오류가 나거나, 덮어썼는데도 화면에 예전 이미지가 남는 문제를 막기 위한 것 — 로컬 파일 경로로 이미지를 표시하는 모든 코드는 이 헬퍼(또는 아래 컨버터)를 거친다.
- **`ThumbnailPathConverter`**: XAML에서 `Image.Source`를 문자열 경로에 바인딩할 때 `ImageLoadHelper.Load`를 거치도록 하는 `IValueConverter`.
- **`ThumbnailHelper`**: 이미지를 원본과 리사이즈본(320x240 이내, 가로세로 비율 유지) 두 파일로 저장하고, 소스로 쓰인 임시 파일은 정리한다.
- **`DragDropImageHelper`**: 드래그앤드롭된 데이터에서 이미지를 꺼내 파일로 확보한다. 로컬 파일뿐 아니라 브라우저에서 드래그한 이미지(웹 URL/`data:` URI/렌더링된 비트맵)도 지원한다.

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

## 오류 처리

예외를 catch해 `MessageBox`로 알리고, 프로그램은 종료하지 않고 계속 동작 가능한 상태를 유지한다 (video-vault와 동일한 정책).

- 실행하려는 파일이 존재하지 않는 경우 → 실행 실패 메시지, 카드는 그대로 유지
- 이미지 파일이 손상된 경우 → 기본 아이콘으로 대체 표시

## 확인/결정이 더 필요한 사항

- `games.json`이 손상됐을 때 빈 목록으로 시작할지, 로딩을 막고 사용자가 직접 조치하게 할지.
- 앱 시작 시 모든 게임의 `ExecutablePath` 존재 여부를 매번 다시 검사할지 (video-vault의 `IsExist` 갱신과 동일한 방식).
