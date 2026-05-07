# Building the documentation site

This repo's documentation is generated with [DocFx](https://dotnet.github.io/docfx/). The source lives at `Docs/`; the generated site lands in `Docs/_site/`. The API reference YAML lands in `Docs/api/` (generated from the `Src/*` projects' XML doc comments). Both output directories are gitignored — never commit either.

## Prerequisites

- The current LTS .NET SDK (whatever this repo's `global.json` and `.csproj` files pin).
- DocFx as a global .NET tool.

Install DocFx once:

```bash
dotnet tool install --global docfx
```

Verify:

```bash
docfx --version
```

If the command is not found after install, ensure `~/.dotnet/tools` is on your `PATH`.

## Build sequence

From the repository root:

```bash
# 1. Generate API YAML from the source projects.
docfx metadata Docs/docfx.json

# 2. Build the conceptual site (reads articles + API YAML, writes HTML).
docfx build Docs/docfx.json

# 3. Preview locally.
docfx serve Docs/_site
```

The preview binds `http://localhost:8080`. Open the browser there to navigate the site. Re-run `docfx build` after any content edit; the browser refresh picks up the new HTML on the next request.

## What gets generated

`docfx metadata` writes one YAML per type into `Docs/api/`, plus a `toc.yml`. The `metadata.filter` property in `docfx.json` points at `Docs/filterConfig.yml`, which governs which namespaces appear in the public API reference:

- **Included**: `GrpCurl.Net.DescriptorSources.*`, `GrpCurl.Net.Exceptions.*`, `Gql2Grpc.Configuration.*`, `Gql2Grpc.GraphQL.*`, `Gql2Grpc.Introspection.*`, plus `Gql2Grpc.Response.RootFieldResult`.
- **Excluded**: implementation namespaces (`Gql2Grpc.Commands/Diagnostics/Execution/Translation`, `Gql2Grpc.Response.SelectionProjector`/`GraphQLResponseBuilder`/`StreamingResponseWriter`), `GrpCurl.Net.Invocation.*`, and the auto-generated `Grpc.Reflection.*` types.

`docfx build` reads every `.md` and `.yml` file matching `**/*.{md,yml}` (excluding `_site/**` and `filterConfig.yml`), resolves cross-references, and writes the static HTML site to `Docs/_site/`.

## Hand-authored content inside `Docs/api/`

`Docs/api/index.md` is hand-written (see the file for the API-reference landing page content). `docfx metadata` preserves non-generated files in the destination directory; it only overwrites the YAML and the manifest.

## Expected warnings during development

As you add new articles or cross-references, you may see warnings like `InvalidFileLink: …`. These become errors at publish time, so fix them before committing. The build succeeds with `0 warning(s), 0 error(s)` when every referenced file exists.

## CI / deployment

Suggested pipeline steps:

```yaml
- run: dotnet build GrpCurl.Net.slnx
- run: dotnet tool install --global docfx
- run: docfx metadata Docs/docfx.json
- run: docfx build Docs/docfx.json
- name: Deploy to GitHub Pages
  uses: peaceiris/actions-gh-pages@v4
  with:
    publish_dir: Docs/_site
```

Both `Docs/api/` and `Docs/_site/` must be regenerated in every build — they are gitignored, not cached. The full sequence takes ~10 seconds on a laptop-class CI runner.

## Troubleshooting

### `MissingYamlMime` warnings

Means a `.yml` file is being treated as managed-reference YAML but lacks the DocFx schema directive. Confirm that the file is excluded via the `content.exclude` array in `docfx.json` (for example, `filterConfig.yml` is deliberately excluded).

### `InvalidFileLink` warnings

A Markdown file links to a file that doesn't exist in the build output. Either the target is missing, or the path is wrong. Paths are relative to the Markdown source file. `api/index.md` resolves only if `Docs/api/index.md` is present — confirm with `ls Docs/api/index.md` after `docfx metadata` runs.

### `xref` links resolve to raw YAML paths

`xref:FullyQualified.Type.Name` needs the metadata step to have run. If you're serving `Docs/_site/` from a pipeline that skipped `docfx metadata`, the xref resolver has no data to consult. Add the metadata step before `build`.

### Search doesn't work in preview

Global metadata sets `_enableSearch: true`, which injects a client-side search index. The index is generated during `docfx build` — a plain `docfx serve` of a stale `_site/` won't refresh it. Re-run `docfx build` after content changes.
