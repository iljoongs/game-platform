namespace GamePlatform;

/// <summary>
/// 재시작 후에도 복원해야 하는 설정(settings.json). doc/common-management.md 참고.
/// </summary>
public class AppSettings
{
    /// <summary><see cref="GameCardSize"/> enum 이름 문자열 (예: "Large"/"Small").</summary>
    public string CardSizePreset { get; set; } = nameof(GameCardSize.Large);

    /// <summary><see cref="GameScreenshotSize"/> enum 이름 문자열 (예: "Large"/"Medium"/"Small") — 정보 창 게임 요약 갤러리 이미지 크기.</summary>
    public string ScreenshotSizePreset { get; set; } = nameof(GameScreenshotSize.Large);

    /// <summary><see cref="GameViewMode"/> enum 이름 문자열 (예: "Icon"/"List") — 메인 화면 카드/리스트 보기 전환.</summary>
    public string ViewMode { get; set; } = nameof(GameViewMode.Icon);

    /// <summary><see cref="GameSortOrder"/> enum 이름 문자열 (예: "Ascending"/"Descending") — 메인 화면 게임 정렬 순서.</summary>
    public string SortOrder { get; set; } = nameof(GameSortOrder.Ascending);

    /// <summary>리스트 보기(테이블)의 "버전"/"평점"/"신사 등급"/"폴더" 칼럼 표시 여부 — 테이블 헤더 우클릭 메뉴로
    /// 전환한다(doc/game-management.md "메인 화면" 참고). "게임 이름"/"정보"/"실행"은 항상 보이므로 설정이 없다.</summary>
    public bool ListShowVersion { get; set; } = true;
    public bool ListShowRating { get; set; } = true;
    public bool ListShowGentlemanGrade { get; set; } = true;
    public bool ListShowFolder { get; set; } = true;

    /// <summary>리스트 보기(테이블) 칼럼 너비 — 사용자가 헤더 경계를 드래그해서 조절하면 저장되어 다음 실행에도
    /// 유지된다(doc/game-management.md "메인 화면" 참고). 키는 칼럼 이름("Name"/"Version"/"Rating"/
    /// "GentlemanGrade"/"Folder"/"Info"/"Run", `MainWindow._listColumnKeys` 참고).</summary>
    public Dictionary<string, double> ListColumnWidths { get; set; } = new();

    /// <summary>마지막으로 선택했던 게임의 <see cref="GameItem.Id"/> — 다음 실행 시 같은 게임을 다시
    /// 선택된 상태로 복원한다(doc/game-management.md "메인 화면" 참고, 사용자 요청). 선택한 적이 없거나
    /// 그 게임이 목록에서 지워졌으면 null.</summary>
    public string? LastSelectedGameId { get; set; }

    public double? MainWindowWidth { get; set; }
    public double? MainWindowHeight { get; set; }
    public double? MainWindowLeft { get; set; }
    public double? MainWindowTop { get; set; }

    /// <summary>MainWindow 이외의 창(예: GameInfoWindow)의 마지막 위치. 키는 창 클래스 이름.</summary>
    public Dictionary<string, double[]> WindowPositions { get; set; } = new();

    /// <summary>MainWindow 이외의 창(예: GameInfoWindow)의 마지막 크기. 키는 창 클래스 이름.</summary>
    public Dictionary<string, double[]> WindowSizes { get; set; } = new();

    public DateTime LastDailyBackupUtc { get; set; } = DateTime.MinValue;
    public DateTime LastWeeklyBackupUtc { get; set; } = DateTime.MinValue;
}
