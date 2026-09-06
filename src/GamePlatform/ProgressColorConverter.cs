using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace GamePlatform;

/// <summary>0~100 값을 sample.png 참고 디자인처럼 구간별로 다른 색(청록 → 하늘색 → 보라 → 분홍 → 주황)으로
/// 바꾼다 — 평점/신사 등급 슬라이더의 트랙 배경에 쓴다(doc/game-management.md "정보 창" 참고). 각 구간은
/// 살짝 어두워지는 2색 그라데이션(원본 이미지와 같은 느낌)이다.</summary>
public class ProgressColorConverter : IValueConverter
{
    private static readonly (double UpperBound, Color Start, Color End)[] Bands =
    {
        (20, Color.FromRgb(0x17, 0x8F, 0x82), Color.FromRgb(0x2E, 0x6F, 0x68)), // 청록
        (40, Color.FromRgb(0x22, 0xAE, 0xDB), Color.FromRgb(0x2E, 0x86, 0xAE)), // 하늘색
        (60, Color.FromRgb(0x5B, 0x3F, 0xA6), Color.FromRgb(0x6D, 0x5A, 0xA0)), // 보라
        (80, Color.FromRgb(0xD6, 0x29, 0x5F), Color.FromRgb(0xB8, 0x45, 0x70)), // 분홍
        (100, Color.FromRgb(0xF2, 0x87, 0x2E), Color.FromRgb(0xE0, 0x95, 0x4B)), // 주황
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var amount = value is double d ? d : 0;
        var band = Array.Find(Bands, b => amount <= b.UpperBound);
        if (band == default)
        {
            band = Bands[^1];
        }

        return new LinearGradientBrush(band.Start, band.End, new Point(0, 0), new Point(1, 0));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
