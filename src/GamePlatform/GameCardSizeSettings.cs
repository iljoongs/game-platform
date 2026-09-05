using System.ComponentModel;

namespace GamePlatform;

public enum GameCardSize
{
    Large,
    Small,
}

/// <summary>
/// 메인 화면 카드 크기 프리셋(320x240 / 160x120)을 전역으로 관리하는 싱글턴. XAML이 이 인스턴스의
/// 속성에 직접 바인딩하므로(<see cref="Current"/>), <see cref="Apply"/>로 프리셋을 바꾸면 PropertyChanged를
/// 통해 이미 그려진 모든 카드에 즉시 반영된다. (video-vault의 IconSizeSettings와 같은 역할이지만 프리셋이 2개뿐이다.)
/// </summary>
public class GameCardSizeSettings : INotifyPropertyChanged
{
    public static readonly GameCardSizeSettings Current = new();

    private GameCardSize _preset;
    private double _cardWidth;
    private double _cardHeight;
    private double _thumbnailWidth;
    private double _thumbnailHeight;

    private GameCardSizeSettings() => Apply(GameCardSize.Large);

    public GameCardSize Preset => _preset;

    public double CardWidth
    {
        get => _cardWidth;
        private set { _cardWidth = value; OnPropertyChanged(nameof(CardWidth)); }
    }

    public double CardHeight
    {
        get => _cardHeight;
        private set { _cardHeight = value; OnPropertyChanged(nameof(CardHeight)); }
    }

    public double ThumbnailWidth
    {
        get => _thumbnailWidth;
        private set { _thumbnailWidth = value; OnPropertyChanged(nameof(ThumbnailWidth)); }
    }

    public double ThumbnailHeight
    {
        get => _thumbnailHeight;
        private set { _thumbnailHeight = value; OnPropertyChanged(nameof(ThumbnailHeight)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(GameCardSize size)
    {
        _preset = size;
        (ThumbnailWidth, ThumbnailHeight) = size switch
        {
            GameCardSize.Large => (320.0, 240.0),
            GameCardSize.Small => (160.0, 120.0),
            _ => (320.0, 240.0),
        };
        // 카드 여백(이름 텍스트 + 버튼 행 + 테두리/패딩) — 썸네일 높이에 고정으로 더한다.
        CardWidth = ThumbnailWidth + 20.0;
        CardHeight = ThumbnailHeight + 80.0;
    }

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
