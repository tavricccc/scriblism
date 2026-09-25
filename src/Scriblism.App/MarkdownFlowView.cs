using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Scriblism.Core;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace Scriblism.App;

// A single native document flow. Tables and prose share the same ScrollViewer, so a table
// cannot drift away from its paragraph when the document scrolls.
internal sealed class MarkdownFlowView
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    private readonly Action<int> _editAt;
    private readonly StackPanel _blocks = new() { Spacing = 0, Margin = new Thickness(18, 8, 18, 20) };
    private readonly List<(int Start, FrameworkElement Element)> _positions = [];
    public ScrollViewer Scroller { get; }

    public MarkdownFlowView(Action<int> editAt)
    {
        _editAt = editAt;
        Scroller = new ScrollViewer
        {
            Content = _blocks, HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsTabStop = true, Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetName(Scroller, "Markdown 文件預覽；點選區塊編輯原文");
    }

    public void Render(string source, double fontSize, string fontFamily)
    {
        _blocks.Children.Clear(); _positions.Clear();
        var document = Markdown.Parse(source, Pipeline);
        foreach (var block in document) AddBlock(block, source, _blocks, fontSize, fontFamily);
        if (_blocks.Children.Count == 0)
        {
            var empty = new TextBlock { Text = "點擊以開始編輯 Markdown", FontSize = fontSize, Opacity = .6, Margin = new Thickness(0, 8, 0, 0) };
            empty.Tapped += (_, args) => { args.Handled = true; _editAt(0); };
            _blocks.Children.Add(empty);
        }
    }

    public void ScrollTo(int offset)
    {
        var target = _positions.LastOrDefault(item => item.Start <= offset).Element;
        if (target is not null) target.StartBringIntoView();
    }

    private void AddBlock(MdBlock block, string source, Panel parent, double size, string font)
    {
        FrameworkElement view;
        switch (block)
        {
            case Table table:
                view = RenderTable(table, source, size, font);
                break;
            case HeadingBlock heading:
                var headingText = Text(heading.Inline, size * (heading.Level == 1 ? 1.9 : heading.Level == 2 ? 1.5 : 1.18), font);
                headingText.FontWeight = FontWeights.SemiBold;
                headingText.Margin = new Thickness(0, heading.Level <= 2 ? 20 : 14, 0, 9);
                view = headingText;
                break;
            case ParagraphBlock paragraph:
                var paragraphText = Text(paragraph.Inline, size, font);
                paragraphText.Margin = new Thickness(0, 0, 0, 12);
                view = paragraphText;
                break;
            case QuoteBlock quote:
                var quoted = new StackPanel { Spacing = 0 };
                foreach (var child in quote) AddBlock(child, source, quoted, size, font);
                view = new Border
                {
                    Child = quoted, BorderBrush = Resource("AccentFillColorDefaultBrush"),
                    BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(14, 4, 0, 0),
                    Margin = new Thickness(0, 0, 0, 12)
                };
                break;
            case ListBlock list:
                var items = new StackPanel { Spacing = 6, Margin = new Thickness(4, 0, 0, 14) };
                var number = 1;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.Children.Add(new TextBlock { Text = list.IsOrdered ? $"{number++}." : "•", FontSize = size, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 0) });
                    var content = new StackPanel();
                    foreach (var child in item) AddBlock(child, source, content, size, font);
                    Grid.SetColumn(content, 1); row.Children.Add(content); items.Children.Add(row);
                }
                view = items;
                break;
            case FencedCodeBlock or CodeBlock:
                var raw = Slice(source, block);
                if (block is FencedCodeBlock)
                {
                    var first = raw.IndexOf('\n');
                    raw = first < 0 ? "" : raw[(first + 1)..];
                    var last = raw.LastIndexOf('\n');
                    if (last >= 0 && raw[(last + 1)..].TrimStart().StartsWith("```", StringComparison.Ordinal)) raw = raw[..last];
                }
                view = new Border
                {
                    Background = Resource("SubtleFillColorSecondaryBrush"), Padding = new Thickness(12, 9, 12, 9),
                    CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 0, 14),
                    Child = new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = new TextBlock { Text = raw.TrimEnd(), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = size * .94, TextWrapping = TextWrapping.NoWrap }
                    }
                };
                break;
            case ThematicBreakBlock:
                view = new Border { Height = 1, Background = Resource("DividerStrokeColorDefaultBrush"), Margin = new Thickness(0, 12, 0, 20) };
                break;
            default:
                view = new TextBlock { Text = Markdown.ToPlainText(Slice(source, block), Pipeline), FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
                break;
        }
        var start = Math.Max(0, block.Span.Start);
        view.Tapped += (_, args) => { args.Handled = true; _editAt(start); };
        parent.Children.Add(view);
        if (ReferenceEquals(parent, _blocks)) _positions.Add((start, view));
    }

    private static Grid RenderTable(Table table, string source, double size, string font)
    {
        var rows = table.OfType<TableRow>().ToArray();
        var columns = Math.Max(1, rows.Select(row => row.OfType<TableCell>().Sum(cell => Math.Max(1, cell.ColumnSpan))).DefaultIfEmpty(1).Max());
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 18), HorizontalAlignment = HorizontalAlignment.Stretch };
        for (var col = 0; col < columns; col++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < rows.Length; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var col = 0;
            foreach (var cell in rows[row].OfType<TableCell>())
            {
                if (col >= columns) break;
                var cellSource = Slice(source, cell);
                var cellDocument = Markdown.Parse(cellSource, Pipeline);
                var cellParagraph = cellDocument.OfType<ParagraphBlock>().FirstOrDefault();
                var label = cellParagraph is null
                    ? new TextBlock { Text = Markdown.ToPlainText(cellSource, Pipeline).Trim(), FontSize = size, FontFamily = new FontFamily(font), TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.5 }
                    : Text(cellParagraph.Inline, size, font);
                if (rows[row].IsHeader) label.FontWeight = FontWeights.SemiBold;
                label.TextAlignment = col < table.ColumnDefinitions.Count ? table.ColumnDefinitions[col].Alignment switch
                {
                    TableColumnAlign.Center => TextAlignment.Center,
                    TableColumnAlign.Right => TextAlignment.Right,
                    _ => TextAlignment.Left
                } : TextAlignment.Left;
                var border = new Border
                {
                    Child = label, Padding = new Thickness(10, 7, 10, 7),
                    BorderThickness = new Thickness(0, 0, 0, rows[row].IsHeader ? 1.5 : 1),
                    BorderBrush = Resource("DividerStrokeColorDefaultBrush")
                };
                Grid.SetRow(border, row); Grid.SetColumn(border, col);
                Grid.SetColumnSpan(border, Math.Min(Math.Max(1, cell.ColumnSpan), columns - col));
                grid.Children.Add(border); col += Math.Max(1, cell.ColumnSpan);
            }
        }
        return grid;
    }

    private static TextBlock Text(ContainerInline? content, double size, string font)
    {
        var block = new TextBlock { FontSize = size, FontFamily = new FontFamily(font), TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.5 };
        for (var inline = content?.FirstChild; inline is not null; inline = inline.NextSibling) Append(block, inline, false, false, false, false);
        return block;
    }

    private static void Append(TextBlock block, MdInline inline, bool bold, bool italic, bool strike, bool link)
    {
        switch (inline)
        {
            case LiteralInline literal: Run(literal.Content.ToString()); break;
            case CodeInline code:
                block.Inlines.Add(new Run { Text = code.Content, FontFamily = new FontFamily("Cascadia Mono, Consolas") });
                break;
            case LineBreakInline: block.Inlines.Add(new LineBreak()); break;
            case AutolinkInline auto: Run(auto.Url, isLink: true); break;
            case EmphasisInline emphasis:
                Children(emphasis, bold || emphasis.DelimiterCount >= 2 && emphasis.DelimiterChar != '~',
                    italic || emphasis.DelimiterCount == 1 && emphasis.DelimiterChar != '~', strike || emphasis.DelimiterChar == '~', link);
                break;
            case LinkInline linked: Children(linked, bold, italic, strike, true); break;
            case ContainerInline container: Children(container, bold, italic, strike, link); break;
            default: Run(inline.ToString() ?? ""); break;
        }
        void Children(ContainerInline parent, bool b, bool i, bool s, bool l)
        {
            for (var child = parent.FirstChild; child is not null; child = child.NextSibling) Append(block, child, b, i, s, l);
        }
        void Run(string value, bool isLink = false)
        {
            var run = new Run { Text = value };
            if (bold) run.FontWeight = FontWeights.SemiBold;
            if (italic) run.FontStyle = Windows.UI.Text.FontStyle.Italic;
            if (strike) run.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
            if (link || isLink) { run.Foreground = Resource("AccentTextFillColorPrimaryBrush"); run.TextDecorations = Windows.UI.Text.TextDecorations.Underline; }
            block.Inlines.Add(run);
        }
    }

    private static string Slice(string source, MdBlock block)
    {
        var start = Math.Clamp(block.Span.Start, 0, source.Length);
        var end = Math.Clamp(block.Span.End + 1, start, source.Length);
        return source[start..end];
    }

    private static Brush Resource(string key) => (Brush)Application.Current.Resources[key];
}
