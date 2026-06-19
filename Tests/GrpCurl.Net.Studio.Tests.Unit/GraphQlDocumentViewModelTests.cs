using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class GraphQlDocumentViewModelTests
{
    private static SavedConnection Conn() => new() { Name = "c", Address = "h:1" };

    private static GraphQlDocumentViewModel Create(out FakeGraphQlService graphql, out FakeClipboardService clipboard)
    {
        graphql = new FakeGraphQlService();
        clipboard = new FakeClipboardService();
        return new GraphQlDocumentViewModel(Conn(), graphql, new ImmediateUiDispatcher(), clipboard)
        {
            ParseDebounce = TimeSpan.Zero
        };
    }

    private static GraphQlDocumentViewModel CreateWithRecorder(out FakeGraphQlService graphql, out FakeHistoryRecorder recorder)
    {
        graphql = new FakeGraphQlService { ParseResult = OneQuery() };
        recorder = new FakeHistoryRecorder();
        return new GraphQlDocumentViewModel(Conn(), graphql, new ImmediateUiDispatcher(), new FakeClipboardService(), recorder)
        {
            ParseDebounce = TimeSpan.Zero
        };
    }

    private static GraphQlParseResult OneQuery(string name = "Q")
        => new([new GraphQlOperationInfo(name, GraphQlOperationKind.Query)], []);

    [Fact]
    public void A_single_operation_auto_selects_and_enables_execute()
    {
        var vm = Create(out _, out _);

        vm.ApplyParse(OneQuery());

        _ = vm.SelectedOperation.ShouldNotBeNull();
        vm.SelectedOperation!.Name.ShouldBe("Q");
        vm.ExecuteCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void A_syntax_error_blocks_execute()
    {
        var vm = Create(out _, out _);

        vm.ApplyParse(new GraphQlParseResult([], [new GraphQlProblem("unexpected '}'", GraphQlProblemKind.Syntax)]));

        vm.HasSyntaxError.ShouldBeTrue();
        vm.HasProblems.ShouldBeTrue();
        vm.ExecuteCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Multiple_operations_require_an_explicit_selection()
    {
        var vm = Create(out _, out _);

        vm.ApplyParse(new GraphQlParseResult(
            [new GraphQlOperationInfo("A", GraphQlOperationKind.Query), new GraphQlOperationInfo("B", GraphQlOperationKind.Mutation)],
            []));

        vm.SelectedOperation.ShouldBeNull();
        vm.ExecuteCommand.CanExecute(null).ShouldBeFalse();

        vm.SelectedOperation = vm.Operations[1];

        vm.ExecuteCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task Execute_sends_the_current_state_and_renders_the_envelope()
    {
        var vm = Create(out var graphql, out _);
        graphql.ParseResult = OneQuery();
        graphql.ExecuteResult = new(Ok: true, EnvelopeJson: "{\n  \"data\": { \"x\": 1 }\n}", ConfigurationErrors: []);

        vm.Document = "query Q { x }";
        vm.ApplyParse(graphql.ParseResult);
        vm.VariablesJson = "{\"v\":1}";
        vm.DefaultService = "pkg.Service";
        vm.EmitDefaults = true;

        await vm.ExecuteCommand.ExecuteAsync(null);

        graphql.ExecuteCount.ShouldBe(1);
        graphql.LastRequest!.Document.ShouldBe("query Q { x }");
        graphql.LastRequest.OperationName.ShouldBe("Q");
        graphql.LastRequest.VariablesJson.ShouldBe("{\"v\":1}");
        graphql.LastRequest.DefaultService.ShouldBe("pkg.Service");
        graphql.LastRequest.EmitDefaults.ShouldBeTrue();

        vm.ResponseJson.ShouldBe("{\n  \"data\": { \"x\": 1 }\n}");
        vm.State.ShouldBe(RunState.Completed);
        vm.HasResponse.ShouldBeTrue();
    }

    [Fact]
    public async Task A_configuration_error_surfaces_in_problems_and_makes_no_response()
    {
        var vm = Create(out var graphql, out _);
        graphql.ParseResult = OneQuery();
        graphql.ExecuteResult = new(Ok: false, EnvelopeJson: null,
            [new GraphQlProblem("variable $big is not a valid Int", GraphQlProblemKind.Variables)]);

        vm.Document = "query Q($big: Int) { x }";
        vm.ApplyParse(graphql.ParseResult);

        await vm.ExecuteCommand.ExecuteAsync(null);

        vm.ResponseJson.ShouldBeNull();
        vm.State.ShouldBe(RunState.Failed);
        vm.Problems.ShouldContain(p => p.Kind == GraphQlProblemKind.Variables);
    }

    [Fact]
    public async Task Cancellation_surfaces_as_a_cancelled_state()
    {
        var vm = Create(out var graphql, out _);
        graphql.ParseResult = OneQuery();
        graphql.OnExecute = (_, _, _) => throw new OperationCanceledException();

        vm.Document = "query Q { x }";
        vm.ApplyParse(graphql.ParseResult);

        await vm.ExecuteCommand.ExecuteAsync(null);

        vm.State.ShouldBe(RunState.Cancelled);
        vm.IsCancelled.ShouldBeTrue();
    }

    [Fact]
    public async Task Copy_response_puts_the_envelope_on_the_clipboard()
    {
        var vm = Create(out var graphql, out var clipboard);
        graphql.ParseResult = OneQuery();
        graphql.ExecuteResult = new(Ok: true, EnvelopeJson: "{ \"data\": {} }", ConfigurationErrors: []);

        vm.ApplyParse(graphql.ParseResult);
        await vm.ExecuteCommand.ExecuteAsync(null);
        await vm.CopyResponseCommand.ExecuteAsync(null);

        clipboard.Text.ShouldBe("{ \"data\": {} }");
    }

    [Fact]
    public async Task The_raw_toggle_flows_into_the_request()
    {
        var vm = Create(out var graphql, out _);
        graphql.ParseResult = OneQuery();
        vm.ApplyParse(graphql.ParseResult);
        vm.Raw = true;

        await vm.ExecuteCommand.ExecuteAsync(null);

        graphql.LastRequest!.Raw.ShouldBeTrue();
    }

    [Fact]
    public async Task Per_field_progress_rows_track_each_root_field_in_document_order()
    {
        var vm = Create(out var graphql, out _);
        graphql.ParseResult = OneQuery();
        graphql.ProgressEvents =
        [
            new GraphQlFieldProgress(0, "a", GraphQlFieldState.Queued),
            new GraphQlFieldProgress(1, "b", GraphQlFieldState.Queued),
            new GraphQlFieldProgress(0, "a", GraphQlFieldState.InFlight),
            new GraphQlFieldProgress(0, "a", GraphQlFieldState.Done, 5),
            new GraphQlFieldProgress(1, "b", GraphQlFieldState.InFlight),
            new GraphQlFieldProgress(1, "b", GraphQlFieldState.Failed, 7)
        ];
        vm.ApplyParse(graphql.ParseResult);

        await vm.ExecuteCommand.ExecuteAsync(null);

        vm.HasFieldProgress.ShouldBeTrue();
        vm.FieldProgress.Count.ShouldBe(2);

        vm.FieldProgress[0].ResponseKey.ShouldBe("a");
        vm.FieldProgress[0].State.ShouldBe(GraphQlFieldState.Done);
        vm.FieldProgress[0].ElapsedText.ShouldBe("5 ms");

        vm.FieldProgress[1].State.ShouldBe(GraphQlFieldState.Failed);
        vm.FieldProgress[1].ElapsedText.ShouldBe("7 ms");
    }

    [Fact]
    public async Task A_second_run_resets_the_progress_rows()
    {
        var vm = Create(out var graphql, out _);
        graphql.ParseResult = OneQuery();
        graphql.ProgressEvents = [new GraphQlFieldProgress(0, "a", GraphQlFieldState.Done, 1)];
        vm.ApplyParse(graphql.ParseResult);

        await vm.ExecuteCommand.ExecuteAsync(null);
        vm.FieldProgress.Count.ShouldBe(1);

        await vm.ExecuteCommand.ExecuteAsync(null);
        vm.FieldProgress.Count.ShouldBe(1); // cleared then repopulated, not appended
    }

    [Fact]
    public async Task A_successful_execution_is_recorded_to_history()
    {
        var vm = CreateWithRecorder(out var graphql, out var recorder);
        graphql.ExecuteResult = new(Ok: true, EnvelopeJson: "{ \"data\": {} }", ConfigurationErrors: []);
        vm.Document = "query Q { x }";
        vm.ApplyParse(graphql.ParseResult);

        await vm.ExecuteCommand.ExecuteAsync(null);

        var record = recorder.LastGraphQl.ShouldNotBeNull();
        record.Ok.ShouldBeTrue();
        record.Category.ShouldBe("success");
        record.OperationLabel.ShouldBe("Q");
        record.Document.ShouldBe("query Q { x }");
        record.ResponseEnvelope.ShouldBe("{ \"data\": {} }");
    }

    [Fact]
    public async Task A_configuration_error_is_recorded_as_a_configuration_outcome()
    {
        var vm = CreateWithRecorder(out var graphql, out var recorder);
        graphql.ExecuteResult = new(Ok: false, EnvelopeJson: null,
            [new GraphQlProblem("variable $big is invalid", GraphQlProblemKind.Variables)]);
        vm.Document = "query Q($big: Int) { x }";
        vm.ApplyParse(graphql.ParseResult);

        await vm.ExecuteCommand.ExecuteAsync(null);

        var record = recorder.LastGraphQl.ShouldNotBeNull();
        record.Ok.ShouldBeFalse();
        record.Category.ShouldBe("configuration");
        record.ErrorMessage.ShouldBe("variable $big is invalid");
        record.ResponseEnvelope.ShouldBeNull();
    }

    [Fact]
    public async Task A_cancelled_execution_is_recorded_as_cancelled()
    {
        var vm = CreateWithRecorder(out var graphql, out var recorder);
        graphql.OnExecute = (_, _, _) => throw new OperationCanceledException();
        vm.Document = "query Q { x }";
        vm.ApplyParse(graphql.ParseResult);

        await vm.ExecuteCommand.ExecuteAsync(null);

        var record = recorder.LastGraphQl.ShouldNotBeNull();
        record.Category.ShouldBe("cancelled");
        record.Ok.ShouldBeFalse();
    }

    [Fact]
    public async Task Open_document_loads_a_graphql_file()
    {
        var picker = new FakeFilePickerService { OpenResult = "/q.graphql" };
        var vm = new GraphQlDocumentViewModel(
            Conn(), new FakeGraphQlService { ParseResult = OneQuery() }, new ImmediateUiDispatcher(), new FakeClipboardService(),
            filePicker: picker, fileReader: (_, _, _) => Task.FromResult("query Loaded { x }"))
        {
            ParseDebounce = TimeSpan.Zero
        };

        await vm.OpenDocumentCommand.ExecuteAsync(null);

        vm.Document.ShouldBe("query Loaded { x }");
    }

    [Fact]
    public async Task Save_document_writes_the_current_document()
    {
        StringWriter? captured = null;
        var picker = new FakeFilePickerService { SaveResult = "/out.graphql" };
        var vm = new GraphQlDocumentViewModel(
            Conn(), new FakeGraphQlService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
            filePicker: picker, writerFactory: _ => captured = new StringWriter())
        {
            ParseDebounce = TimeSpan.Zero,
            Document = "query Save { x }"
        };

        await vm.SaveDocumentCommand.ExecuteAsync(null);

        _ = captured.ShouldNotBeNull();
        captured!.GetStringBuilder().ToString().ShouldBe("query Save { x }");
        picker.LastSaveSuggestedName.ShouldBe("operation.graphql");
    }

    [Fact]
    public async Task Import_variables_loads_a_json_file()
    {
        var picker = new FakeFilePickerService { OpenResult = "/v.json" };
        var vm = new GraphQlDocumentViewModel(
            Conn(), new FakeGraphQlService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
            filePicker: picker, fileReader: (_, _, _) => Task.FromResult("{ \"a\": 1 }"))
        {
            ParseDebounce = TimeSpan.Zero
        };

        await vm.ImportVariablesCommand.ExecuteAsync(null);

        vm.VariablesJson.ShouldBe("{ \"a\": 1 }");
    }

    [Fact]
    public async Task An_unreadable_or_oversize_file_surfaces_a_configuration_problem()
    {
        var picker = new FakeFilePickerService { OpenResult = "/big.graphql" };
        var vm = new GraphQlDocumentViewModel(
            Conn(), new FakeGraphQlService(), new ImmediateUiDispatcher(), new FakeClipboardService(),
            filePicker: picker, fileReader: (_, _, _) => throw new InvalidOperationException("File exceeds the 4 MiB limit."))
        {
            ParseDebounce = TimeSpan.Zero
        };

        await vm.OpenDocumentCommand.ExecuteAsync(null);

        vm.Document.ShouldBeEmpty();
        vm.Problems.ShouldContain(p => p.Kind == GraphQlProblemKind.Configuration);
    }

    [Fact]
    public void File_commands_are_disabled_without_a_picker()
    {
        var vm = Create(out _, out _);

        vm.OpenDocumentCommand.CanExecute(null).ShouldBeFalse();
        vm.SaveDocumentCommand.CanExecute(null).ShouldBeFalse();
        vm.ImportVariablesCommand.CanExecute(null).ShouldBeFalse();
    }

    private static GraphQlParseResult QueryWithVars()
        => new([
            new GraphQlOperationInfo("Q", GraphQlOperationKind.Query)
            {
                Variables = [new GraphQlVariableInfo("big", "Int!", Required: true), new GraphQlVariableInfo("name", "String", Required: false)]
            }
        ], []);

    [Fact]
    public void Selecting_an_operation_builds_the_quick_var_rows()
    {
        var vm = Create(out _, out _);

        vm.ApplyParse(QueryWithVars());

        vm.HasVariableRows.ShouldBeTrue();
        vm.VariableRows.Select(r => r.Name).ShouldBe(["big", "name"]);
        vm.VariableRows[0].Required.ShouldBeTrue();
        vm.VariableRows[0].Type.ShouldBe("Int!");
    }

    [Fact]
    public void Setting_variables_json_populates_grid_values()
    {
        var vm = Create(out _, out _);
        vm.ApplyParse(QueryWithVars());

        vm.VariablesJson = "{ \"big\": 5, \"name\": \"hi\" }";

        vm.VariableRows.Single(r => r.Name == "big").Value.ShouldBe("5");
        vm.VariableRows.Single(r => r.Name == "name").Value.ShouldBe("\"hi\"");
    }

    [Fact]
    public void Editing_a_grid_value_rebuilds_the_variables_json()
    {
        var vm = Create(out _, out _);
        vm.ApplyParse(QueryWithVars());

        vm.VariableRows.Single(r => r.Name == "big").Value = "42";

        var json = System.Text.Json.Nodes.JsonNode.Parse(vm.VariablesJson)!.AsObject();
        json["big"]!.GetValue<int>().ShouldBe(42);
    }

    [Fact]
    public void A_required_unbound_variable_is_warned()
    {
        var vm = Create(out _, out _);

        vm.ApplyParse(QueryWithVars()); // big is required and unbound

        vm.HasVariableWarnings.ShouldBeTrue();
        vm.VariableWarnings.ShouldContain(p => p.Message.Contains("$big") && p.Message.Contains("required"));
    }

    [Fact]
    public void An_undeclared_bound_variable_is_warned()
    {
        var vm = Create(out _, out _);
        vm.ApplyParse(QueryWithVars());

        vm.VariablesJson = "{ \"big\": 1, \"name\": \"x\", \"extra\": true }";

        vm.VariableWarnings.ShouldContain(p => p.Message.Contains("$extra") && p.Message.Contains("not declared"));
    }

    [Fact]
    public async Task Copy_as_cli_copies_a_gql2grpc_command_for_the_tab()
    {
        var vm = Create(out var graphql, out var clipboard);
        graphql.ParseResult = OneQuery();
        vm.Document = "query Q { x }";
        vm.ApplyParse(OneQuery());

        await vm.CopyAsCliCommand.ExecuteAsync(null);

        _ = clipboard.Text.ShouldNotBeNull();
        clipboard.Text!.ShouldStartWith("gql2grpc ");
        clipboard.Text.ShouldContain("query Q { x }");
        clipboard.Text.ShouldContain("--operation Q");
    }

    [Fact]
    public async Task The_verbose_log_is_populated_and_the_verbosity_flows_into_the_request()
    {
        var vm = Create(out var graphql, out _);
        graphql.ParseResult = OneQuery();
        graphql.ExecuteResult = new(Ok: true, EnvelopeJson: "{ \"data\": {} }", ConfigurationErrors: [])
        {
            VerboseLog = ["[x] → testing.TestService/UnaryCall"]
        };
        vm.ApplyParse(OneQuery());
        vm.Verbosity = GraphQlVerbosity.VeryVerbose;

        await vm.ExecuteCommand.ExecuteAsync(null);

        graphql.LastRequest!.Verbosity.ShouldBe(GraphQlVerbosity.VeryVerbose);
        vm.HasVerboseLog.ShouldBeTrue();
        vm.VerboseLog.ShouldContain("[x] → testing.TestService/UnaryCall");
    }

    [Fact]
    public async Task Response_errors_are_surfaced_as_structured_entries()
    {
        var vm = Create(out var graphql, out _);
        graphql.ParseResult = OneQuery();
        graphql.ExecuteResult = new(Ok: false, EnvelopeJson: "{ \"data\": null }", ConfigurationErrors: [])
        {
            Errors = [new GraphQlErrorInfo("boom", ["dashboard"], "UPSTREAM_ERROR", "InvalidArgument", 3, GraphQlErrorClass.Upstream)]
        };
        vm.ApplyParse(OneQuery());

        await vm.ExecuteCommand.ExecuteAsync(null);

        vm.HasErrors.ShouldBeTrue();
        vm.Errors.ShouldContain(e => e.IsUpstream && e.Message == "boom" && e.PathText == "dashboard");
        vm.State.ShouldBe(RunState.Failed);
    }

    [Fact]
    public async Task Load_schema_populates_the_type_tree_and_copy_works()
    {
        var vm = Create(out var graphql, out var clipboard);
        graphql.SchemaResult = new(
            Ok: true, "MyApi",
            [new GraphQlSchemaType("User", "OBJECT", [new GraphQlSchemaMember("id", "String!")])],
            "{ \"__schema\": {} }", Error: null);

        await vm.LoadSchemaCommand.ExecuteAsync(null);

        vm.HasSchema.ShouldBeTrue();
        vm.SchemaName.ShouldBe("MyApi");
        vm.SchemaTypes.ShouldContain(t => t.Name == "User");

        await vm.CopySchemaJsonCommand.ExecuteAsync(null);
        clipboard.Text.ShouldBe("{ \"__schema\": {} }");
    }

    [Fact]
    public async Task A_schema_load_failure_surfaces_a_problem()
    {
        var vm = Create(out var graphql, out _);
        graphql.SchemaResult = new(Ok: false, "Schema", [], null,
            new GraphQlProblem("reflection failed", GraphQlProblemKind.Configuration));

        await vm.LoadSchemaCommand.ExecuteAsync(null);

        vm.HasSchema.ShouldBeFalse();
        vm.Problems.ShouldContain(p => p.Message == "reflection failed");
    }

    private static GraphQlParseResult OneSubscription(int rootFields = 1)
        => new([new GraphQlOperationInfo("S", GraphQlOperationKind.Subscription) { RootFieldCount = rootFields }], []);

    [Fact]
    public async Task A_subscription_streams_envelopes_into_the_console()
    {
        var vm = Create(out var graphql, out _);
        graphql.ParseResult = OneSubscription();
        graphql.StreamEnvelopes = ["{ \"data\": { \"a\": 1 } }", "{ \"data\": { \"a\": 2 } }"];
        vm.ApplyParse(OneSubscription());

        vm.IsSubscription.ShouldBeTrue();
        await vm.ExecuteCommand.ExecuteAsync(null);

        graphql.StreamCount.ShouldBe(1);
        vm.StreamLog.TotalReceived.ShouldBe(2);
        vm.StreamLog.Rows.Count.ShouldBe(2);
        vm.State.ShouldBe(RunState.Completed);
    }

    [Fact]
    public async Task A_multi_root_subscription_is_blocked_before_streaming()
    {
        var vm = Create(out var graphql, out _);
        graphql.ParseResult = OneSubscription(rootFields: 2);
        vm.ApplyParse(OneSubscription(rootFields: 2));

        await vm.ExecuteCommand.ExecuteAsync(null);

        graphql.StreamCount.ShouldBe(0); // never reached the engine (GQL-064 pre-flight)
        vm.State.ShouldBe(RunState.Failed);
        vm.Problems.ShouldContain(p => p.Kind == GraphQlProblemKind.Configuration && p.Message.Contains("one root field"));
    }

    [Fact]
    public async Task Cancelling_a_subscription_preserves_received_envelopes()
    {
        var vm = Create(out var graphql, out _);
        graphql.ParseResult = OneSubscription();
        graphql.OnStream = (_, _) => YieldThenCancel(2);
        vm.ApplyParse(OneSubscription());

        await vm.ExecuteCommand.ExecuteAsync(null);

        vm.StreamLog.TotalReceived.ShouldBe(2); // AC-3: nothing lost
        vm.State.ShouldBe(RunState.Cancelled);
        vm.StreamLog.Rows[^1].IsStatus.ShouldBeTrue();
        vm.StreamLog.Rows[^1].Json.ShouldContain("Cancelled after 2");
    }

    private static async IAsyncEnumerable<string> YieldThenCancel(int n)
    {
        for (var i = 0; i < n; i++)
        {
            yield return $"{{ \"tick\": {i} }}";
            await Task.Yield();
        }

        throw new OperationCanceledException();
    }

    [Fact]
    public async Task Export_stream_writes_message_envelopes_as_ndjson()
    {
        StringWriter? captured = null;
        var picker = new FakeFilePickerService { SaveResult = "/s.ndjson" };
        var graphql = new FakeGraphQlService { ParseResult = OneSubscription(), StreamEnvelopes = ["{\"a\":1}", "{\"a\":2}"] };
        var vm = new GraphQlDocumentViewModel(
            Conn(), graphql, new ImmediateUiDispatcher(), new FakeClipboardService(),
            filePicker: picker, writerFactory: _ => captured = new StringWriter())
        {
            ParseDebounce = TimeSpan.Zero
        };
        vm.ApplyParse(OneSubscription());
        await vm.ExecuteCommand.ExecuteAsync(null);

        await vm.ExportStreamCommand.ExecuteAsync(null);

        _ = captured.ShouldNotBeNull();
        captured!.GetStringBuilder().ToString().ShouldBe("{\"a\":1}" + Environment.NewLine + "{\"a\":2}" + Environment.NewLine);
    }

    [Fact]
    public async Task The_inline_mapping_flows_into_the_request()
    {
        var vm = Create(out var graphql, out _);
        graphql.ParseResult = OneQuery();
        vm.ApplyParse(OneQuery());
        vm.MappingText = "version: 1";

        await vm.ExecuteCommand.ExecuteAsync(null);

        graphql.LastRequest!.MappingText.ShouldBe("version: 1");
    }

    [Fact]
    public void Apply_mapping_problems_surfaces_validation()
    {
        var vm = Create(out _, out _);

        vm.ApplyMappingProblems([new GraphQlProblem("duplicate operation entry", GraphQlProblemKind.Configuration)]);

        vm.HasMappingProblems.ShouldBeTrue();
        vm.MappingProblems.ShouldContain(p => p.Message == "duplicate operation entry");
    }

    [Fact]
    public async Task Load_translation_populates_the_inspector_and_flags_dropped_arguments()
    {
        var vm = Create(out var graphql, out _);
        graphql.TranslationResult = new(
            [new GraphQlFieldTranslation("unaryCall", "testing.TestService/UnaryCall", "{ \"x\": 1 }", ["noSuchField"], Error: null)]);

        await vm.LoadTranslationCommand.ExecuteAsync(null);

        vm.HasTranslation.ShouldBeTrue();
        vm.TranslationFields.ShouldContain(f => f.FieldName == "unaryCall" && f.HasRequestJson);
        vm.Problems.ShouldContain(p => p.Kind == GraphQlProblemKind.Configuration && p.Message.Contains("noSuchField"));
    }

    [Fact]
    public async Task Copy_request_json_copies_the_field_json()
    {
        var vm = Create(out _, out var clipboard);

        await vm.CopyRequestJsonCommand.ExecuteAsync(new GraphQlFieldTranslation("f", "pkg.S/M", "{ \"x\": 1 }", [], Error: null));

        clipboard.Text.ShouldBe("{ \"x\": 1 }");
    }

    [Fact]
    public void Open_as_invocation_routes_to_the_host_with_the_method_and_json()
    {
        var host = new FakeDocumentHost();
        var vm = new GraphQlDocumentViewModel(
            Conn(), new FakeGraphQlService(), new ImmediateUiDispatcher(), new FakeClipboardService(), documentHost: host)
        {
            ParseDebounce = TimeSpan.Zero
        };

        vm.OpenAsInvocationCommand.Execute(new GraphQlFieldTranslation("f", "testing.TestService/UnaryCall", "{ \"x\": 1 }", [], Error: null));

        var invocation = host.LastInvocation.ShouldNotBeNull();
        invocation.Symbol.ShouldBe("testing.TestService/UnaryCall");
        invocation.InitialJson.ShouldBe("{ \"x\": 1 }");
    }

    [Fact]
    public void Apply_resolution_populates_the_preview_and_override_note()
    {
        var vm = Create(out _, out _);

        vm.ApplyResolution(new GraphQlResolutionResult(
            [
                new GraphQlFieldResolution("unaryCall", Resolved: true, "testing.TestService", "UnaryCall", "unary",
                    GraphQlResolutionSource.Convention, "unaryCall → UnaryCall on testing.TestService", Error: null)
            ],
            DefaultServiceOverridden: true, OverriddenService: "tab.Service"));

        vm.HasResolutions.ShouldBeTrue();
        vm.Resolutions.ShouldContain(f => f.FieldName == "unaryCall" && f.IsConvention);
        vm.HasDefaultServiceOverride.ShouldBeTrue();
        vm.DefaultServiceOverride.ShouldBe("tab.Service");
    }

    [Fact]
    public void Re_parsing_keeps_the_prior_selection_when_the_operation_still_exists()
    {
        var vm = Create(out _, out _);
        vm.ApplyParse(new GraphQlParseResult(
            [new GraphQlOperationInfo("A", GraphQlOperationKind.Query), new GraphQlOperationInfo("B", GraphQlOperationKind.Query)],
            []));
        vm.SelectedOperation = vm.Operations[1]; // B

        vm.ApplyParse(new GraphQlParseResult(
            [new GraphQlOperationInfo("A", GraphQlOperationKind.Query), new GraphQlOperationInfo("B", GraphQlOperationKind.Query)],
            []));

        vm.SelectedOperation!.Name.ShouldBe("B");
    }
}
