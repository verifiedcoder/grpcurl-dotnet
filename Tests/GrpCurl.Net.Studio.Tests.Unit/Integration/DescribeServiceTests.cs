using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.Unit.Fixtures;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Tests.Unit.Integration;

/// <summary>
///     L2 service-layer E2E for describe + template (FR-050/052/022): drives the real
///     <see cref="DescriptorService" /> against the in-process TestServer through Core reflection.
/// </summary>
[Collection(StudioPlaintextServerCollection.Name)]
public sealed class DescribeServiceTests(StudioPlaintextServerFixture server)
{
    private static SavedConnection PlaintextReflection(string address) => new()
    {
        Name = "test",
        Address = address,
        Transport = TransportMode.Plaintext,
        DescriptorSource = new DescriptorSourceConfig()
    };

    private async Task<SymbolDescription> Describe(string symbol)
    {
        IDescriptorService descriptors = new DescriptorService();
        var result = await descriptors.DescribeAsync(
            PlaintextReflection(server.Address), symbol, TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.Error?.Message);
        return result.Symbol!;
    }

    [Fact]
    public async Task Describe_service_returns_its_methods_with_shapes()
    {
        var service = (ServiceDescription)await Describe("testing.TestService");

        service.Name.ShouldBe("TestService");
        service.SourceFile.ShouldNotBeNullOrWhiteSpace();

        var unary = service.Methods.Single(m => m.Name == "UnaryCall");
        unary.Shape.ShouldBe(StreamingShape.Unary);
        unary.InputType.FullName.ShouldBe("testing.SimpleRequest");
        unary.OutputType.FullName.ShouldBe("testing.SimpleResponse");

        service.Methods.Single(m => m.Name == "StreamingOutputCall").Shape.ShouldBe(StreamingShape.ServerStreaming);
    }

    [Fact]
    public async Task Describe_message_returns_fields_a_template_and_type_links()
    {
        var message = (MessageDescription)await Describe("testing.StreamingOutputCallRequest");

        var responseType = message.Fields.Single(f => f.Name == "response_type");
        _ = responseType.Link.ShouldNotBeNull();
        responseType.Link!.FullName.ShouldBe("testing.PayloadType");

        var responseParams = message.Fields.Single(f => f.Name == "response_parameters");
        responseParams.Label.ShouldBe(FieldLabel.Repeated);
        responseParams.Link!.FullName.ShouldBe("testing.ResponseParameters");

        // Template parses and carries the enum default + the repeated array (AC-05 shape).
        using var doc = JsonDocument.Parse(message.TemplateJson);
        doc.RootElement.GetProperty("response_type").GetString().ShouldBe("COMPRESSABLE");
        doc.RootElement.GetProperty("response_parameters").ValueKind.ShouldBe(JsonValueKind.Array);
    }

    [Fact]
    public async Task Describe_enum_returns_its_values()
    {
        var enumeration = (EnumDescription)await Describe("testing.PayloadType");

        enumeration.Values.ShouldContain(v => v.Name == "COMPRESSABLE" && v.Number == 0);
    }

    [Fact]
    public async Task Describe_method_template_matches_its_input_message_template()
    {
        var method = (MethodDescription)await Describe("testing.TestService/UnaryCall");

        method.Shape.ShouldBe(StreamingShape.Unary);
        method.InputType.FullName.ShouldBe("testing.SimpleRequest");
        method.ParentService.FullName.ShouldBe("testing.TestService");

        // The method template is the input message's template (same Core path, FR-052).
        var input = (MessageDescription)await Describe("testing.SimpleRequest");
        method.TemplateJson.ShouldBe(input.TemplateJson);
    }

    [Fact]
    public async Task Describe_message_with_a_oneof_reports_oneof_membership()
    {
        var message = (MessageDescription)await Describe("testing.OneofMessage");

        message.Fields.ShouldContain(f => f.OneofName != null);
    }

    [Fact]
    public async Task Describe_unknown_symbol_returns_a_failure()
    {
        IDescriptorService descriptors = new DescriptorService();

        var result = await descriptors.DescribeAsync(
            PlaintextReflection(server.Address), "testing.NoSuchType", TestContext.Current.CancellationToken);

        result.Ok.ShouldBeFalse();
        _ = result.Error.ShouldNotBeNull();
    }

    [Fact]
    public async Task Load_populates_the_types_branch_grouped_by_package()
    {
        IDescriptorService descriptors = new DescriptorService();

        var result = await descriptors.LoadAsync(PlaintextReflection(server.Address), TestContext.Current.CancellationToken);

        result.Ok.ShouldBeTrue(result.Error?.Message);
        var types = result.Catalog!.Types;

        types.ShouldContain(t => t.FullName == "testing.SimpleRequest" && t.Kind == TypeNodeKind.Message && t.Package == "testing");
        types.ShouldContain(t => t.FullName == "testing.PayloadType" && t.Kind == TypeNodeKind.Enum);
        // Nested types are surfaced with their full path.
        types.ShouldContain(t => t.FullName == "testing.NestedTypesMessage.Status");
    }

    [Fact]
    public async Task Describe_honours_user_cancellation()
    {
        IDescriptorService descriptors = new DescriptorService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _ = await Should.ThrowAsync<OperationCanceledException>(
            async () => await descriptors.DescribeAsync(PlaintextReflection(server.Address), "testing.TestService", cts.Token));
    }

    // FR-059: the deprecated-symbol mapping, end-to-end against a real descriptor (the TestServer's
    // DeprecatedCall rpc carries `option deprecated = true`). Covers both the describe and catalog paths.

    [Fact]
    public async Task A_deprecated_method_is_flagged_in_describe()
    {
        var service = (ServiceDescription)await Describe("testing.TestService");

        service.Methods.Single(m => m.Name == "DeprecatedCall").Deprecated.ShouldBeTrue();
        service.Methods.Single(m => m.Name == "UnaryCall").Deprecated.ShouldBeFalse();
    }

    [Fact]
    public async Task A_deprecated_method_is_flagged_in_the_catalog()
    {
        var result = await new DescriptorService().LoadAsync(
            PlaintextReflection(server.Address), TestContext.Current.CancellationToken);
        result.Ok.ShouldBeTrue(result.Error?.Message);

        var service = result.Catalog!.Services.Single(s => s.FullName == "testing.TestService");
        service.Methods.Single(m => m.Name == "DeprecatedCall").Deprecated.ShouldBeTrue();
    }
}
