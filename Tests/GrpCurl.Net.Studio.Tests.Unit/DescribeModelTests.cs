using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class DescribeModelTests
{
    [Fact]
    public void Describe_result_factories_carry_their_payloads()
    {
        var symbol = new EnumDescription("p.E", "E", "p.proto", [new EnumValue("ZERO", 0)]);

        var ok = DescribeResult.Success(symbol);
        ok.Ok.ShouldBeTrue();
        ok.Symbol.ShouldBe(symbol);
        ok.Error.ShouldBeNull();

        var error = new DescriptorLoadError("missing", null, ReflectionUnavailable: false);
        var bad = DescribeResult.Failure(error);
        bad.Ok.ShouldBeFalse();
        bad.Symbol.ShouldBeNull();
        bad.Error.ShouldBe(error);
    }

    [Fact]
    public void Symbol_descriptions_expose_their_kind()
    {
        SymbolDescription service = new ServiceDescription("p.S", "S", "p.proto", []);
        SymbolDescription method = new MethodDescription("p.S/M", "M", "p.proto", StreamingShape.Unary,
            new TypeRef("p.In", true), new TypeRef("p.Out", true), new TypeRef("p.S", true), "{}");
        SymbolDescription message = new MessageDescription("p.M", "M", "p.proto", [], [], "{}");
        SymbolDescription enumeration = new EnumDescription("p.E", "E", "p.proto", []);

        service.Kind.ShouldBe(SymbolKind.Service);
        method.Kind.ShouldBe(SymbolKind.Method);
        message.Kind.ShouldBe(SymbolKind.Message);
        enumeration.Kind.ShouldBe(SymbolKind.Enum);
    }

    [Fact]
    public void Catalog_types_default_to_empty()
    {
        ServiceCatalog.Empty.Types.ShouldBeEmpty();
        new ServiceCatalog([], []).Types.ShouldBeEmpty();
    }

    [Fact]
    public void Unresolvable_type_ref_is_marked_non_navigable()
    {
        var scalar = new TypeRef("string", Resolvable: false);

        scalar.Resolvable.ShouldBeFalse();
    }
}
