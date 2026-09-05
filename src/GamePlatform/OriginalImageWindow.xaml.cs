using System.IO;
using System.Windows;
using System.Windows.Input;

namespace GamePlatform;

/// <summary>
/// 리사이즈 전 원본 이미지를 크게 보여주는 창. 클릭하면 닫힌다.
/// (video-vault의 OriginalImageWindow를 이식 — ManagedVideoItem 대신 경로 문자열을 직접 받도록 일반화)
/// </summary>
public partial class OriginalImageWindow : Window
{
    public OriginalImageWindow(string originalImagePath)
    {
        InitializeComponent();
        OriginalImage.Source = ImageLoadHelper.Load(originalImagePath);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Close();

    /// <summary>원본 이미지 경로가 있고 실제로 존재하면 원본창을 연다. 없으면 아무 동작도 하지 않는다.</summary>
    public static void ShowFor(Window owner, string? originalImagePath)
    {
        if (string.IsNullOrEmpty(originalImagePath) || !File.Exists(originalImagePath))
        {
            return;
        }

        new OriginalImageWindow(originalImagePath) { Owner = owner }.ShowDialog();
    }
}
