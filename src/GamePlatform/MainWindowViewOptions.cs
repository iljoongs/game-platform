namespace GamePlatform;

/// <summary>메인 화면 게임 목록의 정렬 순서 — `GameItem.DisplayName` 기준(doc/game-management.md
/// "메인 화면" 참고).</summary>
public enum GameSortOrder
{
    Ascending,
    Descending,
}

/// <summary>메인 화면 게임 목록의 표시 방식. Icon은 기존 썸네일 카드 그리드, List는 한 줄짜리 목록이다
/// (doc/game-management.md "메인 화면" 참고).</summary>
public enum GameViewMode
{
    Icon,
    List,
}
