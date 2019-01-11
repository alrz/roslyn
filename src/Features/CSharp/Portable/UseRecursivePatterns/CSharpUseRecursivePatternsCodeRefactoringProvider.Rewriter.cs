// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.UseRecursivePatterns
{
    using static SyntaxFactory;

    internal sealed partial class CSharpUseRecursivePatternsCodeRefactoringProvider
    {
        private sealed class Rewriter : Visitor<SyntaxNode, bool>
        {
            private readonly static Rewriter s_instance = new Rewriter();

            private Rewriter() { }

            public static ExpressionSyntax Rewrite(AnalyzedNode analyzedNode)
            {
                return (ExpressionSyntax)s_instance.Visit(analyzedNode, false);
            }

            public override SyntaxNode VisitPatternMatch(PatternMatch node, bool isPattern)
            {
                if (isPattern)
                {
                    return Subpattern(NameColon((IdentifierNameSyntax)node.Expression), AsPattern(Visit(node.Pattern, true)));
                }
                else
                {
                    return IsPatternExpression(node.Expression, AsPattern(Visit(node.Pattern, true)));
                }
            }

            private static PatternSyntax AsPattern(SyntaxNode node)
            {
                switch (node)
                {
                    case PatternSyntax n:
                        return n;
                    case SubpatternSyntax n:
                        return RecursivePattern(null, null, PropertyPatternClause(SingletonSeparatedList(n)), null);
                    case var value:
                        throw ExceptionUtilities.UnexpectedValue(value.Kind());
                }
            }

            private void VisitPatternConjuction(Conjuction node, ArrayBuilder<SubpatternSyntax> nodes, ref TypeSyntax type, ref SyntaxToken identifier)
            {
                VisitPatternConjuctionOperand(node.Left, nodes, ref type, ref identifier);
                VisitPatternConjuctionOperand(node.Right, nodes, ref type, ref identifier);
            }

            private void VisitPatternConjuctionOperand(AnalyzedNode node, ArrayBuilder<SubpatternSyntax> nodes, ref TypeSyntax type, ref SyntaxToken identifier)
            {
                switch (node)
                {
                    case Conjuction n:
                        VisitPatternConjuction(n, nodes, ref type, ref identifier);
                        break;
                    case TypePattern n:
                        type = n.Type;
                        break;
                    case VarPattern n:
                        identifier = n.Identifier;
                        break;
                    default:
                        nodes.Add((SubpatternSyntax)Visit(node, true));
                        break;
                }
            }

            public override SyntaxNode VisitConjuction(Conjuction node, bool isPattern)
            {
                if (isPattern)
                {
                    var nodes = ArrayBuilder<SubpatternSyntax>.GetInstance();
                    TypeSyntax type = null;
                    SyntaxToken identifier = default;
                    VisitPatternConjuction(node, nodes, ref type, ref identifier);

                    return RecursivePattern(
                        type,
                        deconstructionPatternClause: null,
                        PropertyPatternClause(SeparatedList(nodes.ToArrayAndFree())),
                        identifier.IsKind(SyntaxKind.None) ? null : SingleVariableDesignation(identifier));
                }
                else
                {
                    return BinaryExpression(SyntaxKind.LogicalAndExpression,
                        (ExpressionSyntax)Visit(node.Left, false),
                        (ExpressionSyntax)Visit(node.Right, false));
                }
            }

            public override SyntaxNode VisitConstantPattern(ConstantPattern node, bool isPattern)
            {
                return ConstantPattern(node.Expression);
            }

            public override SyntaxNode VisitTypePattern(TypePattern node, bool isPattern)
            {
                return DeclarationPattern(node.Type, DiscardDesignation());
            }

            public override SyntaxNode VisitSourcePattern(SourcePattern node, bool isPattern)
            {
                throw ExceptionUtilities.Unreachable;
            }

            public override SyntaxNode VisitNotNullPattern(NotNullPattern node, bool isPattern)
            {
                return RecursivePattern(null, null, PropertyPatternClause(SeparatedList<SubpatternSyntax>()), null);
            }

            public override SyntaxNode VisitVarPattern(VarPattern node, bool isPattern)
            {
                throw new NotImplementedException();
            }
        }
    }
}
