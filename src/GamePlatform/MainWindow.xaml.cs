using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
            game.RefreshArchiveValid();
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

        var imagePath = DragDropImageHelper.TryGetImagePath(e.Data, out var isTemporary);
        if (imagePath is null)
        {
            return;
        }

        ApplyThumbnail(item, imagePath, isTemporary);
        e.Handled = true;
    }

    private static bool IsNonImageFileDrop(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) &&
        data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files &&
        !ImageExtensions.Contains(Path.GetExtension(files[0]).ToLowerInvariant());

    private void ApplyThumbnail(GameItem item, string sourceImagePath, bool deleteSource)
    {
        try
        {
            var destDir = AppPaths.GameImagesDir(item.Id);
            item.ThumbnailPath = ThumbnailHelper.CopyOriginal(sourceImagePath, destDir, "cover", deleteSource);
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

            if (item.IsCompressed && !string.IsNullOrEmpty(item.ArchivePath) && File.Exists(item.ArchivePath))
            {
                File.Delete(item.ArchivePath);
            }
        }
        catch
        {
            // 이미지 폴더/압축 파일 삭제 실패는 무시한다 — 목록에서는 이미 제거되었다.
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

    private void RunOrExtractButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameItem item })
        {
            return;
        }

        if (item.IsCompressed)
        {
            ExtractGame(item);
        }
        else
        {
            RunGame(item);
        }
    }

    private void RunGame(GameItem item)
    {
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

    /// <summary>카드 우클릭 메뉴의 "압축" — 실행 파일이 있는 폴더 전체를 zip으로 묶어 앱 데이터 폴더에 저장하고,
    /// 원본 폴더는 삭제해 디스크 공간을 확보한다 (doc/game-management.md "게임 압축" 참고). 되돌리려면
    /// 카드의 "압축 풀기" 버튼을 쓴다.</summary>
    private void CompressGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameItem item } || item.IsCompressed)
        {
            return;
        }

        if (string.IsNullOrEmpty(item.ExecutablePath))
        {
            MessageBox.Show(this, "실행 파일이 지정되지 않아 압축할 수 없습니다. 정보 창에서 먼저 지정하세요.",
                "압축", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var gameDir = Path.GetDirectoryName(item.ExecutablePath);
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            MessageBox.Show(this, "게임 폴더를 찾을 수 없어 압축할 수 없습니다.", "압축", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"'{gameDir}' 폴더 전체를 압축하고 원본 폴더는 삭제합니다.\n계속할까요?",
            "게임 압축", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            AppPaths.EnsureArchivesDirectory();
            var archivePath = AppPaths.GameArchivePath(item.Id);
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            ZipFile.CreateFromDirectory(gameDir, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
            Directory.Delete(gameDir, recursive: true);

            item.ArchivePath = archivePath;
            item.ArchiveSizeBytes = new FileInfo(archivePath).Length;
            item.CompressedAtUtc = DateTime.UtcNow;
            item.IsCompressed = true;
            item.RefreshExecutableValid();
            item.RefreshArchiveValid();
            SaveState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"압축하지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    /// <summary>카드의 "압축 풀기" 버튼 — 압축 파일을 원래 게임 폴더 위치에 풀고 압축 파일은 지운다.</summary>
    private void ExtractGame(GameItem item)
    {
        if (string.IsNullOrEmpty(item.ArchivePath) || !File.Exists(item.ArchivePath))
        {
            item.RefreshArchiveValid();
            MessageBox.Show(this, "압축 파일을 찾을 수 없습니다.", "압축 풀기", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var gameDir = string.IsNullOrEmpty(item.ExecutablePath) ? null : Path.GetDirectoryName(item.ExecutablePath);
        if (string.IsNullOrEmpty(gameDir))
        {
            MessageBox.Show(this, "압축을 풀 폴더 위치를 알 수 없습니다.", "압축 풀기", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            Directory.CreateDirectory(gameDir);
            ZipFile.ExtractToDirectory(item.ArchivePath, gameDir, overwriteFiles: true);
            File.Delete(item.ArchivePath);

            item.IsCompressed = false;
            item.ArchivePath = null;
            item.ArchiveSizeBytes = 0;
            item.CompressedAtUtc = null;
            item.RefreshExecutableValid();
            item.RefreshArchiveValid();
            SaveState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"압축을 풀지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
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
