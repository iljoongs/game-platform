namespace GamePlatform;

/// <summary>
/// 게임 데이터의 위치 자체를 가리키는 부트스트랩 설정 — 앱을 시작했을 때 "실제 데이터(games.json 등)가
/// 어디 있는지"를 알아내려면 먼저 이 값부터 읽어야 하므로, 그 실제 데이터와 같은 곳(D:\game\GamePlatform\)에
/// 둘 수 없다. 항상 고정된 위치(<see cref="AppConfigRepository"/>)에 저장한다.
/// </summary>
public class AppConfig
{
    /// <summary>게임 관련 파일의 기본 폴더 — <see cref="AppPaths.GamesBaseDir"/>의 영구 저장값.</summary>
    public string GamesBaseDir { get; set; } = @"D:\game";

    /// <summary>압축 명령으로 만든 압축 파일을 저장할 위치. null/빈 문자열이면 기본값(GamesBaseDir\GamePlatform\archives)을 쓴다.</summary>
    public string? ArchivesDirOverride { get; set; }
}
