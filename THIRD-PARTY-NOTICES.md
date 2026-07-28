# Third-party notices

GrpCurl.Net Studio, `grpcn` and `gql2grpc` are published as **self-contained** builds: each release
archive bundles the .NET runtime and the third-party libraries listed below alongside the product
code. The product itself is MIT licensed — see `LICENSE`, which ships in the same archive.

This file is generated from the committed `packages.lock.json` files of the five projects that ship
(`GrpCurl.Net`, `Gql2Grpc`, `GrpCurl.Net.Core`, `GrpCurl.Net.Studio`, `GrpCurl.Net.Studio.ViewModels`)
by `Scripts/package/generate-third-party-notices.sh`. **Do not edit it by hand** — CI regenerates it
and fails the build if it has drifted from the lock files. Build-time-only dependencies (analyzers,
code generators, test frameworks) are excluded: they are never distributed.

## .NET runtime and libraries

Portions of this software are distributed with the **.NET runtime and shared framework**
(`Microsoft.NETCore.App`, `Microsoft.WindowsDesktop.App` and the matching runtime packs), which the
self-contained publish embeds in every archive.

- Publisher: Microsoft Corporation and the .NET Foundation
- Project: <https://github.com/dotnet/runtime>
- Licence: MIT (<https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>)

The exact runtime-pack versions are resolved from the SDK pinned in `global.json` at publish time
and are recorded per artifact in the release's CycloneDX SBOM (`*.cdx.json`).

## NuGet packages

### Avalonia 11.3.17

- Authors: Avalonia Team
- Project: <https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link>
- Licence: MIT
- Copyright: Copyright 2013-2026 © The AvaloniaUI Project

### Avalonia.Angle.Windows.Natives 2.1.25547.20250602

- Authors: Avalonia Team
- Project: <https://avaloniaui.net/>
- Licence: see the licence text below (LICENSE)
- Copyright: Copyright 2013-2025 © The AvaloniaUI Project

```text
// Copyright 2018 The ANGLE Project Authors.
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions
// are met:
//
//     Redistributions of source code must retain the above copyright
//     notice, this list of conditions and the following disclaimer.
//
//     Redistributions in binary form must reproduce the above
//     copyright notice, this list of conditions and the following
//     disclaimer in the documentation and/or other materials provided
//     with the distribution.
//
//     Neither the name of TransGaming Inc., Google Inc., 3DLabs Inc.
//     Ltd., nor the names of their contributors may be used to endorse
//     or promote products derived from this software without specific
//     prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
// FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
// COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
// INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
// BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
// LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN
// ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
// POSSIBILITY OF SUCH DAMAGE.
```

### Avalonia.AvaloniaEdit 11.3.0

- Authors: Avalonia Team
- Licence: MIT
- Copyright: Copyright 2017-2025 © The AvaloniaUI Project

### Avalonia.Controls.ColorPicker 11.3.17

- Authors: Avalonia Team
- Project: <https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link>
- Licence: MIT
- Copyright: Copyright 2013-2026 © The AvaloniaUI Project

### Avalonia.Desktop 11.3.17

- Authors: Avalonia Team
- Project: <https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link>
- Licence: MIT
- Copyright: Copyright 2013-2026 © The AvaloniaUI Project

### Avalonia.Diagnostics 11.3.17

- Authors: Avalonia Team
- Project: <https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link>
- Licence: MIT
- Copyright: Copyright 2013-2026 © The AvaloniaUI Project

### Avalonia.Fonts.Inter 11.3.17

- Authors: Avalonia Team
- Project: <https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link>
- Licence: MIT
- Copyright: Copyright 2013-2026 © The AvaloniaUI Project

### Avalonia.FreeDesktop 11.3.17

- Authors: Avalonia Team
- Project: <https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link>
- Licence: MIT
- Copyright: Copyright 2013-2026 © The AvaloniaUI Project

### Avalonia.Native 11.3.17

