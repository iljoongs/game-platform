using System.IO;
using System.Text.Json;

namespace GamePlatform;

/// <summary>
/// <see cref="AppConfig"/>(게임 기본 폴더/압축 위치 부트스트랩 설정)를 항상 고정된 위치
/// (%LOCALAPPDATA%\GamePlatform\config.json)에서 읽고 쓴다 — 이 파일 자체의 위치는 설정으로 바꿀 수 없다
/// (바꿀 수 있다면 그 설정을 어디서 읽어야 할지 알 수 없는 닭-달걀 문제가 생기므로).
/// </summary>
public static class AppConfigRepository
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamePlatform", "config.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new AppConfig();
            }

            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var json = JsonSerializer.Serialize(config, Options);
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // 부트스트랩 설정 저장 실패는 다음 실행에 기본값(D:\game)으로 대체되므로 앱 동작에 치명적이지 않다.
        }
    }
}
