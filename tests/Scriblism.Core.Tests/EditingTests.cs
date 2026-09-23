using Scriblism.Core;

namespace Scriblism.Core.Tests;

public sealed class EditingTests
{
    [Fact] public void FormattingEquivalentUpdateDoesNotCreateHistory()
    { var buffer = new DocumentBuffer("same"); Assert.False(buffer.Update("same", 2)); Assert.False(buffer.IsDirty); Assert.False(buffer.CanUndo); }
    [Fact] public void TypingGroupsAndRedoWorks()
    {
        var buffer = new DocumentBuffer(); buffer.Update("a", 1); buffer.Update("ab", 2); buffer.Update("abc", 3);
        Assert.Equal("", buffer.Undo().Text); Assert.Equal("abc", buffer.Redo().Text);
    }
    [Fact] public void SaveBreaksUndoGroup()
    {
        var buffer = new DocumentBuffer(); buffer.Update("a", 1); buffer.MarkSaved("a"); buffer.Update("ab", 2);
        Assert.Equal("a", buffer.Undo().Text); Assert.False(buffer.IsDirty);
    }
    [Fact] public void SaveSnapshotDoesNotClearLaterEdits()
    {
        var buffer = new DocumentBuffer(); buffer.Update("before IO", 9, false); var saving = buffer.Text;
        buffer.Update("after IO", 8, false); buffer.MarkSaved(saving); Assert.True(buffer.IsDirty);
    }
    [Fact] public void EditingAfterUndoInvalidatesRedo()
    {
        var buffer = new DocumentBuffer("a"); buffer.Update("b", 1, false); buffer.Undo(); buffer.Update("c", 1, false);
        Assert.False(buffer.CanRedo); Assert.Equal("c", buffer.Text);
    }
    [Fact] public void UnicodeEditsRoundTrip()
    {
        var buffer = new DocumentBuffer("a😀繁體\nb"); buffer.Update("a😃中文\nb", 7, false);
        Assert.Equal("a😀繁體\nb", buffer.Undo().Text); Assert.Equal("a😃中文\nb", buffer.Redo().Text);
    }
    [Fact] public void RandomEditsUndoBackToOriginal()
    {
        var random = new Random(173); var buffer = new DocumentBuffer("seed\n中文😀"); var states = new List<string> { buffer.Text };
        for (var i = 0; i < 300; i++)
        {
            var index = random.Next(buffer.Text.Length + 1);
            var next = buffer.Text.Insert(index, i + "\n"); buffer.Update(next, index, false); states.Add(next);
        }
        for (var i = states.Count - 2; i >= 0; i--) Assert.Equal(states[i], buffer.Undo().Text);
        for (var i = 1; i < states.Count; i++) Assert.Equal(states[i], buffer.Redo().Text);
    }
    [Fact] public void LiteralReplacementDoesNotInterpretDollarGroups()
    { Assert.Equal("$1 $1", SearchEngine.ReplaceAll("ab ab", "ab", "$1", new())); }
    [Fact] public void RegexGroupsAndLookbehindWork()
    {
        var options = new SearchOptions(Regex: true); const string text = "pre12 pre34";
        var result = SearchEngine.Find(text, @"(?<=pre)(\d+)", options);
        Assert.Equal(2, result.Matches.Count);
        Assert.Equal("(12)", SearchEngine.ReplacementFor(text, @"(?<=pre)(\d+)", "($1)", options, result.Matches[0]));
        Assert.Equal("pre(12) pre(34)", SearchEngine.ReplaceAll(text, @"(?<=pre)(\d+)", "($1)", options));
    }
    [Fact] public void WholeWordAndCaseOptionsWork()
    {
        Assert.Equal(2, SearchEngine.Find("cat Cat scatter cat_", "cat", new(WholeWord: true)).Matches.Count);
        Assert.Single(SearchEngine.Find("cat Cat scatter cat_", "cat", new(MatchCase: true, WholeWord: true)).Matches);
    }
    [Fact] public void EmptyAndInvalidSearchAreSafe()
    {
        Assert.Empty(SearchEngine.Find("abc", "", new()).Matches);
        Assert.NotNull(SearchEngine.Find("abc", "[", new(Regex: true)).Error);
        Assert.Equal("abc", SearchEngine.ReplaceAll("abc", "", "x", new()));
    }
    [Fact] public void ZeroWidthRegexIsBounded()
    { Assert.Equal(3, SearchEngine.Find("a\nb\nc", "^", new(Regex: true)).Matches.Count); }
    [Fact] public void SearchResultCountIsBounded()
    { var result = SearchEngine.Find(new string('a', 20000), "a", new()); Assert.True(result.Truncated); Assert.Equal(10000, result.Matches.Count); }
    [Fact] public void ReplacementExpansionIsBounded()
    {
        Assert.Throws<ArgumentException>(() => SearchEngine.ReplaceAll(new string('a', 20000), "a", new string('x', 1000), new()));
        Assert.Throws<ArgumentException>(() => SearchEngine.ReplaceAll(new string('a', 20000), "(a+)", string.Concat(Enumerable.Repeat("$1", 1000)), new(Regex: true)));
    }
    [Fact] public void CatastrophicRegexHasTimeout()
    { Assert.NotNull(SearchEngine.Find(new string('a', 20000) + "!", "(a+)+$", new(Regex: true)).Error); }
}
