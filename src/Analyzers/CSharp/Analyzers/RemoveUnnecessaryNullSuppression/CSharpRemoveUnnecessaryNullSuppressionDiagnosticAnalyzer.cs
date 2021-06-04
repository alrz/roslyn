// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Extensions;

namespace Microsoft.CodeAnalysis.CSharp.RemoveUnnecessaryNullSuppression
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    internal sealed class CSharpRemoveUnnecessaryNullSuppressionDiagnosticAnalyzer : AbstractBuiltInCodeStyleDiagnosticAnalyzer
    {
        public CSharpRemoveUnnecessaryNullSuppressionDiagnosticAnalyzer()
            : base(IDEDiagnosticIds.RemoveUnnecessaryNullSuppressionDiagnosticId,
                   EnforceOnBuildValues.RemoveUnnecessarySuppression,
                   option: null,
                   new LocalizableResourceString(nameof(CSharpAnalyzersResources.Remove_unnecessary_suppression_operator), CSharpAnalyzersResources.ResourceManager, typeof(CSharpAnalyzersResources)),
                   new LocalizableResourceString(nameof(CSharpAnalyzersResources.Suppression_operator_has_no_effect_and_can_be_misinterpreted), CSharpAnalyzersResources.ResourceManager, typeof(CSharpAnalyzersResources)))
        {
        }

        public override DiagnosticAnalyzerCategory GetAnalyzerCategory() => DiagnosticAnalyzerCategory.SemanticSpanAnalysis;

        protected override void InitializeWorker(AnalysisContext context)
            => context.RegisterSyntaxNodeAction(AnalyzeSyntax, SyntaxKind.SuppressNullableWarningExpression);

        private void AnalyzeSyntax(SyntaxNodeAnalysisContext context)
        {
            var node = context.Node;
            var nullableContext = context.SemanticModel.GetNullableContext(node.SpanStart);
            if (!nullableContext.WarningsEnabled() || !nullableContext.AnnotationsEnabled())
                return;

            var suppression = (PostfixUnaryExpressionSyntax)node;
            var operand = suppression.Operand;
            if (operand.WalkDownParentheses().IsKind(SyntaxKind.NullLiteralExpression, SyntaxKind.DefaultLiteralExpression))
                return;

            var type = context.SemanticModel.GetTypeInfo(operand);
            var flowState = type.Nullability.FlowState;
            if (flowState != NullableFlowState.NotNull)
                return;

            context.ReportDiagnostic(DiagnosticHelper.Create(
                Descriptor,
                suppression.OperatorToken.GetLocation(),
                ReportDiagnostic.Warn,
                ImmutableArray.Create(suppression.GetLocation()),
                properties: null));
        }
    }
}
