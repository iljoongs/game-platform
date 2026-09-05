using System.Windows;
using System.Windows.Input;

namespace GamePlatform;

/// <summary>
/// 폴더/압축 파일 안에 실행 파일(exe)이 여러 개 있을 때 사용자가 직접 고르게 하는 대화상자
/// (doc/game-management.md "게임 추가" 참고 — 자동 추측은 설치 프로그램/제거 프로그램 등을 잘못 고를 수 있어
/// 채택하지 않았다).
/// </summary>
public partial class SelectExecutableWindow : Window
{
    public string? SelectedRelativePath { get; private set; }

    public SelectExecutableWindow(IReadOnlyList<string> relativePaths)
    {
        InitializeComponent();
        CandidatesListBox.ItemsSource = relativePaths;
        CandidatesListBox.SelectedIndex = 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Accept();

    private void CandidatesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Accept();

    private void Accept()
    {
        if (CandidatesListBox.SelectedItem is not string path)
        {
            return;
        }

        SelectedRelativePath = path;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>후보가 하나면 바로 그것을, 없으면 null을 반환한다. 여러 개일 때만 실제로 대화상자를 띄운다.</summary>
    public static string? PickFrom(Window owner, IReadOnlyList<string> relativePaths)
    {
        if (relativePaths.Count == 0)
        {
            return null;
        }

        if (relativePaths.Count == 1)
        {
            return relativePaths[0];
        }

        var window = new SelectExecutableWindow(relativePaths) { Owner = owner };
        return window.ShowDialog() == true ? window.SelectedRelativePath : null;
    }
}
