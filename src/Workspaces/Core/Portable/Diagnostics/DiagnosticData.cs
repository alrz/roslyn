﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Diagnostics
{
    [DataContract]
    internal sealed class DiagnosticData : IEquatable<DiagnosticData?>
    {
        [DataMember(Order = 0)]
        public readonly string Id;

        [DataMember(Order = 1)]
        public readonly string Category;

        [DataMember(Order = 2)]
        public readonly string? Message;

        [DataMember(Order = 3)]
        public readonly DiagnosticSeverity Severity;

        [DataMember(Order = 4)]
        public readonly DiagnosticSeverity DefaultSeverity;

        [DataMember(Order = 5)]
        public readonly bool IsEnabledByDefault;

        [DataMember(Order = 6)]
        public readonly int WarningLevel;

        [DataMember(Order = 7)]
        public readonly ImmutableArray<string> CustomTags;

        [DataMember(Order = 8)]
        public readonly ImmutableDictionary<string, string?> Properties;

        [DataMember(Order = 9)]
        public readonly ProjectId? ProjectId;

        [DataMember(Order = 10)]
        public readonly DiagnosticDataLocation DataLocation;

        [DataMember(Order = 11)]
        public readonly ImmutableArray<DiagnosticDataLocation> AdditionalLocations;

        /// <summary>
        /// Language name (<see cref="LanguageNames"/>) or null if the diagnostic is not associated with source code.
        /// </summary>
        [DataMember(Order = 12)]
        public readonly string? Language;

        [DataMember(Order = 13)]
        public readonly string? Title;

        [DataMember(Order = 14)]
        public readonly string? Description;

        [DataMember(Order = 15)]
        public readonly string? HelpLink;

        [DataMember(Order = 16)]
        public readonly bool IsSuppressed;

        /// <summary>
        /// Properties for a diagnostic generated by an explicit build.
        /// </summary>
        internal static ImmutableDictionary<string, string> PropertiesForBuildDiagnostic { get; }
            = ImmutableDictionary<string, string>.Empty.Add(WellKnownDiagnosticPropertyNames.Origin, WellKnownDiagnosticTags.Build);

        public DiagnosticData(
            string id,
            string category,
            string? message,
            DiagnosticSeverity severity,
            DiagnosticSeverity defaultSeverity,
            bool isEnabledByDefault,
            int warningLevel,
            ImmutableArray<string> customTags,
            ImmutableDictionary<string, string?> properties,
            ProjectId? projectId,
            DiagnosticDataLocation location,
            ImmutableArray<DiagnosticDataLocation> additionalLocations = default,
            string? language = null,
            string? title = null,
            string? description = null,
            string? helpLink = null,
            bool isSuppressed = false)
        {
            Id = id;
            Category = category;
            Message = message;

            Severity = severity;
            DefaultSeverity = defaultSeverity;
            IsEnabledByDefault = isEnabledByDefault;
            WarningLevel = warningLevel;
            CustomTags = customTags;
            Properties = properties;

            ProjectId = projectId;
            DataLocation = location;
            AdditionalLocations = additionalLocations.NullToEmpty();

            Language = language;
            Title = title;
            Description = description;
            HelpLink = helpLink;
            IsSuppressed = isSuppressed;
        }

        public DiagnosticData WithLocations(DiagnosticDataLocation location, ImmutableArray<DiagnosticDataLocation> additionalLocations)
            => new(Id, Category, Message, Severity, DefaultSeverity, IsEnabledByDefault,
                WarningLevel, CustomTags, Properties, ProjectId, location, additionalLocations,
                Language, Title, Description, HelpLink, IsSuppressed);

        public DocumentId? DocumentId => DataLocation.DocumentId;
        public bool HasTextSpan => DataLocation.SourceSpan.HasValue;

        /// <summary>
        /// Get <see cref="TextSpan"/> if it exists, throws otherwise.
        /// 
        /// Some diagnostic data such as those created from build have original line/column but not <see cref="TextSpan"/>.
        /// In those cases use <see cref="GetTextSpan(DiagnosticDataLocation, SourceText)"/> method instead to calculate span from original line/column.
        /// </summary>
        public TextSpan GetTextSpan()
        {
            Contract.ThrowIfFalse(DataLocation.SourceSpan.HasValue);
            return DataLocation.SourceSpan.Value;
        }

        public override bool Equals(object? obj)
            => obj is DiagnosticData data && Equals(data);

        public bool Equals(DiagnosticData? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other is null)
            {
                return false;
            }

            return
               DataLocation.OriginalFileSpan.StartLinePosition == other.DataLocation.OriginalFileSpan.StartLinePosition &&
               Id == other.Id &&
               Category == other.Category &&
               Severity == other.Severity &&
               WarningLevel == other.WarningLevel &&
               IsSuppressed == other.IsSuppressed &&
               ProjectId == other.ProjectId &&
               DocumentId == other.DocumentId &&
               Message == other.Message;
        }

        public override int GetHashCode()
            => Hash.Combine(Id,
               Hash.Combine(Category,
               Hash.Combine(Message,
               Hash.Combine(WarningLevel,
               Hash.Combine(IsSuppressed,
               Hash.Combine(ProjectId,
               Hash.Combine(DocumentId,
               Hash.Combine(DataLocation.OriginalFileSpan.StartLinePosition.GetHashCode(), (int)Severity))))))));

        public override string ToString()
        {
            return string.Format("{0} {1} {2} {3} {4} {5} ({5}, {6}) [original: {7} ({8}, {9})]",
                Id,
                Severity,
                Message,
                ProjectId,
                DataLocation.MappedFileSpan?.Path ?? "",
                DataLocation.MappedFileSpan?.StartLinePosition.Line,
                DataLocation.MappedFileSpan?.StartLinePosition.Character,
                DataLocation.OriginalFileSpan.Path ?? "",
                DataLocation.OriginalFileSpan.StartLinePosition.Line,
                DataLocation.OriginalFileSpan.StartLinePosition.Character);
        }

        public static TextSpan GetExistingOrCalculatedTextSpan(DiagnosticDataLocation diagnosticLocation, SourceText text)
        {
            if (diagnosticLocation.SourceSpan != null)
            {
                return EnsureInBounds(diagnosticLocation.SourceSpan.Value, text);
            }
            else
            {
                return GetTextSpan(diagnosticLocation, text);
            }
        }

        private static TextSpan EnsureInBounds(TextSpan textSpan, SourceText text)
            => TextSpan.FromBounds(
                Math.Min(textSpan.Start, text.Length),
                Math.Min(textSpan.End, text.Length));

        public DiagnosticData WithSpan(SourceText text, SyntaxTree tree)
        {
            Contract.ThrowIfNull(DocumentId);
            Contract.ThrowIfNull(DataLocation);
            Contract.ThrowIfTrue(HasTextSpan);

            var span = GetTextSpan(DataLocation, text);
            var newLocation = DataLocation.WithSpan(span, tree);

            return new DiagnosticData(
                id: Id,
                category: Category,
                message: Message,
                severity: Severity,
                defaultSeverity: DefaultSeverity,
                isEnabledByDefault: IsEnabledByDefault,
                warningLevel: WarningLevel,
                customTags: CustomTags,
                properties: Properties,
                projectId: ProjectId,
                location: newLocation,
                additionalLocations: AdditionalLocations,
                language: Language,
                title: Title,
                description: Description,
                helpLink: HelpLink,
                isSuppressed: IsSuppressed);
        }

        public async Task<Diagnostic> ToDiagnosticAsync(Project project, CancellationToken cancellationToken)
        {
            var location = await DataLocation.ConvertLocationAsync(project, cancellationToken).ConfigureAwait(false);
            var additionalLocations = await AdditionalLocations.ConvertLocationsAsync(project, cancellationToken).ConfigureAwait(false);

            return ToDiagnostic(location, additionalLocations);
        }

        public Diagnostic ToDiagnostic(Location location, ImmutableArray<Location> additionalLocations)
        {
            return Diagnostic.Create(
                Id, Category, Message, Severity, DefaultSeverity,
                IsEnabledByDefault, WarningLevel, IsSuppressed, Title, Description, HelpLink,
                location, additionalLocations, customTags: CustomTags, properties: Properties);
        }

        public static LinePositionSpan GetLinePositionSpan(DiagnosticDataLocation dataLocation, SourceText text, bool useMapped)
        {
            var lines = text.Lines;
            if (lines.Count == 0)
                return default;

            var fileSpan = useMapped && dataLocation.MappedFileSpan != null ? dataLocation.MappedFileSpan.Value : dataLocation.OriginalFileSpan;

            var startLine = fileSpan.StartLinePosition.Line;
            var endLine = fileSpan.EndLinePosition.Line;

            // Make sure the starting columns are never negative.
            var startColumn = Math.Max(fileSpan.StartLinePosition.Character, 0);
            var endColumn = Math.Max(fileSpan.EndLinePosition.Character, 0);

            if (startLine < 0)
            {
                // If the start line is negative (e.g. before the start of the actual document) then move the start to the 0,0 position.
                startLine = 0;
                startColumn = 0;
            }
            else if (startLine >= lines.Count)
            {
                // if the start line is after the end of the document, move the start to the last location in the document.
                startLine = lines.Count - 1;
                startColumn = lines[startLine].SpanIncludingLineBreak.Length;
            }

            if (endLine < 0)
            {
                // if the end is before the start of the document, then move the end to wherever the start position was determined to be.
                endLine = startLine;
                endColumn = startColumn;
            }
            else if (endLine >= lines.Count)
            {
                // if the end line is after the end of the document, move the end to the last location in the document.
                endLine = lines.Count - 1;
                endColumn = lines[endLine].SpanIncludingLineBreak.Length;
            }

            // now, ensure that the column of the start/end positions is within the length of its line.
            startColumn = Math.Min(startColumn, lines[startLine].SpanIncludingLineBreak.Length);
            endColumn = Math.Min(endColumn, lines[endLine].SpanIncludingLineBreak.Length);

            var start = new LinePosition(startLine, startColumn);
            var end = new LinePosition(endLine, endColumn);

            // swap if necessary
            if (end < start)
                (start, end) = (end, start);

            return new LinePositionSpan(start, end);
        }

        public static TextSpan GetTextSpan(DiagnosticDataLocation dataLocation, SourceText text)
        {
            var linePositionSpan = GetLinePositionSpan(dataLocation, text, useMapped: false);

            var span = text.Lines.GetTextSpan(linePositionSpan);
            return EnsureInBounds(TextSpan.FromBounds(Math.Max(span.Start, 0), Math.Max(span.End, 0)), text);
        }

        private static DiagnosticDataLocation CreateLocation(TextDocument? document, Location location)
        {
            GetLocationInfo(out var sourceSpan, out var originalLineInfo, out var mappedLineInfo);

            if (!originalLineInfo.IsValid)
                originalLineInfo = new FileLinePositionSpan(document?.FilePath ?? "", span: default);

            return new DiagnosticDataLocation(
                originalLineInfo, document?.Id, sourceSpan, mappedLineInfo);

            void GetLocationInfo(out TextSpan sourceSpan, out FileLinePositionSpan originalLineInfo, out FileLinePositionSpan mappedLineInfo)
            {
                var diagnosticSpanMappingService = document?.Project.Solution.Services.GetService<IWorkspaceVenusSpanMappingService>();
                if (document != null && diagnosticSpanMappingService != null)
                {
                    diagnosticSpanMappingService.GetAdjustedDiagnosticSpan(document.Id, location, out sourceSpan, out originalLineInfo, out mappedLineInfo);
                }
                else
                {
                    sourceSpan = location.SourceSpan;
                    originalLineInfo = location.GetLineSpan();
                    mappedLineInfo = location.GetMappedLineSpan();
                }
            }
        }

        public static DiagnosticData Create(Solution solution, Diagnostic diagnostic, Project? project)
            => Create(diagnostic, project?.Id, project?.Language,
                location: new DiagnosticDataLocation(new FileLinePositionSpan(project?.FilePath ?? solution.FilePath ?? "", span: default)),
                additionalLocations: default, additionalProperties: null);

        public static DiagnosticData Create(Diagnostic diagnostic, TextDocument document)
        {
            var project = document.Project;
            var location = CreateLocation(document, diagnostic.Location);

            var additionalLocations = GetAdditionalLocations(document, diagnostic);
            var additionalProperties = GetAdditionalProperties(document, diagnostic);

            var documentPropertiesService = document.Services.GetService<DocumentPropertiesService>();
            var diagnosticsLspClientName = documentPropertiesService?.DiagnosticsLspClientName;

            if (diagnosticsLspClientName != null)
            {
                additionalProperties ??= ImmutableDictionary.Create<string, string?>();

                additionalProperties = additionalProperties.Add(nameof(documentPropertiesService.DiagnosticsLspClientName), diagnosticsLspClientName);
            }

            return Create(diagnostic,
                project.Id,
                project.Language,
                location,
                additionalLocations,
                additionalProperties);
        }

        private static DiagnosticData Create(
            Diagnostic diagnostic,
            ProjectId? projectId,
            string? language,
            DiagnosticDataLocation location,
            ImmutableArray<DiagnosticDataLocation> additionalLocations,
            ImmutableDictionary<string, string?>? additionalProperties)
        {
            return new DiagnosticData(
                diagnostic.Id,
                diagnostic.Descriptor.Category,
                diagnostic.GetMessage(CultureInfo.CurrentUICulture),
                diagnostic.Severity,
                diagnostic.DefaultSeverity,
                diagnostic.Descriptor.IsEnabledByDefault,
                diagnostic.WarningLevel,
                diagnostic.Descriptor.ImmutableCustomTags(),
                (additionalProperties == null) ? diagnostic.Properties : diagnostic.Properties.AddRange(additionalProperties),
                projectId,
                location,
                additionalLocations,
                language: language,
                title: diagnostic.Descriptor.Title.ToString(CultureInfo.CurrentUICulture),
                description: diagnostic.Descriptor.Description.ToString(CultureInfo.CurrentUICulture),
                helpLink: diagnostic.Descriptor.HelpLinkUri,
                isSuppressed: diagnostic.IsSuppressed);
        }

        private static ImmutableDictionary<string, string?>? GetAdditionalProperties(TextDocument document, Diagnostic diagnostic)
        {
            var service = document.Project.GetLanguageService<IDiagnosticPropertiesService>();
            return service?.GetAdditionalProperties(diagnostic);
        }

        private static ImmutableArray<DiagnosticDataLocation> GetAdditionalLocations(TextDocument document, Diagnostic diagnostic)
        {
            if (diagnostic.AdditionalLocations.Count == 0)
            {
                return ImmutableArray<DiagnosticDataLocation>.Empty;
            }

            using var _ = ArrayBuilder<DiagnosticDataLocation>.GetInstance(diagnostic.AdditionalLocations.Count, out var builder);
            foreach (var location in diagnostic.AdditionalLocations)
            {
                if (location.IsInSource)
                {
                    builder.AddIfNotNull(CreateLocation(document.Project.GetRequiredDocument(location.SourceTree), location));
                }
                else if (location.Kind == LocationKind.ExternalFile)
                {
                    var textDocumentId = document.Project.GetDocumentForExternalLocation(location);
                    builder.AddIfNotNull(CreateLocation(document.Project.GetTextDocument(textDocumentId), location));
                }
            }

            return builder.ToImmutableAndClear();
        }

        /// <summary>
        /// Create a host/VS specific diagnostic with the given descriptor and message arguments for the given project.
        /// Note that diagnostic created through this API cannot be suppressed with in-source suppression due to performance reasons (see the PERF remark below for details).
        /// </summary>
        public static bool TryCreate(DiagnosticDescriptor descriptor, string[] messageArguments, Project project, [NotNullWhen(true)] out DiagnosticData? diagnosticData)
        {
            diagnosticData = null;

            DiagnosticSeverity effectiveSeverity;
            if (project.SupportsCompilation)
            {
                // Get the effective severity of the diagnostic from the compilation options.
                // PERF: We do not check if the diagnostic was suppressed by a source suppression, as this requires us to force complete the assembly attributes, which is very expensive.
                var reportDiagnostic = descriptor.GetEffectiveSeverity(project.CompilationOptions!);
                if (reportDiagnostic == ReportDiagnostic.Suppress)
                {
                    // Rule is disabled by compilation options.
                    return false;
                }

                effectiveSeverity = GetEffectiveSeverity(reportDiagnostic, descriptor.DefaultSeverity);
            }
            else
            {
                effectiveSeverity = descriptor.DefaultSeverity;
            }

            var diagnostic = Diagnostic.Create(descriptor, Location.None, effectiveSeverity, additionalLocations: null, properties: null, messageArgs: messageArguments);
            diagnosticData = Create(project.Solution, diagnostic, project);
            return true;
        }

        private static DiagnosticSeverity GetEffectiveSeverity(ReportDiagnostic effectiveReportDiagnostic, DiagnosticSeverity defaultSeverity)
        {
            switch (effectiveReportDiagnostic)
            {
                case ReportDiagnostic.Default:
                    return defaultSeverity;

                case ReportDiagnostic.Error:
                    return DiagnosticSeverity.Error;

                case ReportDiagnostic.Hidden:
                    return DiagnosticSeverity.Hidden;

                case ReportDiagnostic.Info:
                    return DiagnosticSeverity.Info;

                case ReportDiagnostic.Warn:
                    return DiagnosticSeverity.Warning;

                default:
                    throw ExceptionUtilities.Unreachable;
            }
        }

        /// <summary>
        /// Returns true if the diagnostic was generated by an explicit build, not live analysis.
        /// </summary>
        internal bool IsBuildDiagnostic()
        {
            return Properties.TryGetValue(WellKnownDiagnosticPropertyNames.Origin, out var value) &&
                value == WellKnownDiagnosticTags.Build;
        }

        // TODO: the value stored in HelpLink should already be valid URI (https://github.com/dotnet/roslyn/issues/59205)
        internal Uri? GetValidHelpLinkUri()
            => Uri.TryCreate(HelpLink, UriKind.Absolute, out var uri) ? uri : null;
    }
}
