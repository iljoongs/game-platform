using System.IO;

namespace GamePlatform;

/// <summary>
/// 게임 목록 / 설정 / 백업 / 이미지 / 압축 파일의 기본 저장 위치를 관리한다.
/// </summary>
public static class AppPaths
{
    /// <summary>게임 관련 파일의 기본 폴더. 폴더/압축 파일로 게임을 추가할 때(exe 드래그드롭과 달리 원본을
    /// 그대로 참조할 수 없는 경우) 실제 저장 위치로 쓴다 — doc/game-management.md "게임 추가" 참고.
    /// 환경설정("설정 > 환경설정")에서 바꿀 수 있으므로 <see cref="Initialize"/>가 시작 시 <see cref="AppConfig"/>에서
    /// 불러와 채운다 — 기본값(D:\game)은 그 전까지(또는 설정 로딩에 실패했을 때)의 대체값일 뿐이다.</summary>
    public static string GamesBaseDir { get; set; } = @"D:\game";

    /// <summary>압축 명령으로 만든 압축 파일의 저장 위치를 기본값(<see cref="AppDataDir"/>\archives) 대신 다른
    /// 곳으로 쓰고 싶을 때 지정. null/빈 문자열이면 기본값을 쓴다 — 환경설정에서 관리.</summary>
    public static string? ArchivesDirOverride { get; set; }

