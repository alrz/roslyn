﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.CodeAnalysis.Structure;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Text.Shared.Extensions;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.Implementation.Structure
{
#pragma warning disable CS0618 // Type or member is obsolete
    internal sealed class StructureTag : IStructureTag2, IEquatable<StructureTag>
#pragma warning restore CS0618 // Type or member is obsolete
    {
        private readonly AbstractStructureTaggerProvider _tagProvider;

        public StructureTag(AbstractStructureTaggerProvider tagProvider, BlockSpan blockSpan, ITextSnapshot snapshot)
        {
            Snapshot = snapshot;
            OutliningSpan = blockSpan.TextSpan.ToSpan();
            Type = ConvertType(blockSpan.Type);
            IsCollapsible = blockSpan.IsCollapsible;
            IsDefaultCollapsed = blockSpan.IsDefaultCollapsed;
            IsImplementation = blockSpan.AutoCollapse;
            HeaderSpan = StructureUtilities.DetermineHeaderSpan(
                blockSpan.TextSpan,
                blockSpan.HintSpan,
                snapshot.AsText()).ToSnapshotSpan(snapshot);
            CollapsedText = blockSpan.BannerText;
            CollapsedHintFormSpan = blockSpan.HintSpan.ToSpan();
            _tagProvider = tagProvider;

            if (blockSpan.)
        }

        /// <summary>
        /// The contents of the buffer to show if we mouse over the collapsed indicator.
        /// </summary>
        public readonly Span CollapsedHintFormSpan;

        public readonly string CollapsedText;

        public ITextSnapshot Snapshot { get; }
        public Span? OutliningSpan { get; }
        public Span? HeaderSpan { get; }
        public Span? GuideLineSpan => null;
        public Span? PrimaryHeaderSpan { get; }

        public int? GuideLineHorizontalAnchorPoint => null;
        public string Type { get; }
        public bool IsCollapsible { get; }
        public bool IsDefaultCollapsed { get; }
        public bool IsImplementation { get; }

        // Editor uses this here:
        // https://devdiv.visualstudio.com/DefaultCollection/DevDiv/_git/VS-Platform?path=/src/Editor/Text/Impl/Structure/StructureSpanningTree/StructureSpanningTree.cs&version=GBmain&line=308&lineEnd=309&lineStartColumn=1&lineEndColumn=1&lineStyle=plain&_a=contents
        public override int GetHashCode()
            => Hash.Combine(this.GuideLineHorizontalAnchorPoint.GetHashCode(),
               Hash.Combine(this.Type,
               Hash.Combine(this.IsCollapsible,
               Hash.Combine(this.IsDefaultCollapsed,
               Hash.Combine(this.IsImplementation,
               Hash.Combine(this.OutliningSpan.GetHashCode(),
               Hash.Combine(this.HeaderSpan.GetHashCode(), this.GuideLineSpan.GetHashCode())))))));

        public override bool Equals(object? obj)
            => Equals(obj as StructureTag);

        public bool Equals(StructureTag? other)
        {
            return other != null &&
                this.GuideLineHorizontalAnchorPoint == other.GuideLineHorizontalAnchorPoint &&
                this.Type == other.Type &&
                this.IsCollapsible == other.IsCollapsible &&
                this.IsDefaultCollapsed == other.IsDefaultCollapsed &&
                this.IsImplementation == other.IsImplementation &&
                _tagProvider.SpanEquals(this.Snapshot, this.OutliningSpan, other.Snapshot, other.OutliningSpan) &&
                _tagProvider.SpanEquals(this.Snapshot, this.HeaderSpan, other.Snapshot, other.HeaderSpan) &&
                _tagProvider.SpanEquals(this.Snapshot, this.GuideLineSpan, other.Snapshot, other.GuideLineSpan);
        }

        public object? GetCollapsedForm()
        {
            return CollapsedText;
        }

        public object? GetCollapsedHintForm()
        {
            return _tagProvider.GetCollapsedHintForm(this);
        }

        private static string ConvertType(string type)
        {
            return type switch
            {
                BlockTypes.Conditional => PredefinedStructureTagTypes.Conditional,
                BlockTypes.Comment => PredefinedStructureTagTypes.Comment,
                BlockTypes.Expression => PredefinedStructureTagTypes.Expression,
                BlockTypes.Imports => PredefinedStructureTagTypes.Imports,
                BlockTypes.Loop => PredefinedStructureTagTypes.Loop,
                BlockTypes.Member => PredefinedStructureTagTypes.Member,
                BlockTypes.Namespace => PredefinedStructureTagTypes.Namespace,
                BlockTypes.Nonstructural => PredefinedStructureTagTypes.Nonstructural,
                BlockTypes.PreprocessorRegion => PredefinedStructureTagTypes.PreprocessorRegion,
                BlockTypes.Statement => PredefinedStructureTagTypes.Statement,
                BlockTypes.Type => PredefinedStructureTagTypes.Type,
                _ => PredefinedStructureTagTypes.Structural
            };
        }
    }
}
