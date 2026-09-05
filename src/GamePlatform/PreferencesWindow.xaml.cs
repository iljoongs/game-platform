using System.IO;
using System.Windows;

namespace GamePlatform;

/// <summary>
/// "설정 > 환경설정" 대화상자. 기본 폴더(<see cref="AppPaths.GamesBaseDir"/>)와 압축 위치
/// (<see cref="AppPaths.ArchivesDirOverride"/>)를 편집하고, 확인을 누르면 <see cref="AppConfigRepository"/>에
/// 즉시 저장한다. 기본 폴더가 실제로 바뀌면 이 앱의 관리 데이터 폴더를 새 위치로 옮기는 것까지 여기서 처리하고,
/// 게임 목록에 이미 저장된 절대경로를 바로잡는 것은 호출자(<see cref="MainWindow"/>)의 몫이다 — 이 창은
/// 어떤 게임이 있는지 모르기 때문이다.
/// </summary>
public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
        GamesBaseDirTextBox.Text = AppPaths.GamesBaseDir;
        ArchivesDirTextBox.Text = AppPaths.ArchivesDirOverride ?? string.Empty;
    }

    private void BrowseGamesBaseDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            InitialDirectory = GamesBaseDirTextBox.Text,
        };

        if (dialog.ShowDialog(this) == true)
        {
            GamesBaseDirTextBox.Text = dialog.FolderName;
        }
    }

    private void BrowseArchivesDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            InitialDirectory = string.IsNullOrWhiteSpace(ArchivesDirTextBox.Text) ? AppPaths.GamesBaseDir : ArchivesDirTextBox.Text,
        };

        if (dialog.ShowDialog(this) == true)
        {
            ArchivesDirTextBox.Text = dialog.FolderName;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var newBaseDir = GamesBaseDirTextBox.Text.Trim();
        if (string.IsNullOrEmpty(newBaseDir))
        {
            MessageBox.Show(this, "기본 폴더를 입력하세요.", "환경설정", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var oldBaseDir = AppPaths.GamesBaseDir;
        var baseDirChanged = !string.Equals(Path.GetFullPath(oldBaseDir), Path.GetFullPath(newBaseDir), StringComparison.OrdinalIgnoreCase);

        if (baseDirChanged)
        {
            var confirm = MessageBox.Show(this,
                $"기본 폴더를 '{oldBaseDir}'에서 '{newBaseDir}'(으)로 바꿉니다.\n" +
                "이 앱의 관리 데이터(게임 목록, 썸네일 등)를 새 위치로 옮깁니다. 이미 다른 곳에 있는 게임 폴더/압축 파일 자체는 옮기지 않습니다.\n계속할까요?",
                "기본 폴더 변경", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                AppPaths.MigrateAppDataDir(oldBaseDir, newBaseDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"관리 데이터를 옮기지 못했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            AppPaths.GamesBaseDir = newBaseDir;
        }

        var newArchivesOverride = ArchivesDirTextBox.Text.Trim();
        AppPaths.ArchivesDirOverride = string.IsNullOrEmpty(newArchivesOverride) ? null : newArchivesOverride;

        AppConfigRepository.Save(new AppConfig
        {
            GamesBaseDir = AppPaths.GamesBaseDir,
            ArchivesDirOverride = AppPaths.ArchivesDirOverride,
        });

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