- Authors: Avalonia Team
- Project: <https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link>
- Licence: MIT
- Copyright: Copyright 2013-2026 © The AvaloniaUI Project

### Avalonia.Remote.Protocol 11.3.17

- Authors: Avalonia Team
- Project: <https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link>
- Licence: MIT
- Copyright: Copyright 2013-2026 © The AvaloniaUI Project

### Avalonia.Skia 11.3.17

- Authors: Avalonia Team
- Project: <https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link>
- Licence: MIT
- Copyright: Copyright 2013-2026 © The AvaloniaUI Project

### Avalonia.Themes.Fluent 11.3.17

- Authors: Avalonia Team
- Project: <https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link>
- Licence: MIT
- Copyright: Copyright 2013-2026 © The AvaloniaUI Project

### Avalonia.Themes.Simple 11.3.17

- Authors: Avalonia Team
- Project: <https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link>
- Licence: MIT
- Copyright: Copyright 2013-2026 © The AvaloniaUI Project

### Avalonia.Win32 11.3.17

- Authors: Avalonia Team
- Project: <https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link>
- Licence: MIT
- Copyright: Copyright 2013-2026 © The AvaloniaUI Project

### Avalonia.X11 11.3.17

- Authors: Avalonia Team
- Project: <https://avaloniaui.net/?utm_source=nuget&utm_medium=referral&utm_content=project_homepage_link>
- Licence: MIT
- Copyright: Copyright 2013-2026 © The AvaloniaUI Project

### AvaloniaEdit.TextMate 11.3.0

- Authors: Avalonia Team
- Licence: MIT
- Copyright: Copyright 2017-2025 © The AvaloniaUI Project

### CommunityToolkit.Mvvm 8.4.2

- Authors: Microsoft
- Project: <https://github.com/CommunityToolkit/dotnet>
- Licence: MIT
- Copyright: (c) .NET Foundation and Contributors. All rights reserved.

### Google.Api.CommonProtos 2.16.0

- Authors: Google LLC
- Project: <https://github.com/googleapis/gax-dotnet>
- Licence: BSD-3-Clause
- Copyright: Copyright 2020 Google LLC

### Google.Protobuf 3.34.1

- Authors: Google Inc.
- Project: <https://github.com/protocolbuffers/protobuf>
- Licence: BSD-3-Clause
- Copyright: Copyright 2015, Google Inc.

### GraphQL-Parser 9.5.1

- Authors: Marek Magdziak
- Licence: MIT
- Copyright: Copyright 2016-2019 Marek Magdziak et al. All rights reserved.

### Grpc.Core.Api 2.76.0

- Authors: The gRPC Authors
- Project: <https://github.com/grpc/grpc-dotnet>
- Licence: Apache-2.0
- Copyright: Copyright 2019 The gRPC Authors

### Grpc.Net.Client 2.76.0

- Authors: The gRPC Authors
- Project: <https://github.com/grpc/grpc-dotnet>
- Licence: Apache-2.0
- Copyright: Copyright 2019 The gRPC Authors

### Grpc.Net.Common 2.76.0

- Authors: The gRPC Authors
- Project: <https://github.com/grpc/grpc-dotnet>
- Licence: Apache-2.0
- Copyright: Copyright 2019 The gRPC Authors

### HarfBuzzSharp 8.3.1.1

- Authors: Microsoft
- Project: <https://go.microsoft.com/fwlink/?linkid=868515>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### HarfBuzzSharp.NativeAssets.Linux 8.3.1.1

- Authors: Microsoft
- Project: <https://go.microsoft.com/fwlink/?linkid=868515>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### HarfBuzzSharp.NativeAssets.macOS 8.3.1.1

- Authors: Microsoft
- Project: <https://go.microsoft.com/fwlink/?linkid=868515>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### HarfBuzzSharp.NativeAssets.WebAssembly 8.3.1.1

- Authors: Microsoft
- Project: <https://go.microsoft.com/fwlink/?linkid=868515>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### HarfBuzzSharp.NativeAssets.Win32 8.3.1.1

