using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GamePlatform;

/// <summary>
/// 문자열(이미지 경로)이 비어 있으면 <see cref="Visibility.Visible"/>, 값이 있으면 <see cref="Visibility.Collapsed"/>.
/// 게임 요약 갤러리의 대표 썸네일 슬롯이 비어 있을 때 "썸네일 없음" placeholder를 보여주는 데 사용한다.
/// </summary>
public class EmptyPathToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
