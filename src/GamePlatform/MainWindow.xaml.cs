using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace GamePlatform;

public partial class MainWindow : Window
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

    private readonly ObservableCollection<GameItem> _games;
    private readonly AppSettings _settings;
    private readonly ICollectionView _gamesView;

    /// <summary>리스트 보기(테이블) 칼럼 객체 ↔ 설정에 저장할 때 쓰는 이름. <see cref="ApplyListColumnWidths"/>가
    /// 시작할 때 저장된 너비를 적용하고, 이후 각 칼럼의 `Width`가 바뀔 때마다(사용자가 헤더 경계를 드래그)
    /// 그 이름으로 <see cref="AppSettings.ListColumnWidths"/>에 저장한다.</summary>
    private Dictionary<GridViewColumn, string> _listColumnKeys = new();

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

        _gamesView = CollectionViewSource.GetDefaultView(_games);
        GamesItemsControl.ItemsSource = _gamesView;
        GamesListView.ItemsSource = _gamesView;

        var sortOrder = Enum.TryParse<GameSortOrder>(_settings.SortOrder, out var parsedSortOrder) ? parsedSortOrder : GameSortOrder.Ascending;
        ApplySortOrder(sortOrder);
        UpdateSortMenuChecks(sortOrder);

        var viewMode = Enum.TryParse<GameViewMode>(_settings.ViewMode, out var parsedViewMode) ? parsedViewMode : GameViewMode.Icon;
        ApplyViewMode(viewMode);
        UpdateViewModeMenuChecks(viewMode);

        // ListColumnVisibilityMenu의 MenuItem들은 Window.Resources 안에 있어 x:Name으로 필드가 생성되지
        // 않으므로, 리소스 자체를 찾아 순서(XAML 선언 순서: 버전/평점/신사 등급/폴더)로 접근한다.
        var columnMenu = (ContextMenu)Resources["ListColumnVisibilityMenu"];
        ((MenuItem)columnMenu.Items[0]).IsChecked = _settings.ListShowVersion;
        ((MenuItem)columnMenu.Items[1]).IsChecked = _settings.ListShowRating;
        ((MenuItem)columnMenu.Items[2]).IsChecked = _settings.ListShowGentlemanGrade;
        ((MenuItem)columnMenu.Items[3]).IsChecked = _settings.ListShowFolder;
        RebuildListColumns();
        ApplyListColumnWidths();

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

    #region 등록된 게임 정리 (분류 폴더 밖으로 이동)

    /// <summary>메뉴 "설정 > 분류 폴더 안의 게임 정리" — 이미 등록된 게임 중 사용자가 기본 폴더 안에 직접
    /// 만들어 둔 분류용 하위 폴더(예: `D:\game\- rpg -\게임폴더`)에 남아 있는 것을 찾아 기본 폴더 바로 밑으로
    /// 옮기고 games.json의 ExecutablePath/ArchivePath를 새 위치로 갱신한다. 폴더/압축 파일로 게임을 새로
    /// 추가할 때 이미 적용 중인 "기본 폴더 바로 밑이 아니면 옮긴다" 규칙
    /// (<see cref="AppPaths.IsDirectlyUnderGamesBaseDir"/>, doc/game-management.md "게임 추가" 참고)을 과거에
    /// 등록된 게임에도 소급 적용하는 일회성 정리 기능이다(2026-09-06 추가, 사용자 요청). exe 하나만 단독으로
    /// 등록한 게임(기본 폴더 밖 어디에 있든)은 애초에 옮기는 대상이 아니므로 건드리지 않는다.</summary>
    private async void CleanUpCategoryFolderGames_Click(object sender, RoutedEventArgs e)
    {
        var folderCandidates = new List<(GameItem Item, string GameFolder, string RelativeExecutablePath)>();
        var archiveCandidates = new List<(GameItem Item, string ArchiveFile)>();

        foreach (var item in _games)
        {
            if (item.IsBusy)
            {
                continue;
            }

            if (item.IsCompressed)
            {
                if (!string.IsNullOrEmpty(item.ArchivePath) && File.Exists(item.ArchivePath) &&
                    TryGetNestedArchiveFile(item.ArchivePath, out var archiveFile))
                {
                    archiveCandidates.Add((item, archiveFile));
                }
            }
            else if (!string.IsNullOrEmpty(item.ExecutablePath) &&
                     TryGetNestedGameFolder(item.ExecutablePath, out var gameFolder, out var relativeExe))
            {
                folderCandidates.Add((item, gameFolder, relativeExe));
            }
        }

        var totalCount = folderCandidates.Count + archiveCandidates.Count;
        if (totalCount == 0)
        {
            MessageBox.Show(this, "분류 폴더 안에 남아 있는 게임이 없습니다.", "정리", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"'{AppPaths.GamesBaseDir}' 안의 분류 폴더에 있는 게임 {totalCount}개를 기본 폴더 바로 밑으로 옮기고 정보를 갱신합니다.\n계속할까요?",
            "게임 정리", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var succeeded = 0;
        var failed = 0;
        var current = 0;

        foreach (var (item, gameFolder, relativeExe) in folderCandidates)
        {
            current++;
            item.IsBusy = true;
            ShowCompressProgress($"({current}/{totalCount}) '{item.DisplayName}' 폴더를 '{AppPaths.GamesBaseDir}'(으)로 옮기는 중...", indeterminate: true);
            SetStatus($"'{item.DisplayName}' 폴더를 '{AppPaths.GamesBaseDir}'(으)로 옮기는 중입니다...", StatusType.Info);

            var destFolder = AppPaths.ReserveUniquePath(FileNameHelper.Sanitize(Path.GetFileName(gameFolder)));
            try
            {
                await Task.Run(() => MoveDirectory(gameFolder, destFolder));
                item.ExecutablePath = Path.Combine(destFolder, relativeExe);
                item.RefreshExecutableValid();
                succeeded++;
            }
            catch (Exception ex)
            {
                failed++;
                SetStatus($"'{item.DisplayName}' 폴더를 옮기지 못했습니다: {ex.Message}", StatusType.Error);
            }
            finally
            {
                item.IsBusy = false;
                HideCompressProgress();
            }
        }

        foreach (var (item, archiveFile) in archiveCandidates)
        {
            current++;
            item.IsBusy = true;
            ShowCompressProgress($"({current}/{totalCount}) '{item.DisplayName}' 압축 파일을 '{AppPaths.GamesBaseDir}'(으)로 옮기는 중...", indeterminate: true);
            SetStatus($"'{item.DisplayName}' 압축 파일을 '{AppPaths.GamesBaseDir}'(으)로 옮기는 중입니다...", StatusType.Info);

            var destFile = AppPaths.ReserveUniquePath(Path.GetFileName(archiveFile));
            try
            {
                await Task.Run(() => File.Move(archiveFile, destFile));
                item.ArchivePath = destFile;
                item.RefreshArchiveValid();
                succeeded++;
            }
            catch (Exception ex)
            {
                failed++;
                SetStatus($"'{item.DisplayName}' 압축 파일을 옮기지 못했습니다: {ex.Message}", StatusType.Error);
            }
            finally
            {
                item.IsBusy = false;
                HideCompressProgress();
            }
        }

        SaveState();
        SetStatus(
            failed == 0
                ? $"게임 {succeeded}개를 '{AppPaths.GamesBaseDir}' 밑으로 옮기고 정보를 갱신했습니다."
                : $"게임 {succeeded}개를 옮겼고, {failed}개는 실패했습니다.",
            failed == 0 ? StatusType.Success : StatusType.Warning);
    }

    /// <summary>실행 파일 경로가 기본 폴더 안의 분류용 하위 폴더 밑에 있으면(기본 폴더 기준 "분류 폴더/게임
    /// 폴더/.../실행 파일"처럼 3단계 이상 깊이) 옮겨야 할 게임 폴더 자체(분류 폴더 바로 밑의 폴더)와 그 폴더
    /// 기준 실행 파일의 상대 경로를 계산해 돌려준다. 이미 기본 폴더 바로 밑에 있거나(정상), 기본 폴더 밖에
    /// 있으면(exe 단독 드래그드롭 — 원래부터 옮기지 않는 대상), 또는 분류 폴더 바로 밑에 exe가 단독으로 있어
    /// (2단계 깊이) 같이 옮겨야 할 폴더를 특정할 수 없으면 false.</summary>
    private static bool TryGetNestedGameFolder(string executablePath, out string gameFolder, out string relativeExecutablePath)
    {
        gameFolder = string.Empty;
        relativeExecutablePath = string.Empty;

        var baseFull = Path.GetFullPath(AppPaths.GamesBaseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = baseFull + Path.DirectorySeparatorChar;
        var fullExe = Path.GetFullPath(executablePath);
        if (!fullExe.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = fullExe[prefix.Length..].Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3)
        {
            return false;
        }

        gameFolder = Path.Combine(baseFull, segments[0], segments[1]);
        relativeExecutablePath = Path.Combine(segments[2..]);
        return true;
    }

    /// <summary>압축 파일 경로가 기본 폴더 안의 분류용 하위 폴더 밑에 있으면(기본 폴더 바로 밑이 아님) 그
    /// 파일 경로를 그대로 돌려준다. "압축" 명령이 만드는 내부 관리 위치(<see cref="AppPaths.ArchivesDir"/> 밑)는
    /// 원래부터 깊은 곳에 있는 게 정상이므로 제외한다.</summary>
    private static bool TryGetNestedArchiveFile(string archivePath, out string archiveFile)
    {
        archiveFile = string.Empty;

        var baseFull = Path.GetFullPath(AppPaths.GamesBaseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = baseFull + Path.DirectorySeparatorChar;
        var fullArchive = Path.GetFullPath(archivePath);
        if (!fullArchive.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var archivesFull = Path.GetFullPath(AppPaths.ArchivesDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (fullArchive.Equals(archivesFull, StringComparison.OrdinalIgnoreCase) ||
            fullArchive.StartsWith(archivesFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (AppPaths.IsDirectlyUnderGamesBaseDir(fullArchive))
        {
            return false;
        }

        archiveFile = fullArchive;
        return true;
    }

    /// <summary>같은 기본 폴더 안에서의 이동은 항상 같은 드라이브이므로 <see cref="Directory.Move"/>로
    /// 즉시(이름 변경 수준으로) 옮긴다. 혹시 실패하면(예: 파일 잠금) 게임 추가와 같은 복사 후 삭제 방식으로
    /// 대체한다.</summary>
    private static void MoveDirectory(string sourceDir, string destDir)
    {
        try
        {
            Directory.Move(sourceDir, destDir);
        }
        catch
        {
            CopyDirectoryContents(sourceDir, destDir, new Progress<int>());
            RetryDelete(() => Directory.Delete(sourceDir, recursive: true));
        }
    }

    #endregion

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

    private void AscendingSortMenuItem_Click(object sender, RoutedEventArgs e) => ChangeSortOrder(GameSortOrder.Ascending);

    private void DescendingSortMenuItem_Click(object sender, RoutedEventArgs e) => ChangeSortOrder(GameSortOrder.Descending);

    private void ChangeSortOrder(GameSortOrder order)
    {
        ApplySortOrder(order);
        UpdateSortMenuChecks(order);
        _settings.SortOrder = order.ToString();
        SettingsRepository.Save(_settings);
    }

    /// <summary>게임 목록을 `DisplayName` 기준으로 정렬한다 — 원본 컬렉션(<see cref="_games"/>, games.json 저장
    /// 순서)은 건드리지 않고, 화면에 보여주는 <see cref="_gamesView"/>(아이콘/리스트 보기 공용)의 정렬 조건만
    /// 바꾼다.</summary>
    private void ApplySortOrder(GameSortOrder order)
    {
        _gamesView.SortDescriptions.Clear();
        _gamesView.SortDescriptions.Add(new SortDescription(
            nameof(GameItem.DisplayName),
            order == GameSortOrder.Ascending ? ListSortDirection.Ascending : ListSortDirection.Descending));
    }

    /// <summary>"보기 > 정렬" 메뉴도 카드 크기와 같은 방식으로 상호 배타를 직접 관리한다.</summary>
    private void UpdateSortMenuChecks(GameSortOrder order)
    {
        AscendingSortMenuItem.IsChecked = order == GameSortOrder.Ascending;
        DescendingSortMenuItem.IsChecked = order == GameSortOrder.Descending;
    }

    private void ListViewMenuItem_Click(object sender, RoutedEventArgs e) => ChangeViewMode(GameViewMode.List);

    private void IconViewMenuItem_Click(object sender, RoutedEventArgs e) => ChangeViewMode(GameViewMode.Icon);

    private void ChangeViewMode(GameViewMode mode)
    {
        ApplyViewMode(mode);
        UpdateViewModeMenuChecks(mode);
        _settings.ViewMode = mode.ToString();
        SettingsRepository.Save(_settings);
    }

    /// <summary>아이콘 보기(기존 썸네일 카드 그리드)와 리스트 보기(한 줄 목록) 중 하나만 보여준다 — 둘 다 같은
    /// <see cref="_gamesView"/>를 공유하므로 정렬/데이터는 항상 같고 화면에 보이는 모양만 바뀐다.</summary>
    private void ApplyViewMode(GameViewMode mode)
    {
        GamesItemsControl.Visibility = mode == GameViewMode.Icon ? Visibility.Visible : Visibility.Collapsed;
        GamesListView.Visibility = mode == GameViewMode.List ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>"보기 > 리스트 보기/아이콘 보기" 메뉴도 같은 방식으로 상호 배타를 직접 관리한다.</summary>
    private void UpdateViewModeMenuChecks(GameViewMode mode)
    {
        ListViewMenuItem.IsChecked = mode == GameViewMode.List;
        IconViewMenuItem.IsChecked = mode == GameViewMode.Icon;
    }

    /// <summary>리스트 보기(테이블) 헤더 우클릭 메뉴의 버전/평점/신사 등급/폴더 항목 — 넷 다 `IsCheckable`이라
    /// 클릭 시점에는 이미 새 체크 상태로 바뀌어 있으므로, 그 값을 그대로 설정에 반영하고 칼럼 목록을 다시
    /// 구성한다.</summary>
    private void ToggleVersionColumn_Click(object sender, RoutedEventArgs e) => ToggleListColumn(sender, v => _settings.ListShowVersion = v);

    private void ToggleRatingColumn_Click(object sender, RoutedEventArgs e) => ToggleListColumn(sender, v => _settings.ListShowRating = v);

    private void ToggleGentlemanGradeColumn_Click(object sender, RoutedEventArgs e) => ToggleListColumn(sender, v => _settings.ListShowGentlemanGrade = v);

    private void ToggleFolderColumn_Click(object sender, RoutedEventArgs e) => ToggleListColumn(sender, v => _settings.ListShowFolder = v);

    private void ToggleListColumn(object sender, Action<bool> applyToSettings)
    {
        if (sender is not MenuItem menuItem)
        {
            return;
        }

        applyToSettings(menuItem.IsChecked);
        RebuildListColumns();
        SettingsRepository.Save(_settings);
    }

    /// <summary>리스트 보기 테이블의 칼럼을 "게임 이름/버전/평점/신사 등급/폴더/정보/실행" 고정 순서로 다시
    /// 채운다 — 버전/평점/신사 등급/폴더는 설정에서 꺼져 있으면 통째로 뺀다. `GridViewColumn`은 일반 시각
    /// 요소가 아니라 매번 새로 만들 필요 없이 Columns 컬렉션에서 넣었다 뺐다 할 수 있다.</summary>
    private void RebuildListColumns()
    {
        GamesGridView.Columns.Clear();
        GamesGridView.Columns.Add(NameColumn);
        if (_settings.ListShowVersion) GamesGridView.Columns.Add(VersionColumn);
        if (_settings.ListShowRating) GamesGridView.Columns.Add(RatingColumn);
        if (_settings.ListShowGentlemanGrade) GamesGridView.Columns.Add(GentlemanGradeColumn);
        if (_settings.ListShowFolder) GamesGridView.Columns.Add(FolderColumn);
        GamesGridView.Columns.Add(InfoColumn);
        GamesGridView.Columns.Add(RunColumn);
    }

    /// <summary>저장된 리스트 보기 칼럼 너비(<see cref="AppSettings.ListColumnWidths"/>)를 한 번 적용하고,
    /// 이후 사용자가 헤더 경계를 드래그해서 너비를 바꿀 때마다 다시 저장하도록 건다. `GridViewColumn.Width`는
    /// 보통의 CLR 이벤트가 없는 의존 속성이라 `DependencyPropertyDescriptor`로 변경을 감시한다 — 칼럼을
    /// 숨겼다 다시 보여도(<see cref="RebuildListColumns"/>) 같은 칼럼 객체를 재사용하므로 이 훅은 시작할 때
    /// 한 번만 걸면 된다.</summary>
    private void ApplyListColumnWidths()
    {
        _listColumnKeys = new Dictionary<GridViewColumn, string>
        {
            [NameColumn] = "Name",
            [VersionColumn] = "Version",
            [RatingColumn] = "Rating",
            [GentlemanGradeColumn] = "GentlemanGrade",
            [FolderColumn] = "Folder",
            [InfoColumn] = "Info",
            [RunColumn] = "Run",
        };

        foreach (var (column, key) in _listColumnKeys)
        {
            if (_settings.ListColumnWidths.TryGetValue(key, out var width))
            {
                column.Width = width;
            }

            DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty, typeof(GridViewColumn))
                .AddValueChanged(column, ListColumnWidth_Changed);
        }
    }

    private void ListColumnWidth_Changed(object? sender, EventArgs e)
    {
        if (sender is not GridViewColumn column || !_listColumnKeys.TryGetValue(column, out var key))
        {
            return;
        }

        _settings.ListColumnWidths[key] = column.Width;
        SettingsRepository.Save(_settings);
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

    /// <summary>새로 추가한 게임 카드/행으로 스크롤을 옮긴다 — 정렬(보기 > 정렬)이 적용되어 있으면 새 게임이
    /// 목록 끝이 아니라 정렬 순서상의 위치에 나타나므로, 아이콘/리스트 보기 둘 다 실제 화면에 보이는 컨트롤
    /// 기준으로 그 항목의 컨테이너를 찾아 <see cref="FrameworkElement.BringIntoView()"/>로 스크롤한다
    /// (2026-09-06 추가, 사용자 요청). `WrapPanel`/`StackPanel`은 가상화하지 않으므로 항목을 추가한 직후에도
    /// 컨테이너가 이미 만들어져 있지만, 레이아웃이 아직 안 끝났을 수 있어 `Dispatcher.BeginInvoke`로 한 박자
    /// 늦춰서 찾는다.</summary>
    private void ScrollGameIntoView(GameItem item)
    {
        var activeControl = GamesListView.Visibility == Visibility.Visible ? (ItemsControl)GamesListView : GamesItemsControl;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (activeControl.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement container)
            {
                container.BringIntoView();
            }
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void AddGameFromExecutable(string executablePath)
    {
        var name = Path.GetFileNameWithoutExtension(executablePath);
        if (!ConfirmAddDuplicateName(name))
        {
            return;
        }

        var item = new GameItem
        {
            Name = name,
            ExecutablePath = executablePath,
        };
        item.RefreshExecutableValid();
        _games.Add(item);
        SaveState();
        SetStatus($"'{item.DisplayName}' 게임을 추가했습니다.", StatusType.Success);
        ScrollGameIntoView(item);
    }

    /// <summary>같은 이름의 게임이 이미 있으면(버전만 다르게 여러 개 존재하는 것이 정상 시나리오이므로)
    /// 버전이 다른 게임이 맞는지 사용자에게 확인한다 — 예: 새 버전으로 추가 진행, 아니요: 추가 취소
    /// (doc/game-management.md "게임 추가" 참고, 사용자 요청). 같은 이름이 없으면 바로 true.</summary>
    private bool ConfirmAddDuplicateName(string name)
    {
        if (!_games.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var confirm = MessageBox.Show(this,
            $"'{name}' 이름의 게임이 이미 있습니다.\n버전이 다른 게임인가요?\n\n예: 새 버전으로 추가합니다.\n아니요: 추가를 취소합니다.",
            "동일한 이름의 게임", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            SetStatus($"'{name}' 게임 추가를 취소했습니다.", StatusType.Info);
            return false;
        }

        return true;
    }

    /// <summary>폴더로 게임을 추가한다. 이미 <see cref="AppPaths.GamesBaseDir"/>(D:\game) 밑에 있는 폴더면
    /// 그 자리를 그대로 쓰고, 그 바깥에 있으면 게임 등록과 함께 그 밑으로 옮긴다(doc/game-management.md
    /// "게임 추가" 참고, 사용자 요청) — 백그라운드 스레드에서 진행하며 상태바에 진행률을 보여준다.</summary>
    private async Task AddGameFromFolderAsync(string folderPath)
    {
        var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!ConfirmAddDuplicateName(folderName))
        {
            return;
        }

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

        var targetFolderPath = folderPath;

        if (!AppPaths.IsDirectlyUnderGamesBaseDir(folderPath))
        {
            var destFolder = AppPaths.ReserveUniquePath(FileNameHelper.Sanitize(folderName));
            ShowCompressProgress($"'{folderName}' 폴더를 '{AppPaths.GamesBaseDir}'(으)로 옮기는 중...", indeterminate: true);
            SetStatus($"'{folderName}' 폴더를 '{AppPaths.GamesBaseDir}'(으)로 옮기는 중입니다...", StatusType.Info);

            // 같은 드라이브 안에서의 이동이면 Directory.Move가 이름 변경 수준으로 즉시 끝나므로(폴더 크기와
            // 무관하게 빠르다) 먼저 시도해본다 — 실제로 겪은 문제: 항상 복사 후 삭제 방식을 썼더니 같은
            // 드라이브 안에서 옮길 때도 대용량 게임 폴더가 파일 단위로 통째로 복사되어 불필요하게 오래
            // 걸렸다. 드라이브가 다르면(Directory.Move가 지원하지 않음) 아래에서 복사 후 삭제로 대체한다.
            bool renamed;
            try
            {
                renamed = await Task.Run(() => TryMoveByRename(folderPath, destFolder));
            }
            catch (Exception ex)
            {
                SetStatus($"'{folderName}' 폴더를 옮기지 못했습니다: {ex.Message}", StatusType.Error);
                HideCompressProgress();
                return;
            }

            if (!renamed)
            {
                ShowCompressProgress($"'{folderName}' 폴더를 '{AppPaths.GamesBaseDir}'(으)로 복사하는 중...");
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
            }

            targetFolderPath = destFolder;
            HideCompressProgress();
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
        ScrollGameIntoView(item);
    }

    /// <summary>압축 파일(zip)로 게임을 추가한다 — 압축을 그 자리에서 풀지 않고, zip 자체를 이 게임의 압축
    /// 파일로 등록한다(압축된 상태로 시작). 실제 압축 해제는 사용자가 카드 우클릭 메뉴의 "압축 풀기"를 눌렀을 때
    /// <see cref="AppPaths.GamesBaseDir"/> 밑에 예약해둔 폴더에서 이루어진다. 압축 파일 자체도 이미
    /// <see cref="AppPaths.GamesBaseDir"/> 밑에 있지 않으면(압축 명령이 만든, 이미 관리 중인 압축 파일이
    /// 아니면) 그 밑으로 옮긴다(doc/game-management.md "게임 추가" 참고, 사용자 요청).</summary>
    private async Task AddGameFromArchiveAsync(string zipPath)
    {
        var name = Path.GetFileNameWithoutExtension(zipPath);
        if (!ConfirmAddDuplicateName(name))
        {
            return;
        }

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

        var item = new GameItem { Name = name };
        var gameFolder = AppPaths.ReserveGameFolder(item.DisplayName);
        item.ExecutablePath = Path.Combine(gameFolder, chosen);

        var archivePath = zipPath;
        if (!AppPaths.IsDirectlyUnderGamesBaseDir(zipPath))
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
            ScrollGameIntoView(item);
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
            // 참조된 파일 하나만이 아니라 "cover.original.*" 전부를 지운다 — 확장자가 바뀐 적이 있었다면
            // (예: png → jpg) 더 이상 참조되지 않는 옛 파일이 남아 있을 수 있으므로, 새 썸네일을 저장할 때
            // 쓰는 것과 같은 정리 로직(DeleteExistingCoverFiles)을 그대로 재사용한다.
            DeleteExistingCoverFiles(AppPaths.GameImagesDir(item.Id));
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

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameItem item })
        {
            return;
        }

        RunGame(item);
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
    /// <see cref="GameItem.IsBusy"/>로 막는다. 되돌리려면 카드 우클릭 메뉴의 "압축 풀기" 항목을 쓴다.</summary>
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

    /// <summary>카드 우클릭 메뉴의 "압축 풀기" 항목.</summary>
    private void ExtractGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GameItem item })
        {
            return;
        }

        _ = ExtractGameAsync(item);
    }

    /// <summary>카드 우클릭 메뉴의 "압축 풀기" — 압축 파일을 원래 게임 폴더 위치(또는 zip으로 추가된 게임이면
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

    /// <summary>같은 드라이브 안에서의 이동이면 <see cref="Directory.Move"/>로 이름 변경 수준으로 즉시(폴더
    /// 크기와 무관하게 빠르게) 옮긴다. 성공하면 true. 드라이브가 다르면 <see cref="Directory.Move"/>가
    /// IOException("Move will not work across volumes")을 던지는데, 이 경우만 false를 돌려주고 호출자가
    /// <see cref="CopyDirectoryContents"/>로 대체하게 한다 — 그 외의 예외(예: 권한 부족)는 그대로 전파해서
    /// 호출자가 실패로 처리하게 둔다. 백그라운드 스레드에서 호출된다.</summary>
    private static bool TryMoveByRename(string sourceDir, string destDir)
    {
        try
        {
            Directory.Move(sourceDir, destDir);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>폴더를 다른 폴더로 통째로 복사하며 진행률(전체 파일 수 대비 처리한 파일 수)을 보고한다 —
    /// <see cref="TryMoveByRename"/>이 드라이브가 달라 실패했을 때만 쓰는 대체 경로다. 백그라운드
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