- Authors: Microsoft
- Project: <https://go.microsoft.com/fwlink/?linkid=868515>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### MicroCom.Runtime 0.11.0

- Authors: MicroCom.Runtime
- Licence: MIT
- Copyright: Copyright 2021 © Nikita Tsukanov

### Microsoft.Extensions.Configuration 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Configuration.Abstractions 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Configuration.Binder 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Configuration.CommandLine 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Configuration.EnvironmentVariables 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Configuration.FileExtensions 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Configuration.Json 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Configuration.UserSecrets 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.DependencyInjection 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.DependencyInjection.Abstractions 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.DependencyInjection.Abstractions 8.0.0

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Diagnostics 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Diagnostics.Abstractions 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.FileProviders.Abstractions 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.FileProviders.Physical 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.FileSystemGlobbing 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Hosting 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Hosting.Abstractions 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Logging 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Logging.Abstractions 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Logging.Abstractions 8.0.0

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Logging.Configuration 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Logging.Console 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Logging.Debug 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Logging.EventLog 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Logging.EventSource 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Options 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Options.ConfigurationExtensions 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Microsoft.Extensions.Primitives 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Onigwrap 1.0.8

- Authors: Aikawa Yataro
- Project: <https://github.com/aikawayataro/Onigwrap>
- Licence: MIT
- Copyright: Copyright (c) 2024 Aikawa Yataro

### SkiaSharp 2.88.9

- Authors: Microsoft
- Project: <https://go.microsoft.com/fwlink/?linkid=868515>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### SkiaSharp.NativeAssets.Linux 2.88.9

- Authors: Microsoft
- Project: <https://go.microsoft.com/fwlink/?linkid=868515>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### SkiaSharp.NativeAssets.macOS 2.88.9

- Authors: Microsoft
- Project: <https://go.microsoft.com/fwlink/?linkid=868515>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### SkiaSharp.NativeAssets.WebAssembly 2.88.9

- Authors: Microsoft
- Project: <https://go.microsoft.com/fwlink/?linkid=868515>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### SkiaSharp.NativeAssets.Win32 2.88.9

- Authors: Microsoft
- Project: <https://go.microsoft.com/fwlink/?linkid=868515>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### Spectre.Console 0.55.2

- Authors: Patrik Svensson, Phil Scott, Nils Andresen, Cédric Luthi
- Project: <https://github.com/spectreconsole/spectre.console>
- Licence: MIT
- Copyright: Patrik Svensson, Phil Scott, Nils Andresen, Cédric Luthi

### Spectre.Console.Ansi 0.55.2

- Authors: Patrik Svensson, Phil Scott, Nils Andresen, Cédric Luthi
- Project: <https://github.com/spectreconsole/spectre.console>
- Licence: MIT
- Copyright: Patrik Svensson, Phil Scott, Nils Andresen, Cédric Luthi

### System.CommandLine 2.0.6

- Authors: Microsoft
- Project: <https://github.com/dotnet/command-line-api>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### System.Diagnostics.EventLog 10.0.9

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### System.Security.Cryptography.ProtectedData 10.0.0

- Authors: Microsoft
- Project: <https://dot.net/>
- Licence: MIT
- Copyright: © Microsoft Corporation. All rights reserved.

### TextMateSharp 1.0.70

- Authors: Daniel Peñalba
- Project: <https://github.com/danipen/TextMateSharp>
- Licence: MIT

### TextMateSharp.Grammars 1.0.70

- Authors: Daniel Peñalba
- Project: <https://github.com/danipen/TextMateSharp>
- Licence: MIT

### Tmds.DBus.Protocol 0.21.3

- Authors: Tom Deseyn
- Licence: MIT
- Copyright: Tom Deseyn

### YamlDotNet 16.3.0

- Authors: Antoine Aubry
- Project: <https://github.com/aaubry/YamlDotNet/wiki>
- Licence: MIT
- Copyright: Copyright (c) Antoine Aubry and contributors
