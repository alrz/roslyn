// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Shared.Extensions;

namespace Microsoft.CodeAnalysis.CSharp.RemoveUnnecessaryNullSuppression
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = PredefinedCodeFixProviderNames.RemoveUnnecessaryNullSuppression), Shared]
    internal sealed partial class CSharpRemoveUnnecessaryNullSuppressionCodeFixProvider : SyntaxEditorBasedCodeFixProvider
    {
        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public CSharpRemoveUnnecessaryNullSuppressionCodeFixProvider()
        {
        }

        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create(IDEDiagnosticIds.RemoveUnnecessaryNullSuppressionDiagnosticId);

        internal override CodeFixCategory CodeFixCategory => CodeFixCategory.CodeQuality;

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            context.RegisterCodeFix(new MyCodeAction(
                CSharpAnalyzersResources.Remove_suppression_operators,
                c => FixAsync(context.Document, context.Diagnostics[0], c)),
                context.Diagnostics);
            return Task.CompletedTask;
        }

        protected override Task FixAllAsync(Document document, ImmutableArray<Diagnostic> diagnostics, SyntaxEditor editor, CancellationToken cancellationToken)
        {
            foreach (var diagnostic in diagnostics)
            {
                var node = diagnostic.AdditionalLocations[0].FindNode(getInnermostNodeForTie: true, cancellationToken);
                Debug.Assert(node.IsKind(SyntaxKind.SuppressNullableWarningExpression));
                var suppression = (PostfixUnaryExpressionSyntax)node;
                var withoutSuppression = suppression.Operand.WithAppendedTrailingTrivia(suppression.OperatorToken.GetAllTrivia());
                editor.ReplaceNode(node, withoutSuppression);
            }

            return Task.FromResult(document.WithSyntaxRoot(editor.GetChangedRoot()));
        }

        private class MyCodeAction : CustomCodeActions.DocumentChangeAction
        {
            public MyCodeAction(string title, Func<CancellationToken, Task<Document>> createChangedDocument)
                : base(title, createChangedDocument, nameof(CSharpRemoveUnnecessaryNullSuppressionCodeFixProvider))
            {
            }
        }
    }
}
