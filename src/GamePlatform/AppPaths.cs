using System.IO;

namespace GamePlatform;

/// <summary>
/// 게임 목록 / 설정 / 백업 / 이미지의 기본 저장 위치를 관리한다.
/// </summary>
public static class AppPaths
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamePlatform");

    /// <summary>게임 목록 전체를 저장하는 유일한 파일.</summary>
    public static string GamesPath => Path.Combine(AppDataDir, "games.json");

    public static string SettingsPath => Path.Combine(AppDataDir, "settings.json");

    private static string BackupDir => Path.Combine(AppDataDir, "backup");

    /// <summary>최근 1일 주기 백업 (덮어씀, 파일 하나만 유지).</summary>
    public static string DailyBackupPath => Path.Combine(BackupDir, "games.daily.json");

    /// <summary>최근 1주 주기 백업 (덮어씀, 파일 하나만 유지).</summary>
    public static string WeeklyBackupPath => Path.Combine(BackupDir, "games.weekly.json");

    private static string ImagesDir => Path.Combine(AppDataDir, "images");

    /// <summary>게임 하나의 이미지(대표 썸네일/게임 요약 캡처)를 저장하는 폴더.</summary>
    public static string GameImagesDir(string gameId) => Path.Combine(ImagesDir, gameId);

    public static void EnsureAppDataDirectory() => Directory.CreateDirectory(AppDataDir);

    public static void EnsureBackupDirectory() => Directory.CreateDirectory(BackupDir);
}
