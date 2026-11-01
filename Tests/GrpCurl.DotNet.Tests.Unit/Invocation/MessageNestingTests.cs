using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.TestSupport;
using Grpc.Core;
using System.Text.Json;

namespace GrpCurl.Net.Tests.Unit.Invocation;

public sealed class MessageNestingTests
{
    [Theory]
    [InlineData(99, 1)]
    [InlineData(100, 1)]
    [InlineData(101, 1)]
    [InlineData(99, 3)]
    [InlineData(100, 3)]
    [InlineData(101, 3)]
    public void Binary_and_rendering_boundaries_agree(int depth, int field)
    {
        if (depth > 100)
        {
            var error = Should.Throw<MessageNestingException>(() => NestingData.Parse(NestingData.Deep(depth, false, field)));
            error.Limit.ShouldBe(100);
            error.Phase.ShouldBe("binary parsing");
        }
        else
        {
            var message = NestingData.Parse(NestingData.Deep(depth, false, field));
            foreach (var defaults in new[] { false, true })
            {
                foreach (var indent in new[] { false, true })
                {
                    var json = DynamicInvoker.MessageToJson(message, defaults, indent);
                    using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 202 });
                    document.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
                }
            }
        }
    }

    [Theory]
    [InlineData(49, 1, false)]
    [InlineData(50, 0, false)]
    [InlineData(50, 1, true)]
    [InlineData(51, 0, true)]
    public void Any_and_embedded_binary_share_the_remaining_budget(int wrappers, int innerDepth, bool fails)
    {
        var bytes = NestingData.Deep(innerDepth, false);
        for (var i = 0; i < wrappers; i++)
        {
            bytes = NestingData.Field(2, new Any { TypeUrl = "type.googleapis.com/nest.Node", Value = ByteString.CopyFrom(bytes) }.ToByteArray());
        }
        var message = NestingData.Parse(bytes);
        if (fails)
        {
            var error = Should.Throw<RpcException>(() => DynamicInvoker.MessageToJson(message));
            error.StatusCode.ShouldBe(StatusCode.Internal);
            error.Status.Detail.ShouldContain("limit of 100");
        }
        else { DynamicInvoker.MessageToJson(message).ShouldContain("@type"); }
    }

    [Theory]
    [InlineData(100, 5)]
    [InlineData(101, 5)]
    [InlineData(100, 7)]
    [InlineData(101, 7)]
    public void Known_and_unknown_groups_are_bounded(int depth, int field)
    {
        var bytes = Enumerable.Repeat((byte)((field << 3) | 3), depth)
            .Concat(Enumerable.Repeat((byte)((field << 3) | 4), depth)).ToArray();
        if (depth > 100) { _ = Should.Throw<MessageNestingException>(() => NestingData.Parse(bytes)); }
        else { _ = NestingData.Parse(bytes); }
    }

    [Fact]
    public void Unknown_groups_cannot_reset_budget_inside_a_nested_stream()
    {
        byte[] bytes = [59, 59, 60, 60]; // two unknown groups
        for (var i = 0; i < 99; i++) { bytes = NestingData.Field(1, bytes); }
        _ = Should.Throw<MessageNestingException>(() => NestingData.Parse(bytes));
    }

    [Theory]
    [InlineData(49)]
    [InlineData(50)]
    [InlineData(51)]
    public void Map_entries_count_even_though_their_JSON_is_flattened(int entries)
    {
        byte[] bytes = [];
        for (var i = 0; i < entries; i++) { bytes = NestingData.Field(4, new byte[] { 10, 1, 120 }.Concat(NestingData.Field(2, bytes)).ToArray()); }
        if (entries > 50) { _ = Should.Throw<MessageNestingException>(() => NestingData.Parse(bytes)); }
        else { DynamicInvoker.MessageToJson(NestingData.Parse(bytes)).ShouldContain("entries"); }
    }

    [Theory]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(101)]
    public void Recursive_well_known_JSON_callbacks_enforce_exact_boundaries(int depth)
    {
        var root = new SimpleDynamicMessage(NestingData.Descriptor);
        var current = new SimpleDynamicMessage(Value.Descriptor);
        root.Fields[root.Descriptor.FindFieldByNumber(6)] = current;
        for (var level = 2; level <= depth; level++)
        {
            var isList = level % 2 == 0;
            var next = new SimpleDynamicMessage(isList ? ListValue.Descriptor : Value.Descriptor);
            if (isList) { current.Fields[Value.Descriptor.FindFieldByNumber(6)] = next; }
            else { current.RepeatedFields[ListValue.Descriptor.FindFieldByNumber(1)] = [next]; }
            current = next;
        }
        if (depth > 100) { _ = Should.Throw<RpcException>(() => DynamicInvoker.MessageToJson(root)); }
        else { DynamicInvoker.MessageToJson(root).ShouldContain("value"); }
    }

    [Theory]
    [InlineData(98, false)]
    [InlineData(99, true)]
    public void Any_special_WKT_rendering_does_not_reset_depth(int prefix, bool fails)
    {
        var bytes = NestingData.Field(2, new Any { TypeUrl = "type.googleapis.com/google.protobuf.Struct", Value = ByteString.Empty }.ToByteArray());
        for (var i = 0; i < prefix; i++) { bytes = NestingData.Field(1, bytes); }
        var message = NestingData.Parse(bytes);
        if (fails) { _ = Should.Throw<RpcException>(() => DynamicInvoker.MessageToJson(message)); }
        else { DynamicInvoker.MessageToJson(message).ShouldContain("@type"); }
    }

    [Fact]
    public void Malformed_unknown_groups_remain_errors()
    {
        _ = Should.Throw<InvalidDataException>(() => NestingData.Parse([59]));
        _ = Should.Throw<InvalidDataException>(() => NestingData.Parse([59, 68]));
    }

    [Fact]
    public void Wide_siblings_and_independent_calls_do_not_consume_shared_state()
    {
        var bytes = Enumerable.Range(0, 200).SelectMany(_ => NestingData.Deep(2, false, 3)).ToArray();
        for (var i = 0; i < 3; i++) { DynamicInvoker.MessageToJson(NestingData.Parse(bytes)).ShouldContain("children"); }
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("map")]
    [InlineData("list")]
    [InlineData("struct")]
    public void Cyclic_in_memory_models_fail_rendering_without_poisoning_other_calls(string kind)
    {
        var root = new SimpleDynamicMessage(NestingData.Descriptor);
        if (kind == "ordinary") { root.Fields[root.Descriptor.FindFieldByNumber(1)] = root; }
        else if (kind == "map") { root.MapFields[root.Descriptor.FindFieldByNumber(4)] = new() { ["x"] = root }; }
        else
        {
            var value = new SimpleDynamicMessage(Value.Descriptor);
            root.Fields[root.Descriptor.FindFieldByNumber(6)] = value;
            if (kind == "list")
            {
                var list = new SimpleDynamicMessage(ListValue.Descriptor);
                value.Fields[Value.Descriptor.FindFieldByNumber(6)] = list;
                list.RepeatedFields[ListValue.Descriptor.FindFieldByNumber(1)] = [value];
            }
            else
            {
                var structure = new SimpleDynamicMessage(Struct.Descriptor);
                value.Fields[Value.Descriptor.FindFieldByNumber(5)] = structure;
                structure.MapFields[Struct.Descriptor.FindFieldByNumber(1)] = new() { ["x"] = value };
            }
        }
        Should.Throw<RpcException>(() => DynamicInvoker.MessageToJson(root)).StatusCode.ShouldBe(StatusCode.Internal);
        DynamicInvoker.MessageToJson(new SimpleDynamicMessage(NestingData.Descriptor), indent: false).ShouldBe("{}");
    }
}
