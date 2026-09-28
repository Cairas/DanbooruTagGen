using System.Text;
using DanbooruTagGen.Core.Models;

namespace DanbooruTagGen.Core.Tags;

/// <summary>danbooru.csv(a1111 tagcomplete 형식) 한 줄을 Tag로 변환.</summary>
public static class CsvTagParser
{
    public static bool TryParseLine(string line, out Tag tag)
    {
        tag = null!;
        if (string.IsNullOrWhiteSpace(line)) return false;

        var fields = SplitCsvFields(line);
        if (fields.Count < 3) return false;

        var name = fields[0];
        if (name.Length == 0) return false;
        if (!int.TryParse(fields[1], out var categoryCode)) return false;
        int.TryParse(fields[2], out var postCount); // 비숫자 빈도(품질태그 'Quality tag' 등)면 0

        var category = Enum.IsDefined(typeof(TagCategory), categoryCode)
            ? (TagCategory)categoryCode
            : TagCategory.Unknown;

        var aliases = fields.Count >= 4 && fields[3].Length > 0
            ? fields[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();

        // 5번째 칸(선택): 한국어 설명. 표준 danbooru csv엔 없고 큐레이션 파일에만 있음.
        var description = fields.Count >= 5 ? fields[4].Trim() : "";

        tag = new Tag(name, category, postCount, aliases, description);
        return true;
    }

    /// <summary>RFC4180 식 한 줄 분리. 따옴표 안의 콤마는 보존, "" 는 리터럴 따옴표.</summary>
    public static IReadOnlyList<string> SplitCsvFields(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
