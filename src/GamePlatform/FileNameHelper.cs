using System.IO;
using System.Text.RegularExpressions;

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

    // 이름 끝에 "-v1.2.3"/" v1.2.3"/"-1.2.3"처럼 하이픈 또는 공백으로 구분된 버전이 붙어 있는지 찾는다.
    // 오탐(예: "Agent17"의 "17")을 피하기 위해 점으로 나뉜 숫자가 2개 이상 있어야 버전으로 인정한다.
    private static readonly Regex VersionSuffixRegex = new(
        @"^(?<name>.+?)[-\s]+(?<version>v?\d+(?:\.\d+){1,4}[a-zA-Z0-9]*)$",
        RegexOptions.Compiled);

    /// <summary>게임 이름 끝에 붙은 버전처럼 보이는 부분을 이름과 버전으로 분리한다
    /// (예: "Game-v1.2.3" → Name="Game", Version="v1.2.3"). 버전 패턴을 찾지 못하면
    /// 이름은 원본 그대로, 버전은 빈 문자열로 돌려준다.</summary>
    public static (string Name, string Version) SplitNameAndVersion(string name)
    {
        var match = VersionSuffixRegex.Match(name.Trim());
        if (!match.Success)
        {
            return (name, string.Empty);
        }

        var baseName = match.Groups["name"].Value.TrimEnd('-', ' ');
        return string.IsNullOrEmpty(baseName) ? (name, string.Empty) : (baseName, match.Groups["version"].Value);
    }
}
