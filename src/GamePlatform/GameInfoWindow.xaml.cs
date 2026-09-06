using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GamePlatform;

/// <summary>
/// 게임 정보 창. 필드가 바뀌면 별도의 "저장" 버튼 없이 즉시 <see cref="_onChanged"/>(MainWindow의 저장 로직)를
/// 호출한다 — doc/game-management.md "저장 시점" 참고. 창 종류당 하나만 열리도록 MainWindow에서
/// <see cref="SingleInstanceWindow{T}"/>로 띄운다.
/// </summary>
public partial class GameInfoWindow : Window
{
    private GameItem _item;
    private readonly IEnumerable<GameItem> _allGames;
    private readonly AppSettings _settings;
    private readonly Action _onChanged;
    private bool _isLoading;
    private bool _isSyncingRating;
    private bool _isSyncingGentlemanGrade;

    /// <summary>게임 요약 갤러리에 실제로 보여주는 목록. 맨 앞에 메인 화면 대표 썸네일(<see cref="ScreenshotItem.IsCover"/>)을
    /// 얹고 그 뒤에 <see cref="GameItem.Screenshots"/>를 이어붙인 것 — <see cref="RefreshGallery"/>로 다시 만든다.</summary>
    private readonly ObservableCollection<ScreenshotItem> _gallery = new();

    public GameInfoWindow(GameItem item, IEnumerable<GameItem> allGames, AppSettings settings, Action onChanged)
    {
        InitializeComponent();
        _item = item;
        _allGames = allGames;
        _settings = settings;
        _onChanged = onChanged;

        _isLoading = true;
        ScreenshotsItemsControl.ItemsSource = _gallery;
        LoadFromItem();
        _item.PropertyChanged += Item_PropertyChanged;

        (GameScreenshotSizeSettings.Current.Preset switch
        {
            GameScreenshotSize.Medium => ScreenshotMediumRadio,
            GameScreenshotSize.Small => ScreenshotSmallRadio,
            _ => ScreenshotLargeRadio,
        }).IsChecked = true;
        _isLoading = false;

        if (WindowSizeMemory.TryGetSize(nameof(GameInfoWindow), out var width, out var height))
        {
            Width = width;
            Height = height;
        }
    }

    /// <summary>메인 화면에서 다른 게임을 선택하면, 이미 열려 있는 이 창을 새로 열지 않고 그 자리에서 새
    /// 게임의 내용으로 바꿔 보여준다(doc/game-management.md "정보 창" 참고, 사용자 요청). 지금 보고 있는
    /// 게임과 같으면 아무 것도 하지 않는다.</summary>
    public void SwitchTo(GameItem newItem)
    {
        if (ReferenceEquals(_item, newItem))
        {
            return;
        }

        _item.PropertyChanged -= Item_PropertyChanged;
        _item = newItem;
        _item.PropertyChanged += Item_PropertyChanged;

        _isLoading = true;
        LoadFromItem();
        _isLoading = false;
    }

    /// <summary>텍스트박스/슬라이더/갤러리/압축 정보/창 제목 등 <see cref="_item"/>에 의존하는 화면 요소를
    /// 전부 지금의 <see cref="_item"/> 값으로 채운다 — 생성자와 <see cref="SwitchTo"/> 양쪽에서 쓴다. 항상
    /// <see cref="_isLoading"/>가 true인 동안 호출해서, 값을 채우는 과정에서 TextChanged 등으로 다시
    /// <see cref="_onChanged"/>가 불리거나 <see cref="_item"/>에 값이 덮어써지지 않게 한다.</summary>
    private void LoadFromItem()
    {
        NameTextBox.Text = _item.Name;
        VersionTextBox.Text = _item.Version;
        DescriptionTextBox.Text = _item.Description;
        ExecutablePathTextBox.Text = _item.ExecutablePath;
        RatingSlider.Value = _item.Rating;
        RatingTextBox.Text = _item.Rating.ToString("0");
        GentlemanGradeSlider.Value = _item.GentlemanGrade;
        GentlemanGradeTextBox.Text = _item.GentlemanGrade.ToString("0");
        RefreshGallery();
        RefreshArchiveInfo();
        UpdateTitle();
    }

