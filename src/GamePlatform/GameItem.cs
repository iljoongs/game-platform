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
    private bool _isCompressed;
    private string? _archivePath;
    private long _archiveSizeBytes;
    private DateTime? _compressedAtUtc;
    private bool _isArchiveValid;
    private bool _isBusy;
    private double _rating;
    private double _gentlemanGrade;

    /// <summary>고유 식별자. 이미지 저장 폴더명(<see cref="AppPaths.GameImagesDir"/>)으로 쓰인다.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } }
    }

    public string Version
    {
        get => _version;
        set { if (_version != value) { _version = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); } }
    }

    /// <summary>화면에 표시할 이름. 같은 이름의 다른 버전을 구분할 수 있도록 "이름-버전" 형식을 쓴다
    /// (버전이 비어 있으면 이름만). 메인 화면 카드/정보 창 제목 등 게임을 식별해서 보여주는 모든 곳에서 사용한다.</summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Version) ? Name : $"{Name}-{Version}";

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

    /// <summary>메인 카드 대표 썸네일 경로. 원본 크기 그대로 저장하고, 화면에는 스케일해서 보여준다
    /// (doc/game-management.md "대표 썸네일 지정" 참고 — 별도의 리사이즈본을 만들지 않는다).</summary>
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
        private set { if (_isExecutableValid != value) { _isExecutableValid = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsRunButtonEnabled)); } }
    }

    public void RefreshExecutableValid() =>
        IsExecutableValid = !string.IsNullOrEmpty(ExecutablePath) && File.Exists(ExecutablePath);

    /// <summary>이 게임이 지금 압축된(원본 폴더는 지워지고 zip으로만 존재하는) 상태인지 여부 —
    /// doc/game-management.md "게임 압축" 참고. 압축 중에는 <see cref="ExecutablePath"/> 파일이 실제로
    /// 존재하지 않으므로 <see cref="IsExecutableValid"/>는 자연히 false가 된다.</summary>
    public bool IsCompressed
    {
        get => _isCompressed;
        set
        {
            if (_isCompressed != value)
            {
                _isCompressed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRunButtonEnabled));
                OnPropertyChanged(nameof(CanCompress));
                OnPropertyChanged(nameof(CanExtract));
            }
        }
    }

    /// <summary>압축 파일(zip) 경로. 압축 상태가 아니면 null (<see cref="AppPaths.GameArchivePath"/>에 저장됨).</summary>
    public string? ArchivePath
    {
        get => _archivePath;
        set { if (_archivePath != value) { _archivePath = value; OnPropertyChanged(); } }
    }

    /// <summary>압축 파일 크기(바이트). 정보 창의 압축 정보 표시용.</summary>
    public long ArchiveSizeBytes
    {
        get => _archiveSizeBytes;
        set { if (_archiveSizeBytes != value) { _archiveSizeBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(ArchiveSizeDisplay)); } }
    }

    /// <summary>압축을 수행한 시각(UTC). 정보 창의 압축 정보 표시용.</summary>
    public DateTime? CompressedAtUtc
    {
        get => _compressedAtUtc;
        set { if (_compressedAtUtc != value) { _compressedAtUtc = value; OnPropertyChanged(); } }
    }

    [JsonIgnore]
    public string ArchiveSizeDisplay => ArchiveSizeBytes <= 0 ? string.Empty : $"{ArchiveSizeBytes / 1024.0 / 1024.0:N1} MB";

    /// <summary><see cref="ArchivePath"/>가 실제로 존재하는지 여부. JSON에는 저장하지 않고, 앱 시작 시/
    /// 압축·압축 해제 시 <see cref="RefreshArchiveValid"/>로 다시 계산한다.</summary>
    [JsonIgnore]
    public bool IsArchiveValid
    {
        get => _isArchiveValid;
        private set { if (_isArchiveValid != value) { _isArchiveValid = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsRunButtonEnabled)); } }
    }

    public void RefreshArchiveValid() =>
        IsArchiveValid = IsCompressed && !string.IsNullOrEmpty(ArchivePath) && File.Exists(ArchivePath);

    /// <summary>메인 카드 "실행" 버튼의 활성화 여부. 압축 상태면 실행할 파일이 없으므로 항상 비활성화되고,
    /// 그 외에는 실행 파일이 있어야 활성화된다. 이 게임 자신의 압축/압축 해제가 진행 중이면(<see cref="IsBusy"/>)
    /// 항상 비활성화된다.</summary>
    [JsonIgnore]
    public bool IsRunButtonEnabled => !IsBusy && !IsCompressed && IsExecutableValid;

    /// <summary>카드 우클릭 메뉴의 "압축" 항목을 보여줄지 여부 — 이미 압축된 게임은 다시 압축할 수 없고
    /// (압축을 풀려면 같은 메뉴의 "압축 풀기" 항목을 쓴다), 이 게임 자신의 압축/압축 해제가 진행 중일 때도 숨긴다.</summary>
    [JsonIgnore]
    public bool CanCompress => !IsBusy && !IsCompressed;

    /// <summary>카드 우클릭 메뉴의 "압축 풀기" 항목을 보여줄지 여부 — 압축된 게임에서만 보이고,
    /// 이 게임 자신의 압축/압축 해제가 진행 중일 때는 숨긴다.</summary>
    [JsonIgnore]
    public bool CanExtract => !IsBusy && IsCompressed;

    /// <summary>이 게임의 압축/압축 해제가 지금 진행 중인지 여부 — 진행 중에는 같은 게임에 대한 압축/압축 해제를
    /// 중복으로 시작할 수 없게 막는다(다른 게임은 동시에 압축할 수 있음, doc/game-management.md "게임 압축" 참고).
    /// 저장하지 않는 런타임 전용 상태.</summary>
    [JsonIgnore]
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRunButtonEnabled));
                OnPropertyChanged(nameof(CanCompress));
            }
        }
    }

    /// <summary>평점(0~100) — 정보 창 게임 요약 위의 진행바 형태 슬라이더로 표시/편집한다
    /// (doc/game-management.md "정보 창" 참고).</summary>
    public double Rating
    {
        get => _rating;
        set { if (_rating != value) { _rating = value; OnPropertyChanged(); } }
    }

    /// <summary>신사 등급(0~100) — <see cref="Rating"/>과 같은 방식의 별개 평가 항목.</summary>
    public double GentlemanGrade
    {
        get => _gentlemanGrade;
        set { if (_gentlemanGrade != value) { _gentlemanGrade = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>게임 요약 갤러리 캡처 이미지 한 장. 원본 크기 그대로 저장하고 화면에는 스케일해서 보여준다
/// (메인 카드 대표 썸네일과 같은 방식 — doc/game-management.md "게임 요약" 참고).</summary>
public class ScreenshotItem
{
    public string Path { get; set; } = string.Empty;

    /// <summary>실제 저장된 스크린샷이 아니라, 갤러리 맨 앞에 표시하는 "메인 화면 대표 썸네일" 슬롯이면 true
    /// (GameInfoWindow가 표시 목적으로만 만드는 값 — <see cref="GameItem.Screenshots"/>에는 절대 들어가지 않으므로
    /// 저장할 필요가 없다). doc/game-management.md "게임 요약 첫 번째 슬롯" 참고.</summary>
    [JsonIgnore]
    public bool IsCover { get; init; }

    /// <summary>우클릭 삭제 메뉴를 보여줄지 여부 — 대표 썸네일 슬롯(<see cref="IsCover"/>)은 여기서 지울 수 없다.</summary>
    [JsonIgnore]
    public bool IsDeletable => !IsCover;
}
