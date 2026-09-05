using System.IO;
using System.Text.Json;

namespace GamePlatform;

/// <summary>설정(settings.json) 읽기/쓰기. 손상되어 있으면 기본값으로 시작한다.</summary>
public static class SettingsRepository
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(AppPaths.SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            AppPaths.EnsureAppDataDirectory();
            var json = JsonSerializer.Serialize(settings, Options);
            File.WriteAllText(AppPaths.SettingsPath, json);
        }
        catch
        {
            // 설정 저장 실패는 다음 실행에 기본값으로 대체되므로 앱 동작에 치명적이지 않다.
        }
    }
}
