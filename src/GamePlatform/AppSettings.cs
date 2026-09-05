namespace GamePlatform;

/// <summary>
/// 재시작 후에도 복원해야 하는 설정(settings.json). doc/common-management.md 참고.
/// </summary>
public class AppSettings
{
    /// <summary><see cref="GameCardSize"/> enum 이름 문자열 (예: "Large"/"Small").</summary>
    public string CardSizePreset { get; set; } = nameof(GameCardSize.Large);

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
