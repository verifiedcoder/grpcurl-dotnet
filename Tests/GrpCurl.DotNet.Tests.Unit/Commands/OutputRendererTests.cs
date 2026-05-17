using GrpCurl.Net.Commands;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Tests.Unit.Fixtures;
using System.IO;
using System.Text.Json;

namespace GrpCurl.Net.Tests.Unit.Commands;

public sealed class OutputRendererTests
{
    [Fact]
    public void WriteListServices_Text_OutputsOneServicePerLine()
    {
        var services = new[] { "alpha.Foo", "beta.Bar" };

        var output = Capture(w => OutputRenderer.WriteListServices(services, OutputFormat.Text, w));

        var lines = TestConsole.SplitLines(output);

        lines.Length.ShouldBe(2);
        lines[0].ShouldBe("alpha.Foo");
        lines[1].ShouldBe("beta.Bar");
    }

    [Fact]
    public void WriteListServices_Json_OutputsSingleEnvelope()
    {
        var services = new[] { "alpha.Foo", "beta.Bar" };

        var output = Capture(w => OutputRenderer.WriteListServices(services, OutputFormat.Json, w));

        var trimmed = output.TrimEnd();

        trimmed.IndexOf('\n').ShouldBe(-1);

        using var doc = JsonDocument.Parse(trimmed);

        doc.RootElement.GetProperty("kind").GetString().ShouldBe("services");

        var arr = doc.RootElement.GetProperty("services");

        arr.GetArrayLength().ShouldBe(2);
        arr[0].GetString().ShouldBe("alpha.Foo");
        arr[1].GetString().ShouldBe("beta.Bar");
    }

    [Fact]
    public async Task WriteListMethods_Json_IncludesMethodMetadata()
    {
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);
        var descriptor = await source.FindSymbolAsync("testing.TestService", TestContext.Current.CancellationToken);
        var svc = (Google.Protobuf.Reflection.ServiceDescriptor)descriptor!;

        var output = Capture(w => OutputRenderer.WriteListMethods("testing.TestService", svc, OutputFormat.Json, w));

        using var doc = JsonDocument.Parse(output.TrimEnd());
        var root = doc.RootElement;

        root.GetProperty("kind").GetString().ShouldBe("methods");
        root.GetProperty("service").GetString().ShouldBe("testing.TestService");

        var methods = root.GetProperty("methods");

        methods.GetArrayLength().ShouldBeGreaterThan(0);

        var first = methods[0];

        first.TryGetProperty("name", out _).ShouldBeTrue();
        first.TryGetProperty("fullName", out _).ShouldBeTrue();
        first.TryGetProperty("inputType", out _).ShouldBeTrue();
        first.TryGetProperty("outputType", out _).ShouldBeTrue();
        first.TryGetProperty("clientStreaming", out _).ShouldBeTrue();
        first.TryGetProperty("serverStreaming", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task WriteDescribeJson_Service_HasExpectedShape()
    {
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);
        var descriptor = await source.FindSymbolAsync("testing.TestService", TestContext.Current.CancellationToken);

        var output = Capture(w => OutputRenderer.WriteDescribeJson(descriptor!, msgTemplate: false, w));

        using var doc = JsonDocument.Parse(output.TrimEnd());
        var root = doc.RootElement;

        root.GetProperty("kind").GetString().ShouldBe("service");
        root.GetProperty("fullName").GetString().ShouldBe("testing.TestService");
        root.GetProperty("name").GetString().ShouldBe("TestService");
        root.TryGetProperty("file", out _).ShouldBeTrue();
        root.GetProperty("methods").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task WriteDescribeJson_Message_IncludesFieldsAndOneofs()
    {
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);
        var descriptor = await source.FindSymbolAsync("testing.SimpleRequest", TestContext.Current.CancellationToken);

        var output = Capture(w => OutputRenderer.WriteDescribeJson(descriptor!, msgTemplate: false, w));

        using var doc = JsonDocument.Parse(output.TrimEnd());
        var root = doc.RootElement;

        root.GetProperty("kind").GetString().ShouldBe("message");
        root.GetProperty("fullName").GetString().ShouldBe("testing.SimpleRequest");
        root.GetProperty("fields").GetArrayLength().ShouldBeGreaterThan(0);
        root.TryGetProperty("oneofs", out _).ShouldBeTrue();
        root.TryGetProperty("nestedTypes", out _).ShouldBeTrue();
        root.TryGetProperty("nestedEnums", out _).ShouldBeTrue();

        var firstField = root.GetProperty("fields")[0];

        firstField.TryGetProperty("name", out _).ShouldBeTrue();
        firstField.TryGetProperty("number", out _).ShouldBeTrue();
        firstField.TryGetProperty("type", out _).ShouldBeTrue();
        firstField.TryGetProperty("label", out _).ShouldBeTrue();
        firstField.TryGetProperty("jsonName", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task WriteDescribeJson_Enum_HasValuesArray()
    {
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);
        var descriptor = await source.FindSymbolAsync("testing.PayloadType", TestContext.Current.CancellationToken);

        var output = Capture(w => OutputRenderer.WriteDescribeJson(descriptor!, msgTemplate: false, w));

        using var doc = JsonDocument.Parse(output.TrimEnd());
        var root = doc.RootElement;

        root.GetProperty("kind").GetString().ShouldBe("enum");

        var values = root.GetProperty("values");

        values.GetArrayLength().ShouldBeGreaterThan(0);
        values[0].TryGetProperty("name", out _).ShouldBeTrue();
        values[0].TryGetProperty("number", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task WriteDescribeJson_MessageTemplate_EmitsTemplateKind()
    {
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);
        var descriptor = await source.FindSymbolAsync("testing.SimpleRequest", TestContext.Current.CancellationToken);

        var output = Capture(w => OutputRenderer.WriteDescribeJson(descriptor!, msgTemplate: true, w));

        using var doc = JsonDocument.Parse(output.TrimEnd());
        var root = doc.RootElement;

        root.GetProperty("kind").GetString().ShouldBe("messageTemplate");
        root.GetProperty("fullName").GetString().ShouldBe("testing.SimpleRequest");
        root.TryGetProperty("template", out _).ShouldBeTrue();
    }

    private static string Capture(Action<TextWriter> renderer)
    {
        using var writer = new StringWriter();

        renderer(writer);

        return writer.ToString();
    }
}
