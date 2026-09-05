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
    private readonly GameItem _item;
    private readonly IEnumerable<GameItem> _allGames;
    private readonly AppSettings _settings;
    private readonly Action _onChanged;
    private readonly bool _isLoading;

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
        NameTextBox.Text = item.Name;
        VersionTextBox.Text = item.Version;
        DescriptionTextBox.Text = item.Description;
        ExecutablePathTextBox.Text = item.ExecutablePath;
        ScreenshotsItemsControl.ItemsSource = _gallery;
        RefreshGallery();
        RefreshArchiveInfo();
        _item.PropertyChanged += Item_PropertyChanged;
        UpdateTitle();

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
