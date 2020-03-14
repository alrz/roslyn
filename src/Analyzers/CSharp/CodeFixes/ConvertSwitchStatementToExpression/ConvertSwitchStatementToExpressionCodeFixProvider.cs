// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.ConvertSwitchStatementToExpression
{
    using Constants = ConvertSwitchStatementToExpressionConstants;

    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    internal sealed partial class ConvertSwitchStatementToExpressionCodeFixProvider : SyntaxEditorBasedCodeFixProvider
    {
        private static readonly SyntaxAnnotation s_switchExpressionAnnotation = new SyntaxAnnotation();

        [ImportingConstructor]
        public ConvertSwitchStatementToExpressionCodeFixProvider()
        {
        }

        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create(IDEDiagnosticIds.ConvertSwitchStatementToExpressionDiagnosticId);

        internal sealed override CodeFixCategory CodeFixCategory => CodeFixCategory.CodeStyle;

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            context.RegisterCodeFix(
                new MyCodeAction(c => FixAsync(context.Document, context.Diagnostics.First(), c)),
                context.Diagnostics);
            return Task.CompletedTask;
        }

        protected override async Task FixAllAsync(Document document, ImmutableArray<Diagnostic> diagnostics, SyntaxEditor editor, CancellationToken cancellationToken)
        {
            using var spansDisposer = ArrayBuilder<TextSpan>.GetInstance(diagnostics.Length, out var spans);
            foreach (var diagnostic in diagnostics)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var switchLocation = diagnostic.AdditionalLocations[0];
                if (spans.Any((s, nodeSpan) => s.Contains(nodeSpan), switchLocation.SourceSpan))
                {
                    // Skip nested switch expressions in case of a fix-all operation.
                    continue;
                }

                spans.Add(switchLocation.SourceSpan);

                var properties = diagnostic.Properties;
                var nodeToGenerate = (SyntaxKind)int.Parse(properties[Constants.NodeToGenerateKey]);
                var shouldRemoveNextStatement = bool.Parse(properties[Constants.ShouldRemoveNextStatementKey]);

                var declaratorToRemoveLocationOpt = diagnostic.AdditionalLocations.ElementAtOrDefault(1);
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

                ITypeSymbol declaratorToRemoveTypeOpt = null;

                if (declaratorToRemoveLocationOpt != null)
                {
                    var declaratorToRemoveNode = declaratorToRemoveLocationOpt.FindNode(cancellationToken);
                    declaratorToRemoveTypeOpt = semanticModel.GetDeclaredSymbol(declaratorToRemoveNode).GetSymbolType();
                }

                var switchStatement = (SwitchStatementSyntax)switchLocation.FindNode(cancellationToken);
                var rewrittenStatement = Rewriter.Rewrite(switchStatement, declaratorToRemoveTypeOpt, nodeToGenerate,
                    shouldMoveNextStatementToSwitchExpression: shouldRemoveNextStatement,
                    generateDeclaration: declaratorToRemoveLocationOpt is object, s_switchExpressionAnnotation);

                var switchExpression = rewrittenStatement.GetAnnotatedNodes<SwitchExpressionSyntax>(s_switchExpressionAnnotation).Single();
                var incompatibleType = GetSwitchTypeIfIncompatible();
                if (incompatibleType is object)
                {
                    rewrittenStatement = rewrittenStatement.ReplaceNodes(switchExpression.Arms,
                        (switchArmNode, _) =>
                        {
                            var expr = switchArmNode.Expression;
                            return expr.IsKind(SyntaxKind.ThrowExpression)
                                ? switchArmNode 
                                : switchArmNode.WithExpression(expr.Cast(incompatibleType));
                        });
                }

                editor.ReplaceNode(switchStatement, rewrittenStatement.WithAdditionalAnnotations(Formatter.Annotation));

                if (declaratorToRemoveLocationOpt is object)
                {
                    editor.RemoveNode(declaratorToRemoveLocationOpt.FindNode(cancellationToken));
                }

                if (shouldRemoveNextStatement)
                {
                    // Already morphed into the top-level switch expression.
                    var nextStatement = switchStatement.GetNextStatement();
                    Debug.Assert(nextStatement.IsKind(SyntaxKind.ThrowStatement, SyntaxKind.ReturnStatement));
                    editor.RemoveNode(nextStatement);
                }

                ITypeSymbol GetSwitchTypeIfIncompatible()
                {
                    if (semanticModel.TryGetSpeculativeSemanticModel(switchStatement.SpanStart, rewrittenStatement, out var speculativeModel))
                    {
                        var switchType = speculativeModel.GetTypeInfo(switchExpression).ConvertedType;
                        if (switchType is null)
                            return null;

                        foreach (var switchArm in switchExpression.Arms)
                        {
                            var switchArmType = speculativeModel.GetTypeInfo(switchArm.Expression).ConvertedType;
                            if (switchArmType is null)
                                return null;

                            if (!switchType.Equals(switchArmType) &&
                                !switchArmType.IsReferenceType &&
                                !switchArmType.IsNullable())
                            {
                                return switchType;
                            }
                        }
                    }

                    return null;
                }
            }
        }

        private sealed class MyCodeAction : CustomCodeActions.DocumentChangeAction
        {
            public MyCodeAction(Func<CancellationToken, Task<Document>> createChangedDocument)
                : base(CSharpAnalyzersResources.Convert_switch_statement_to_expression, createChangedDocument)
            {
            }
        }
    }
}
