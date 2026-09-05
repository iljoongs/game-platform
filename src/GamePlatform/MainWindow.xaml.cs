using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace GamePlatform;

public partial class MainWindow : Window
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

    private readonly ObservableCollection<GameItem> _games;
    private readonly AppSettings _settings;

    public MainWindow()
    {
        InitializeComponent();

        AppPaths.EnsureAppDataDirectory();

        _settings = SettingsRepository.Load();
        WindowPositionMemory.LoadFrom(_settings.WindowPositions);
        WindowSizeMemory.LoadFrom(_settings.WindowSizes);

        var preset = Enum.TryParse<GameCardSize>(_settings.CardSizePreset, out var parsed) ? parsed : GameCardSize.Large;
        GameCardSizeSettings.Current.Apply(preset);
        (preset == GameCardSize.Small ? SmallSizeRadio : LargeSizeRadio).IsChecked = true;

        var screenshotPreset = Enum.TryParse<GameScreenshotSize>(_settings.ScreenshotSizePreset, out var parsedScreenshotPreset)
            ? parsedScreenshotPreset
            : GameScreenshotSize.Large;
        GameScreenshotSizeSettings.Current.Apply(screenshotPreset);

        _games = new ObservableCollection<GameItem>(GameLibraryRepository.Load());
        foreach (var game in _games)
        {
            game.RefreshExecutableValid();
        }
        GamesItemsControl.ItemsSource = _games;

        ApplyWindowBounds();

        BackupService.CheckAndBackup(_settings);
    }

    private void ApplyWindowBounds()
    {
        if (_settings.MainWindowWidth is { } w && _settings.MainWindowHeight is { } h &&
            _settings.MainWindowLeft is { } l && _settings.MainWindowTop is { } t &&
            WindowPositionMemory.IsOnScreen(l, t))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = w;
            Height = h;
            Left = l;
            Top = t;
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        var bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        _settings.MainWindowWidth = bounds.Width;
        _settings.MainWindowHeight = bounds.Height;
        _settings.MainWindowLeft = bounds.X;
        _settings.MainWindowTop = bounds.Y;
        _settings.WindowPositions = WindowPositionMemory.ToDictionary();
        _settings.WindowSizes = WindowSizeMemory.ToDictionary();
        SettingsRepository.Save(_settings);
    }

    private void LargeSizeRadio_Checked(object sender, RoutedEventArgs e) => ChangeCardSize(GameCardSize.Large);

    private void SmallSizeRadio_Checked(object sender, RoutedEventArgs e) => ChangeCardSize(GameCardSize.Small);

    private void ChangeCardSize(GameCardSize size)
    {
        GameCardSizeSettings.Current.Apply(size);
        _settings.CardSizePreset = size.ToString();
        SettingsRepository.Save(_settings);
    }

    private void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return;
        }

        foreach (var file in files)
        {
            if (string.Equals(Path.GetExtension(file), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                AddGame(file);
            }
        }
    }

    private void AddGame(string executablePath)
    {
        var item = new GameItem
        {
            Name = Path.GetFileNameWithoutExtension(executablePath),
            ExecutablePath = executablePath,
        };
        item.RefreshExecutableValid();
        _games.Add(item);
        SaveState();
    }

    private void Card_DragOver(object sender, DragEventArgs e)
    {
        // 이미지가 아닌 로컬 파일(exe 등)은 여기서 처리하지 않고 상위(Window)로 넘겨 새 게임 추가로 처리한다.
        var acceptable = DragDropImageHelper.CanAccept(e.Data) && !IsNonImageFileDrop(e.Data);
        e.Effects = acceptable ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = acceptable;
    }

    private void Card_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameItem item })
        {
            return;
        }

        if (IsNonImageFileDrop(e.Data))
        {
            return; // 처리하지 않고 상위(Window)로 넘어가도록 Handled를 세우지 않는다.
        }

        var imagePath = DragDropImageHelper.TryGetImagePath(e.Data);
        if (imagePath is null)
        {
            return;
        }

        ApplyThumbnail(item, imagePath);
        e.Handled = true;
    }

    private static bool IsNonImageFileDrop(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) &&
        data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files &&
        !ImageExtensions.Contains(Path.GetExtension(files[0]).ToLowerInvariant());

    private void ApplyThumbnail(GameItem item, string sourceImagePath)
    {
        try
        {
            var destDir = AppPaths.GameImagesDir(item.Id);
            var result = ThumbnailHelper.CreateThumbnail(sourceImagePath, destDir, "cover");
            item.ThumbnailPath = result.ThumbnailPath;
            item.ThumbnailOriginalPath = result.OriginalPath;
            SaveState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"썸네일을 지정하지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameItem item })
        {
            return;
        }

        _games.Remove(item);

        try
        {
            var dir = AppPaths.GameImagesDir(item.Id);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // 이미지 폴더 삭제 실패는 무시한다 — 목록에서는 이미 제거되었다.
        }

        SaveState();
    }

    private void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameItem item })
        {
            return;
        }

        SingleInstanceWindow<GameInfoWindow>.Show(new GameInfoWindow(item, _games, _settings, SaveState) { Owner = this });
    }

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameItem item })
        {
            return;
        }

        if (string.IsNullOrEmpty(item.ExecutablePath) || !File.Exists(item.ExecutablePath))
        {
            item.RefreshExecutableValid();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.ExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(item.ExecutablePath),
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"게임을 실행하지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>게임 목록과 설정을 함께 저장한다. GameInfoWindow에서 이름/버전/실행파일/게임 요약/
    /// 이미지 크기 설정 등 무엇이 바뀌든 이 하나의 콜백으로 저장을 위임받는다.</summary>
    private void SaveState()
    {
        GameLibraryRepository.Save(_games);
        SettingsRepository.Save(_settings);
        BackupService.CheckAndBackup(_settings);
    }
}
