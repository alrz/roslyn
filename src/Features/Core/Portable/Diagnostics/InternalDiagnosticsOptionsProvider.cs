﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Options.Providers;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    [ExportOptionProvider, Shared]
    internal class InternalDiagnosticsOptionsProvider : IOptionProvider
    {
        [ImportingConstructor]
        public InternalDiagnosticsOptionsProvider()
        {
        }

        public ImmutableArray<IOption> Options { get; } = ImmutableArray.Create<IOption>(
            InternalDiagnosticsOptions.CompilationEndCodeFix,
            InternalDiagnosticsOptions.UseCompilationEndCodeFixHeuristic,
            InternalDiagnosticsOptions.PreferLiveErrorsOnOpenedFiles,
            InternalDiagnosticsOptions.PreferBuildErrorsOverLiveErrors);
    }
}
