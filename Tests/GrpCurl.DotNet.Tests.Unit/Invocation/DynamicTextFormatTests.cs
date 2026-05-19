using Google.Protobuf.Reflection;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Invocation;
using GrpCurl.Net.Tests.Unit.Fixtures;

namespace GrpCurl.Net.Tests.Unit.Invocation;

public sealed class DynamicTextFormatTests
{
    private static async Task<MessageDescriptor> LoadSimpleRequestAsync()
    {
        var protosetPath = Path.Combine(
            Path.GetDirectoryName(typeof(TestDescriptorProvider).Assembly.Location)!,
            "TestProtosets",
            "test.protoset");

        var source = await ProtosetSource.LoadFromFilesAsync([protosetPath], TestContext.Current.CancellationToken);
        var symbol = await source.FindSymbolAsync("testing.SimpleRequest", TestContext.Current.CancellationToken);

        return (MessageDescriptor)symbol!;
    }

    [Fact]
    public async Task PrintAndParse_RoundTrip_Scalars()
    {
        // Arrange
        var descriptor = await LoadSimpleRequestAsync();
        var message = new SimpleDynamicMessage(descriptor);

        var responseSize = descriptor.Fields.InDeclarationOrder().First(f => f.Name == "response_size");

        message.Fields[responseSize] = 1024;

        // Act
        var text = DynamicTextFormat.Print(message);

        // Assert
        text.ShouldContain("response_size: 1024");

        var parsed = DynamicTextFormat.Parse(descriptor, text);

        parsed.Fields[responseSize].ShouldBe(1024);
    }

    [Fact]
    public async Task Parse_EnumByName_ResolvesNumber()
    {
        // Arrange
        var descriptor = await LoadSimpleRequestAsync();
        var responseType = descriptor.Fields.InDeclarationOrder().FirstOrDefault(f => f.Name == "response_type");

        if (responseType is null || responseType.FieldType != FieldType.Enum)
        {
            return;
        }

        var enumValue = responseType.EnumType.Values[0];
        var text = $"response_type: {enumValue.Name}";

        // Act
        var parsed = DynamicTextFormat.Parse(descriptor, text);

        // Assert
        parsed.Fields[responseType].ShouldBe(enumValue.Number);
    }

    [Fact]
    public async Task Print_QuotesStrings_AndEscapesSpecialCharacters()
    {
        // Arrange
        var descriptor = await LoadSimpleRequestAsync();
        var stringField = descriptor.Fields.InDeclarationOrder()
            .FirstOrDefault(f => f.FieldType == FieldType.String);

        if (stringField is null)
        {
            return;
        }

        var message = new SimpleDynamicMessage(descriptor)
        {
            Fields =
            {
                [stringField] = "hello\nworld"
            }
        };

        // Act
        var text = DynamicTextFormat.Print(message);

        // Assert
        text.ShouldContain($"{stringField.Name}: \"hello\\nworld\"");
    }
}
