namespace Scriblism.Core;

public enum TokenKind { Keyword, String, Comment, Number, Type, Function, Property, Operator, Added, Removed }
public sealed record Token(int Start, int Length, TokenKind Kind);

/// <summary>A bounded lexical highlighter; not a compiler or language server.</summary>
public static class SyntaxHighlighter
{
    public const int MaximumHighlightLength = 256 * 1024;
    public static IReadOnlyList<Token> Highlight(string text, Language language, CancellationToken cancellation = default)
    {
        var tokens = new List<Token>();
        if (language.Id is "text" or "markdown" || text.Length > MaximumHighlightLength) return tokens;
        var words = language.Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(
            language.Id is "sql" or "powershell" or "batch" ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        bool markup = language.Id is "html" or "xml";
        var i = 0;
        while (i < text.Length && tokens.Count < 30000)
        {
            if ((i & 1023) == 0) cancellation.ThrowIfCancellationRequested();
            var start = i; var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (language.Id == "diff")
            {
                var end = text.IndexOf('\n', i); if (end < 0) end = text.Length;
                if (c is '+' or '-' or '@') tokens.Add(new(i, end - i, c == '+' ? TokenKind.Added : c == '-' ? TokenKind.Removed : TokenKind.Keyword));
                i = end; continue;
            }
            string? close = null;
            if (markup && Starts("<!--")) close = "-->";
            else if (language.BlockComment && Starts("/*")) close = "*/";
            else if (language.Id == "powershell" && Starts("<#")) close = "#>";
            else if (language.Id == "lua" && Starts("--[[")) close = "]]";
            else if (language.Id == "fsharp" && Starts("(*")) close = "*)";
            if (close is not null)
            {
                var end = text.IndexOf(close, i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + close.Length;
                tokens.Add(new(start, i - start, TokenKind.Comment)); continue;
            }
            if (language.LineComment.Length > 0 && Starts(language.LineComment))
            {
                var end = text.IndexOf('\n', i); i = end < 0 ? text.Length : end;
                tokens.Add(new(start, i - start, TokenKind.Comment)); continue;
            }
            if (c is '"' or '\'' or '`')
            {
                var triple = i + 2 < text.Length && text[i + 1] == c && text[i + 2] == c && language.Id is "python" or "kotlin" or "scala" or "swift";
                var verbatim = language.Id == "csharp" && i > 0 && text[i - 1] == '@';
                i += triple ? 3 : 1;
                while (i < text.Length)
                {
                    if (!verbatim && text[i] == '\\') { i = Math.Min(i + 2, text.Length); continue; }
                    if (triple && i + 2 < text.Length && text[i] == c && text[i + 1] == c && text[i + 2] == c) { i += 3; break; }
                    if (!triple && text[i] == c)
                    {
                        i++;
                        if ((verbatim || language.Id == "sql") && i < text.Length && text[i] == c) { i++; continue; }
                        break;
                    }
                    if (!triple && c != '`' && !verbatim && text[i] == '\n') break;
                    i++;
                }
                var next = i; while (next < text.Length && text[next] is ' ' or '\t') next++;
                tokens.Add(new(start, i - start, language.Id == "json" && next < text.Length && text[next] == ':' ? TokenKind.Property : TokenKind.String));
                continue;
            }
            if (char.IsDigit(c) && (i == 0 || !char.IsLetterOrDigit(text[i - 1])))
            {
                i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '.' or '_')) i++;
                tokens.Add(new(start, i - start, TokenKind.Number)); continue;
            }
            if (char.IsLetter(c) || c is '_' or '$' || (markup && c == '<'))
            {
                i++;
                if (markup && c == '<' && i < text.Length && text[i] == '/') i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '$' || markup && text[i] is '-' or ':')) i++;
                var word = text[start..i]; var next = i;
                while (next < text.Length && text[next] is ' ' or '\t') next++;
                TokenKind? kind = words.Contains(word) ? TokenKind.Keyword :
                    markup && c == '<' ? TokenKind.Keyword :
                    next < text.Length && text[next] == '(' ? TokenKind.Function :
                    next < text.Length && text[next] is ':' or '=' && language.Id is "yaml" or "toml" or "css" or "xml" or "html" ? TokenKind.Property :
                    char.IsUpper(c) && language.Id is not ("sql" or "json") ? TokenKind.Type :
                    c == '$' ? TokenKind.Property : null;
                if (kind.HasValue) tokens.Add(new(start, i - start, kind.Value));
                continue;
            }
            i++;
            if ("{}[]()=+-*/<>!&|?:;,".Contains(c)) tokens.Add(new(start, 1, TokenKind.Operator));
        }
        return tokens;
        bool Starts(string value) => text.AsSpan(i).StartsWith(value, StringComparison.Ordinal);
    }
}