    public static void Initialize(AppConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.GamesBaseDir))
        {
            GamesBaseDir = config.GamesBaseDir;
        }

        ArchivesDirOverride = config.ArchivesDirOverride;
    }

    private static string AppDataDir => Path.Combine(GamesBaseDir, "GamePlatform");

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

    /// <summary>압축 명령으로 만든 압축 파일을 저장하는 기본 폴더 — <see cref="ArchivesDirOverride"/>가
    /// 지정되어 있으면 그 경로를, 아니면 <see cref="AppDataDir"/>\archives를 쓴다.</summary>
    public static string ArchivesDir => string.IsNullOrWhiteSpace(ArchivesDirOverride)
        ? Path.Combine(AppDataDir, "archives")
        : ArchivesDirOverride;

    /// <summary>게임 하나의 압축 파일을 저장하는 폴더 (게임 Id별로 분리 — 압축 파일 이름 자체는 표시 이름을
    /// 쓰므로, 같은 이름의 게임이 여럿이어도 폴더가 겹치지 않게 하기 위함).</summary>
    private static string GameArchiveDir(string gameId) => Path.Combine(ArchivesDir, gameId);

    /// <summary>게임 하나를 통째로 압축한 zip 파일 경로. 파일명은 메인 화면에 보이는 이름(이름-버전)을 그대로
    /// 쓴다 — doc/game-management.md "게임 압축" 참고. 이 경로는 "압축" 명령으로 만드는 압축 파일 전용이며,
    /// 폴더/압축 파일로 새 게임을 추가할 때는 쓰지 않는다 — 그 경우는 <see cref="ReserveUniquePath"/>로
    /// <see cref="GamesBaseDir"/> 밑에 직접 둔다(doc/game-management.md "게임 추가" 참고).</summary>
    public static string GameArchivePath(string gameId, string displayName) =>
        Path.Combine(GameArchiveDir(gameId), $"{FileNameHelper.Sanitize(displayName)}.zip");

    public static void EnsureArchiveDirectory(string gameId) => Directory.CreateDirectory(GameArchiveDir(gameId));

    /// <summary>
    /// 폴더/압축 파일로 게임을 추가할 때 실행 파일이 위치할 폴더를 <see cref="GamesBaseDir"/> 밑에 예약한다.
    /// 실제로 폴더를 만들지는 않는다 (압축 파일로 추가한 경우 실제 압축 해제 시점에 만들어짐).
    /// </summary>
    public static string ReserveGameFolder(string displayName) => ReserveUniquePath(FileNameHelper.Sanitize(displayName));

    /// <summary>
    /// <see cref="GamesBaseDir"/> 밑에서 <paramref name="desiredName"/>과 겹치지 않는 경로를 찾는다 — 폴더든
    /// 파일이든(확장자 포함) 같은 이름이 이미 있으면 "{이름} (2)", "{이름} (3)"...을 붙인다. 아무 것도 만들지는
    /// 않고 경로만 계산한다.
    /// </summary>
    public static string ReserveUniquePath(string desiredName)
    {
        var candidate = Path.Combine(GamesBaseDir, desiredName);
        if (!Directory.Exists(candidate) && !File.Exists(candidate))
        {
            return candidate;
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(desiredName);
        var extension = Path.GetExtension(desiredName);
        for (var suffix = 2; ; suffix++)
        {
            candidate = Path.Combine(GamesBaseDir, $"{nameWithoutExtension} ({suffix}){extension}");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>주어진 경로가 <see cref="GamesBaseDir"/> 바로 밑에 있는지 여부(더 깊은 하위 폴더는 해당하지
    /// 않음 — 예: `D:\game\- rpg -\게임`처럼 분류용 하위 폴더 안에 있으면 false). 게임 추가 시 폴더/압축
    /// 파일을 옮길지 말지 결정하는 데 쓴다 — 이미 기본 폴더 바로 밑에 있으면(예: 압축 명령이 만든 압축 파일도
    /// 결국 이 밑이므로) 다시 옮기지 않지만, 사용자가 기본 폴더 안에 직접 만들어 둔 분류 폴더 안에 있는 경우는
    /// "기본 폴더 밑"으로 보지 않고 바로 밑으로 끌어올린다(2026-09-06 수정 — 분류 폴더 안의 게임을 추가해도
    /// 옮겨지지 않던 문제, 사용자 요청. doc/game-management.md "게임 추가" 참고).</summary>
    public static bool IsDirectlyUnderGamesBaseDir(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var baseFull = Path.GetFullPath(GamesBaseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(full);
        return full.Equals(baseFull, StringComparison.OrdinalIgnoreCase)
            || (parent is not null && parent.Equals(baseFull, StringComparison.OrdinalIgnoreCase));
    }

    /// <returns>옛 위치에서 데이터를 옮겼으면 true. 이 경우 호출자는 <see cref="RewriteLegacyPath"/>로
    /// games.json에 저장된 절대경로(ThumbnailPath/Screenshots/ArchivePath)도 새 위치 기준으로 바로잡아야 한다 —
    /// 파일은 옮겼지만 이미 games.json에 문자열로 박혀 있는 옛 절대경로까지 저절로 바뀌지는 않기 때문이다.</returns>
    public static bool EnsureAppDataDirectory()
    {
        if (!Directory.Exists(AppDataDir) && Directory.Exists(LegacyAppDataDir))
        {
            Directory.CreateDirectory(GamesBaseDir);
            CopyDirectoryRecursive(LegacyAppDataDir, AppDataDir);
            Directory.Delete(LegacyAppDataDir, recursive: true);
            return true;
        }

        Directory.CreateDirectory(AppDataDir);
        return false;
    }

    /// <summary>옛 위치(%LOCALAPPDATA%\GamePlatform) 기준 절대경로를 새 위치(현재 <see cref="GamesBaseDir"/>\GamePlatform)
    /// 기준으로 바꾼다. 그 접두사로 시작하지 않는 경로(또는 null/빈 문자열)는 그대로 돌려준다.</summary>
    public static string? RewriteLegacyPath(string? path) => RewritePathPrefix(path, LegacyAppDataDir, AppDataDir);

    /// <summary>경로가 <paramref name="oldPrefix"/>로 시작하면 <paramref name="newPrefix"/>로 바꿔치기하고,
    /// 그렇지 않으면(또는 null/빈 문자열이면) 그대로 돌려준다 — 폴더를 통째로 옮긴 뒤 그 밑을 가리키던
    /// 절대경로 문자열들을 바로잡는 데 쓴다 (<see cref="RewriteLegacyPath"/>, `MainWindow.RewriteGamePathsPrefix` 참고).</summary>
    public static string? RewritePathPrefix(string? path, string oldPrefix, string newPrefix)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return newPrefix + path[oldPrefix.Length..];
    }

    /// <summary>
    /// 환경설정에서 "기본 폴더"를 바꿨을 때, 이 앱의 관리 데이터 폴더(games.json/settings.json/images/archives/backup)
    /// 를 옛 기본 폴더 밑에서 새 기본 폴더 밑으로 옮긴다. 사용자의 실제 게임 폴더/파일은 옮기지 않는다 — 그건
    /// 여기서 다루는 범위 밖이며, 이미 <see cref="GamesBaseDir"/> 자체가 새 값으로 바뀌므로 앞으로 추가하는
    /// 게임부터 새 위치를 쓰게 된다.
    /// </summary>
    public static void MigrateAppDataDir(string oldGamesBaseDir, string newGamesBaseDir)
    {
        var oldAppDataDir = Path.Combine(oldGamesBaseDir, "GamePlatform");
        var newAppDataDir = Path.Combine(newGamesBaseDir, "GamePlatform");

        if (!Directory.Exists(oldAppDataDir) ||
            string.Equals(Path.GetFullPath(oldAppDataDir), Path.GetFullPath(newAppDataDir), StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(newAppDataDir);
            return;
        }

        Directory.CreateDirectory(newGamesBaseDir);
        CopyDirectoryRecursive(oldAppDataDir, newAppDataDir);
        Directory.Delete(oldAppDataDir, recursive: true);
    }

    /// <summary><see cref="Directory.Move"/>는 서로 다른 드라이브 사이에서는 동작하지 않으므로("Move will not
    /// work across volumes" — 실제로 겪은 오류), 폴더를 옮겨야 할 때는 항상 이 헬퍼로 전체를 복사한 뒤
    /// 원본을 지우는 방식을 쓴다.</summary>
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
