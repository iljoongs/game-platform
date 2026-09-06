using System.Runtime.InteropServices;

namespace GamePlatform;

/// <summary>문자열 안의 숫자를 자릿수가 아니라 실제 값으로 비교하는 "자연 정렬"(예: "v0.23.8" &lt; "v0.23.10") —
/// 게임 목록 정렬(doc/game-management.md "메인 화면" 참고, 사용자 요청)에 쓴다. 윈도우 탐색기가 파일 이름을
/// 정렬할 때 쓰는 것과 같은 <c>StrCmpLogicalW</c>(shlwapi.dll)를 그대로 사용해 별도로 숫자/문자 파싱 로직을
/// 새로 만들지 않는다.</summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? "", y ?? "");
}
