using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace GamePlatform;

/// <summary>
/// 게임 목록(games.json)의 항목 하나. doc/game-management.md의 데이터 모델 참고.
/// </summary>
public class GameItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _version = string.Empty;
    private string _description = string.Empty;
    private string? _executablePath;
    private string? _thumbnailPath;
    private bool _isExecutableValid;

    /// <summary>고유 식별자. 이미지 저장 폴더명(<see cref="AppPaths.GameImagesDir"/>)으로 쓰인다.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    public string Version
    {
        get => _version;
        set { if (_version != value) { _version = value; OnPropertyChanged(); } }
    }

    public string Description
    {
        get => _description;
        set { if (_description != value) { _description = value; OnPropertyChanged(); } }
    }

    public string? ExecutablePath
    {
        get => _executablePath;
        set { if (_executablePath != value) { _executablePath = value; OnPropertyChanged(); } }
    }

    /// <summary>메인 카드 대표 썸네일(320x240 이내 리사이즈본) 경로.</summary>
    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        set
        {
            if (_thumbnailPath != value)
            {
                _thumbnailPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasThumbnail));
            }
        }
    }

    /// <summary>대표 썸네일의 리사이즈 전 원본 경로.</summary>
    public string? ThumbnailOriginalPath { get; set; }

    /// <summary>게임 요약 갤러리용 캡처 이미지 목록.</summary>
    [JsonInclude]
    public ObservableCollection<ScreenshotItem> Screenshots { get; private set; } = new();

    [JsonIgnore]
    public bool HasThumbnail => !string.IsNullOrEmpty(ThumbnailPath);

    /// <summary><see cref="ExecutablePath"/>가 실제로 존재하는지 여부. JSON에는 저장하지 않고,
    /// 앱 시작 시/경로 변경 시 <see cref="RefreshExecutableValid"/>로 다시 계산한다 (doc/game-management.md 참고).</summary>
    [JsonIgnore]
    public bool IsExecutableValid
    {
        get => _isExecutableValid;
        private set { if (_isExecutableValid != value) { _isExecutableValid = value; OnPropertyChanged(); } }
    }

    public void RefreshExecutableValid() =>
        IsExecutableValid = !string.IsNullOrEmpty(ExecutablePath) && File.Exists(ExecutablePath);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>게임 요약 갤러리 캡처 이미지 한 장 (리사이즈본/원본 경로 쌍).</summary>
public class ScreenshotItem
{
    public string Path { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
}