    private void UpdateTitle() => Title = $"게임 정보 - {_item.DisplayName}";

    /// <summary>메인 화면에서 대표 썸네일을 바꾸거나(같은 게임을 이 창을 열어둔 채로) 카드 우클릭 메뉴로
    /// 압축/압축 풀기를 하면, 이 창이 열려 있어도 갤러리 첫 슬롯과 압축 정보가 즉시 따라간다.</summary>
    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameItem.ThumbnailPath))
        {
            RefreshGallery();
        }

        if (e.PropertyName is nameof(GameItem.IsCompressed) or nameof(GameItem.ArchivePath)
            or nameof(GameItem.ArchiveSizeDisplay) or nameof(GameItem.CompressedAtUtc))
        {
            RefreshArchiveInfo();
        }
    }

    /// <summary>압축 정보 패널을 갱신한다 — 압축 상태가 아니면 패널 자체를 숨긴다 (doc/game-management.md "게임 압축" 참고).</summary>
    private void RefreshArchiveInfo()
    {
        ArchiveInfoPanel.Visibility = _item.IsCompressed ? Visibility.Visible : Visibility.Collapsed;
        if (!_item.IsCompressed)
        {
            return;
        }

        var compressedAt = _item.CompressedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "알 수 없음";
        ArchiveInfoTextBlock.Text = $"압축 파일: {_item.ArchivePath}\n크기: {_item.ArchiveSizeDisplay}\n압축 일시: {compressedAt}";
    }

    /// <summary>갤러리를 [대표 썸네일 슬롯] + [게임 요약 스크린샷들]로 다시 채운다. 대표 썸네일이 없어도
    /// 슬롯 자체는 항상 보여주고(빈 이미지 placeholder), 삭제 대상이 아니므로 <see cref="ScreenshotItem.IsCover"/>로 표시한다.</summary>
    private void RefreshGallery()
    {
        _gallery.Clear();
        _gallery.Add(new ScreenshotItem
        {
            Path = _item.ThumbnailPath ?? string.Empty,
            IsCover = true,
        });

        foreach (var screenshot in _item.Screenshots)
        {
            _gallery.Add(screenshot);
        }
    }

    private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        _item.Name = NameTextBox.Text;
        UpdateTitle();
        _onChanged();
    }

    private void VersionTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        _item.Version = VersionTextBox.Text;
        UpdateTitle();
        _onChanged();
    }

    private void DescriptionTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        _item.Description = DescriptionTextBox.Text;
        _onChanged();
    }

    private void RatingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading) return;
        SetRating(RatingSlider.Value);
    }

    private void RatingTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _isSyncingRating) return;
        if (double.TryParse(RatingTextBox.Text, out var value))
        {
            SetRating(value);
        }
    }

    private void RatingDecrease_Click(object sender, RoutedEventArgs e) => SetRating(RatingSlider.Value - 1);

    private void RatingIncrease_Click(object sender, RoutedEventArgs e) => SetRating(RatingSlider.Value + 1);

    /// <summary>평점 슬라이더/텍스트박스/±버튼 중 어디서 값이 바뀌든 이 메서드 하나로 모은다 — 셋을 항상
    /// 같은 값으로 맞추고 딱 한 번만 저장한다. <see cref="_isSyncingRating"/>으로 서로 값을 되돌려 쓰다가
    /// 무한 루프에 빠지는 것을 막는다(슬라이더 값을 바꾸면 TextChanged가, 텍스트를 바꾸면 ValueChanged가
    /// 다시 불리기 때문).</summary>
    private void SetRating(double value)
    {
        if (_isLoading || _isSyncingRating) return;
        _isSyncingRating = true;
        try
        {
            value = Math.Clamp(value, RatingSlider.Minimum, RatingSlider.Maximum);
            _item.Rating = value;
            RatingSlider.Value = value;
            RatingTextBox.Text = value.ToString("0");
        }
        finally
        {
            _isSyncingRating = false;
        }

        _onChanged();
    }

    private void GentlemanGradeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading) return;
        SetGentlemanGrade(GentlemanGradeSlider.Value);
    }

    private void GentlemanGradeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _isSyncingGentlemanGrade) return;
        if (double.TryParse(GentlemanGradeTextBox.Text, out var value))
        {
            SetGentlemanGrade(value);
        }
    }

    private void GentlemanGradeDecrease_Click(object sender, RoutedEventArgs e) => SetGentlemanGrade(GentlemanGradeSlider.Value - 1);

    private void GentlemanGradeIncrease_Click(object sender, RoutedEventArgs e) => SetGentlemanGrade(GentlemanGradeSlider.Value + 1);

    /// <summary>신사 등급 쪽의 <see cref="SetRating"/>과 같은 역할.</summary>
    private void SetGentlemanGrade(double value)
    {
        if (_isLoading || _isSyncingGentlemanGrade) return;
        _isSyncingGentlemanGrade = true;
        try
        {
            value = Math.Clamp(value, GentlemanGradeSlider.Minimum, GentlemanGradeSlider.Maximum);
            _item.GentlemanGrade = value;
            GentlemanGradeSlider.Value = value;
            GentlemanGradeTextBox.Text = value.ToString("0");
        }
        finally
        {
            _isSyncingGentlemanGrade = false;
        }

        _onChanged();
    }

    private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "실행 파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _item.ExecutablePath = dialog.FileName;
        ExecutablePathTextBox.Text = dialog.FileName;
        _item.RefreshExecutableValid();
        _onChanged();
    }

    /// <summary>"게임 폴더 이름 변경" 버튼 — 실행 파일이 들어 있는 폴더 자체의 이름을 바꾸고, `ExecutablePath`를
    /// 새 위치로 갱신한다. 압축 상태라 폴더가 실제로 존재하지 않으면(doc/game-management.md "게임 압축" 참고)
    /// 안내만 하고 아무 것도 하지 않는다.</summary>
    private void RenameGameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_item.ExecutablePath))
        {
            MessageBox.Show(this, "실행 파일 경로가 지정되지 않아 폴더를 찾을 수 없습니다.", "폴더 이름 변경", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var currentDir = Path.GetDirectoryName(_item.ExecutablePath);
        if (string.IsNullOrEmpty(currentDir) || !Directory.Exists(currentDir))
        {
            MessageBox.Show(this, "게임 폴더를 찾을 수 없습니다.", "폴더 이름 변경", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var currentName = Path.GetFileName(currentDir);
        var input = RenameFolderWindow.Prompt(this, currentName);
        if (input is null)
        {
            return;
        }

        var newName = FileNameHelper.Sanitize(input);
        if (newName == currentName)
        {
            return;
        }

        var parentDir = Path.GetDirectoryName(currentDir);
        if (string.IsNullOrEmpty(parentDir))
        {
            MessageBox.Show(this, "상위 폴더를 찾을 수 없습니다.", "폴더 이름 변경", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newDir = Path.Combine(parentDir, newName);

        try
        {
            if (string.Equals(newDir, currentDir, StringComparison.OrdinalIgnoreCase))
            {
                // 대소문자만 바꾸는 경우 — Directory.Move는 대소문자만 다른 같은 경로로는 아무 일도 하지
                // 않으므로(윈도우 파일시스템은 대소문자를 구분하지 않는다), 임시 이름을 한 번 거쳐 간다.
                var tempDir = Path.Combine(parentDir, $"{newName}.renaming-{Guid.NewGuid():N}");
                Directory.Move(currentDir, tempDir);
                Directory.Move(tempDir, newDir);
            }
            else
            {
                if (Directory.Exists(newDir) || File.Exists(newDir))
                {
                    MessageBox.Show(this, $"'{newName}' 이름이 이미 있습니다.", "폴더 이름 변경", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Directory.Move(currentDir, newDir);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"폴더 이름을 바꾸지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var relativeExecutablePath = Path.GetRelativePath(currentDir, _item.ExecutablePath);
        var newExecutablePath = Path.Combine(newDir, relativeExecutablePath);

        _item.ExecutablePath = newExecutablePath;
        ExecutablePathTextBox.Text = newExecutablePath;
        _item.RefreshExecutableValid();
        _onChanged();
    }

    private void ScreenshotArea_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropImageHelper.CanAccept(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ScreenshotArea_Drop(object sender, DragEventArgs e)
    {
        var imagePath = DragDropImageHelper.TryGetImagePath(e.Data, out var isTemporary);
        if (imagePath is null)
        {
            e.Handled = true;
            return;
        }

        try
        {
            var destDir = AppPaths.GameImagesDir(_item.Id);
            var baseName = $"screenshot-{Guid.NewGuid():N}";
            var path = ThumbnailHelper.CopyOriginal(imagePath, destDir, baseName, isTemporary);
            _item.Screenshots.Add(new ScreenshotItem { Path = path });
            RefreshGallery();
            _onChanged();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"이미지를 추가하지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        e.Handled = true;
    }

    private void Screenshot_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ScreenshotItem screenshot })
        {
            return;
        }

        OriginalImageWindow.ShowFor(this, screenshot.Path);
    }

    private void DeleteScreenshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ScreenshotItem screenshot } || screenshot.IsCover)
        {
            return;
        }

        _item.Screenshots.Remove(screenshot);
        DeleteScreenshotFiles(new[] { screenshot });
        RefreshGallery();
        _onChanged();
    }

    private void ScreenshotLargeRadio_Checked(object sender, RoutedEventArgs e) => ChangeScreenshotSize(GameScreenshotSize.Large);

    private void ScreenshotMediumRadio_Checked(object sender, RoutedEventArgs e) => ChangeScreenshotSize(GameScreenshotSize.Medium);

    private void ScreenshotSmallRadio_Checked(object sender, RoutedEventArgs e) => ChangeScreenshotSize(GameScreenshotSize.Small);

    private void ChangeScreenshotSize(GameScreenshotSize size)
    {
        if (_isLoading) return;
        GameScreenshotSizeSettings.Current.Apply(size);
        _settings.ScreenshotSizePreset = size.ToString();
        _onChanged();
    }

    /// <summary>
    /// 이름이 같은 모든 버전(다른 GameItem)의 게임 내용/게임 요약을 하나로 합쳐, 그 결과를 해당 버전 전부에
    /// 똑같이 적용한다 — doc/game-management.md "버전 통합" 참고. 이 창이 열려 있는 게임뿐 아니라 이름이
    /// 같은 다른 버전들의 파일도 함께 갱신되므로, 나중에 그 버전의 정보 창을 열어도 같은 내용이 보인다.
    /// </summary>
    private void MergeVersions_Click(object sender, RoutedEventArgs e)
    {
        var siblings = _allGames
            .Where(g => string.Equals(g.Name, _item.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (siblings.Count <= 1)
        {
            MessageBox.Show(this, "이름이 같은 다른 버전이 없습니다.", "버전 통합", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var mergedDescription = BuildMergedDescription(siblings);

        // 옛 스크린샷 파일을 지우기 전에, 병합에 쓸 원본 정보를 모든 버전에서 먼저 수집해둔다.
        var sourceScreenshots = siblings.SelectMany(g => g.Screenshots).ToList();

        var newScreenshotsByGame = new Dictionary<GameItem, List<ScreenshotItem>>();
        foreach (var game in siblings)
        {
            var destDir = AppPaths.GameImagesDir(game.Id);
            newScreenshotsByGame[game] = sourceScreenshots.Select(s => CopyScreenshot(s, destDir)).ToList();
        }

        // 대표 썸네일이 아직 없는 버전에, 이미 대표 썸네일이 있는 다른 버전(우선 이 창에서 연 버전 자신)의
        // 것을 가져와 채운다 — 이미 자기 썸네일이 있는 버전은 건드리지 않는다(doc/game-management.md
        // "버전 통합" 참고, 사용자 요청).
        var sourceThumbnail = _item.HasThumbnail ? _item.ThumbnailPath : siblings.FirstOrDefault(g => g.HasThumbnail)?.ThumbnailPath;
        var thumbnailTargets = sourceThumbnail is null ? new List<GameItem>() : siblings.Where(g => !g.HasThumbnail).ToList();

        foreach (var game in siblings)
        {
            DeleteScreenshotFiles(game.Screenshots);
            game.Screenshots.Clear();
            foreach (var screenshot in newScreenshotsByGame[game])
            {
                game.Screenshots.Add(screenshot);
            }

            game.Description = mergedDescription;
        }

        foreach (var game in thumbnailTargets)
        {
            ApplyThumbnailFrom(game, sourceThumbnail!);
        }

        DescriptionTextBox.Text = _item.Description;
        RefreshGallery();

        _onChanged();
        var thumbnailNote = thumbnailTargets.Count > 0 ? $" 대표 썸네일이 없던 {thumbnailTargets.Count}개에도 대표 썸네일을 채웠습니다." : "";
        MessageBox.Show(this, $"이름이 같은 버전 {siblings.Count}개의 게임 내용/게임 요약을 통합했습니다.{thumbnailNote}",
            "버전 통합", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>다른 버전의 대표 썸네일 파일을 이 게임의 이미지 폴더로 복사해 대표 썸네일로 지정한다(원본은
    /// 지우지 않음). <see cref="MainWindow.ApplyThumbnail"/>과 같은 방식 — <see cref="ThumbnailHelper.CopyOriginal"/>은
    /// 항상 "cover.original.{확장자}"에 저장하므로 기존 파일을 먼저 지우고, `ThumbnailPath`도 값이 그대로일 수
    /// 있어 한 번 null로 지웠다가 다시 지정해 변경 알림이 항상 나가게 한다.</summary>
    private static void ApplyThumbnailFrom(GameItem game, string sourceThumbnailPath)
    {
        var destDir = AppPaths.GameImagesDir(game.Id);
        foreach (var file in Directory.Exists(destDir) ? Directory.GetFiles(destDir, "cover.original.*") : Array.Empty<string>())
        {
            try { File.Delete(file); } catch { /* 새 썸네일 저장에는 지장 없으므로 무시 */ }
        }

        var newPath = ThumbnailHelper.CopyOriginal(sourceThumbnailPath, destDir, "cover", deleteSource: false);
        game.ThumbnailPath = null;
        game.ThumbnailPath = newPath;
    }

    private static string BuildMergedDescription(IEnumerable<GameItem> siblings) => string.Join("\n\n", siblings
        .Where(g => !string.IsNullOrWhiteSpace(g.Description))
        .Select(g => $"[{(string.IsNullOrWhiteSpace(g.Version) ? "버전 미지정" : g.Version)}]\n{g.Description.Trim()}"));

    /// <summary>스크린샷 한 장을 다른 게임의 이미지 폴더로 복사한다(원본은 지우지 않음 — 이미 저장된 파일을
    /// 그대로 복제하는 것뿐이다).</summary>
    private static ScreenshotItem CopyScreenshot(ScreenshotItem source, string destDir) => new()
    {
        Path = ThumbnailHelper.CopyOriginal(source.Path, destDir, $"screenshot-{Guid.NewGuid():N}", deleteSource: false),
    };

    private static void DeleteScreenshotFiles(IEnumerable<ScreenshotItem> screenshots)
    {
        foreach (var screenshot in screenshots)
        {
            try { if (File.Exists(screenshot.Path)) File.Delete(screenshot.Path); } catch { }
        }
    }

    private void GameInfoWindow_Closed(object? sender, EventArgs e)
    {
        _item.PropertyChanged -= Item_PropertyChanged;
        WindowSizeMemory.Remember(nameof(GameInfoWindow), Width, Height);
    }
}
