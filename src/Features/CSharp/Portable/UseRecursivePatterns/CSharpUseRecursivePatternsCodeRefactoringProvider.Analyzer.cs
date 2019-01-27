﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.UseRecursivePatterns
{
    internal sealed partial class CSharpUseRecursivePatternsCodeRefactoringProvider
    {
        private sealed class Analyzer : CSharpSyntaxVisitor<AnalyzedNode>
        {
            private readonly SemanticModel _semanticModel;

            private Analyzer(SemanticModel semanticModel)
            {
                _semanticModel = semanticModel;
            }

            public static AnalyzedNode Analyze(SyntaxNode node, SemanticModel semanticModel)
            {
                return new Analyzer(semanticModel).Visit(node);
            }

            public override AnalyzedNode VisitCasePatternSwitchLabel(CasePatternSwitchLabelSyntax node)
            {
                if (node.WhenClause is var whenClause && whenClause != null)
                {
                    return new Conjunction(Visit(node.Pattern), Visit(whenClause.Condition));
                }

                return null;
            }

            public override AnalyzedNode VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                var left = node.Left;
                var right = node.Right;
                switch (node.Kind())
                {
                    case SyntaxKind.EqualsExpression when IsConstant(right):
                        return new PatternMatch(left, new ConstantPattern(right));

                    case SyntaxKind.EqualsExpression when IsConstant(left):
                        return new PatternMatch(right, new ConstantPattern(left));

                    case SyntaxKind.NotEqualsExpression when IsConstantNull(right):
                        return new PatternMatch(left, NotNullPattern.Instance);

                    case SyntaxKind.NotEqualsExpression when IsConstantNull(left):
                        return new PatternMatch(right, NotNullPattern.Instance);

                    case SyntaxKind.IsExpression:
                        return new PatternMatch(left,
                            IsLoweredToNullCheck(left, right)
                                ? NotNullPattern.Instance
                                : new TypePattern((TypeSyntax)right));

                    case SyntaxKind.LogicalAndExpression
                        when Visit(left) is var analyzedLeft && analyzedLeft != null &&
                            Visit(right) is var analyzedRight && analyzedRight != null:
                        return new Conjunction(analyzedLeft, analyzedRight);
                }

                return new Evaluation(node);
            }

            private bool IsLoweredToNullCheck(ExpressionSyntax e, ExpressionSyntax type)
            {
                return _semanticModel.ClassifyConversion(e,
                    _semanticModel.GetTypeInfo(type).Type).IsIdentityOrImplicitReference();
            }

            private bool IsConstantNull(ExpressionSyntax e)
            {
                return e.IsKind(SyntaxKind.NullLiteralExpression);
                var constant = _semanticModel.GetConstantValue(e);
                return constant.HasValue && constant.Value is null;
            }

            private bool IsConstant(ExpressionSyntax e)
            {
                return true;
                return _semanticModel.GetConstantValue(e).HasValue;
            }

            public override AnalyzedNode VisitIsPatternExpression(IsPatternExpressionSyntax node)
                => new PatternMatch(node.Expression, Visit(node.Pattern));

            public override AnalyzedNode VisitConstantPattern(ConstantPatternSyntax node)
                => new ConstantPattern(node.Expression);

            public override AnalyzedNode VisitDeclarationPattern(DeclarationPatternSyntax node)
                => new Conjunction(new TypePattern(node.Type), Visit(node.Designation));

            public override AnalyzedNode VisitDiscardPattern(DiscardPatternSyntax node)
                => DiscardPattern.Instance;

            public override AnalyzedNode VisitDiscardDesignation(DiscardDesignationSyntax node)
                => DiscardPattern.Instance;

            public override AnalyzedNode VisitParenthesizedVariableDesignation(ParenthesizedVariableDesignationSyntax node)
                => new PositionalPattern(node.Variables.SelectAsArray(v => ((NameColonSyntax)null, Visit(v))));

            public override AnalyzedNode VisitSingleVariableDesignation(SingleVariableDesignationSyntax node)
                => new VarPattern(node.Identifier);

            public override AnalyzedNode VisitVarPattern(VarPatternSyntax node)
                => Visit(node.Designation);

            public override AnalyzedNode VisitRecursivePattern(RecursivePatternSyntax node)
            {
                var nodes = new List<AnalyzedNode>();

                if (node.Type is var type && type != null)
                {
                    nodes.Add(new TypePattern(type));
                }

                if (node.PositionalPatternClause is var positinal && positinal != null)
                {
                    nodes.Add(new PositionalPattern(
                        positinal.Subpatterns.SelectAsArray(sub => (sub.NameColon, Visit(sub.Pattern)))));
                }

                if (node.PropertyPatternClause is var property && property != null)
                {
                    nodes.AddRange(property.Subpatterns
                        .Select(sub => new PatternMatch(sub.NameColon.Name, Visit(sub.Pattern))));
                }

                if (node.Designation is var designation && designation != null)
                {
                    nodes.Add(Visit(designation));
                }

                if (nodes.Count == 0)
                {
                    return NotNullPattern.Instance;
                }

                return nodes.Aggregate((left, right) => new Conjunction(left, right));
            }

            public override AnalyzedNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                return new PatternMatch(node,
                    new ConstantPattern(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)));
            }

            private static bool IsValidPropertyPattern(ExpressionSyntax node)
            {
                switch(node.Kind())
                {
                    default:
                        return false;
                    case SyntaxKind.IdentifierName:
                        return true;
                    case SyntaxKind.ParenthesizedExpression:
                        return IsValidPropertyPattern(((ParenthesizedExpressionSyntax)node).Expression);
                    case SyntaxKind.SimpleMemberAccessExpression:
                        return ((MemberAccessExpressionSyntax)node).Name.IsKind(SyntaxKind.IdentifierName);
                    case SyntaxKind.ConditionalAccessExpression:
                        return IsValidPropertyPattern(((ConditionalAccessExpressionSyntax)node).WhenNotNull);
                }
            }

            public override AnalyzedNode VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
            {
                if (node.IsKind(SyntaxKind.LogicalNotExpression) &&
                    IsValidPropertyPattern(node.Operand))
                {
                    return new PatternMatch(node.Operand,
                        new ConstantPattern(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)));
                }

                return new Evaluation(node);
            }

            public override AnalyzedNode DefaultVisit(SyntaxNode node)
            {
                if (node is ExpressionSyntax expression)
                {
                    return new Evaluation(expression);
                }

                return null;
            }
        }
    }
}
