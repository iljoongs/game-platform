# 공통 관리

> [메인 지시서](../CLAUDE.md)의 하위 문서. 특정 게임 항목에 속하지 않고 앱 전체에 적용되는 것들 — 저장 경로, 설정, 썸네일/드래그앤드롭 공용 인프라, 오류 처리 — 을 모은다. 게임 카드/실행/정보 창 자체는 [게임 관리](game-management.md) 참고.

**관련 파일**: `App.xaml`/`.xaml.cs`, `AppPaths.cs`, `AppConfig.cs`, `AppConfigRepository.cs`, `AppSettings.cs`, `SettingsRepository.cs`, `ImageLoadHelper.cs`, `ThumbnailPathConverter.cs`, `ThumbnailHelper.cs`, `DragDropImageHelper.cs`, `OriginalImageWindow.xaml`/`.xaml.cs`, `SingleInstanceWindow.cs`, `WindowPositionMemory.cs`, `WindowSizeMemory.cs`, `BackupService.cs`, `FileNameHelper.cs`, `SelectExecutableWindow.xaml`/`.xaml.cs`, `PreferencesWindow.xaml`/`.xaml.cs`

## video-vault에서 이식하는 인프라

상위 폴더의 형제 프로젝트 `video-vault`가 이미 검증한 아래 공용 로직을 그대로 가져와 game-platform에 맞게 이름/경로만 조정해서 쓴다 (사소한 재작성이 아니라 그대로 이식 — 버그가 이미 잡혀 있는 코드이므로).

- **`ImageLoadHelper`**: 로컬 파일 경로에서 `BitmapImage`를 `BitmapCacheOption.OnLoad` + `IgnoreImageCache`로 즉시 전부 읽어들여 `Freeze()`한다. 같은 경로에 썸네일을 덮어쓸 때 "파일이 사용 중" 오류가 나거나, 덮어썼는데도 화면에 예전 이미지가 남는 문제를 막기 위한 것 — 로컬 파일 경로로 이미지를 표시하는 모든 코드는 이 헬퍼(또는 아래 컨버터)를 거친다. **디코딩 자체가 실패해도(파일은 있지만 내용이 진짜 이미지가 아닌 경우) 예외를 던지지 않고 `null`을 돌려준다**(2026-09-06 수정 — 실제로 겪은 문제: 웹에서 드래그한 이미지가 사실은 오류 페이지(HTML)였는데 그걸 모르고 `cover.original.jpg`로 저장해버린 게임이 있었고, 그 게임의 정보 창을 열 때마다(이미지를 실제로 그리려는 순간) `BitmapImage` 디코딩이 던지는 예외가 그대로 앱을 통째로 죽였다). 이 안전장치 덕분에 어떤 이유로든 깨진 이미지 파일이 하나 섞여 있어도 그 항목만 빈 이미지로 보이고 앱 전체가 죽지는 않는다 — 근본 원인 차단은 아래 `DragDropImageHelper`의 `IsDecodableImage` 참고.
- **`ThumbnailPathConverter`**: XAML에서 `Image.Source`를 문자열 경로에 바인딩할 때 `ImageLoadHelper.Load`를 거치도록 하는 `IValueConverter`.
- **`ThumbnailHelper`**: `CopyOriginal`이 이미지를 리사이즈 없이 원본 크기 그대로 한 파일만 저장한다 — 메인 카드 대표 썸네일과 게임 요약 스크린샷 모두 이 방식을 쓰며, 화면에는 항상 표시 크기에 맞게 스케일해서 보여준다(별도의 리사이즈본 파일은 만들지 않는다). `deleteSource` 매개변수를 받아 소스로 쓰인 파일을 지울지 호출자가 명시적으로 결정한다 — 위 `DragDropImageHelper`의 `isTemporary`를 그대로 전달해, 임시 파일만 지우고 사용자가 드래그한 로컬 원본은 그대로 남겨둔다.
- **`DragDropImageHelper`**: 드래그앤드롭된 데이터에서 이미지를 꺼내 파일로 확보한다. 로컬 파일뿐 아니라 브라우저에서 드래그한 이미지(웹 URL/`data:` URI/렌더링된 비트맵)도 지원한다. `TryGetImagePath`는 반환하는 파일이 이 메서드가 직접 만든 **임시 파일**(웹 다운로드/`data:` URI/비트맵)인지, 사용자의 **로컬 파일 그대로**인지를 `isTemporary` out 매개변수로 함께 알려준다 — `ThumbnailHelper`를 호출할 때 이 값을 그대로 `deleteSource`에 넘겨서, 임시 파일만 정리하고 사용자의 원본 파일은 절대 건드리지 않는다(2026-09-05 수정 — 이전에는 로컬 파일을 드래그해도 항상 삭제했었다). **웹 URL/`data:` URI로 받은 바이트가 실제로 디코딩 가능한 이미지인지 `IsDecodableImage`로 직접 확인한 뒤에만 파일로 저장한다**(2026-09-06 추가, 실제로 겪은 문제) — HTTP 응답의 `Content-Type`이나 URL의 확장자는 서버/URL이 "주장"하는 값일 뿐이라, 로그인/쿠키 동의 등으로 서버가 200 OK와 함께 HTML 오류 페이지를 돌려줘도 그 헤더만 보면 못 걸러낸다. `BitmapDecoder.Create`로 직접 디코딩을 시도해봐서 실패하면 이미지가 아닌 것으로 보고 조용히 거절한다(예외를 던지지 않고 그 후보를 건너뛴다 — `TryGetImagePath`가 다음 후보를 계속 시도하거나 결국 null을 돌려준다).
- **`OriginalImageWindow`**: 리사이즈 전 원본 이미지를 크게 보여주고, 아무 곳이나 클릭하면 닫힌다. [게임 관리](game-management.md)의 게임 요약 갤러리 항목 클릭 시 사용.
- **`SingleInstanceWindow<T>`**: 창 종류(T)별로 현재 열려 있는 인스턴스를 추적해, 같은 종류의 새 창을 열면 기존 창을 먼저 닫는다. [게임 관리](game-management.md)의 `GameInfoWindow`(한 번에 하나만 열림)에 사용.

