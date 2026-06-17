using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     L1 tests for <see cref="TlsProfileEditorViewModel" />: validation-mode mapping, FR-035 client-cert
///     validation, content-based format detection, PKCS12 password persistence (SEC-017), and the
///     insecure-skip-verify confirmation gate (FR-031).
/// </summary>
public sealed class TlsProfileEditorViewModelTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try
            {
                File.Delete(f);
            }
            catch (IOException)
            {
                // best-effort
            }
        }
    }

    private string TempFile(string content, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"grpcn-tls-{Guid.NewGuid():N}.{extension}");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private string TempBinaryFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"grpcn-tls-{Guid.NewGuid():N}.p12");
        File.WriteAllBytes(path, [0x30, 0x82, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06]);
        _tempFiles.Add(path);
        return path;
    }

    private static TlsProfileEditorViewModel Create(
        out FakeFilePickerService picker, out FakeDialogService dialog, out FakeSecretStore secrets, TlsProfile? existing = null)
    {
        picker = new FakeFilePickerService();
        dialog = new FakeDialogService();
        secrets = new FakeSecretStore();
        return new TlsProfileEditorViewModel(picker, dialog, secrets, existing);
    }

    [Fact]
    public void New_profile_defaults_to_system_roots_and_is_invalid_without_a_name()
    {
        var vm = Create(out _, out _, out _);

        vm.SelectedValidationMode.ShouldBe(TlsValidationMode.SystemRoots);
        vm.IsCustomCa.ShouldBeFalse();
        vm.SaveCommand.CanExecute(null).ShouldBeFalse();

        vm.Name = "prod";
        vm.SaveCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void Existing_insecure_profile_maps_to_skip_verification()
    {
        var vm = Create(out _, out _, out _, new TlsProfile { Name = "x", InsecureSkipVerify = true });

        vm.SelectedValidationMode.ShouldBe(TlsValidationMode.SkipVerification);
        vm.IsSkipVerification.ShouldBeTrue();
    }

    [Fact]
    public void Existing_custom_ca_profile_maps_to_custom_ca()
    {
        var vm = Create(out _, out _, out _, new TlsProfile { Name = "x", CaCertPath = "/ca.pem", RevocationMode = "nocheck" });

        vm.SelectedValidationMode.ShouldBe(TlsValidationMode.CustomCa);
        vm.IsCustomCa.ShouldBeTrue();
        vm.SelectedRevocationMode.ShouldBe("nocheck");
    }

    [Fact]
    public void Custom_ca_without_a_file_is_invalid()
    {
        var vm = Create(out _, out _, out _);
        vm.Name = "p";
        vm.SelectedValidationMode = TlsValidationMode.CustomCa;

        vm.CaCertError.ShouldNotBeNull();
        vm.SaveCommand.CanExecute(null).ShouldBeFalse();

        vm.CaCertPath = "/path/to/ca.pem";
        vm.CaCertError.ShouldBeNull();
        vm.SaveCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void Pem_cert_without_a_key_is_invalid_FR035()
    {
        var pem = TempFile("-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----", "pem");
        var vm = Create(out _, out _, out _);
        vm.Name = "p";
        vm.ClientCertPath = pem;
        vm.DetectedClientCertFormat = "PEM"; // as BrowseClientCert would set

        vm.ClientCertError.ShouldNotBeNull();
        vm.SaveCommand.CanExecute(null).ShouldBeFalse();

        vm.ClientKeyPath = "/path/to/key.pem";
        vm.ClientCertError.ShouldBeNull();
    }

    [Fact]
    public void Key_without_a_cert_is_invalid_FR035()
    {
        var vm = Create(out _, out _, out _);
        vm.Name = "p";
        vm.ClientKeyPath = "/path/to/key.pem";

        vm.ClientCertError.ShouldNotBeNull();
    }

    [Fact]
    public async Task Browse_detects_pem_format()
    {
        var pem = TempFile("-----BEGIN CERTIFICATE-----\nabc\n-----END CERTIFICATE-----", "crt");
        var vm = Create(out var picker, out _, out _);
        picker.OpenResult = pem;

        await vm.BrowseClientCertCommand.ExecuteAsync(null);

        vm.ClientCertPath.ShouldBe(pem);
        vm.DetectedClientCertFormat.ShouldBe("PEM");
        vm.IsPkcs12.ShouldBeFalse();
    }

    [Fact]
    public async Task Browse_detects_pkcs12_format()
    {
        var pfx = TempBinaryFile();
        var vm = Create(out var picker, out _, out _);
        picker.OpenResult = pfx;

        await vm.BrowseClientCertCommand.ExecuteAsync(null);

        vm.DetectedClientCertFormat.ShouldBe("PKCS12");
        vm.IsPkcs12.ShouldBeTrue();
    }

    [Fact]
    public async Task Saving_a_pkcs12_password_writes_it_to_the_secret_store()
    {
        var pfx = TempBinaryFile();
        var vm = Create(out var picker, out _, out var secrets);
        picker.OpenResult = pfx;
        vm.Name = "pfx";
        await vm.BrowseClientCertCommand.ExecuteAsync(null);
        vm.ClientCertPassword = "p@ss";

        TlsProfile? saved = null;
        vm.CloseRequested += p => saved = p;
        await vm.SaveCommand.ExecuteAsync(null);

        saved.ShouldNotBeNull();
        saved!.ClientCertPasswordSecretRef.ShouldNotBeNull();
        (await secrets.GetAsync(saved.ClientCertPasswordSecretRef!, TestContext.Current.CancellationToken)).ShouldBe("p@ss");
        saved.ClientCertPath.ShouldBe(pfx);
        saved.ClientKeyPath.ShouldBeNull(); // PKCS12 carries its own key
    }

    [Fact]
    public async Task Selecting_skip_verification_requires_confirmation_and_reverts_when_declined()
    {
        var vm = Create(out _, out var dialog, out _);
        dialog.ConfirmResult = false;

        vm.SelectedValidationMode = TlsValidationMode.SkipVerification;

        // FakeDialogService completes synchronously, so the revert has already applied.
        dialog.ConfirmCount.ShouldBe(1);
        vm.SelectedValidationMode.ShouldBe(TlsValidationMode.SystemRoots);
    }

    [Fact]
    public void Selecting_skip_verification_sticks_when_confirmed()
    {
        var vm = Create(out _, out var dialog, out _);
        dialog.ConfirmResult = true;

        vm.SelectedValidationMode = TlsValidationMode.SkipVerification;

        dialog.ConfirmCount.ShouldBe(1);
        vm.SelectedValidationMode.ShouldBe(TlsValidationMode.SkipVerification);

        TlsProfile? saved = null;
        vm.Name = "insecure";
        vm.CloseRequested += p => saved = p;
        vm.SaveCommand.Execute(null);

        saved!.InsecureSkipVerify.ShouldBeTrue();
    }

    // ── FR-037: certificate facts after a file is selected ───────────────────

    [Fact]
    public void Selecting_a_ca_certificate_surfaces_its_facts()
    {
        var path = TestCertificate.WritePem("CN=ca.example.test");
        _tempFiles.Add(path);
        var vm = Create(out _, out _, out _);

        vm.SelectedValidationMode = TlsValidationMode.CustomCa;
        vm.CaCertPath = path;

        vm.HasCaCertFacts.ShouldBeTrue();
        vm.CaCertFacts!.Subject.ShouldContain("ca.example.test");
        vm.CaCertFacts.IsOutOfValidity.ShouldBeFalse();
    }

    [Fact]
    public void A_bad_certificate_path_yields_no_facts_without_blocking()
    {
        var vm = Create(out _, out _, out _);

        vm.ClientCertPath = "/no/such/cert.pem";

        vm.HasClientCertFacts.ShouldBeFalse();
        vm.ClientCertFacts.ShouldBeNull();
    }
}
