using System.Windows;

namespace GamePlatform;

/// <summary>
/// 주요 창(예: GameInfoWindow)이 마지막으로 열려 있던 화면 위치(Left/Top)를 창 종류 이름 기준으로
/// 기억해두는 공용 저장소. 앱 실행 중에는 이 클래스가 메모리에 직접 들고 있다가, `MainWindow`가 시작 시
/// <see cref="AppSettings.WindowPositions"/>에서 불러와 채우고(<see cref="LoadFrom"/>) 종료 시 다시 그
/// 값을 읽어(<see cref="ToDictionary"/>) 설정 파일에 저장한다. <see cref="SingleInstanceWindow{T}"/>가
/// 창을 열 때/닫을 때 이 클래스를 통해 위치를 읽고 기록한다.
/// (video-vault 프로젝트에서 이미 검증된 로직을 그대로 이식)
/// </summary>
public static class WindowPositionMemory
{
    private static readonly Dictionary<string, (double Left, double Top)> Positions = new();

    public static void LoadFrom(IReadOnlyDictionary<string, double[]> saved)
    {
        Positions.Clear();
        foreach (var (key, value) in saved)
        {
            if (value.Length == 2)
            {
                Positions[key] = (value[0], value[1]);
            }
        }
    }

    public static Dictionary<string, double[]> ToDictionary() =>
        Positions.ToDictionary(kv => kv.Key, kv => new[] { kv.Value.Left, kv.Value.Top });

    public static void Remember(string key, double left, double top) => Positions[key] = (left, top);

    /// <summary>기억해둔 위치가 있고, 지금도 화면(가상 데스크톱 전체) 안에 있을 만한 값이면 true를 반환한다.
    /// 모니터 구성이 그 사이 바뀌어(예: 보조 모니터 연결 해제) 화면 밖 좌표가 남아있는 경우를 걸러낸다.</summary>
    public static bool TryGetOnScreenPosition(string key, out double left, out double top)
    {
        if (Positions.TryGetValue(key, out var pos) && IsOnScreen(pos.Left, pos.Top))
        {
            left = pos.Left;
            top = pos.Top;
            return true;
        }

        left = 0;
        top = 0;
        return false;
    }

    /// <summary>창의 타이틀바 일부라도 화면(가상 데스크톱 전체) 안에 걸쳐 있을 만한 좌표인지 확인한다.
    /// <see cref="MainWindow"/>가 자신의 마지막 크기/위치를 복원할 때도 이 검증을 공유해서 쓴다.</summary>
    public static bool IsOnScreen(double left, double top)
    {
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        // 창의 타이틀바 일부라도(대략 100px) 화면 안에 걸쳐 있으면 충분히 "화면 안"으로 취급한다.
        const double margin = 100;
        return left <= virtualRight - margin && left >= virtualLeft - margin
            && top <= virtualBottom - margin && top >= virtualTop - margin;
    }
}
