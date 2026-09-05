using System.IO;

namespace GamePlatform;

/// <summary>
/// 게임 목록 / 설정 / 백업 / 이미지 / 압축 파일의 기본 저장 위치를 관리한다.
/// </summary>
public static class AppPaths
{
    /// <summary>게임 관련 파일의 기본 폴더. 폴더/압축 파일로 게임을 추가할 때(exe 드래그드롭과 달리 원본을
    /// 그대로 참조할 수 없는 경우) 실제 저장 위치로 쓴다 — doc/game-management.md "게임 추가" 참고.</summary>
    public static string GamesBaseDir { get; } = @"D:\game";

    private static readonly string AppDataDir = Path.Combine(GamesBaseDir, "GamePlatform");

    /// <summary>이 폴더를 D:\game 밑으로 옮기기 전(2026-09-05 이전)에 쓰던 위치. 이미 이 위치에 데이터가
    /// 있으면 <see cref="EnsureAppDataDirectory"/>가 한 번만 새 위치로 옮긴다.</summary>
    private static readonly string LegacyAppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamePlatform");

    /// <summary>게임 목록 전체를 저장하는 유일한 파일.</summary>
    public static string GamesPath => Path.Combine(AppDataDir, "games.json");

    public static string SettingsPath => Path.Combine(AppDataDir, "settings.json");

    /// <summary>게임 목록 파일(어떤 파일이든 — 메인 창의 파일 메뉴로 다른 파일을 열었을 수도 있으므로) 바로
    /// 옆에 두는 백업 폴더. 백업은 항상 "지금 실제로 저장 중인 파일" 기준으로 동작한다.</summary>
    private static string BackupDir(string gamesPath) => Path.Combine(Path.GetDirectoryName(gamesPath)!, "backup");

    /// <summary>최근 1일 주기 백업 (덮어씀, 파일 하나만 유지).</summary>
    public static string DailyBackupPath(string gamesPath) => Path.Combine(BackupDir(gamesPath), "games.daily.json");

    /// <summary>최근 1주 주기 백업 (덮어씀, 파일 하나만 유지).</summary>
    public static string WeeklyBackupPath(string gamesPath) => Path.Combine(BackupDir(gamesPath), "games.weekly.json");

    private static string ImagesDir => Path.Combine(AppDataDir, "images");

    /// <summary>게임 하나의 이미지(대표 썸네일/게임 요약 캡처)를 저장하는 폴더.</summary>
    public static string GameImagesDir(string gameId) => Path.Combine(ImagesDir, gameId);

    private static string ArchivesDir => Path.Combine(AppDataDir, "archives");

    /// <summary>게임 하나의 압축 파일을 저장하는 폴더 (게임 Id별로 분리 — 압축 파일 이름 자체는 표시 이름을
    /// 쓰므로, 같은 이름의 게임이 여럿이어도 폴더가 겹치지 않게 하기 위함).</summary>
    private static string GameArchiveDir(string gameId) => Path.Combine(ArchivesDir, gameId);

    /// <summary>게임 하나를 통째로 압축한 zip 파일 경로. 파일명은 메인 화면에 보이는 이름(이름-버전)을 그대로
    /// 쓴다 — doc/game-management.md "게임 압축" 참고.</summary>
    public static string GameArchivePath(string gameId, string displayName) =>
        Path.Combine(GameArchiveDir(gameId), $"{FileNameHelper.Sanitize(displayName)}.zip");

    public static void EnsureArchiveDirectory(string gameId) => Directory.CreateDirectory(GameArchiveDir(gameId));

    /// <summary>
    /// 폴더/압축 파일로 게임을 추가할 때 실행 파일이 위치할 폴더를 <see cref="GamesBaseDir"/> 밑에 예약한다.
    /// 같은 이름의 폴더가 이미 있으면 " (2)", " (3)"... 을 붙여 겹치지 않게 한다. 폴더 자체를 만들지는 않는다
    /// (실제 압축 해제 시점에 만들어짐).
    /// </summary>
    public static string ReserveGameFolder(string displayName)
    {
        var baseName = FileNameHelper.Sanitize(displayName);
        var candidate = Path.Combine(GamesBaseDir, baseName);
        for (var suffix = 2; Directory.Exists(candidate) || File.Exists(candidate); suffix++)
        {
            candidate = Path.Combine(GamesBaseDir, $"{baseName} ({suffix})");
        }

        return candidate;
    }

    /// <returns>옛 위치에서 데이터를 옮겼으면 true. 이 경우 호출자는 <see cref="RewriteLegacyPath"/>로
    /// games.json에 저장된 절대경로(ThumbnailPath/Screenshots/ArchivePath)도 새 위치 기준으로 바로잡아야 한다 —
    /// 파일은 옮겼지만 이미 games.json에 문자열로 박혀 있는 옛 절대경로까지 저절로 바뀌지는 않기 때문이다.</returns>
    public static bool EnsureAppDataDirectory()
    {
        if (!Directory.Exists(AppDataDir) && Directory.Exists(LegacyAppDataDir))
        {
            MigrateLegacyAppDataDirectory();
            return true;
        }

        Directory.CreateDirectory(AppDataDir);
        return false;
    }

    /// <summary>옛 위치(%LOCALAPPDATA%\GamePlatform) 기준 절대경로를 새 위치(D:\game\GamePlatform) 기준으로
    /// 바꾼다. 그 접두사로 시작하지 않는 경로(또는 null/빈 문자열)는 그대로 돌려준다.</summary>
    public static string? RewriteLegacyPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith(LegacyAppDataDir, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return AppDataDir + path[LegacyAppDataDir.Length..];
    }

    /// <summary>
    /// 옛 위치(보통 C:\의 %LOCALAPPDATA%)에서 새 위치(D:\game\GamePlatform)로 데이터를 옮긴다.
    /// <see cref="Directory.Move"/>는 서로 다른 드라이브 사이에서는 동작하지 않으므로("Move will not work
    /// across volumes" — 실제로 겪은 오류), 전체를 복사한 뒤 원본을 지우는 방식으로 직접 구현했다.
    /// </summary>
    private static void MigrateLegacyAppDataDirectory()
    {
        Directory.CreateDirectory(GamesBaseDir);
        CopyDirectoryRecursive(LegacyAppDataDir, AppDataDir);
        Directory.Delete(LegacyAppDataDir, recursive: true);
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var filePath in Directory.GetFiles(sourceDir))
        {
            File.Copy(filePath, Path.Combine(destDir, Path.GetFileName(filePath)), overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectoryRecursive(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
        }
    }

    public static void EnsureBackupDirectory(string gamesPath) => Directory.CreateDirectory(BackupDir(gamesPath));
}
