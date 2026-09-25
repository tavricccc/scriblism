using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Scriblism.Core;

public enum MarkdownStyle { Heading, Bold, Italic, Strike, Code, Quote, Link, Marker }
public sealed record MarkdownSpan(int Start, int Length, MarkdownStyle Style, int Level = 0, int OwnerStart = -1, int OwnerEnd = -1);
public sealed record OutlineEntry(string Title, int Level, int Offset);
public sealed record MarkdownTableCell(string Text, int ColumnSpan = 1);
public sealed record MarkdownTableRow(IReadOnlyList<MarkdownTableCell> Cells, bool IsHeader);
public sealed record MarkdownTable(int Start, int End, IReadOnlyList<MarkdownTableRow> Rows, IReadOnlyList<TableColumnAlign?> Alignments);
public sealed record MarkdownLayout(IReadOnlyList<MarkdownSpan> Spans, IReadOnlyList<Token> CodeTokens, IReadOnlyList<OutlineEntry> Outline, IReadOnlyList<MarkdownTable> Tables);

public static class MarkdownPresentation
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static MarkdownLayout Parse(string text, CancellationToken cancellation = default)
    {
        var spans = new List<MarkdownSpan>(); var tokens = new List<Token>(); var outline = new List<OutlineEntry>(); var tables = new List<MarkdownTable>();
        if (text.Length > SyntaxHighlighter.MaximumHighlightLength) return new(spans, tokens, outline, tables);
        var document = Markdown.Parse(text, Pipeline);
        foreach (var node in document.Descendants())
        {
            cancellation.ThrowIfCancellationRequested();
            var start = Math.Clamp(node.Span.Start, 0, text.Length);
            var end = Math.Clamp(node.Span.End + 1, start, text.Length);
            if (start == end) continue;
            switch (node)
            {
                case Table table:
                    var rows = new List<MarkdownTableRow>();
                    foreach (var row in table.OfType<TableRow>())
                    {
                        var cells = row.OfType<TableCell>().Select(cell =>
                        {
                            var cellStart = Math.Clamp(cell.Span.Start, 0, text.Length);
                            var cellEnd = Math.Clamp(cell.Span.End + 1, cellStart, text.Length);
                            var source = text[cellStart..cellEnd];
                            return new MarkdownTableCell(Markdown.ToPlainText(source, Pipeline).Trim(), Math.Max(1, cell.ColumnSpan));
                        }).ToArray();
                        rows.Add(new(cells, row.IsHeader));
                    }
                    if (rows.Count > 0)
                        tables.Add(new(start, end, rows, table.ColumnDefinitions?.Select(column => column.Alignment).ToArray() ?? []));
                    break;
                case HeadingBlock heading:
                    spans.Add(new(start, end - start, MarkdownStyle.Heading, heading.Level));
                    var firstEnd = text.IndexOf('\n', start); if (firstEnd < 0 || firstEnd > end) firstEnd = end;
                    var title = text[start..firstEnd].TrimStart('#', ' ').TrimEnd('#', ' ');
                    outline.Add(new(title, heading.Level, start));
                    if (!heading.IsSetext)
                    {
                        var prefix = start;
                        while (prefix < end && text[prefix] is '#' or ' ' or '\t') prefix++;
                        Marker(start, prefix - start, start, end);
                    }
                    break;
                case EmphasisInline emphasis:
                    var count = emphasis.DelimiterCount;
                    spans.Add(new(start, end - start, emphasis.DelimiterChar == '~' ? MarkdownStyle.Strike : count >= 2 ? MarkdownStyle.Bold : MarkdownStyle.Italic));
                    Marker(start, count, start, end); Marker(end - count, count, start, end);
                    break;
                case CodeInline code:
                    spans.Add(new(start, end - start, MarkdownStyle.Code));
                    Marker(start, code.DelimiterCount, start, end); Marker(end - code.DelimiterCount, code.DelimiterCount, start, end);
                    break;
                case LinkInline link when !link.IsImage:
                    spans.Add(new(start, end - start, MarkdownStyle.Link));
                    if (link.FirstChild is not null && link.LastChild is not null && text[start] == '[')
                    {
                        var labelStart = link.FirstChild.Span.Start;
                        var labelEnd = link.LastChild.Span.End + 1;
                        if (labelStart >= start && labelEnd <= end)
                        {
                            Marker(start, labelStart - start, start, end);
                            Marker(labelEnd, end - labelEnd, start, end);
                        }
                    }
                    break;
                case QuoteBlock:
                    spans.Add(new(start, end - start, MarkdownStyle.Quote));
                    break;
                case FencedCodeBlock fence:
                    spans.Add(new(start, end - start, MarkdownStyle.Code));
                    var bodyStart = text.IndexOf('\n', start);
                    if (bodyStart >= 0 && bodyStart < end)
                    {
                        var bodyEnd = end;
                        if (fence.ClosingFencedCharCount > 0)
                        {
                            var closing = text.LastIndexOf('\n', Math.Max(start, end - 1));
                            if (closing >= bodyStart) bodyEnd = closing;
                        }
                        var languageId = fence.Info?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                        var language = FenceLanguage(languageId);
                        foreach (var token in SyntaxHighlighter.Highlight(text[(bodyStart + 1)..bodyEnd], language, cancellation))
                            tokens.Add(token with { Start = token.Start + bodyStart + 1 });
                    }
                    break;
                case CodeBlock:
                    spans.Add(new(start, end - start, MarkdownStyle.Code));
                    break;
            }
        }
        return new(spans, tokens, outline, tables);
        void Marker(int start, int length, int ownerStart, int ownerEnd)
        {
            if (length > 0 && start >= 0 && start + length <= text.Length)
                spans.Add(new(start, length, MarkdownStyle.Marker, OwnerStart: ownerStart, OwnerEnd: ownerEnd));
        }
    }

    public static bool ShouldHide(MarkdownSpan marker, string text, int selectionStart, int selectionEnd)
    {
        if (selectionStart != selectionEnd) return false;
        var lineStart = selectionStart <= 0 ? 0 : text.LastIndexOf('\n', Math.Min(selectionStart - 1, text.Length - 1)) + 1;
        var lineEnd = text.IndexOf('\n', Math.Min(selectionStart, text.Length)); if (lineEnd < 0) lineEnd = text.Length;
        return marker.OwnerEnd < lineStart || marker.OwnerStart > lineEnd;
    }

    private static Language FenceLanguage(string id) => id.ToLowerInvariant() switch
    {
        "cs" or "c#" => Languages.ById("csharp"), "js" or "jsx" => Languages.ById("javascript"),
        "ts" or "tsx" => Languages.ById("typescript"), "py" => Languages.ById("python"),
        "c" or "c++" => Languages.ById("cpp"), "sh" or "bash" or "zsh" => Languages.ById("shell"),
        "ps1" or "pwsh" => Languages.ById("powershell"), "yml" => Languages.ById("yaml"),
        "rb" => Languages.ById("ruby"), "rs" => Languages.ById("rust"),
        _ => Languages.ById(id.ToLowerInvariant())
    };
}
