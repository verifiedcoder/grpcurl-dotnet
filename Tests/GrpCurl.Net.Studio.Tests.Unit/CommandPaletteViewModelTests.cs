using GrpCurl.Net.Studio.ViewModels;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>L1 tests for the command palette (Ctrl+K): fuzzy filtering, keyboard navigation, and accept/cancel.</summary>
public sealed class CommandPaletteViewModelTests
{
    private static PaletteItem Item(string title, string category = "Command")
        => new(title, category, () => Task.CompletedTask);

    private static CommandPaletteViewModel Palette(params PaletteItem[] items) => new(items);

    [Fact]
    public void All_items_show_and_the_first_is_selected_with_an_empty_query()
    {
        var palette = Palette(Item("New workspace"), Item("Open Settings"));

        palette.Items.Count.ShouldBe(2);
        palette.SelectedItem.ShouldBe(palette.Items[0]);
        palette.HasNoMatches.ShouldBeFalse();
    }

    [Fact]
    public void Query_fuzzy_filters_by_subsequence()
    {
        var palette = Palette(Item("New workspace"), Item("Open Settings"), Item("Export workspace…"));

        palette.Query = "nw"; // subsequence of "New Workspace"

        palette.Items.ShouldHaveSingleItem().Title.ShouldBe("New workspace");
    }

    [Fact]
    public void Query_matches_the_category_too()
    {
        var palette = Palette(Item("alpha", "Connection"), Item("New workspace", "Command"));

        palette.Query = "connection";

        palette.Items.ShouldHaveSingleItem().Title.ShouldBe("alpha");
    }

    [Fact]
    public void A_query_with_no_matches_reports_empty()
    {
        var palette = Palette(Item("New workspace"));

        palette.Query = "zzzzz";

        palette.Items.ShouldBeEmpty();
        palette.HasNoMatches.ShouldBeTrue();
        palette.SelectedItem.ShouldBeNull();
    }

    [Fact]
    public void Move_down_and_up_change_the_selection_and_clamp()
    {
        var palette = Palette(Item("one"), Item("two"), Item("three"));

        palette.MoveDownCommand.Execute(null);
        palette.SelectedItem!.Title.ShouldBe("two");
        palette.MoveDownCommand.Execute(null);
        palette.MoveDownCommand.Execute(null); // clamps at the last
        palette.SelectedItem!.Title.ShouldBe("three");

        palette.MoveUpCommand.Execute(null);
        palette.SelectedItem!.Title.ShouldBe("two");
    }

    [Fact]
    public void Accept_closes_with_the_selected_item()
    {
        var palette = Palette(Item("one"), Item("two"));
        PaletteItem? result = null;
        var closed = false;
        palette.CloseRequested += r => { result = r; closed = true; };
        palette.MoveDownCommand.Execute(null);

        palette.AcceptCommand.Execute(null);

        closed.ShouldBeTrue();
        result!.Title.ShouldBe("two");
    }

    [Fact]
    public void Cancel_closes_with_null()
    {
        var palette = Palette(Item("one"));
        PaletteItem? result = Item("sentinel");
        palette.CloseRequested += r => result = r;

        palette.CancelCommand.Execute(null);

        result.ShouldBeNull();
    }
}
