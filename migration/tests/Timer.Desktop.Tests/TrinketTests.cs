using Timer.Core;
using Timer.Desk;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class TrinketTests
{
    private static readonly string[] Accounts = { "Account one", "Account two", "Internal" };

    [Theory]
    [InlineData("Send invoice #AccountOne", "Send invoice", "Account one")]
    [InlineData("#accountone call back", "call back", "Account one")]
    [InlineData("Plan #Int", "Plan", "Internal")]
    [InlineData("Wrap up #Account", "Wrap up #Account", null)]
    [InlineData("Nothing to link #nobody", "Nothing to link #nobody", null)]
    [InlineData("Plain item", "Plain item", null)]
    public void ATagLinksTheAccountWhenItNamesExactlyOne(string typed, string text, string? account)
    {
        var item = Trinkets.ParseTodo(typed, Accounts);
        Assert.Equal(text, item.Text); Assert.Equal(account, item.Account); Assert.False(item.Done);
    }

    [Fact]
    public void ATagAloneKeepsTheAccountNameAsTheWords() => Assert.Equal("Internal", Trinkets.ParseTodo("#Internal", Accounts).Text);

    [Fact]
    public void TheBreakIsNeverALinkTarget() => Assert.Null(Trinkets.ParseTodo("rest #Break", Accounts.Append(BreakAccount.Name)).Account);

    [Fact]
    public void TrinketsSurviveSavingAndLoading()
    {
        var data = new TrinketData(new[] { new TodoItem("a", true, "Internal"), new TodoItem("b") }, new[] { new PostIt("hello", "rose") });
        var back = Trinkets.FromJson(Trinkets.ToJson(data));
        Assert.Equal(data.Todos, back.Todos); Assert.Equal(data.PostIts[0], back.PostIts[0]); Assert.Equal(Trinkets.PostItCount, back.PostIts.Count); // Older saves are filled out to three post-its.
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("null")]
    public void MissingOrDamagedTrinketsGiveAnEmptyDesk(string? json) { var data = Trinkets.FromJson(json); Assert.Empty(data.Todos); Assert.Equal(Trinkets.PostItCount, data.PostIts.Count); }

    [Fact]
    public void AnUnknownPostItColourBecomesLemon() => Assert.Equal("lemon", Trinkets.FromJson("{\"Todos\":[],\"PostIts\":[{\"Text\":\"x\",\"Colour\":\"puce\"}]}").PostIts[0].Colour);

    [Fact]
    public void ARenameMovesLinksAndADeleteClearsThem()
    {
        var data = new TrinketData(new[] { new TodoItem("a", false, "Old"), new TodoItem("b", false, "Gone"), new TodoItem("c", false, "Same") }, Array.Empty<PostIt>());
        var after = Trinkets.Relink(data, new Dictionary<string, string> { ["Old"] = "New" }, new[] { "Gone" });
        Assert.Equal(new string?[] { "New", null, "Same" }, after.Todos.Select(t => t.Account));
        Assert.Equal("b", after.Todos[1].Text);
    }

    [Fact]
    public void TapeIsOnUnlessSaidOtherwiseAndOlderSavesKeepIt()
    {
        Assert.True(Trinkets.FromJson("{\"Todos\":[],\"PostIts\":[{\"Text\":\"x\",\"Colour\":\"rose\"}]}").PostIts[0].Tape); // Saved before tape could be taken off.
        var back = Trinkets.FromJson(Trinkets.ToJson(new TrinketData(Array.Empty<TodoItem>(), new[] { new PostIt("a", "sky", false), new PostIt("b", "mint", true) })));
        Assert.False(back.PostIts[0].Tape); Assert.Equal("sky", back.PostIts[0].Colour); Assert.Equal("mint", back.PostIts[1].Colour);
    }
}
