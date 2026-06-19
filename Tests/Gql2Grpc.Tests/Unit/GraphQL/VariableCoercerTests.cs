using Gql2Grpc.GraphQL;
using System.Text.Json.Nodes;

namespace Gql2Grpc.Tests.Unit.GraphQL;

public sealed class VariableCoercerTests
{
    [Fact]
    public void Coerces_int_from_cli_string()
    {
        // Arrange
        var doc = GraphQLDocumentParser.Parse("query($first: Int) { x }");
        var op = doc.SelectOperation(null);

        // Act
        var result = VariableCoercer.Coerce(
            op.VariableDefinitions,
            new Dictionary<string, string> { ["first"] = "42" },
            null);

        // Assert
        (result["first"] as JsonValue)!.GetValue<int>().ShouldBe(42);
    }

    [Fact]
    public void Applies_default_when_no_value_supplied()
    {
        // Arrange
        var doc = GraphQLDocumentParser.Parse("query($n: Int = 7) { x }");
        var op = doc.SelectOperation(null);

        // Act
        var result = VariableCoercer.Coerce(op.VariableDefinitions, null, null);

        // Assert
        (result["n"] as JsonValue)!.GetValue<int>().ShouldBe(7);
    }

    [Fact]
    public void Throws_when_non_null_required_missing()
    {
        // Arrange
        var doc = GraphQLDocumentParser.Parse("query($id: String!) { x }");

        // Act
        var op = doc.SelectOperation(null);

        // Assert
        _ = Should.Throw<ArgumentException>(() => VariableCoercer.Coerce(op.VariableDefinitions, null, null));
    }

    [Fact]
    public void Variables_file_fills_missing_names()
    {
        // Arrange
        var doc = GraphQLDocumentParser.Parse("query($s: String) { x }");
        var op = doc.SelectOperation(null);

        var file = JsonNode.Parse("""{ "s": "hello" }""");

        // Act
        var result = VariableCoercer.Coerce(op.VariableDefinitions, null, file);

        // Assert
        (result["s"] as JsonValue)!.GetValue<string>().ShouldBe("hello");
    }

    [Fact]
    public void Cli_overrides_variables_file()
    {
        // Arrange
        var doc = GraphQLDocumentParser.Parse("query($s: String) { x }");
        var op = doc.SelectOperation(null);

        var file = JsonNode.Parse("""{ "s": "from-file" }""");

        // Act
        var result = VariableCoercer.Coerce(
            op.VariableDefinitions,
            new Dictionary<string, string> { ["s"] = "from-cli" },
            file);

        // Assert
        (result["s"] as JsonValue)!.GetValue<string>().ShouldBe("from-cli");
    }
}
