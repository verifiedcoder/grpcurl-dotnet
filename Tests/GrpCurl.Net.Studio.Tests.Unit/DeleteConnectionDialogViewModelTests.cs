using GrpCurl.Net.Studio.ViewModels.Connections;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>FR-126: the delete-connection dialog returns true (purge), false (keep), or null (cancel).</summary>
public sealed class DeleteConnectionDialogViewModelTests
{
    [Fact]
    public void It_offers_the_history_option_only_when_entries_exist()
    {
        new DeleteConnectionDialogViewModel("prod", 0).HasHistory.ShouldBeFalse();

        var withHistory = new DeleteConnectionDialogViewModel("prod", 3);
        withHistory.HasHistory.ShouldBeTrue();
        withHistory.HistoryOptionText.ShouldContain("3 history entries");
    }

    [Fact]
    public void Delete_closes_with_the_purge_choice()
    {
        var vm = new DeleteConnectionDialogViewModel("prod", 2) { PurgeHistory = true };
        bool? result = null;
        var closed = false;
        vm.CloseRequested += r => { closed = true; result = r; };

        vm.DeleteCommand.Execute(null);

        closed.ShouldBeTrue();
        result.ShouldBe(true);
    }

    [Fact]
    public void Cancel_closes_with_null()
    {
        var vm = new DeleteConnectionDialogViewModel("prod", 2);
        bool? result = true;
        vm.CloseRequested += r => result = r;

        vm.CancelCommand.Execute(null);

        result.ShouldBeNull();
    }
}
