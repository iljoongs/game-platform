using System.IO;

namespace GamePlatform;

/// <summary>게임 이름/버전처럼 사용자가 자유롭게 입력한 문자열을 파일/폴더 이름으로 안전하게 바꾼다.</summary>
public static class FileNameHelper
{
    public static string Sanitize(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrEmpty(sanitized) ? "game" : sanitized;
    }
}
