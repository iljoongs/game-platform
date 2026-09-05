namespace GamePlatform;

/// <summary>
/// 주요 창(예: GameInfoWindow)이 마지막으로 열려 있던 창 크기(Width/Height)를 창 종류 이름 기준으로
/// 기억해두는 공용 저장소. <see cref="WindowPositionMemory"/>와 같은 패턴 — 앱 실행 중에는 이 클래스가
/// 메모리에 직접 들고 있다가, `MainWindow`가 시작 시 <see cref="AppSettings.WindowSizes"/>에서 불러와
/// 채우고(<see cref="LoadFrom"/>) 종료 시 다시 그 값을 읽어(<see cref="ToDictionary"/>) 설정 파일에
/// 저장한다. 위치와 달리 <see cref="SingleInstanceWindow{T}"/>가 자동으로 처리해주지 않으므로, 크기를
/// 기억해야 하는 각 창이 생성자/Closed에서 직접 <see cref="TryGetSize"/>/<see cref="Remember"/>를 호출한다.
/// (video-vault 프로젝트에서 이미 검증된 로직을 그대로 이식)
/// </summary>
public static class WindowSizeMemory
{
    private static readonly Dictionary<string, (double Width, double Height)> Sizes = new();

    public static void LoadFrom(IReadOnlyDictionary<string, double[]> saved)
    {
        Sizes.Clear();
        foreach (var (key, value) in saved)
        {
            if (value.Length == 2)
            {
                Sizes[key] = (value[0], value[1]);
            }
        }
    }

    public static Dictionary<string, double[]> ToDictionary() =>
        Sizes.ToDictionary(kv => kv.Key, kv => new[] { kv.Value.Width, kv.Value.Height });

    public static void Remember(string key, double width, double height) => Sizes[key] = (width, height);

    public static bool TryGetSize(string key, out double width, out double height)
    {
        if (Sizes.TryGetValue(key, out var size) && size.Width > 0 && size.Height > 0)
        {
            width = size.Width;
            height = size.Height;
            return true;
        }

        width = 0;
        height = 0;
        return false;
    }
}
