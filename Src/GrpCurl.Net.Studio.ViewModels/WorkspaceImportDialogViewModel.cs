using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels;

public sealed partial class WorkspaceImportTlsItemViewModel(WorkspaceTlsSecurityEntry entry) : ViewModelBase
{
    public WorkspaceTlsSecurityEntry Entry { get; } = entry;
    public string BypassLabel => $"Disable certificate verification for '{Entry.DestinationName}'";
    public bool HasCustomCa => !string.IsNullOrWhiteSpace(Entry.CustomCaPath);
    public string Outcome => DisableCertificateVerification ? "Certificate verification will be disabled." : "Certificate verification will be enabled.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Outcome))]
    public partial bool DisableCertificateVerification { get; set; }
}

/// <summary>Import-only consent; every bypass checkbox starts unchecked on every import.</summary>
public sealed partial class WorkspaceImportDialogViewModel : DialogViewModel<WorkspaceImportDecision?>
{
    private readonly WorkspacePreparation _preparation;
    private readonly WorkspaceMergeSummary _summary;

    public WorkspaceImportDialogViewModel(WorkspacePreparation preparation, WorkspaceModel current)
    {
        preparation.ValidateImportSource();
        _preparation = preparation;
        (_, _summary) = WorkspaceMerger.Merge(current, preparation.Workspace);
        TlsProfiles = _summary.TlsSecurity.Select(e => new WorkspaceImportTlsItemViewModel(e)).ToArray();
    }

    public override string Title => $"Import {_summary.TotalAdded} item(s) from '{Path.GetFileName(_preparation.Path)}'?";
    public string SummaryText => _summary.Describe(includeSecurity: false);
    public bool HasTlsSecurity => TlsProfiles.Count > 0;
    public IReadOnlyList<WorkspaceImportTlsItemViewModel> TlsProfiles { get; }
    public WorkspaceMergeSummary Summary => _summary with
    {
        TlsSecurity = TlsProfiles.Select(i => i.Entry with { SelectedBypass = i.DisableCertificateVerification }).ToArray()
    };

    public WorkspaceImportDecision CreateDecision() => new(_preparation,
        TlsProfiles.Where(i => i.DisableCertificateVerification).Select(i => i.Entry.SourceProfileId).ToArray());

    [RelayCommand]
    private void Import() => Close(CreateDecision());
    [RelayCommand]
    private void Cancel() => Close(null);
}
