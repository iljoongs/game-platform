using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GamePlatform;

public partial class MainWindow : Window
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

    private readonly ObservableCollection<GameItem> _games;
    private readonly AppSettings _settings;

    public MainWindow()
    {
        InitializeComponent();

        var didMigrateAppData = AppPaths.EnsureAppDataDirectory();

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

        // 데이터 폴더 이동이 이번 실행이 아니라 이전에 이미 일어났을 수도 있으므로(예: 파일만 옮겨진 뒤
        // 경로 문자열 교정 전에 껐다 켠 경우), 매번 확인한다 — 옛 경로가 없으면 그냥 아무 것도 하지 않는다.
        RewriteLegacyImagePaths();

        foreach (var game in _games)
        {
            game.RefreshExecutableValid();
            game.RefreshArchiveValid();
        }
        GamesItemsControl.ItemsSource = _games;

        ApplyWindowBounds();

        BackupService.CheckAndBackup(_settings);

        SetStatus(didMigrateAppData
            ? $"데이터 폴더를 '{AppPaths.GamesBaseDir}' 밑으로 옮겼습니다."
            : "실행 파일/폴더/압축 파일(zip)을 이 창에 끌어다 놓으면 게임이 추가됩니다. 카드에 이미지를 끌어다 놓으면 대표 썸네일이 지정됩니다. 카드 우클릭으로 삭제/압축할 수 있습니다.",
            StatusType.Info);
    }

    /// <summary>데이터 폴더를 D:\game 밑으로 옮긴 뒤, games.json에 저장돼 있던 옛 절대경로
    /// (ThumbnailPath/Screenshots/ArchivePath)를 새 위치 기준으로 바로잡고, 실제로 뭔가 바뀌었을 때만
    /// 다시 저장한다 — 실제 파일은 <see cref="AppPaths.EnsureAppDataDirectory"/>에서 이미 옮겨졌지만,
    /// JSON에 문자열로 박혀 있는 경로는 저절로 바뀌지 않기 때문이다. 옛 경로가 하나도 없으면(이미 교정됐거나
    /// 애초에 이동한 적이 없으면) 아무 것도 하지 않는 가벼운 검사라 매번 실행해도 된다.</summary>
    private void RewriteLegacyImagePaths()
    {
        var changed = false;

        foreach (var game in _games)
        {
            var newThumbnail = AppPaths.RewriteLegacyPath(game.ThumbnailPath);
            if (newThumbnail != game.ThumbnailPath)
            {
                game.ThumbnailPath = newThumbnail;
                changed = true;
            }

            var newArchive = AppPaths.RewriteLegacyPath(game.ArchivePath);
            if (newArchive != game.ArchivePath)
            {
                game.ArchivePath = newArchive;
                changed = true;
            }

            foreach (var screenshot in game.Screenshots)
            {
                var newPath = AppPaths.RewriteLegacyPath(screenshot.Path) ?? screenshot.Path;
                if (newPath != screenshot.Path)
                {
                    screenshot.Path = newPath;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            GameLibraryRepository.Save(_games);
        }
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

    #region 상태바

    /// <summary>상태바 메시지의 종류 — 종류별로 <see cref="SetStatus"/>가 아이콘과 글자색을 다르게 지정해서
    /// 한눈에 구분되게 한다 (video-vault의 SeriesManagerWindow와 같은 패턴).</summary>
    private enum StatusType { Success, Warning, Info, Error }

    /// <summary>게임 추가/삭제/썸네일 지정/압축 등 단순 안내·성공·오류 이벤트를 대화상자 대신 하단 상태바에
    /// 표시한다. 실제 사용자 결정이 필요한 Yes/No 확인(삭제 확인, 압축 확인, exe 여러 개 중 선택)은
    /// 여전히 대화상자를 그대로 쓴다.</summary>
    private void SetStatus(string message, StatusType type)
    {
        StatusText.Text = message;
        (StatusIcon.Text, var color) = type switch
        {
            StatusType.Success => ("✓", "#2E7D32"),
            StatusType.Warning => ("⚠", "#B26A00"),
            StatusType.Error => ("✕", "#C62828"),
            _ => ("ℹ", "#1565C0"),
        };
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        StatusIcon.Foreground = brush;
        StatusText.Foreground = brush;
    }

    private void ShowCompressProgress(string label)
    {
        CompressProgressPanel.Visibility = Visibility.Visible;
        CompressProgressLabel.Text = label;
        CompressProgressBar.Value = 0;
    }

    private void HideCompressProgress() => CompressProgressPanel.Visibility = Visibility.Collapsed;

    #endregion

    #region 게임 추가

    private void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                AddGameFromFolder(path);
            }
            else if (string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                AddGameFromExecutable(path);
            }
            else if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                AddGameFromArchive(path);
            }
        }
    }

    private void AddGameFromExecutable(string executablePath)
    {
        var item = new GameItem
        {
            Name = Path.GetFileNameWithoutExtension(executablePath),
            ExecutablePath = executablePath,
        };
        item.RefreshExecutableValid();
        _games.Add(item);
        SaveState();
        SetStatus($"'{item.DisplayName}' 게임을 추가했습니다.", StatusType.Success);
    }

    /// <summary>폴더로 게임을 추가한다 — exe 드래그드롭과 달리 폴더는 옮기거나 복사하지 않고, 그 안에서 찾은
    /// 실행 파일의 경로만 그대로 참조한다 (doc/game-management.md "게임 추가" 참고).</summary>
    private void AddGameFromFolder(string folderPath)
    {
        List<string> relativePaths;
        try
        {
            relativePaths = EnumerateExecutableCandidates(
                Directory.GetFiles(folderPath, "*.exe", SearchOption.AllDirectories)
                    .Select(full => Path.GetRelativePath(folderPath, full)));
        }
        catch (Exception ex)
        {
            SetStatus($"'{Path.GetFileName(folderPath)}' 폴더를 읽지 못했습니다: {ex.Message}", StatusType.Error);
            return;
        }

        if (relativePaths.Count == 0)
        {
            SetStatus($"'{Path.GetFileName(folderPath)}' 폴더에 실행 파일(exe)이 없어 추가하지 못했습니다.", StatusType.Warning);
            return;
        }

        var chosen = SelectExecutableWindow.PickFrom(this, relativePaths);
        if (chosen is null)
        {
            return;
        }

        var item = new GameItem
        {
            Name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            ExecutablePath = Path.Combine(folderPath, chosen),
        };
        item.RefreshExecutableValid();
        _games.Add(item);
        SaveState();
        SetStatus($"'{item.DisplayName}' 게임을 폴더에서 추가했습니다.", StatusType.Success);
    }

    /// <summary>압축 파일(zip)로 게임을 추가한다 — 압축을 그 자리에서 풀지 않고, zip 자체를 이 게임의 압축
    /// 파일로 등록한다(압축된 상태로 시작). 실제 압축 해제는 사용자가 카드의 [압축 풀기]를 눌렀을 때
    /// <see cref="AppPaths.GamesBaseDir"/> 밑에 예약해둔 폴더에서 이루어진다 (doc/game-management.md 참고).</summary>
    private void AddGameFromArchive(string zipPath)
    {
        List<string> relativePaths;
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            relativePaths = EnumerateExecutableCandidates(archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name))
                .Select(entry => entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"압축 파일을 열지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (relativePaths.Count == 0)
        {
            SetStatus($"'{Path.GetFileName(zipPath)}' 압축 파일에 실행 파일(exe)이 없어 추가하지 못했습니다.", StatusType.Warning);
            return;
        }

        var chosen = SelectExecutableWindow.PickFrom(this, relativePaths);
        if (chosen is null)
        {
            return;
        }

        var item = new GameItem { Name = Path.GetFileNameWithoutExtension(zipPath) };
        var gameFolder = AppPaths.ReserveGameFolder(item.DisplayName);
        item.ExecutablePath = Path.Combine(gameFolder, chosen);

        try
        {
            AppPaths.EnsureArchiveDirectory(item.Id);
            var archivePath = AppPaths.GameArchivePath(item.Id, item.DisplayName);
            File.Move(zipPath, archivePath);

            item.ArchivePath = archivePath;
            item.ArchiveSizeBytes = new FileInfo(archivePath).Length;
            item.CompressedAtUtc = DateTime.UtcNow;
            item.IsCompressed = true;
            item.RefreshArchiveValid();

            _games.Add(item);
            SaveState();
            SetStatus($"'{item.DisplayName}' 게임을 압축 파일로 추가했습니다 (압축 상태 — 실행하려면 먼저 압축을 풀어야 합니다).", StatusType.Success);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"압축 파일을 추가하지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>실행 파일 후보를 얕은 경로(하위 폴더가 적은 순) → 이름 순으로 정렬해, 최상위에 가까운
    /// exe가 목록 위쪽에 오게 한다 (설치 프로그램 등은 보통 하위 redist 폴더에 있는 경우가 많다).</summary>
    private static List<string> EnumerateExecutableCandidates(IEnumerable<string> relativePaths) => relativePaths
        .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar))
        .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
        .ToList();

    #endregion

    #region 대표 썸네일 / 삭제

    private void Card_DragOver(object sender, DragEventArgs e)
    {
        // 이미지가 아닌 로컬 파일(exe/폴더/zip 등)은 여기서 처리하지 않고 상위(Window)로 넘겨 새 게임 추가로 처리한다.
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
            SetStatus($"'{item.DisplayName}'의 대표 썸네일을 지정했습니다.", StatusType.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"썸네일을 지정하지 못했습니다: {ex.Message}", StatusType.Error);
        }
    }

    private void DeleteGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameItem item })
        {
            return;
        }

        var confirm = MessageBox.Show(this, $"'{item.DisplayName}'을(를) 목록에서 삭제할까요?",
            "게임 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
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
        SetStatus($"'{item.DisplayName}'을(를) 삭제했습니다.", StatusType.Success);
    }

    #endregion

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
            _ = ExtractGameAsync(item);
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
            SetStatus($"'{item.DisplayName}'의 실행 파일을 찾을 수 없습니다.", StatusType.Warning);
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
            SetStatus($"'{item.DisplayName}'을(를) 실행했습니다.", StatusType.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"'{item.DisplayName}'을(를) 실행하지 못했습니다: {ex.Message}", StatusType.Error);
        }
    }

    #region 게임 압축 / 압축 풀기

    /// <summary>카드 우클릭 메뉴의 "압축" — 실행 파일이 있는 폴더 전체를 zip으로 묶어 앱 데이터 폴더에 저장하고,
    /// 원본 폴더는 삭제해 디스크 공간을 확보한다 (doc/game-management.md "게임 압축" 참고). 백그라운드 스레드에서
    /// 진행하므로 압축 중에도 창을 움직이거나 다른 카드를 조작할 수 있다 — 같은 게임에 대한 중복 압축만
    /// <see cref="GameItem.IsBusy"/>로 막는다. 되돌리려면 카드의 "압축 풀기" 버튼을 쓴다.</summary>
    private async void CompressGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameItem item } || item.IsCompressed || item.IsBusy)
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

        item.IsBusy = true;
        ShowCompressProgress($"'{item.DisplayName}' 압축 중...");
        SetStatus($"'{item.DisplayName}' 압축을 시작했습니다.", StatusType.Info);

        try
        {
            AppPaths.EnsureArchiveDirectory(item.Id);
            var archivePath = AppPaths.GameArchivePath(item.Id, item.DisplayName);
            var progress = new Progress<int>(percent => CompressProgressBar.Value = percent);

            await Task.Run(() => CompressDirectory(gameDir, archivePath, progress));
            Directory.Delete(gameDir, recursive: true);

            item.ArchivePath = archivePath;
            item.ArchiveSizeBytes = new FileInfo(archivePath).Length;
            item.CompressedAtUtc = DateTime.UtcNow;
            item.IsCompressed = true;
            item.RefreshExecutableValid();
            item.RefreshArchiveValid();
            SaveState();
            SetStatus($"'{item.DisplayName}' 압축을 완료했습니다 ({item.ArchiveSizeDisplay}).", StatusType.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"'{item.DisplayName}' 압축에 실패했습니다: {ex.Message}", StatusType.Error);
        }
        finally
        {
            item.IsBusy = false;
            HideCompressProgress();
        }
    }

    /// <summary>카드의 "압축 풀기" 버튼 — 압축 파일을 원래 게임 폴더 위치(또는 zip으로 추가된 게임이면
    /// 예약해둔 폴더)에 풀고 압축 파일은 지운다. 압축과 마찬가지로 백그라운드 스레드에서 진행한다.</summary>
    private async Task ExtractGameAsync(GameItem item)
    {
        if (item.IsBusy)
        {
            return;
        }

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

        item.IsBusy = true;
        ShowCompressProgress($"'{item.DisplayName}' 압축 푸는 중...");
        SetStatus($"'{item.DisplayName}' 압축 풀기를 시작했습니다.", StatusType.Info);

        try
        {
            var archivePath = item.ArchivePath;
            var progress = new Progress<int>(percent => CompressProgressBar.Value = percent);

            await Task.Run(() => ExtractArchive(archivePath, gameDir, progress));
            File.Delete(archivePath);

            item.IsCompressed = false;
            item.ArchivePath = null;
            item.ArchiveSizeBytes = 0;
            item.CompressedAtUtc = null;
            item.RefreshExecutableValid();
            item.RefreshArchiveValid();
            SaveState();
            SetStatus($"'{item.DisplayName}' 압축을 풀었습니다.", StatusType.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"'{item.DisplayName}' 압축을 풀지 못했습니다: {ex.Message}", StatusType.Error);
        }
        finally
        {
            item.IsBusy = false;
            HideCompressProgress();
        }
    }

    /// <summary>.NET의 <c>ZipFile.CreateFromDirectory</c>는 진행률을 알려주지 않으므로, 파일 단위로 직접
    /// 압축하며 진행률(전체 파일 수 대비 처리한 파일 수)을 보고한다. 백그라운드 스레드(<c>Task.Run</c>)에서
    /// 호출된다 — UI 요소를 직접 건드리지 않고 <paramref name="progress"/>로만 보고한다.</summary>
    private static void CompressDirectory(string sourceDir, string destArchivePath, IProgress<int> progress)
    {
        if (File.Exists(destArchivePath))
        {
            File.Delete(destArchivePath);
        }

        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        using var archive = ZipFile.Open(destArchivePath, ZipArchiveMode.Create);

        for (var i = 0; i < files.Length; i++)
        {
            var entryName = Path.GetRelativePath(sourceDir, files[i]).Replace(Path.DirectorySeparatorChar, '/');
            archive.CreateEntryFromFile(files[i], entryName, CompressionLevel.Optimal);
            progress.Report((i + 1) * 100 / files.Length);
        }

        if (files.Length == 0)
        {
            progress.Report(100);
        }
    }

    /// <summary>압축과 대칭으로, 항목 단위로 직접 풀며 진행률을 보고한다. 백그라운드 스레드에서 호출된다.</summary>
    private static void ExtractArchive(string archivePath, string destDir, IProgress<int> progress)
    {
        Directory.CreateDirectory(destDir);

        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToList();

        for (var i = 0; i < entries.Count; i++)
        {
            var destPath = Path.Combine(destDir, entries[i].FullName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entries[i].ExtractToFile(destPath, overwrite: true);
            progress.Report((i + 1) * 100 / entries.Count);
        }

        if (entries.Count == 0)
        {
            progress.Report(100);
        }
    }

    #endregion

    /// <summary>게임 목록과 설정을 함께 저장한다. GameInfoWindow에서 이름/버전/실행파일/게임 요약/
    /// 이미지 크기 설정 등 무엇이 바뀌든 이 하나의 콜백으로 저장을 위임받는다.</summary>
    private void SaveState()
    {
        GameLibraryRepository.Save(_games);
        SettingsRepository.Save(_settings);
        BackupService.CheckAndBackup(_settings);
    }
}
