using System.Collections.ObjectModel;

namespace DanbooruTagGen.Core.Models;

/// <summary>공용 풀 라이브러리의 1급 객체. 여러 레시피가 Id로 참조한다.</summary>
public sealed class Pool
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    // ObservableCollection: 풀 편집 시 UI가 즉시 반영하도록.
    public ObservableCollection<string> Candidates { get; set; } = new();
}
