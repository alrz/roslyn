// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis.Test.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests
{
    [CompilerTrait(CompilerFeature.ReadOnlyLocals)]
    public class ReadOnlyLocalsTests : ParsingTests
    {
        public ReadOnlyLocalsTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TestReadOnlyLocal()
        {
            UsingStatement("readonly int i = 0;");
            N(SyntaxKind.LocalDeclarationStatement);
            {
                N(SyntaxKind.ReadOnlyKeyword);
                N(SyntaxKind.VariableDeclaration);
                {
                    N(SyntaxKind.PredefinedType);
                    {
                        N(SyntaxKind.IntKeyword);
                    }
                    N(SyntaxKind.VariableDeclarator);
                    {
                        N(SyntaxKind.IdentifierToken, "i");
                        N(SyntaxKind.EqualsValueClause);
                        {
                            N(SyntaxKind.EqualsToken);
                            N(SyntaxKind.NumericLiteralExpression);
                            {
                                N(SyntaxKind.NumericLiteralToken, "0");
                            }
                        }
                    }
                }
                N(SyntaxKind.SemicolonToken);
            }
            EOF();
        }

        [Fact]
        public void TestReadOnlyLocalUninitialized()
        {
            UsingStatement("readonly int i;",
                // (1,14): error CS9000: Read-only variables must be initialized.
                // readonly int i;
                Diagnostic(ErrorCode.ERR_ReadOnlyVariableWithNoInitializer, "i").WithLocation(1, 14)
                );
            N(SyntaxKind.LocalDeclarationStatement);
            {
                N(SyntaxKind.ReadOnlyKeyword);
                N(SyntaxKind.VariableDeclaration);
                {
                    N(SyntaxKind.PredefinedType);
                    {
                        N(SyntaxKind.IntKeyword);
                    }
                    N(SyntaxKind.VariableDeclarator);
                    {
                        N(SyntaxKind.IdentifierToken, "i");
                    }
                }
                N(SyntaxKind.SemicolonToken);
            }
            EOF();
        }

        [Fact]
        public void TestReadOnlyRefReadOnly()
        {
            UsingStatement("readonly ref readonly int i = 0;",
                // (1,14): error CS8107: Feature 'readonly references' is not available in C# 7.0. Please use language version 7.2 or greater.
                // readonly ref readonly int i = 0;
                Diagnostic(ErrorCode.ERR_FeatureNotAvailableInVersion7, "readonly").WithArguments("readonly references", "7.2").WithLocation(1, 14)
                );
            N(SyntaxKind.LocalDeclarationStatement);
            {
                N(SyntaxKind.ReadOnlyKeyword);
                N(SyntaxKind.VariableDeclaration);
                {
                    N(SyntaxKind.RefType);
                    {
                        N(SyntaxKind.RefKeyword);
                        N(SyntaxKind.ReadOnlyKeyword);
                        N(SyntaxKind.PredefinedType);
                        {
                            N(SyntaxKind.IntKeyword);
                        }
                    }
                    N(SyntaxKind.VariableDeclarator);
                    {
                        N(SyntaxKind.IdentifierToken, "i");
                        N(SyntaxKind.EqualsValueClause);
                        {
                            N(SyntaxKind.EqualsToken);
                            N(SyntaxKind.NumericLiteralExpression);
                            {
                                N(SyntaxKind.NumericLiteralToken, "0");
                            }
                        }
                    }
                }
                N(SyntaxKind.SemicolonToken);
            }
            EOF();

        }

        [Fact]
        public void TestReadOnlyRef()
        {
            UsingStatement("readonly ref int i = 0;");
            N(SyntaxKind.LocalDeclarationStatement);
            {
                N(SyntaxKind.ReadOnlyKeyword);
                N(SyntaxKind.VariableDeclaration);
                {
                    N(SyntaxKind.RefType);
                    {
                        N(SyntaxKind.RefKeyword);
                        N(SyntaxKind.PredefinedType);
                        {
                            N(SyntaxKind.IntKeyword);
                        }
                    }
                    N(SyntaxKind.VariableDeclarator);
                    {
                        N(SyntaxKind.IdentifierToken, "i");
                        N(SyntaxKind.EqualsValueClause);
                        {
                            N(SyntaxKind.EqualsToken);
                            N(SyntaxKind.NumericLiteralExpression);
                            {
                                N(SyntaxKind.NumericLiteralToken, "0");
                            }
                        }
                    }
                }
                N(SyntaxKind.SemicolonToken);
            }
        }

        [Fact]
        public void TestNoRegressionOnRefReadOnly()
        {
            UsingStatement("ref readonly int i = 0;",
                // (1,5): error CS8107: Feature 'readonly references' is not available in C# 7.0. Please use language version 7.2 or greater.
                // ref readonly int i = 0;
                Diagnostic(ErrorCode.ERR_FeatureNotAvailableInVersion7, "readonly").WithArguments("readonly references", "7.2").WithLocation(1, 5)
                );
            N(SyntaxKind.LocalDeclarationStatement);
            {
                N(SyntaxKind.VariableDeclaration);
                {
                    N(SyntaxKind.RefType);
                    {
                        N(SyntaxKind.RefKeyword);
                        N(SyntaxKind.ReadOnlyKeyword);
                        N(SyntaxKind.PredefinedType);
                        {
                            N(SyntaxKind.IntKeyword);
                        }
                    }
                    N(SyntaxKind.VariableDeclarator);
                    {
                        N(SyntaxKind.IdentifierToken, "i");
                        N(SyntaxKind.EqualsValueClause);
                        {
                            N(SyntaxKind.EqualsToken);
                            N(SyntaxKind.NumericLiteralExpression);
                            {
                                N(SyntaxKind.NumericLiteralToken, "0");
                            }
                        }
                    }
                }
                N(SyntaxKind.SemicolonToken);
            }
            EOF();
        }

        [Fact]
        public void TestReadOnlyParameter()
        {
            UsingTree(@"
class C 
{
    void M(readonly int i) {}
}
");
            N(SyntaxKind.CompilationUnit);
            {
                N(SyntaxKind.ClassDeclaration);
                {
                    N(SyntaxKind.ClassKeyword);
                    N(SyntaxKind.IdentifierToken, "C");
                    N(SyntaxKind.OpenBraceToken);
                    N(SyntaxKind.MethodDeclaration);
                    {
                        N(SyntaxKind.PredefinedType);
                        {
                            N(SyntaxKind.VoidKeyword);
                        }
                        N(SyntaxKind.IdentifierToken, "M");
                        N(SyntaxKind.ParameterList);
                        {
                            N(SyntaxKind.OpenParenToken);
                            N(SyntaxKind.Parameter);
                            {
                                N(SyntaxKind.ReadOnlyKeyword);
                                N(SyntaxKind.PredefinedType);
                                {
                                    N(SyntaxKind.IntKeyword);
                                }
                                N(SyntaxKind.IdentifierToken, "i");
                            }
                            N(SyntaxKind.CloseParenToken);
                        }
                        N(SyntaxKind.Block);
                        {
                            N(SyntaxKind.OpenBraceToken);
                            N(SyntaxKind.CloseBraceToken);
                        }
                    }
                    N(SyntaxKind.CloseBraceToken);
                }
                N(SyntaxKind.EndOfFileToken);
            }
            EOF();
        }
    }
}
