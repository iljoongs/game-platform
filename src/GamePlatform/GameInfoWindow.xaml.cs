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
        ScreenshotsItemsControl.ItemsSource = item.Screenshots;
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
        var imagePath = DragDropImageHelper.TryGetImagePath(e.Data);
        if (imagePath is null)
        {
            e.Handled = true;
            return;
        }

        try
        {
            var destDir = AppPaths.GameImagesDir(_item.Id);
            var baseName = $"screenshot-{Guid.NewGuid():N}";
            var result = ThumbnailHelper.CreateThumbnail(imagePath, destDir, baseName);
            _item.Screenshots.Add(new ScreenshotItem { Path = result.ThumbnailPath, OriginalPath = result.OriginalPath });
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

        OriginalImageWindow.ShowFor(this, screenshot.OriginalPath);
    }

    private void DeleteScreenshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ScreenshotItem screenshot })
        {
            return;
        }

        _item.Screenshots.Remove(screenshot);
        DeleteScreenshotFiles(new[] { screenshot });
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

        DescriptionTextBox.Text = _item.Description;
        ScreenshotsItemsControl.ItemsSource = null;
        ScreenshotsItemsControl.ItemsSource = _item.Screenshots;

        _onChanged();
        MessageBox.Show(this, $"이름이 같은 버전 {siblings.Count}개의 게임 내용/게임 요약을 통합했습니다.",
            "버전 통합", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string BuildMergedDescription(IEnumerable<GameItem> siblings) => string.Join("\n\n", siblings
        .Where(g => !string.IsNullOrWhiteSpace(g.Description))
        .Select(g => $"[{(string.IsNullOrWhiteSpace(g.Version) ? "버전 미지정" : g.Version)}]\n{g.Description.Trim()}"));

    /// <summary>스크린샷 한 장(원본+리사이즈본)을 다른 게임의 이미지 폴더로 복사한다. 원본 파일은
    /// 지우지 않는다 — ThumbnailHelper.CreateThumbnail과 달리 이미 저장된 두 파일을 그대로 복제하는 것뿐이다.</summary>
    private static ScreenshotItem CopyScreenshot(ScreenshotItem source, string destDir)
    {
        Directory.CreateDirectory(destDir);
        var baseName = $"screenshot-{Guid.NewGuid():N}";
        var destOriginal = Path.Combine(destDir, $"{baseName}.original{Path.GetExtension(source.OriginalPath)}");
        var destThumbnail = Path.Combine(destDir, $"{baseName}.thumbnail.jpg");

        File.Copy(source.OriginalPath, destOriginal, overwrite: true);
        File.Copy(source.Path, destThumbnail, overwrite: true);

        return new ScreenshotItem { Path = destThumbnail, OriginalPath = destOriginal };
    }

    private static void DeleteScreenshotFiles(IEnumerable<ScreenshotItem> screenshots)
    {
        foreach (var screenshot in screenshots)
        {
            try { if (File.Exists(screenshot.Path)) File.Delete(screenshot.Path); } catch { }
            try { if (File.Exists(screenshot.OriginalPath)) File.Delete(screenshot.OriginalPath); } catch { }
        }
    }

    private void GameInfoWindow_Closed(object? sender, EventArgs e) =>
        WindowSizeMemory.Remember(nameof(GameInfoWindow), Width, Height);
}
