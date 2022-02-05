﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Options;

#if CODE_STYLE
using WorkspacesResources = Microsoft.CodeAnalysis.CodeStyleResources;
#else
using System.Composition;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Options.Providers;
#endif

namespace Microsoft.CodeAnalysis.Formatting
{
    /// <summary>
    /// Formatting options stored in editorconfig.
    /// </summary>
    internal sealed class FormattingOptions2
    {
#if !CODE_STYLE
        [ExportSolutionOptionProvider, Shared]
        internal sealed class Provider : IOptionProvider
        {
            [ImportingConstructor]
            [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
            public Provider()
            {
            }

            public ImmutableArray<IOption> Options { get; } = FormattingOptions2.Options;
        }
#endif
        private const string FeatureName = "FormattingOptions";

        public static PerLanguageOption2<bool> UseTabs =
            new(FeatureName, FormattingOptionGroups.IndentationAndSpacing, nameof(UseTabs), defaultValue: false,
            storageLocation: new EditorConfigStorageLocation<bool>(
                "indent_style",
                s => s == "tab",
                isSet => isSet ? "tab" : "space"));

        // This is also serialized by the Visual Studio-specific LanguageSettingsPersister
        public static PerLanguageOption2<int> TabSize =
            new(FeatureName, FormattingOptionGroups.IndentationAndSpacing, nameof(TabSize), defaultValue: 4,
            storageLocation: EditorConfigStorageLocation.ForInt32Option("tab_width"));

        // This is also serialized by the Visual Studio-specific LanguageSettingsPersister
        public static PerLanguageOption2<int> IndentationSize =
            new(FeatureName, FormattingOptionGroups.IndentationAndSpacing, nameof(IndentationSize), defaultValue: 4,
            storageLocation: EditorConfigStorageLocation.ForInt32Option("indent_size"));

        public static PerLanguageOption2<string> NewLine =
            new(FeatureName, FormattingOptionGroups.NewLine, nameof(NewLine), defaultValue: Environment.NewLine,
            storageLocation: new EditorConfigStorageLocation<string>(
                "end_of_line",
                parseValue: value => value.Trim() switch
                {
                    "lf" => "\n",
                    "cr" => "\r",
                    "crlf" => "\r\n",
                    _ => Environment.NewLine
                },
                getEditorConfigStringForValue: option => option switch
                {
                    "\n" => "lf",
                    "\r" => "cr",
                    "\r\n" => "crlf",
                    _ => "unset"
                }));

        internal static Option2<bool> InsertFinalNewLine =
            new(FeatureName, FormattingOptionGroups.NewLine, nameof(InsertFinalNewLine), defaultValue: false,
            storageLocation: EditorConfigStorageLocation.ForBoolOption("insert_final_newline"));

        /// <summary>
        /// Default value of 120 was picked based on the amount of code in a github.com diff at 1080p.
        /// That resolution is the most common value as per the last DevDiv survey as well as the latest
        /// Steam hardware survey.  This also seems to a reasonable length default in that shorter
        /// lengths can often feel too cramped for .NET languages, which are often starting with a
        /// default indentation of at least 16 (for namespace, class, member, plus the final construct
        /// indentation).
        /// 
        /// TODO: Currently the option has no storage and always has its default value. See https://github.com/dotnet/roslyn/pull/30422#issuecomment-436118696.
        /// </summary>
        internal static Option2<int> PreferredWrappingColumn { get; } =
            new(FeatureName, FormattingOptionGroups.NewLine, nameof(PreferredWrappingColumn), defaultValue: 120);

#if !CODE_STYLE
        internal static readonly ImmutableArray<IOption> Options = ImmutableArray.Create<IOption>(
            UseTabs,
            TabSize,
            IndentationSize,
            NewLine,
            InsertFinalNewLine);
#endif
    }

    internal static class FormattingOptionGroups
    {
        public static readonly OptionGroup IndentationAndSpacing = new(WorkspacesResources.Indentation_and_spacing, priority: 1);
        public static readonly OptionGroup NewLine = new(WorkspacesResources.New_line_preferences, priority: 2);
    }
}
