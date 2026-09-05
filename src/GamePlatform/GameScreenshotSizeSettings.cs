using System.ComponentModel;

namespace GamePlatform;

public enum GameScreenshotSize
{
    Large,
    Medium,
    Small,
}

/// <summary>
/// GameInfoWindow의 게임 요약 갤러리 이미지 크기 프리셋(320x240 / 160x120 / 80x60)을 전역으로 관리하는
/// 싱글턴. GameCardSizeSettings와 같은 패턴 — XAML이 이 인스턴스에 직접 바인딩하므로 <see cref="Apply"/>로
/// 프리셋을 바꾸면 이미 열려 있는 갤러리에도 즉시 반영된다.
/// </summary>
public class GameScreenshotSizeSettings : INotifyPropertyChanged
{
    public static readonly GameScreenshotSizeSettings Current = new();

    private GameScreenshotSize _preset;
    private double _width;
    private double _height;

    private GameScreenshotSizeSettings() => Apply(GameScreenshotSize.Large);

    public GameScreenshotSize Preset => _preset;

    public double Width
    {
        get => _width;
        private set { _width = value; OnPropertyChanged(nameof(Width)); }
    }

    public double Height
    {
        get => _height;
        private set { _height = value; OnPropertyChanged(nameof(Height)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(GameScreenshotSize size)
    {
        _preset = size;
        (Width, Height) = size switch
        {
            GameScreenshotSize.Large => (320.0, 240.0),
            GameScreenshotSize.Medium => (160.0, 120.0),
            GameScreenshotSize.Small => (80.0, 60.0),
            _ => (320.0, 240.0),
        };
    }

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
