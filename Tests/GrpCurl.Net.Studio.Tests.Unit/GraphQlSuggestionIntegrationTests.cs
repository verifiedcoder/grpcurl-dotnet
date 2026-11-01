extern alias CoreReflection;

using Google.Protobuf;
using Google.Protobuf.Reflection;
using Gql2Grpc.Configuration;
using Gql2Grpc.GraphQL;
using Reflection = CoreReflection::Grpc.Reflection.V1Alpha;
using System.Buffers.Binary;
using Microsoft.AspNetCore.Http;
using GrpCurl.Net.DescriptorSources;
using GrpCurl.Net.Studio.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using System.Net;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class GraphQlSuggestionIntegrationTests
{
    private static MappingEntry Entry(string service, string method) => new()
    {
        GraphqlField = "query", OperationType = GraphQLOperationType.Query, Service = service, Method = method
    };

    private static FileDescriptorProto Schema(int serviceLength)
    {
        var message = new DescriptorProto { Name = "Message" };
        foreach (var name in new[] { "payload", new string('f', 300) })
        {
            message.Field.Add(new FieldDescriptorProto { Name = name, Number = message.Field.Count + 1, Type = FieldDescriptorProto.Types.Type.String });
        }
        var file = new FileDescriptorProto { Name = "suggestions.proto", Package = "test", Syntax = "proto3", MessageType = { message } };
        foreach (var name in new[] { "Service", new string('s', serviceLength) })
        {
            var service = new ServiceDescriptorProto { Name = name };
            foreach (var method in new[] { "Unary", new string('m', 300) })
            {
                service.Method.Add(new MethodDescriptorProto { Name = method, InputType = ".test.Message", OutputType = ".test.Message" });
            }
            file.Service.Add(service);
        }
        return file;
    }

    [Fact]
    public async Task Long_symbols_still_resolve_and_all_field_suggestion_paths_are_bounded()
    {
        var descriptor = FileDescriptor.BuildFromByteStrings([Schema(300).ToByteString()]).Single();
        var source = new DescriptorSource(descriptor);
        var field = new string('f', 300);
        var valid = Entry("test." + new string('s', 300), new string('m', 300)) with
        {
            Arguments = new Dictionary<string, ArgumentRule> { ["value"] = new ArgumentRule.PathRule(field) },
            SelectionFieldMaskPath = field, Response = new ResponseShaping { Unwrap = field }
        };
        var ct = TestContext.Current.CancellationToken;
        (await GraphQlService.ValidateEntriesAsync(new MappingConfig { Operations = [valid] }, source, null, ct)).ShouldBeEmpty();
        var invalid = valid with
        {
            Arguments = new Dictionary<string, ArgumentRule> { ["value"] = new ArgumentRule.PathRule(field + "x") },
            SelectionFieldMaskPath = field + "x", Response = new ResponseShaping { Unwrap = field + "x" }
        };
        var errors = await GraphQlService.ValidateEntriesAsync(new MappingConfig { Operations = [invalid] }, source, null, ct);
        errors.Count.ShouldBe(3);
        errors.ShouldAllBe(e => !e.Message.Contains("did you mean"));
        var typo = Entry("test.Service", "Unar");
        (await GraphQlService.ValidateEntriesAsync(new MappingConfig { Operations = [typo] }, source, null, ct))
            .ShouldHaveSingleItem().Message.ShouldContain("did you mean 'Unary'");
        var fieldTypo = valid with
        {
            Arguments = new Dictionary<string, ArgumentRule> { ["value"] = new ArgumentRule.PathRule("payloa") },
            SelectionFieldMaskPath = "payloa", Response = new ResponseShaping { Unwrap = "payloa" }
        };
        (await GraphQlService.ValidateEntriesAsync(new MappingConfig { Operations = [fieldTypo] }, source, null, ct))
            .ShouldAllBe(e => e.Message.Contains("did you mean 'payload'"));
        var methodTypo = valid with { Method = new string('m', 301) };
        (await GraphQlService.ValidateEntriesAsync(new MappingConfig { Operations = [methodTypo] }, source, null, ct))
            .ShouldHaveSingleItem().Message.ShouldNotContain("did you mean");
    }

    [Theory]
    [InlineData("service")]
    [InlineData("method")]
    [InlineData("argument")]
    [InlineData("mask")]
    [InlineData("unwrap")]
    public async Task Validation_passes_cancellation_to_every_suggestion_path(string path)
    {
        using var cts = new CancellationTokenSource();
        var file = FileDescriptor.BuildFromByteStrings([Schema(300).ToByteString()]).Single();
        var source = new DescriptorSource(file, cts.Cancel);
        var entry = Entry("test.Service", "Unary");
        entry = path switch
        {
            "service" => entry with { Service = "test.Servic" },
            "method" => entry with { Method = "Unar" },
            "argument" => entry with { Arguments = new Dictionary<string, ArgumentRule> { ["value"] = new ArgumentRule.PathRule("payloa") } },
            "mask" => entry with { SelectionFieldMaskPath = "payloa" },
            _ => entry with { Response = new ResponseShaping { Unwrap = "payloa" } }
        };
        _ = await Should.ThrowAsync<OperationCanceledException>(() => GraphQlService.ValidateEntriesAsync(new MappingConfig { Operations = [entry] }, source, null, cts.Token));
    }

    [Fact]
    public async Task Reflection_with_a_multi_megabyte_service_name_completes_mapping_validation()
    {
        var schema = Schema(2 * 1024 * 1024);
        var builder = WebApplication.CreateBuilder();
        _ = builder.Logging.SetMinimumLevel(LogLevel.Warning);
        _ = builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0, l => l.Protocols = HttpProtocols.Http2));
        await using var app = builder.Build();
        _ = app.MapPost("/grpc.reflection.v1alpha.ServerReflection/ServerReflectionInfo", async context =>
        {
            var header = new byte[5];
            await context.Request.Body.ReadExactlyAsync(header, context.RequestAborted);
            var bytes = new byte[BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1))];
            await context.Request.Body.ReadExactlyAsync(bytes, context.RequestAborted);
            var request = Reflection.ServerReflectionRequest.Parser.ParseFrom(bytes);
            var response = new Reflection.ServerReflectionResponse { OriginalRequest = request };
            if (request.MessageRequestCase == Reflection.ServerReflectionRequest.MessageRequestOneofCase.ListServices)
            {
                response.ListServicesResponse = new Reflection.ListServiceResponse();
                response.ListServicesResponse.Service.Add(schema.Service.Select(s => new Reflection.ServiceResponse { Name = "test." + s.Name }));
            }
            else if (schema.Service.Any(s => "test." + s.Name == request.FileContainingSymbol))
            {
                response.FileDescriptorResponse = new Reflection.FileDescriptorResponse();
                response.FileDescriptorResponse.FileDescriptorProto.Add(schema.ToByteString());
            }
            else { response.ErrorResponse = new Reflection.ErrorResponse { ErrorCode = 5, ErrorMessage = "symbol not found" }; }
            context.Response.ContentType = "application/grpc";
            context.Response.DeclareTrailer("grpc-status");
            var payload = response.ToByteArray();
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1), payload.Length);
            await context.Response.Body.WriteAsync(header, context.RequestAborted);
            await context.Response.Body.WriteAsync(payload, context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted);
            context.Response.AppendTrailer("grpc-status", "0");
        });
        await app.StartAsync();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var source = ReflectionSource.Create(app.Urls.Single());
            var errors = await GraphQlService.ValidateEntriesAsync(new MappingConfig { Operations = [Entry("test.Servic", "Unary")] }, source, null, timeout.Token);
            errors.ShouldHaveSingleItem().Message.ShouldContain("did you mean 'test.Service'");
            (await source.ListServicesAsync(timeout.Token)).ShouldContain("test." + schema.Service[1].Name);
        }
        finally { await app.StopAsync(); }
    }

    private sealed class DescriptorSource(FileDescriptor file, Action? onLookup = null) : IDescriptorSource
    {
        public FileDescriptorSet? FileDescriptorSet => null;
        public Task<IReadOnlyList<string>> ListServicesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(file.Services.Select(s => s.FullName).ToArray());
        public Task<IDescriptor?> FindSymbolAsync(string fullyQualifiedName, CancellationToken cancellationToken = default)
        {
            onLookup?.Invoke();
            return Task.FromResult<IDescriptor?>(file.Services.FirstOrDefault(s => s.FullName == fullyQualifiedName));
        }
    }

}
