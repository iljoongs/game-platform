using System.IO;
using System.Text.Json;

namespace GamePlatform;

/// <summary>
/// 게임 목록(games.json) 읽기/쓰기. doc/common-management.md 결정에 따라, 파일이 손상되어 읽을 수 없으면
/// 예외를 던지지 않고 빈 목록으로 시작한다 (자동 복구는 하지 않음 — 백업에서 수동 복구 가능).
/// 메인 창의 파일 메뉴(열기/저장/다른 이름으로 저장)로 기본 위치(<see cref="AppPaths.GamesPath"/>)가 아닌
/// 임의의 파일을 다룰 수 있으므로, 경로를 항상 인자로 받는다.
/// </summary>
public static class GameLibraryRepository
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static List<GameItem> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new List<GameItem>();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<GameItem>>(json, Options) ?? new List<GameItem>();
        }
        catch
        {
            return new List<GameItem>();
        }
    }

    public static void Save(IEnumerable<GameItem> games, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(games, Options);
        File.WriteAllText(path, json);
    }
}
