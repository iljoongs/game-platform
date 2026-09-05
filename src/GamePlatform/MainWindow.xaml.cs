using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GamePlatform;

public partial class MainWindow : Window
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

    private readonly ObservableCollection<GameItem> _games;
    private readonly AppSettings _settings;

    /// <summary>지금 저장/불러오기 대상인 게임 목록 파일. 기본값은 <see cref="AppPaths.GamesPath"/>이지만
    /// 파일 메뉴의 "열기"/"다른 이름으로 저장"으로 바뀔 수 있다 — 그 뒤로는 자동 저장(<see cref="SaveState"/>)도
    /// 이 경로를 따라간다(doc/game-management.md "게임 목록 파일 관리" 참고).</summary>
    private string _currentGamesPath;

    public MainWindow()
    {
        InitializeComponent();

        AppPaths.Initialize(AppConfigRepository.Load());
        var didMigrateAppData = AppPaths.EnsureAppDataDirectory();
        _currentGamesPath = AppPaths.GamesPath;

        _settings = SettingsRepository.Load();
        WindowPositionMemory.LoadFrom(_settings.WindowPositions);
        WindowSizeMemory.LoadFrom(_settings.WindowSizes);

        var preset = Enum.TryParse<GameCardSize>(_settings.CardSizePreset, out var parsed) ? parsed : GameCardSize.Large;
        GameCardSizeSettings.Current.Apply(preset);
        UpdateCardSizeMenuChecks(preset);

        var screenshotPreset = Enum.TryParse<GameScreenshotSize>(_settings.ScreenshotSizePreset, out var parsedScreenshotPreset)
            ? parsedScreenshotPreset
            : GameScreenshotSize.Large;
        GameScreenshotSizeSettings.Current.Apply(screenshotPreset);

        _games = new ObservableCollection<GameItem>(GameLibraryRepository.Load(_currentGamesPath));

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

        BackupService.CheckAndBackup(_settings, _currentGamesPath);

        SetStatus(didMigrateAppData
            ? $"데이터 폴더를 '{AppPaths.GamesBaseDir}' 밑으로 옮겼습니다."
            : "실행 파일/폴더/압축 파일(zip)을 이 창에 끌어다 놓으면 게임이 추가됩니다. 카드에 이미지를 끌어다 놓으면 대표 썸네일이 지정됩니다. 카드 우클릭으로 삭제/압축할 수 있습니다.",
            StatusType.Info);

        _ = CleanUpLeftoverCompressedFoldersAsync();
    }

    /// <summary>
    /// 압축은 끝났지만 원본 폴더 삭제가 (백신 실시간 검사 등으로 인한 일시적 파일 잠금 때문에) 실패해서
    /// 원본 폴더가 그대로 남아 있는 게임이 있으면, 시작할 때마다 조용히 다시 지워본다 — 실제로 겪은 문제의
    /// 재발 방지책. 잠금은 대개 몇 초~몇 분 안에 풀리므로, 압축 시점의 즉시 재시도(<see cref="RetryDelete"/>)로
    /// 못 지웠더라도 다음 실행 시점에는 대개 지울 수 있다. 실패해도 조용히 넘어가고 다음 실행 때 다시 시도한다 —
    /// 게임 데이터 자체(zip)는 이미 안전하게 등록되어 있으므로 사용자가 당장 조치할 필요는 없다.
    /// </summary>
    private async Task CleanUpLeftoverCompressedFoldersAsync()
    {
        foreach (var game in _games.Where(g => g.IsCompressed).ToList())
        {
            var gameDir = string.IsNullOrEmpty(game.ExecutablePath) ? null : Path.GetDirectoryName(game.ExecutablePath);
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
            {
                continue;
            }

            try
            {
                await Task.Run(() => RetryDelete(() => Directory.Delete(gameDir, recursive: true), attempts: 3, delayMilliseconds: 1000));
                SetStatus($"이전에 지우지 못했던 '{game.DisplayName}'의 원본 폴더를 정리했습니다.", StatusType.Success);
            }
            catch
            {
                // 여전히 잠겨 있으면 이번에도 조용히 넘어가고 다음 실행 때 다시 시도한다.
            }
        }
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
            GameLibraryRepository.Save(_games, _currentGamesPath);
        }
    }

    /// <summary>환경설정에서 "기본 폴더"를 바꿔 관리 데이터 폴더가 물리적으로 옮겨진 뒤, 게임 목록에 저장된
    /// 절대경로(ThumbnailPath/Screenshots/ArchivePath)도 같은 접두사 교체로 바로잡는다 — <see cref="RewriteLegacyImagePaths"/>와
    /// 같은 문제(파일은 옮겼는데 JSON 속 경로 문자열은 그대로인 것)를 임의의 (옛 경로, 새 경로) 쌍에 대해 처리하는 일반화 버전이다.</summary>
    private void RewriteGamePathsPrefix(string oldPrefix, string newPrefix)
    {
        var changed = false;

        foreach (var game in _games)
        {
            var newThumbnail = AppPaths.RewritePathPrefix(game.ThumbnailPath, oldPrefix, newPrefix);
            if (newThumbnail != game.ThumbnailPath)
            {
                game.ThumbnailPath = newThumbnail;
                changed = true;
            }

            var newArchive = AppPaths.RewritePathPrefix(game.ArchivePath, oldPrefix, newPrefix);
            if (newArchive != game.ArchivePath)
            {
                game.ArchivePath = newArchive;
                changed = true;
            }

            foreach (var screenshot in game.Screenshots)
            {
                var newPath = AppPaths.RewritePathPrefix(screenshot.Path, oldPrefix, newPrefix) ?? screenshot.Path;
                if (newPath != screenshot.Path)
                {
                    screenshot.Path = newPath;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            GameLibraryRepository.Save(_games, _currentGamesPath);
        }
    }

    /// <summary>"설정 &gt; 환경설정" — 기본 폴더/압축 위치를 편집한다. 실제 편집·마이그레이션은
    /// <see cref="PreferencesWindow"/>가 처리하고, 여기서는 그 결과로 게임 목록의 경로들을 바로잡는다
    /// (이 창은 게임 목록을 모르므로 그 부분은 호출자 몫이다).</summary>
    private void OpenPreferences_Click(object sender, RoutedEventArgs e)
    {
        var oldGamesBaseDir = AppPaths.GamesBaseDir;
        var oldAppDataDir = Path.Combine(oldGamesBaseDir, "GamePlatform");
        var oldGamesPath = AppPaths.GamesPath;

        var dialog = new PreferencesWindow { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!string.Equals(Path.GetFullPath(oldGamesBaseDir), Path.GetFullPath(AppPaths.GamesBaseDir), StringComparison.OrdinalIgnoreCase))
        {
            var newAppDataDir = Path.Combine(AppPaths.GamesBaseDir, "GamePlatform");
            RewriteGamePathsPrefix(oldAppDataDir, newAppDataDir);

            // 이 세션이 기본 위치의 게임 목록 파일을 보고 있었다면(파일 메뉴로 다른 파일을 열어두지 않았다면)
            // 새 위치로 계속 따라가게 한다.
            if (_currentGamesPath.Equals(oldGamesPath, StringComparison.OrdinalIgnoreCase))
            {
                _currentGamesPath = AppPaths.GamesPath;
            }
        }

        SetStatus("환경설정을 저장했습니다.", StatusType.Success);
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

    private void LargeSizeMenuItem_Click(object sender, RoutedEventArgs e) => ChangeCardSize(GameCardSize.Large);

    private void SmallSizeMenuItem_Click(object sender, RoutedEventArgs e) => ChangeCardSize(GameCardSize.Small);

    private void ChangeCardSize(GameCardSize size)
    {
        GameCardSizeSettings.Current.Apply(size);
        UpdateCardSizeMenuChecks(size);
        _settings.CardSizePreset = size.ToString();
        SettingsRepository.Save(_settings);
    }

    /// <summary>"보기 > 카드 크기" 메뉴는 라디오 버튼처럼 하나만 체크되어야 하는데, WPF `MenuItem`은
    /// `RadioButton.GroupName`같은 상호 배타 그룹을 제공하지 않아 직접 관리한다.</summary>
    private void UpdateCardSizeMenuChecks(GameCardSize size)
    {
        LargeSizeMenuItem.IsChecked = size == GameCardSize.Large;
        SmallSizeMenuItem.IsChecked = size == GameCardSize.Small;
    }

    #region 게임 목록 파일 (열기 / 저장 / 다른 이름으로 저장)

    private void OpenGameDb_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "게임 목록 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(_currentGamesPath),
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var loaded = GameLibraryRepository.Load(dialog.FileName);

        _games.Clear();
        foreach (var game in loaded)
        {
            game.RefreshExecutableValid();
            game.RefreshArchiveValid();
            _games.Add(game);
        }

        _currentGamesPath = dialog.FileName;
        SetStatus($"'{Path.GetFileName(_currentGamesPath)}' 파일을 열었습니다 ({_games.Count}개 게임). 앞으로 이 파일에 자동 저장됩니다.", StatusType.Success);
    }

    private void SaveGameDb_Click(object sender, RoutedEventArgs e)
    {
        GameLibraryRepository.Save(_games, _currentGamesPath);
        SetStatus($"'{Path.GetFileName(_currentGamesPath)}'에 저장했습니다.", StatusType.Success);
    }

    private void SaveGameDbAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "게임 목록 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
            FileName = Path.GetFileName(_currentGamesPath),
            InitialDirectory = Path.GetDirectoryName(_currentGamesPath),
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _currentGamesPath = dialog.FileName;
        GameLibraryRepository.Save(_games, _currentGamesPath);
        SetStatus($"'{Path.GetFileName(_currentGamesPath)}'(으)로 저장했습니다. 앞으로 이 파일에 자동 저장됩니다.", StatusType.Success);
    }

    #endregion

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

    private void ShowCompressProgress(string label, bool indeterminate = false)
    {
        CompressProgressPanel.Visibility = Visibility.Visible;
        CompressProgressLabel.Text = label;
        CompressProgressBar.IsIndeterminate = indeterminate;
        CompressProgressBar.Value = 0;
    }

    private void HideCompressProgress()
    {
        CompressProgressPanel.Visibility = Visibility.Collapsed;
        CompressProgressBar.IsIndeterminate = false;
    }

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
                _ = AddGameFromFolderAsync(path);
            }
            else if (string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                AddGameFromExecutable(path);
            }
            else if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                _ = AddGameFromArchiveAsync(path);
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

    /// <summary>폴더로 게임을 추가한다. 이미 <see cref="AppPaths.GamesBaseDir"/>(D:\game) 밑에 있는 폴더면
    /// 그 자리를 그대로 쓰고, 그 바깥에 있으면 게임 등록과 함께 그 밑으로 옮긴다(doc/game-management.md
    /// "게임 추가" 참고, 사용자 요청) — 백그라운드 스레드에서 진행하며 상태바에 진행률을 보여준다.</summary>
    private async Task AddGameFromFolderAsync(string folderPath)
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

        var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var targetFolderPath = folderPath;

        if (!AppPaths.IsUnderGamesBaseDir(folderPath))
        {
            var destFolder = AppPaths.ReserveUniquePath(FileNameHelper.Sanitize(folderName));
            ShowCompressProgress($"'{folderName}' 폴더를 '{AppPaths.GamesBaseDir}'(으)로 옮기는 중...");
            SetStatus($"'{folderName}' 폴더를 '{AppPaths.GamesBaseDir}'(으)로 옮기는 중입니다...", StatusType.Info);

            try
            {
                var progress = new Progress<int>(percent => CompressProgressBar.Value = percent);
                await Task.Run(() => CopyDirectoryContents(folderPath, destFolder, progress));
            }
            catch (Exception ex)
            {
                try { if (Directory.Exists(destFolder)) Directory.Delete(destFolder, recursive: true); } catch { /* 미완성 복사본 정리 시도, 실패해도 무시 */ }
                SetStatus($"'{folderName}' 폴더를 옮기지 못했습니다: {ex.Message}", StatusType.Error);
                HideCompressProgress();
                return;
            }

            targetFolderPath = destFolder;

            // 복사는 이미 끝났다 — 원본 삭제가 실패해도(파일 잠금 등) 게임은 새 위치 기준으로 등록한다
            // (게임 압축과 같은 원칙: 핵심 작업 성공과 뒷정리 실패를 분리한다).
            try
            {
                await Task.Run(() => RetryDelete(() => Directory.Delete(folderPath, recursive: true)));
            }
            catch (Exception ex)
            {
                SetStatus($"'{folderName}' 폴더를 옮겼지만 원본을 지우지 못했습니다({ex.Message}) — 나중에 수동으로 지워도 됩니다.", StatusType.Warning);
            }
            finally
            {
                HideCompressProgress();
            }
        }

        var item = new GameItem
        {
            Name = Path.GetFileName(targetFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            ExecutablePath = Path.Combine(targetFolderPath, chosen),
        };
        item.RefreshExecutableValid();
        _games.Add(item);
        SaveState();
        SetStatus($"'{item.DisplayName}' 게임을 폴더에서 추가했습니다.", StatusType.Success);
    }

    /// <summary>압축 파일(zip)로 게임을 추가한다 — 압축을 그 자리에서 풀지 않고, zip 자체를 이 게임의 압축
    /// 파일로 등록한다(압축된 상태로 시작). 실제 압축 해제는 사용자가 카드의 [압축 풀기]를 눌렀을 때
    /// <see cref="AppPaths.GamesBaseDir"/> 밑에 예약해둔 폴더에서 이루어진다. 압축 파일 자체도 이미
    /// <see cref="AppPaths.GamesBaseDir"/> 밑에 있지 않으면(압축 명령이 만든, 이미 관리 중인 압축 파일이
    /// 아니면) 그 밑으로 옮긴다(doc/game-management.md "게임 추가" 참고, 사용자 요청).</summary>
    private async Task AddGameFromArchiveAsync(string zipPath)
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

        var archivePath = zipPath;
        if (!AppPaths.IsUnderGamesBaseDir(zipPath))
        {
            archivePath = AppPaths.ReserveUniquePath(Path.GetFileName(zipPath));
            var zipName = Path.GetFileName(zipPath);
            ShowCompressProgress($"'{zipName}'을(를) '{AppPaths.GamesBaseDir}'(으)로 옮기는 중...", indeterminate: true);
            SetStatus($"'{zipName}'을(를) '{AppPaths.GamesBaseDir}'(으)로 옮기는 중입니다...", StatusType.Info);

            try
            {
                await Task.Run(() => File.Move(zipPath, archivePath));
            }
            catch (Exception ex)
            {
                HideCompressProgress();
                MessageBox.Show(this, $"압축 파일을 옮기지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            HideCompressProgress();
        }

        try
        {
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
            DeleteExistingCoverFiles(destDir);
            var newPath = ThumbnailHelper.CopyOriginal(sourceImagePath, destDir, "cover", deleteSource);

            // CopyOriginal은 항상 "cover.original.{확장자}"에 저장하므로, 새로 드롭한 이미지가 이전 것과
            // 확장자가 같으면 경로 문자열 자체는 그대로다. GameItem.ThumbnailPath의 setter는 값이 실제로
            // 바뀔 때만 PropertyChanged를 올리므로, 그냥 대입하면 파일은 새로 덮어써졌는데 카드 이미지는
            // 갱신되지 않는다(실제로 겪은 버그) — null로 한 번 지웠다가 다시 지정해서 항상 알림이 나가게 한다.
            item.ThumbnailPath = null;
            item.ThumbnailPath = newPath;

            SaveState();
            SetStatus($"'{item.DisplayName}'의 대표 썸네일을 지정했습니다.", StatusType.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"썸네일을 지정하지 못했습니다: {ex.Message}", StatusType.Error);
        }
    }

    /// <summary>새 대표 썸네일을 저장하기 전에 기존 "cover.original.*" 파일을 지운다 — 새 이미지의
    /// 확장자가 이전과 다르면(예: png → jpg) 옛 파일이 정리되지 않고 고아로 남는 것을 막는다.</summary>
    private static void DeleteExistingCoverFiles(string destDir)
    {
        if (!Directory.Exists(destDir))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(destDir, "cover.original.*"))
        {
            try { File.Delete(file); } catch { /* 새 썸네일 저장에는 지장 없으므로 무시 */ }
        }
    }

    private void DeleteThumbnail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameItem item } || !item.HasThumbnail)
        {
            return;
        }

        try
        {
            if (!string.IsNullOrEmpty(item.ThumbnailPath) && File.Exists(item.ThumbnailPath))
            {
                File.Delete(item.ThumbnailPath);
            }
        }
        catch
        {
            // 파일 삭제 실패는 무시한다 — 그래도 목록에서는 참조를 지운다.
        }

        item.ThumbnailPath = null;
        SaveState();
        SetStatus($"'{item.DisplayName}'의 대표 썸네일을 삭제했습니다.", StatusType.Success);
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

        AppPaths.EnsureArchiveDirectory(item.Id);
        var archivePath = AppPaths.GameArchivePath(item.Id, item.DisplayName);

        try
        {
            var progress = new Progress<int>(percent => CompressProgressBar.Value = percent);
            await Task.Run(() => CompressDirectory(gameDir, archivePath, progress));
        }
        catch (Exception ex)
        {
            try { if (File.Exists(archivePath)) File.Delete(archivePath); } catch { /* 미완성 zip 정리 시도, 실패해도 무시 */ }
            SetStatus($"'{item.DisplayName}' 압축에 실패했습니다: {ex.Message}", StatusType.Error);
            item.IsBusy = false;
            HideCompressProgress();
            return;
        }

        // 압축 파일은 이 시점에 이미 완성됐다 — 그 아래 폴더 삭제가 실패하더라도 이 게임을 "압축됨"으로
        // 등록해서, 방금 만든 zip이 games.json 어디서도 참조되지 않는 고아 파일로 남지 않게 한다
        // (실제로 겪은 문제: 압축은 끝났는데 원본 폴더 삭제만 "다른 프로세스가 사용 중"으로 실패하자,
        // 전체를 실패로 취급해서 완성된 zip이 등록되지 않은 채 남아버렸다).
        item.ArchivePath = archivePath;
        item.ArchiveSizeBytes = new FileInfo(archivePath).Length;
        item.CompressedAtUtc = DateTime.UtcNow;
        item.IsCompressed = true;
        item.RefreshExecutableValid();
        item.RefreshArchiveValid();
        SaveState();

        try
        {
            await Task.Run(() => RetryDelete(() => Directory.Delete(gameDir, recursive: true)));
            SetStatus($"'{item.DisplayName}' 압축을 완료했습니다 ({item.ArchiveSizeDisplay}).", StatusType.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"'{item.DisplayName}' 압축은 완료했지만 원본 폴더를 지우지 못했습니다({ex.Message}) — 나중에 수동으로 지워도 됩니다.", StatusType.Warning);
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

        var archivePath = item.ArchivePath;

        try
        {
            var progress = new Progress<int>(percent => CompressProgressBar.Value = percent);
            await Task.Run(() => ExtractArchive(archivePath, gameDir, progress));
        }
        catch (Exception ex)
        {
            SetStatus($"'{item.DisplayName}' 압축을 풀지 못했습니다: {ex.Message}", StatusType.Error);
            item.IsBusy = false;
            HideCompressProgress();
            return;
        }

        // 파일은 이미 다 풀렸다 — 옛 압축 파일 삭제가 실패해도 이 게임은 "압축 안 됨"으로 되돌린다
        // (압축과 대칭: 핵심 작업 성공 여부와 뒷정리 실패를 분리해서, 뒷정리 실패로 전체를 실패 취급하지 않는다).
        item.IsCompressed = false;
        item.ArchivePath = null;
        item.ArchiveSizeBytes = 0;
        item.CompressedAtUtc = null;
        item.RefreshExecutableValid();
        item.RefreshArchiveValid();
        SaveState();

        try
        {
            await Task.Run(() => RetryDelete(() => File.Delete(archivePath)));
            SetStatus($"'{item.DisplayName}' 압축을 풀었습니다.", StatusType.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"'{item.DisplayName}' 압축은 풀었지만 옛 압축 파일을 지우지 못했습니다({ex.Message}) — 나중에 수동으로 지워도 됩니다.", StatusType.Warning);
        }
        finally
        {
            item.IsBusy = false;
            HideCompressProgress();
        }
    }

    /// <summary>백신 실시간 검사/탐색기 등이 파일이나 폴더를 잠깐 잠그는 경우가 흔해서, 바로 실패 처리하지
    /// 않고 짧은 대기 후 몇 번 더 시도한다 (실제로 겪은 사례: 압축은 끝났는데 원본 폴더 삭제만 "다른 프로세스가
    /// 사용 중"으로 실패 — 잠깐 뒤에는 대개 풀린다). 백그라운드 스레드(<c>Task.Run</c>)에서 호출되므로 총
    /// 대기 시간(기본값 기준 약 9초)만큼 걸려도 창은 멈추지 않는다. 그래도 안 풀리면 <see cref="CleanUpLeftoverCompressedFoldersAsync"/>가
    /// 다음 실행 시 다시 시도한다.</summary>
    private static void RetryDelete(Action deleteAction, int attempts = 10, int delayMilliseconds = 1000)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                deleteAction();
                return;
            }
            catch when (attempt < attempts)
            {
                Thread.Sleep(delayMilliseconds);
            }
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

    /// <summary>폴더를 다른 폴더로 통째로 복사하며 진행률(전체 파일 수 대비 처리한 파일 수)을 보고한다 —
    /// <see cref="Directory.Move"/>는 드라이브가 다르면 동작하지 않으므로, 게임 추가 시 폴더를
    /// <see cref="AppPaths.GamesBaseDir"/> 밑으로 옮길 때 복사+원본 삭제 방식으로 쓴다. 백그라운드
    /// 스레드에서 호출된다.</summary>
    private static void CopyDirectoryContents(string sourceDir, string destDir, IProgress<int> progress)
    {
        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);

        for (var i = 0; i < files.Length; i++)
        {
            var relative = Path.GetRelativePath(sourceDir, files[i]);
            var destPath = Path.Combine(destDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(files[i], destPath, overwrite: true);
            progress.Report((i + 1) * 100 / files.Length);
        }

        if (files.Length == 0)
        {
            Directory.CreateDirectory(destDir);
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
        GameLibraryRepository.Save(_games, _currentGamesPath);
        SettingsRepository.Save(_settings);
        BackupService.CheckAndBackup(_settings, _currentGamesPath);
    }
}
