using Scriblism.Core;

namespace Scriblism.Core.Tests;

public sealed class PresentationTests
{
    [Theory]
    [InlineData("test.cs", "csharp")]
    [InlineData("test.TSX", "typescript")]
    [InlineData("Dockerfile.dev", "dockerfile")]
    [InlineData("Makefile", "make")]
    [InlineData("test.xaml", "xml")]
    [InlineData("test.md", "markdown")]
    [InlineData(".gitignore", "shell")]
    [InlineData("unknown.blob", "text")]
    public void LanguageDetection(string name, string expected) => Assert.Equal(expected, Languages.Detect(name).Id);

    [Fact] public void KeywordsInsideStringsAreNotRecolored()
    {
        const string text = "public string x = \"class return\"; // public";
        var tokens = SyntaxHighlighter.Highlight(text, Languages.ById("csharp"));
        Assert.Contains(tokens, t => t.Kind == TokenKind.Keyword && text.Substring(t.Start, t.Length) == "public");
        var quoted = Assert.Single(tokens, t => t.Kind == TokenKind.String);
        Assert.Equal("\"class return\"", text.Substring(quoted.Start, quoted.Length));
        Assert.DoesNotContain(tokens, t => t.Kind == TokenKind.Keyword && t.Start > quoted.Start);
    }
    [Fact] public void EveryLanguageProducesBoundedSpans()
    {
        const string text = "/* comment */\n# comment\nclass Hello { return \"中文😀\"; }\n<!-- xml -->\n<root key=\"value\" />\n123\n";
        foreach (var language in Languages.All)
            foreach (var token in SyntaxHighlighter.Highlight(text, language))
            { Assert.InRange(token.Start, 0, text.Length - 1); Assert.InRange(token.Length, 1, text.Length - token.Start); }
    }
    [Fact] public void SqlCommentAndCaseInsensitiveKeywords()
    {
        var tokens = SyntaxHighlighter.Highlight("SELECT * FROM users -- note", Languages.ById("sql"));
        Assert.Equal(2, tokens.Count(t => t.Kind == TokenKind.Keyword)); Assert.Single(tokens, t => t.Kind == TokenKind.Comment);
    }
    [Fact] public void LargeDocumentsSkipSyntaxWork()
    { Assert.Empty(SyntaxHighlighter.Highlight(new string('x', SyntaxHighlighter.MaximumHighlightLength + 1), Languages.ById("csharp"))); }
    [Fact] public void MarkdownProvidesOutlineAndNestedFormatting()
    {
        const string text = "# 標題\n\n## Next\n\n**bold *nested*** and `code`\n\n```cs\npublic class Example {}\n```\n";
        var result = MarkdownPresentation.Parse(text);
        Assert.Equal(2, result.Outline.Count); Assert.Equal("標題", result.Outline[0].Title);
        Assert.Contains(result.Spans, s => s.Style == MarkdownStyle.Bold);
        Assert.Contains(result.Spans, s => s.Style == MarkdownStyle.Italic);
        Assert.Contains(result.Spans, s => s.Style == MarkdownStyle.Code);
        Assert.Contains(result.CodeTokens, t => t.Kind == TokenKind.Keyword);
        foreach (var span in result.Spans) { Assert.InRange(span.Start, 0, text.Length - 1); Assert.InRange(span.Length, 1, text.Length - span.Start); }
    }
    [Fact] public void ActiveLineMarkersStayEditable()
    {
        const string text = "# Heading\n\n**bold**\n\nlast";
        var markers = MarkdownPresentation.Parse(text).Spans.Where(s => s.Style == MarkdownStyle.Marker).ToArray();
        Assert.NotEmpty(markers);
        foreach (var marker in markers)
        {
            Assert.True(MarkdownPresentation.ShouldHide(marker, text, text.Length, text.Length));
            Assert.False(MarkdownPresentation.ShouldHide(marker, text, marker.Start, marker.Start));
            Assert.False(MarkdownPresentation.ShouldHide(marker, text, 0, text.Length));
        }
    }
    [Fact] public void MarkdownTablesKeepCellsAndAlignment()
    {
        const string text = "| Name | Score |\n| :--- | ---: |\n| **Ada** | 42 |\n| a\\|b | `x` |\n\n```md\n| not | a table |\n| --- | --- |\n```\n";
        var result = MarkdownPresentation.Parse(text);
        var table = Assert.Single(result.Tables);
        Assert.Equal(0, table.Start);
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal("Name", table.Rows[0].Cells[0].Text);
        Assert.Equal("Ada", table.Rows[1].Cells[0].Text);
        Assert.Equal("a|b", table.Rows[2].Cells[0].Text);
        Assert.Equal("x", table.Rows[2].Cells[1].Text);
        Assert.True(table.Rows[0].IsHeader);
        Assert.Equal(Markdig.Extensions.Tables.TableColumnAlign.Left, table.Alignments[0]);
        Assert.Equal(Markdig.Extensions.Tables.TableColumnAlign.Right, table.Alignments[1]);
    }
    [Theory]
    [InlineData("**unfinished")]
    [InlineData("```cs\nclass A {}")]
    [InlineData("[link](broken")]
    [InlineData("![remote](https://example.com/a.png)")]
    [InlineData("<script>alert('x')</script>")]
    [InlineData("")]
    public void IncompleteMarkdownParsesSafely(string text)
    { var result = MarkdownPresentation.Parse(text); Assert.NotNull(result); }
}
