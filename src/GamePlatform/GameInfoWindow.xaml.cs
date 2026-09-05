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
    private readonly Action _onChanged;
    private readonly bool _isLoading;

    public GameInfoWindow(GameItem item, Action onChanged)
    {
        InitializeComponent();
        _item = item;
        _onChanged = onChanged;

        _isLoading = true;
        NameTextBox.Text = item.Name;
        VersionTextBox.Text = item.Version;
        DescriptionTextBox.Text = item.Description;
        ExecutablePathTextBox.Text = item.ExecutablePath;
        ScreenshotsItemsControl.ItemsSource = item.Screenshots;
        UpdateTitle();
        _isLoading = false;

        if (WindowSizeMemory.TryGetSize(nameof(GameInfoWindow), out var width, out var height))
        {
            Width = width;
            Height = height;
        }
    }

    private void UpdateTitle() =>
        Title = string.IsNullOrEmpty(_item.Name) ? "게임 정보" : $"게임 정보 - {_item.Name}";

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

        try
        {
            if (File.Exists(screenshot.Path)) File.Delete(screenshot.Path);
            if (File.Exists(screenshot.OriginalPath)) File.Delete(screenshot.OriginalPath);
        }
        catch
        {
            // 파일 삭제 실패는 무시한다 — 목록에서는 이미 제거되었다.
        }

        _onChanged();
    }

    private void GameInfoWindow_Closed(object? sender, EventArgs e) =>
        WindowSizeMemory.Remember(nameof(GameInfoWindow), Width, Height);
}
