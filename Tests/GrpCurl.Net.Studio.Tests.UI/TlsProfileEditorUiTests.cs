using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.ViewModels.Connections;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     L3 headless render tests for the TLS profile editor (E2.2 PR-C): the conditional sections
///     (custom CA, PKCS12 password) realize with the validation mode / detected format, and every
///     interactive control carries an accessible name (SPEC-020 §6).
/// </summary>
public sealed class TlsProfileEditorUiTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    private static Window Render(TlsProfileEditorViewModel vm)
    {
        var window = new Window
        {
            Content = new Views.Connections.TlsProfileEditorView { DataContext = vm },
            DataContext = vm
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Control? ByName(Visual root, string name) =>
        root.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => Equals(c.GetValue(AutomationProperties.NameProperty), name));

    [Fact]
    public Task System_roots_hides_custom_ca_and_pkcs12_sections() => RunOnUiThread(() =>
    {
        var vm = new TlsProfileEditorViewModel(new FakeFilePickerService(), new FakeDialogService(), new FakeSecretStore());
        var window = Render(vm);

        ByName(window, "CA certificate path")!.IsEffectivelyVisible.ShouldBeFalse();
        ByName(window, "PKCS12 password")!.IsEffectivelyVisible.ShouldBeFalse();
    });

    [Fact]
    public Task Custom_ca_reveals_the_ca_and_revocation_fields() => RunOnUiThread(() =>
    {
        var vm = new TlsProfileEditorViewModel(new FakeFilePickerService(), new FakeDialogService(), new FakeSecretStore())
        {
            SelectedValidationMode = TlsValidationMode.CustomCa
        };
        var window = Render(vm);

        ByName(window, "CA certificate path")!.IsEffectivelyVisible.ShouldBeTrue();
        ByName(window, "Revocation mode")!.IsEffectivelyVisible.ShouldBeTrue();
    });

    [Fact]
    public Task Detected_pkcs12_reveals_the_password_field() => RunOnUiThread(() =>
    {
        var vm = new TlsProfileEditorViewModel(new FakeFilePickerService(), new FakeDialogService(), new FakeSecretStore())
        {
            ClientCertPath = "/some/bundle.p12",
            DetectedClientCertFormat = "PKCS12"
        };
        var window = Render(vm);

        ByName(window, "PKCS12 password")!.IsEffectivelyVisible.ShouldBeTrue();
        ByName(window, "Client key path")!.IsEffectivelyVisible.ShouldBeFalse(); // PKCS12 carries its own key
    });

    [Fact]
    public Task Every_interactive_control_has_an_accessible_name() => RunOnUiThread(() =>
    {
        var vm = new TlsProfileEditorViewModel(new FakeFilePickerService(), new FakeDialogService(), new FakeSecretStore())
        {
            SelectedValidationMode = TlsValidationMode.CustomCa,
            ClientCertPath = "/some/bundle.p12",
            DetectedClientCertFormat = "PKCS12"
        };
        var window = Render(vm);

        var unnamed = window.GetVisualDescendants()
            .OfType<Control>()
            .Where(IsInteractive)
            // Templated sub-parts of a named control (e.g. a ComboBox's internal TextBox) carry the
            // parent's name, not their own — only user-placed controls are required to be named.
            .Where(c => c.TemplatedParent is null)
            .Where(c => string.IsNullOrWhiteSpace(ControlAutomationPeer.CreatePeerForElement(c)?.GetName()))
            .Select(c => c.GetType().Name)
            .ToList();

        unnamed.ShouldBeEmpty("unnamed: " + string.Join(", ", unnamed));
    });

    private static bool IsInteractive(Control control) => control switch
    {
        Button or ToggleButton or CheckBox or RadioButton or TextBox or ComboBox => true,
        _ => false
    };
}
