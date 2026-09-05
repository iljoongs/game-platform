using System.IO;

namespace GamePlatform;

/// <summary>
/// games.json을 일간/주간 각각 파일 하나씩만 유지하며 백업한다 (doc/common-management.md "백업" 참고).
/// 마지막 백업 시각은 <see cref="AppSettings"/>에 저장되어 앱을 재시작해도 이어서 계산된다.
/// </summary>
public static class BackupService
{
    private static readonly TimeSpan DailyInterval = TimeSpan.FromDays(1);
    private static readonly TimeSpan WeeklyInterval = TimeSpan.FromDays(7);

    /// <summary>필요하면 백업을 수행하고, 백업 시각이 갱신됐으면 설정도 함께 저장한다.</summary>
    public static void CheckAndBackup(AppSettings settings)
    {
        if (!File.Exists(AppPaths.GamesPath))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var changed = false;

        if (now - settings.LastDailyBackupUtc >= DailyInterval)
        {
            if (TryCopyBackup(AppPaths.DailyBackupPath))
            {
                settings.LastDailyBackupUtc = now;
                changed = true;
            }
        }

        if (now - settings.LastWeeklyBackupUtc >= WeeklyInterval)
        {
            if (TryCopyBackup(AppPaths.WeeklyBackupPath))
            {
                settings.LastWeeklyBackupUtc = now;
                changed = true;
            }
        }

        if (changed)
        {
            SettingsRepository.Save(settings);
        }
    }

    private static bool TryCopyBackup(string destPath)
    {
        try
        {
            AppPaths.EnsureBackupDirectory();
            File.Copy(AppPaths.GamesPath, destPath, overwrite: true);
            return true;
        }
        catch
        {
            // 백업 실패는 부가 기능일 뿐이므로 앱 동작을 막지 않는다.
            return false;
        }
    }
}
