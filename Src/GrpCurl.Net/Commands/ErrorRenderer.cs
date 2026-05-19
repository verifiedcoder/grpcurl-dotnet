using GrpCurl.Net.Exceptions;
using GrpCurl.Net.Utilities;
using Spectre.Console;
using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrpCurl.Net.Commands;

/// <summary>
///     Renders an <see cref="ErrorEnvelope" /> to stderr. Text mode emits Spectre.Console
///     markup (matching the previous catch-block UX); JSON mode emits a single-line JSON
///     envelope suitable for agent consumption.
/// </summary>
internal static class ErrorRenderer
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public static void Render(ErrorEnvelope envelope, OutputFormat format, TextWriter? writer = null)
    {
        var w = writer ?? Console.Error;

        if (format == OutputFormat.Json)
        {
            RenderJson(envelope, w);

            return;
        }

        RenderText(envelope, w);
    }

    /// <summary>
    ///     Renders the envelope and throws a silent <see cref="GrpcCommandException" />
    ///     carrying it. The outer <c>SetAction</c> will return the envelope's exit code
    ///     without further rendering.
    /// </summary>
    [DoesNotReturn]
    public static void RenderAndThrow(ErrorEnvelope envelope, OutputFormat format, TextWriter? writer = null)
    {
        Render(envelope, format, writer);

        throw new GrpcCommandException(envelope.Message, envelope.ExitCode, true)
        {
            Envelope = envelope
        };
    }

    private static void RenderJson(ErrorEnvelope envelope, TextWriter writer)
    {
        var payload = new
        {
            kind = "error",
            category = envelope.Category,
            exitCode = envelope.ExitCode,
            message = envelope.Message,
            hint = envelope.Hint,
            address = envelope.Address,
            method = envelope.Method,
            grpc = envelope.Grpc
        };

        writer.WriteLine(JsonSerializer.Serialize(payload, CompactJsonOptions));
    }

    private static void RenderText(ErrorEnvelope envelope, TextWriter writer)
    {
        var headline = HeadlineFor(envelope);
        var console = writer == Console.Error
            ? Diagnostics.Stderr
            : AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });

        var escapedMessage = Markup.Escape(envelope.Message);

        console.MarkupLine($"[red]{headline}:[/] {escapedMessage}");

        if (envelope is { Grpc: not null, Category: ErrorCategory.Rpc } &&
            envelope.Grpc.Status != "DeadlineExceeded")
        {
            console.MarkupLine($"[red]Status Code:[/] {Markup.Escape(envelope.Grpc.Status)}");
        }

        if (envelope.Suggestions.Count > 0)
        {
            console.MarkupLine(CommandConstants.Suggestions);

            foreach (var suggestion in envelope.Suggestions)
            {
                console.MarkupLine($"[dim]  - {Markup.Escape(suggestion)}[/]");
            }
        }

        if (envelope.Hint is not null)
        {
            console.MarkupLine($"[dim]{Markup.Escape(envelope.Hint)}[/]");
        }
    }

    private static string HeadlineFor(ErrorEnvelope envelope)
        => envelope.Category switch
        {
            ErrorCategory.Usage                                                => "Error",
            ErrorCategory.Schema                                               => "Error",
            ErrorCategory.Network                                              => "Connection Error",
            ErrorCategory.Timeout                                              => "Timeout Error",
            ErrorCategory.Rpc when envelope.Grpc?.Status == "DeadlineExceeded" => "Deadline Exceeded",
            ErrorCategory.Rpc                                                  => "RPC Error",
            ErrorCategory.Cancelled                                            => "Cancelled",
            _                                                                  => "Error"
        };
}