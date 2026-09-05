using System.Windows;

namespace GamePlatform;

/// <summary>
/// 창 종류(T)별로 동시에 인스턴스가 하나만 열리도록 보장하는 공용 헬퍼.
/// 같은 종류의 창을 다시 열려고 하면 기존에 열려 있던 인스턴스를 먼저 닫는다.
/// (video-vault 프로젝트에서 이미 검증된 로직을 그대로 이식)
/// </summary>
public static class SingleInstanceWindow<T> where T : Window
{
    private static T? _current;

    /// <summary>현재 열려 있는 이 종류의 창 인스턴스(없으면 null).</summary>
    public static T? Current => _current;

    /// <summary>
    /// 같은 종류의 기존 창이 열려 있으면 닫고, 이 창을 새로운 "현재 창"으로 등록한 뒤 보여준다.
    /// <see cref="WindowPositionMemory"/>에 이 창 종류가 마지막으로 열려 있던 위치가 기억되어 있으면(그리고
    /// 그 위치가 여전히 화면 안이면) 그 위치에 열고, 없으면 XAML에 정의된 기본 시작 위치를 그대로 쓴다.
    /// 창이 닫힐 때는 그 시점의 위치를 다시 기억해둔다.
    /// </summary>
    public static void Show(T window)
    {
        _current?.Close();

        var key = typeof(T).Name;
        if (WindowPositionMemory.TryGetOnScreenPosition(key, out var left, out var top))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = left;
            window.Top = top;
        }

        _current = window;
        window.Closed += (_, _) =>
        {
            WindowPositionMemory.Remember(key, window.Left, window.Top);

            if (ReferenceEquals(_current, window))
            {
                _current = null;
            }
        };

        window.Show();
        window.Activate();
    }
}
