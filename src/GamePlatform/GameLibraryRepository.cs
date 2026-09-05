using System.IO;
using System.Text.Json;

namespace GamePlatform;

/// <summary>
/// 게임 목록(games.json) 읽기/쓰기. doc/common-management.md 결정에 따라, 파일이 손상되어 읽을 수 없으면
/// 예외를 던지지 않고 빈 목록으로 시작한다 (자동 복구는 하지 않음 — 백업에서 수동 복구 가능).
/// </summary>
public static class GameLibraryRepository
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static List<GameItem> Load()
    {
        try
        {
            if (!File.Exists(AppPaths.GamesPath))
            {
                return new List<GameItem>();
            }

            var json = File.ReadAllText(AppPaths.GamesPath);
            return JsonSerializer.Deserialize<List<GameItem>>(json, Options) ?? new List<GameItem>();
        }
        catch
        {
            return new List<GameItem>();
        }
    }

    public static void Save(IEnumerable<GameItem> games)
    {
        AppPaths.EnsureAppDataDirectory();
        var json = JsonSerializer.Serialize(games, Options);
        File.WriteAllText(AppPaths.GamesPath, json);
    }
}
