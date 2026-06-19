using GrpCurl.Net.Studio.Services;
using System.Text.Json.Nodes;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>GQL-075: the introspection __schema result is mapped into a navigable type tree.</summary>
public sealed class GraphQlServiceSchemaTests
{
    [Fact]
    public void MapSchemaTypes_builds_a_tree_unwraps_type_refs_and_skips_meta_types()
    {
        var schema = JsonNode.Parse(
            """
            {
              "types": [
                { "name": "__Type", "kind": "OBJECT", "fields": [] },
                { "name": "User", "kind": "OBJECT", "fields": [
                  { "name": "id", "type": { "kind": "NON_NULL", "ofType": { "kind": "SCALAR", "name": "String" } } },
                  { "name": "tags", "type": { "kind": "LIST", "ofType": { "kind": "SCALAR", "name": "String" } } } ] },
                { "name": "Color", "kind": "ENUM", "enumValues": [ { "name": "RED" }, { "name": "GREEN" } ] }
              ]
            }
            """)!.AsObject();

        var types = GraphQlService.MapSchemaTypes(schema);

        types.Select(t => t.Name).ShouldBe(["User", "Color"]); // __Type filtered out

        var user = types.Single(t => t.Name == "User");
        user.Kind.ShouldBe("OBJECT");
        user.Members.ShouldContain(m => m.Name == "id" && m.TypeName == "String!");
        user.Members.ShouldContain(m => m.Name == "tags" && m.TypeName == "[String]");

        var color = types.Single(t => t.Name == "Color");
        color.Members.Select(m => m.Name).ShouldBe(["RED", "GREEN"]);
        color.Members.ShouldAllBe(m => m.TypeName == null); // enum values are bare names
    }

    [Fact]
    public void MapSchemaTypes_is_empty_when_there_are_no_types()
        => GraphQlService.MapSchemaTypes(JsonNode.Parse("""{ "queryType": { "name": "Query" } }""")!.AsObject()).ShouldBeEmpty();
}
