using System.Windows;

namespace GamePlatform;

/// <summary>게임 폴더의 새 이름을 입력받는 대화상자 — <see cref="SelectExecutableWindow"/>와 같은 작은 모달
/// 패턴(doc/game-management.md "정보 창" 참고).</summary>
public partial class RenameFolderWindow : Window
{
    public string? NewName { get; private set; }

    public RenameFolderWindow(string currentName)
    {
        InitializeComponent();
        NameTextBox.Text = currentName;
        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Accept();

    private void Accept()
    {
        var name = NameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        NewName = name;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <returns>사용자가 새 이름을 입력하고 확인하면 그 이름(정리 전 원본 문자열), 취소하면 null.</returns>
    public static string? Prompt(Window owner, string currentName)
    {
        var window = new RenameFolderWindow(currentName) { Owner = owner };
        return window.ShowDialog() == true ? window.NewName : null;
    }
}
