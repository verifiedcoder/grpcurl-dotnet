using GrpCurl.Net.Commands;
using System.CommandLine;

namespace GrpCurl.Net.Tests.Unit.Commands;

public sealed class InvokeCommandHandlerTests
{
    [Fact]
    public void Create_ReturnsValidCommand()
    {
        // Arrange

        // Act
        var command = InvokeCommandHandler.Create();

        // Assert
        _ = command.ShouldNotBeNull();
        command.Name.ShouldBe("invoke");
    }

    [Fact]
    public void Create_HasDescription()
    {
        // Arrange

        // Act
        var command = InvokeCommandHandler.Create();

        // Assert
        string.IsNullOrEmpty(command.Description).ShouldBeFalse();

        command.Description.ShouldContain("Invoke");
    }

    [Fact]
    public void Create_HasTwoArguments()
    {
        // Arrange

        // Act
        var command = InvokeCommandHandler.Create();

        // Assert
        command.Arguments.Count.ShouldBe(2);
    }

    [Fact]
    public void Create_HasAddressArgument()
    {
        // Arrange

        // Act
        var command = InvokeCommandHandler.Create();

        // Assert
        var addressArg = command.Arguments.FirstOrDefault(a => a.Name == "address");

        _ = addressArg.ShouldNotBeNull();
    }

    [Fact]
    public void Create_HasMethodArgument()
    {
        // Arrange

        // Act
        var command = InvokeCommandHandler.Create();

        // Assert
        var methodArg = command.Arguments.FirstOrDefault(a => a.Name == "method");

        _ = methodArg.ShouldNotBeNull();
    }

    [Fact]
    public void Create_AddressArgument_IsRequired()
    {
        // Arrange

        // Act
        var command = InvokeCommandHandler.Create();

        // Assert
        var addressArg = command.Arguments.FirstOrDefault(a => a.Name == "address");

        _ = addressArg.ShouldNotBeNull();
        addressArg.Arity.ShouldBe(ArgumentArity.ExactlyOne);
    }

    [Fact]
    public void Create_MethodArgument_IsRequired()
    {
        // Arrange

        // Act
        var command = InvokeCommandHandler.Create();

        // Assert
        var methodArg = command.Arguments.FirstOrDefault(a => a.Name == "method");

        _ = methodArg.ShouldNotBeNull();
        methodArg.Arity.ShouldBe(ArgumentArity.ExactlyOne);
    }

    [Fact]
    public void Create_HasMultipleOptions()
    {
        // Arrange

        // Act
        var command = InvokeCommandHandler.Create();

        // Assert
        command.Options.Count.ShouldBeGreaterThanOrEqualTo(15);
    }

    [Fact]
    public void Create_HasMaxStdinBytesOption()
    {
        // Arrange

        // Act
        var command = InvokeCommandHandler.Create();

        // Assert
        HasOption(command, "--max-stdin-bytes").ShouldBeTrue();
    }

    [Fact]
    public void ResolveMaxStdinBytes_Null_ReturnsDefault()
    {
        // Arrange

        // Act
        var result = InvokeCommandHandler.ResolveMaxStdinBytes(null);

        // Assert
        result.ShouldBe(InvokeCommandHandler.DefaultMaxStdinBytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1024)]
    public void ResolveMaxStdinBytes_PositiveValue_ReturnsValue(long value)
    {
        // Arrange

        // Act
        var result = InvokeCommandHandler.ResolveMaxStdinBytes(value);

        // Assert
        result.ShouldBe(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ResolveMaxStdinBytes_NonPositiveValue_ThrowsArgumentException(long value)
    {
        // Arrange

        // Act
        var exception = Should.Throw<ArgumentException>(() =>
            InvokeCommandHandler.ResolveMaxStdinBytes(value));

        // Assert
        exception.Message.ShouldContain("--max-stdin-bytes");
    }

    private static bool HasOption(Command command, string name)
        => command.Options.Any(option => option.Name == name || option.Name == name.TrimStart('-'));
}
