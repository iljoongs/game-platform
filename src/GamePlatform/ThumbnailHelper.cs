using System.IO;

namespace GamePlatform;

/// <summary>
/// 이미지를 지정한 폴더에 원본 크기 그대로 저장한다. 리사이즈본은 따로 만들지 않고, 화면에는 항상
/// WPF의 <c>Stretch="Uniform"</c> + 고정 Width/Height로 스케일해서 보여준다(doc/game-management.md
/// "대표 썸네일 지정"/"게임 요약" 참고) — 메인 카드 대표 썸네일과 게임 요약 스크린샷 모두 이 방식을 쓴다.
/// (video-vault의 ThumbnailHelper를 이식하며 리사이즈 로직은 제거하고 복사만 하도록 단순화했다.)
/// </summary>
public static class ThumbnailHelper
{
    /// <summary>
    /// <paramref name="sourceImagePath"/> 이미지를 리사이즈 없이 원본 크기 그대로 <paramref name="destDir"/>에
    /// "{baseName}.original{확장자}"로 복사한다.
    /// </summary>
    /// <param name="deleteSource">true면 복사가 끝난 <paramref name="sourceImagePath"/>를 삭제한다 — 드래그
    /// 앤 드롭/다운로드로 만들어진 임시 파일 정리용으로만 true를 넘겨야 한다. 사용자의 로컬 파일을 그대로
    /// 드래그한 경우(<see cref="DragDropImageHelper.TryGetImagePath"/>의 <c>isTemporary</c>가 false)는
    /// 반드시 false로 호출해 원본을 건드리지 않는다.</param>
    public static string CopyOriginal(string sourceImagePath, string destDir, string baseName, bool deleteSource)
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
            if (deleteSource)
            {
                TryDeleteSource(sourceImagePath);
            }
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
            // 복사는 이미 성공했으므로, 소스 삭제 실패(권한 등)는 전체 동작을 실패로 취급하지 않는다.
        }
    }
}
