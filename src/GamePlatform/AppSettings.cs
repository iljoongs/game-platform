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
