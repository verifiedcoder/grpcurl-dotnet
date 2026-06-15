using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class DescriptorModelTests
{
    [Theory]
    [InlineData(false, false, StreamingShape.Unary, "U", "Unary")]
    [InlineData(false, true, StreamingShape.ServerStreaming, "SS", "Server streaming")]
    [InlineData(true, false, StreamingShape.ClientStreaming, "CS", "Client streaming")]
    [InlineData(true, true, StreamingShape.BidiStreaming, "BD", "Bidirectional streaming")]
    public void Streaming_shape_maps_flags_badge_and_label(
        bool clientStreaming, bool serverStreaming, StreamingShape expected, string badge, string label)
    {
        var shape = StreamingShapeExtensions.FromFlags(clientStreaming, serverStreaming);

        shape.ShouldBe(expected);
        shape.Badge().ShouldBe(badge);
        shape.Label().ShouldBe(label);
    }

    [Fact]
    public void Descriptor_load_result_factories_carry_their_payloads()
    {
        var catalog = new ServiceCatalog([new ServiceEntry("p.S", [])], ["w"]);

        var ok = DescriptorLoadResult.Success(catalog);
        ok.Ok.ShouldBeTrue();
        ok.Catalog.ShouldBe(catalog);
        ok.Error.ShouldBeNull();

        var error = new DescriptorLoadError("boom", "try this", ReflectionUnavailable: true);
        var bad = DescriptorLoadResult.Failure(error);
        bad.Ok.ShouldBeFalse();
        bad.Catalog.ShouldBeNull();
        bad.Error.ShouldBe(error);
    }

    [Fact]
    public void Empty_catalog_has_no_services_or_warnings()
    {
        ServiceCatalog.Empty.Services.ShouldBeEmpty();
        ServiceCatalog.Empty.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Connection_selection_publishes_changes_and_ignores_no_op_sets()
    {
        var selection = new ConnectionSelection();
        var raised = 0;
        selection.CurrentChanged += (_, _) => raised++;

        selection.Current.ShouldBeNull();

        var a = new SavedConnection { Name = "a", Address = "h:1" };
        selection.Set(a);
        selection.Current.ShouldBe(a);
        raised.ShouldBe(1);

        // Same instance: no change, no event.
        selection.Set(a);
        raised.ShouldBe(1);

        // Clearing is a change.
        selection.Set(null);
        selection.Current.ShouldBeNull();
        raised.ShouldBe(2);
    }
}
