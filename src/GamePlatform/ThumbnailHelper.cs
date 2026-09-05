using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GamePlatform;

/// <summary>
/// 원본 이미지를 지정한 폴더에 원본 그대로, 그리고 320x240 이내로 리사이즈한 썸네일용으로 각각 저장한다.
/// (video-vault의 ThumbnailHelper를 이식 — 동영상 파일과 같은 폴더 대신, 호출자가 지정한 임의의 폴더에 저장하도록
/// 일반화했다. game-platform은 실행 파일이 앱이 쓸 수 없는 위치에 있을 수 있어, 이미지를 앱 데이터 폴더
/// [게임 Id]별 폴더에 저장한다 — doc/common-management.md 참고.)
/// </summary>
public static class ThumbnailHelper
{
    /// <summary>썸네일의 최대 가로/세로 크기. 비율을 유지하며 이 범위 안에 맞추므로 실제 결과 크기는 이보다 작을 수 있다.</summary>
    public const int ThumbnailWidth = 320;
    public const int ThumbnailHeight = 240;

    public readonly record struct Result(string ThumbnailPath, string OriginalPath);

    /// <summary>
    /// <paramref name="sourceImagePath"/> 이미지를
    /// (1) 원본 그대로 <paramref name="destDir"/>에 "{baseName}.original{확장자}"로 복사하고,
    /// (2) 가로세로 비율을 유지한 채 320x240 이내로 리사이즈해 같은 폴더에 "{baseName}.thumbnail.jpg"로 저장한 뒤,
    /// (3) 두 파일로의 복사가 끝난 <paramref name="sourceImagePath"/> 원본은 삭제한다
    /// (드래그 앤 드롭/다운로드로 만들어진 임시 파일이 남지 않도록 하기 위함이기도 하다).
    /// </summary>
    public static Result CreateThumbnail(string sourceImagePath, string destDir, string baseName)
    {
        Directory.CreateDirectory(destDir);

        var sourceExtension = Path.GetExtension(sourceImagePath);
        if (string.IsNullOrEmpty(sourceExtension))
        {
            sourceExtension = ".jpg";
        }

        var sourceFullPath = Path.GetFullPath(sourceImagePath);
        var originalPath = Path.Combine(destDir, $"{baseName}.original{sourceExtension}");
        var isSourceSameAsOriginal = string.Equals(sourceFullPath, Path.GetFullPath(originalPath), StringComparison.OrdinalIgnoreCase);

        if (!isSourceSameAsOriginal)
        {
            File.Copy(sourceImagePath, originalPath, overwrite: true);
        }

        var decoder = BitmapDecoder.Create(new Uri(sourceImagePath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var source = decoder.Frames[0];

        var scale = Math.Min((double)ThumbnailWidth / source.PixelWidth, (double)ThumbnailHeight / source.PixelHeight);
        var resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));

        var thumbnailPath = Path.Combine(destDir, $"{baseName}.thumbnail.jpg");

        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(resized));

        using (var stream = new FileStream(thumbnailPath, FileMode.Create, FileAccess.Write))
        {
            encoder.Save(stream);
        }

        var isSourceSameAsThumbnail = string.Equals(sourceFullPath, Path.GetFullPath(thumbnailPath), StringComparison.OrdinalIgnoreCase);
        if (!isSourceSameAsOriginal && !isSourceSameAsThumbnail)
        {
            TryDeleteSource(sourceImagePath);
        }

        return new Result(thumbnailPath, originalPath);
    }

    /// <summary>
    /// <paramref name="sourceImagePath"/> 이미지를 리사이즈 없이 원본 크기 그대로 <paramref name="destDir"/>에
    /// "{baseName}.original{확장자}"로 복사하고, 복사가 끝난 원본은 삭제한다. 메인 화면 카드 대표 썸네일은
    /// 별도의 리사이즈본을 만들지 않고 이 원본 크기 파일을 그대로 저장해뒀다가 화면에는 스케일해서 보여준다
    /// (doc/game-management.md "대표 썸네일 지정" 참고) — 여러 장을 반복해서 작게 표시해야 하는 게임 요약
    /// 갤러리(<see cref="CreateThumbnail"/>)와 달리, 카드 하나당 한 장뿐이라 리사이즈본을 따로 둘 이유가 없다.
    /// </summary>
    public static string CopyOriginal(string sourceImagePath, string destDir, string baseName)
    {
        Directory.CreateDirectory(destDir);

        var sourceExtension = Path.GetExtension(sourceImagePath);
        if (string.IsNullOrEmpty(sourceExtension))
        {
            sourceExtension = ".jpg";
        }

        var sourceFullPath = Path.GetFullPath(sourceImagePath);
        var destPath = Path.Combine(destDir, $"{baseName}.original{sourceExtension}");
        var isSourceSameAsDest = string.Equals(sourceFullPath, Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase);

        if (!isSourceSameAsDest)
        {
            File.Copy(sourceImagePath, destPath, overwrite: true);
            TryDeleteSource(sourceImagePath);
        }

        return destPath;
    }

    private static void TryDeleteSource(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // 원본/썸네일 저장은 이미 성공했으므로, 소스 삭제 실패(권한 등)는 전체 동작을 실패로 취급하지 않는다.
        }
    }
}