## 저장 위치

**게임 관련 파일의 기본 폴더는 `D:\game`이다** (`AppPaths.GamesBaseDir`, 기본값 2026-09-05 확정 — 사용자 요청). 이 앱의 관리 데이터도 그 밑의 `GamePlatform` 폴더에 둔다(video-vault의 `%LOCALAPPDATA%\VideoVault\` 패턴 대신, 게임 관련 파일과 한 드라이브·한 폴더 밑에 모아두기 위함). **"설정 > 환경설정"으로 바꿀 수 있다** — 아래 "환경설정" 절 참고.

```
D:\game\
├── GamePlatform\                  # 앱 관리 데이터 (아래 상세)
├── {게임 이름}\                   # 사용자가 이미 설치해 둔 게임들 (앱이 관리하지 않음, exe 드래그드롭 시 원본 위치 그대로 참조)
└── {게임 이름}\                   # 압축 파일(zip)로 추가한 게임의 압축 해제 예정 위치 (아래 "게임 추가"/"게임 압축" 참고)

D:\game\GamePlatform\
├── games.json                 # 게임 목록 (GameItem 배열)
├── settings.json              # 카드 크기 등 전역 설정
├── backup\
│   ├── games.daily.json
│   └── games.weekly.json
├── images\
│   └── {게임 Id}\
│       ├── cover.original.*            # 대표 썸네일 (원본 크기 그대로)
│       └── screenshot-{guid}.original.*  # 게임 요약 캡처 (각각 원본 크기 그대로)
└── archives\
    └── {게임 Id}\
        └── {이름-버전}.zip     # 압축된 게임 (게임 폴더 전체) — doc/game-management.md "게임 압축" 참고
```

게임의 실행 파일(exe)은 사용자가 이미 설치해 둔 임의의 위치(다른 드라이브, 읽기 전용 폴더 등)를 가리킬 수 있으므로, video-vault처럼 원본 파일과 같은 폴더에 썸네일을 저장하지 않고 앱 데이터 폴더 안에 게임 Id별로 모아 저장한다.

### 옛 위치(`%LOCALAPPDATA%\GamePlatform\`)에서 자동 이동

앱이 처음에 `%LOCALAPPDATA%\GamePlatform\`를 썼다가 이후 `D:\game\GamePlatform\`로 옮겼다(2026-09-05). 실제 사용자 데이터가 이미 쌓여 있었으므로, 새 버전을 켰을 때 자동으로 마이그레이션한다 (`AppPaths.EnsureAppDataDirectory`, `MainWindow` 시작 시 호출):

1. 새 위치(`D:\game\GamePlatform\`)가 없고 옛 위치가 있으면, 옛 폴더 전체를 새 위치로 복사한 뒤 옛 폴더를 지운다. `Directory.Move`는 드라이브가 다르면("C:\ → D:\") 동작하지 않아서("Move will not work across volumes" — 실제로 겪은 오류) 직접 복사+삭제로 구현했다.
2. **파일을 옮기는 것과 `games.json`에 저장된 절대경로 문자열(`ThumbnailPath`/`Screenshots[].Path`/`ArchivePath`)을 바로잡는 것은 별개다** — 1번은 디스크상의 실제 파일 위치만 바꿀 뿐, 이미 로드된 JSON의 경로 문자열까지 자동으로 바뀌지는 않는다. `MainWindow`가 게임 목록을 불러온 직후 `RewriteLegacyImagePaths()`로 옛 접두사(`%LOCALAPPDATA%\GamePlatform\`)로 시작하는 경로를 전부 새 접두사로 바꿔 다시 저장한다. 이 검사는 옛 경로가 하나도 없으면 아무 것도 하지 않는 가벼운 스캔이라, 마이그레이션이 이번 실행에서 일어났는지 여부와 무관하게 **매번** 실행한다 — 파일 이동과 경로 교정이 서로 다른 실행에서 일어나는 경우(예: 이동만 되고 교정 전에 껐다 켠 경우)에도 안전하게 만회하기 위함이다.

## 환경설정 (메뉴 "설정")

메인 창 메뉴("설정 > 환경설정...")로 여는 `PreferencesWindow`에서 아래 두 값을 편집한다(2026-09-06 추가, 사용자 요청):

- **기본 폴더** (`AppPaths.GamesBaseDir`)
- **압축 명령으로 압축된 파일 위치** (`AppPaths.ArchivesDirOverride`) — 비워두면 기본값(`{기본 폴더}\GamePlatform\archives`)을 쓴다. [게임 관리](game-management.md)의 "게임 압축"(카드 우클릭 메뉴)이 만드는 압축 파일만 여기 영향을 받는다 — "게임 추가"로 등록하는 압축 파일은 항상 기본 폴더에 바로 놓인다(위 "저장 위치" 참고).

### 왜 이 둘은 `settings.json`이 아니라 별도 파일(`config.json`)에 저장하는지

`settings.json`은 `{기본 폴더}\GamePlatform\settings.json`에 있다 — 즉 "기본 폴더가 어디인지"를 알아야 그 파일을 찾을 수 있다. 그런데 기본 폴더 자체를 `settings.json` 안에 저장해버리면, 앱을 다시 켰을 때 그 값을 읽으려고 `settings.json`을 열어야 하는데 그러려면 이미 기본 폴더를 알고 있어야 하는 닭-달걀 문제가 생긴다. 그래서 `AppConfig`(`GamesBaseDir`, `ArchivesDirOverride`)는 항상 고정된 위치(`%LOCALAPPDATA%\GamePlatform\config.json` — 절대 옮겨지지 않는 위치)에 별도로 저장한다(`AppConfigRepository`). `MainWindow`는 시작하자마자 제일 먼저(`AppPaths.EnsureAppDataDirectory`보다도 먼저) 이 설정을 읽어(`AppPaths.Initialize`) `AppPaths.GamesBaseDir`를 채운 뒤에야 나머지(games.json 위치 등)를 계산한다.

### 기본 폴더를 바꾸면 일어나는 일

1. **확인 대화상자**를 거친 뒤, 이 앱의 관리 데이터 폴더(`{옛 기본 폴더}\GamePlatform\`) 전체를 새 기본 폴더 밑으로 옮긴다(`AppPaths.MigrateAppDataDir` — 옛 위치 자동 이동과 같은 복사+삭제 방식, 드라이브가 달라도 안전하다).
2. **게임 목록에 이미 저장된 절대경로도 함께 바로잡는다** — `MainWindow.RewriteGamePathsPrefix`가 옛 관리 데이터 폴더로 시작하는 `ThumbnailPath`/`Screenshots[].Path`/`ArchivePath`를 새 경로로 바꿔 다시 저장한다(위 옛 위치 자동 이동과 같은 종류의 문제: 파일은 옮겼는데 JSON 속 문자열은 안 바뀌는 것을 막기 위함). 이 창(`PreferencesWindow`)은 게임 목록을 모르므로, 이 단계는 `MainWindow.OpenPreferences_Click`이 대화상자가 닫힌 뒤 처리한다.
3. **사용자의 실제 게임 폴더/압축 파일 자체는 옮기지 않는다** — 이미 다른 곳(예: 옛 기본 폴더 자리)에 있는 게임들은 그 자리에 그대로 남고, `ExecutablePath`/`ArchivePath`(기본 폴더 바깥을 가리키는 것들)도 그대로 유효하다. 앞으로 새로 추가하는 게임부터 새 기본 폴더를 쓴다.
- **압축 위치**를 바꾸는 것은 앞으로의 압축 명령에만 영향을 준다 — 이미 만들어진 압축 파일은 옮기지 않는다(각 게임의 `ArchivePath`가 이미 절대경로로 저장되어 있어 계속 유효하다).

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

지금 실제로 저장 중인 게임 목록 파일을 주기적으로 백업한다. 기본값은 `games.json`(`D:\game\GamePlatform\games.json`)이지만, [게임 관리](game-management.md)의 "게임 목록 파일 관리"(메인 창 파일 메뉴)로 다른 파일을 열거나 다른 이름으로 저장하면 **그 파일 기준으로** 바뀐다 — `AppPaths.DailyBackupPath`/`WeeklyBackupPath`가 대상 파일 경로를 인자로 받아, 항상 그 파일과 같은 폴더의 `backup\` 하위 폴더를 가리키기 때문이다. 일간/주간 각각 **파일 하나씩만** 유지한다 (날짜별로 계속 쌓지 않고, 같은 파일에 덮어쓴다).

```
{게임 목록 파일이 있는 폴더}\backup\
├── games.daily.json    # 최근 1일 주기 백업 (덮어씀)
└── games.weekly.json   # 최근 1주 주기 백업 (덮어씀)
```

- `settings.json`에 저장해 둔 `LastDailyBackupUtc`/`LastWeeklyBackupUtc`를 기준으로, 마지막 백업 이후 하루/일주일이 지났으면 그 시점의 게임 목록 파일을 각각의 백업 파일로 복사하고 타임스탬프를 갱신한다. 앱 시작 시 한 번, 그리고 게임 목록 저장 시점마다 이 조건을 확인한다 — 앱을 매일 켜지 않아도 다음 실행 시 지난 기간만큼 자동으로 따라잡는다. 이 타임스탬프는 파일별이 아니라 전역(`settings.json`) 값이라, 다른 파일을 열어도 "마지막 백업 이후 경과 시간"은 이어서 계산된다(파일을 바꿨다고 새로 1일/1주를 기다리지 않는다).
- 복구는 아직 UI로 제공하지 않는다 (필요 시 해당 `backup\` 폴더의 파일을 게임 목록 파일에 수동으로 덮어쓰는 방식). 향후 "백업에서 복원" 메뉴를 추가할 수 있다.

## 오류 처리

예외를 catch해 `MessageBox`로 알리고, 프로그램은 종료하지 않고 계속 동작 가능한 상태를 유지한다 (video-vault와 동일한 정책).

- 실행하려는 파일이 존재하지 않는 경우 → [게임 관리](game-management.md)에 따라 애초에 [실행] 버튼이 비활성화되어 있으므로 이 경로로는 발생하지 않는다.
- 이미지 파일이 손상된 경우 → 기본 아이콘으로 대체 표시
- **`games.json`이 손상되어 읽을 수 없는 경우 → 빈 목록으로 시작한다.** 위 백업 파일이 있다는 오류 메시지와 함께, 사용자가 원하면 백업에서 수동으로 복구할 수 있음을 안내한다 (자동 복구는 하지 않음 — 손상 원인을 사용자가 먼저 확인할 수 있도록).

## 확인/결정이 더 필요한 사항

- 앱 시작 시 모든 게임의 `ExecutablePath` 존재 여부를 매번 다시 검사할지, 아니면 캐시된 값을 쓰다가 [실행] 클릭 시점에만 재확인할지 (전자가 기본값에 가깝다 — video-vault의 `IsExist` 갱신과 동일한 방식).
