using System.Text.RegularExpressions;

namespace Scriblism.Core;

public sealed record SearchOptions(bool MatchCase = false, bool WholeWord = false, bool Regex = false);
public sealed record SearchMatch(int Start, int Length);
public sealed record SearchResult(IReadOnlyList<SearchMatch> Matches, string? Error, bool Truncated = false);

public static class SearchEngine
{
    private static Regex Build(string query, SearchOptions options)
    {
        var pattern = options.Regex ? query : Regex.Escape(query);
        if (options.WholeWord) pattern = @"(?<![\p{L}\p{N}_])(?:" + pattern + @")(?![\p{L}\p{N}_])";
        return new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Multiline |
            (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase), TimeSpan.FromMilliseconds(200));
    }

    public static SearchResult Find(string text, string query, SearchOptions options)
    {
        if (query.Length == 0) return new([], null);
        try
        {
            var result = new List<SearchMatch>();
            foreach (Match match in Build(query, options).Matches(text))
            {
                if (result.Count >= 10000) return new(result, null, true);
                result.Add(new(match.Index, match.Length));
            }
            return new(result, null);
        }
        catch (ArgumentException error) { return new([], "正規表示式無效：" + error.Message); }
        catch (RegexMatchTimeoutException) { return new([], "搜尋逾時，請簡化正規表示式。"); }
    }

    public static string ReplaceAll(string text, string query, string replacement, SearchOptions options)
    {
        if (query.Length == 0) return text;
        var regex = Build(query, options);
        var output = new System.Text.StringBuilder(Math.Min(text.Length, 65536));
        var previous = 0;
        foreach (Match match in regex.Matches(text))
        {
            Append(text.AsSpan(previous, match.Index - previous));
            if (options.Regex)
            {
                Append(ExpandReplacement(match, replacement, text.Length));
            }
            else Append(replacement);
            previous = match.Index + match.Length;
        }
        Append(text.AsSpan(previous));
        return output.ToString();
        void Append(ReadOnlySpan<char> value)
        {
            if ((long)output.Length + value.Length > TextFileStore.MaximumBytes)
                throw new ArgumentException("取代後的文件超過 16 MiB，未執行。");
            output.Append(value);
        }
    }

    public static string ReplacementFor(string text, string query, string replacement, SearchOptions options, SearchMatch selected)
    {
        if (!options.Regex) return replacement;
        // Match against the whole document so lookbehind, anchors and capture groups keep their meaning.
        var match = Build(query, options).Match(text, selected.Start);
        return match.Success && match.Index == selected.Start && match.Length == selected.Length
            ? ExpandReplacement(match, replacement, text.Length) : replacement;
    }

    private static string ExpandReplacement(Match match, string replacement, int documentLength)
    {
        var maxCapture = replacement.Contains("$`") || replacement.Contains("$'") || replacement.Contains("$_")
            ? documentLength : match.Groups.Cast<Group>().Max(g => g.Length);
        if ((long)replacement.Count(c => c == '$') * maxCapture + replacement.Length > TextFileStore.MaximumBytes)
            throw new ArgumentException("取代內容可能超過 16 MiB，請縮小取代範圍。");
        return match.Result(replacement);
    }
}
