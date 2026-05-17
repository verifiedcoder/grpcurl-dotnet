using System.Text.Json.Nodes;
using Gql2Grpc.GraphQL;

namespace Gql2Grpc.Tests.Unit.GraphQL;

public sealed class VariableCoercerTests
{
    [Fact]
    public void Coerces_int_from_cli_string()
    {
        var doc = GraphQLDocumentParser.Parse("query($first: Int) { x }");
        var op = doc.SelectOperation(null);

        var result = VariableCoercer.Coerce(
            op.VariableDefinitions,
            new Dictionary<string, string> { ["first"] = "42" },
            null);

        (result["first"] as JsonValue)!.GetValue<int>().ShouldBe(42);
    }

    [Fact]
    public void Applies_default_when_no_value_supplied()
    {
        var doc = GraphQLDocumentParser.Parse("query($n: Int = 7) { x }");
        var op = doc.SelectOperation(null);

        var result = VariableCoercer.Coerce(op.VariableDefinitions, null, null);

        (result["n"] as JsonValue)!.GetValue<int>().ShouldBe(7);
    }

    [Fact]
    public void Throws_when_non_null_required_missing()
    {
        var doc = GraphQLDocumentParser.Parse("query($id: String!) { x }");
        var op = doc.SelectOperation(null);

        Should.Throw<ArgumentException>(() => VariableCoercer.Coerce(op.VariableDefinitions, null, null));
    }

    [Fact]
    public void Variables_file_fills_missing_names()
    {
        var doc = GraphQLDocumentParser.Parse("query($s: String) { x }");
        var op = doc.SelectOperation(null);

        var file = JsonNode.Parse("""{ "s": "hello" }""");
        var result = VariableCoercer.Coerce(op.VariableDefinitions, null, file);

        (result["s"] as JsonValue)!.GetValue<string>().ShouldBe("hello");
    }

    [Fact]
    public void Cli_overrides_variables_file()
    {
        var doc = GraphQLDocumentParser.Parse("query($s: String) { x }");
        var op = doc.SelectOperation(null);

        var file = JsonNode.Parse("""{ "s": "from-file" }""");
        var result = VariableCoercer.Coerce(
            op.VariableDefinitions,
            new Dictionary<string, string> { ["s"] = "from-cli" },
            file);

        (result["s"] as JsonValue)!.GetValue<string>().ShouldBe("from-cli");
    }
}
