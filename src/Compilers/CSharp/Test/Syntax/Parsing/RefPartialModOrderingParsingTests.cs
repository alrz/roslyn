﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Xunit;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Test.Utilities;
using Xunit.Abstractions;
using Microsoft.CodeAnalysis.CSharp.Symbols;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.Parsing
{
    [CompilerTrait(CompilerFeature.RefPartialModOrdering)]
    public class RefPartialModOrderingParsingTests : ParsingTests
    {
        public RefPartialModOrderingParsingTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TestLangVersion_PartialRef()
        {
            var text = @"
ref partial struct S {}
partial ref struct S {}
";

            var tree = SyntaxFactory.ParseSyntaxTree(text, options: TestOptions.Regular7);
            tree.GetDiagnostics().Verify(
                // (2,1): error CS8107: Feature 'ref structs' is not available in C# 7.0. Please use language version 7.2 or greater.
                // ref partial struct S {}
                Diagnostic(ErrorCode.ERR_FeatureNotAvailableInVersion7, "ref").WithArguments("ref structs", "7.2").WithLocation(2, 1),
                // (3,1): error CS8107: Feature 'ref and partial modifier ordering' is not available in C# 7.0. Please use language version 7.3 or greater.
                // partial ref struct S {}
                Diagnostic(ErrorCode.ERR_FeatureNotAvailableInVersion7, "partial").WithArguments("ref and partial modifier ordering", "7.3").WithLocation(3, 1),
                // (3,9): error CS8107: Feature 'ref structs' is not available in C# 7.0. Please use language version 7.2 or greater.
                // partial ref struct S {}
                Diagnostic(ErrorCode.ERR_FeatureNotAvailableInVersion7, "ref").WithArguments("ref structs", "7.2").WithLocation(3, 9)
                );
        }

        [Fact]
        public void TestLangVersion_Partial()
        {
            var text = @"
partial public class C {}
partial public struct S {}
partial public interface I {}
";

            var tree = SyntaxFactory.ParseSyntaxTree(text, options: TestOptions.Regular7);
            tree.GetDiagnostics().Verify(
                // (2,1): error CS8107: Feature 'ref and partial modifier ordering' is not available in C# 7.0. Please use language version 7.3 or greater.
                // partial public class C {}
                Diagnostic(ErrorCode.ERR_FeatureNotAvailableInVersion7, "partial").WithArguments("ref and partial modifier ordering", "7.3").WithLocation(2, 1),
                // (3,1): error CS8107: Feature 'ref and partial modifier ordering' is not available in C# 7.0. Please use language version 7.3 or greater.
                // partial public struct S {}
                Diagnostic(ErrorCode.ERR_FeatureNotAvailableInVersion7, "partial").WithArguments("ref and partial modifier ordering", "7.3").WithLocation(3, 1),
                // (4,1): error CS8107: Feature 'ref and partial modifier ordering' is not available in C# 7.0. Please use language version 7.3 or greater.
                // partial public interface I {}
                Diagnostic(ErrorCode.ERR_FeatureNotAvailableInVersion7, "partial").WithArguments("ref and partial modifier ordering", "7.3").WithLocation(4, 1)
                );
        }

        [Fact]
        public void TestLangVersion_PartialMethod()
        {
            var text = @"
partial class C
{
    partial static void M();
}
";

            var tree = SyntaxFactory.ParseSyntaxTree(text, options: TestOptions.Regular7);
            tree.GetDiagnostics().Verify(
                // (4,5): error CS8107: Feature 'ref and partial modifier ordering' is not available in C# 7.0. Please use language version 7.3 or greater.
                //     partial static void M();
                Diagnostic(ErrorCode.ERR_FeatureNotAvailableInVersion7, "partial").WithArguments("ref and partial modifier ordering", "7.3").WithLocation(4, 5));
        }

        [Fact]
        public void TestBadTypeKind_PartialPublic()
        {
            var text = @"
partial public enum E {}
partial public delegate void D();
";

            var comp = CreateCompilation(text);
            comp.VerifyDiagnostics(
                // (2,21): error CS0106: The modifier 'partial' is not valid for this item
                // partial public enum E {}
                Diagnostic(ErrorCode.ERR_BadMemberFlag, "E").WithArguments("partial").WithLocation(2, 21),
                // (3,30): error CS0106: The modifier 'partial' is not valid for this item
                // partial public delegate void D();
                Diagnostic(ErrorCode.ERR_BadMemberFlag, "D").WithArguments("partial").WithLocation(3, 30)
                );
        }

        [Fact]
        public void TestBadTypeKind_Partial()
        {
            var text = @"
partial enum E {}
partial delegate void D();
";

            var comp = CreateCompilation(text);
            comp.VerifyDiagnostics(
                // (2,14): error CS0106: The modifier 'partial' is not valid for this item
                // partial enum E {}
                Diagnostic(ErrorCode.ERR_BadMemberFlag, "E").WithArguments("partial").WithLocation(2, 14),
                // (3,23): error CS0106: The modifier 'partial' is not valid for this item
                // partial delegate void D();
                Diagnostic(ErrorCode.ERR_BadMemberFlag, "D").WithArguments("partial").WithLocation(3, 23)
                );
        }

        [Fact]
        public void TestBadTypeKind_PartialPartial()
        {
            var text = @"
partial partial enum E {}
partial partial delegate void D();
";

            var comp = CreateCompilation(text);
            comp.VerifyDiagnostics(
                // (2,22): error CS0106: The modifier 'partial' is not valid for this item
                // partial partial enum E {}
                Diagnostic(ErrorCode.ERR_BadMemberFlag, "E").WithArguments("partial").WithLocation(2, 22),
                // (2,9): error CS1004: Duplicate 'partial' modifier
                // partial partial enum E {}
                Diagnostic(ErrorCode.ERR_DuplicateModifier, "partial").WithArguments("partial").WithLocation(2, 9),
                // (3,31): error CS0106: The modifier 'partial' is not valid for this item
                // partial partial delegate void D();
                Diagnostic(ErrorCode.ERR_BadMemberFlag, "D").WithArguments("partial").WithLocation(3, 31),
                // (3,9): error CS1004: Duplicate 'partial' modifier
                // partial partial delegate void D();
                Diagnostic(ErrorCode.ERR_DuplicateModifier, "partial").WithArguments("partial").WithLocation(3, 9)
                );
        }

        [Fact]
        public void TestPartialRefStruct_Symbols()
        {
            var text = @"
partial struct S {}
partial ref struct S {}
ref partial struct S {}
partial ref readonly struct S {}
partial readonly ref struct S {}
partial public struct S {}
";

            var comp = CreateCompilation(text);
            comp.VerifyDiagnostics();

            var s1 = comp.GetTypeByMetadataName("S");
            Assert.True(s1.IsPartial());
            Assert.True(s1.IsReadOnly);
            Assert.True(s1.IsRefLikeType);
            Assert.Equal(s1.DeclaredAccessibility, Accessibility.Public);
        }

        [Fact]
        public void TestPartialClass_Symbols()
        {
            var text = @"
partial class C {}
partial public class C {}
partial abstract class C {}
";

            var comp = CreateCompilation(text);
            comp.VerifyDiagnostics();

            var s1 = comp.GetTypeByMetadataName("C");
            Assert.True(s1.IsPartial());
            Assert.True(s1.IsAbstract);
            Assert.Equal(s1.DeclaredAccessibility, Accessibility.Public);
        }

        [Fact]
        public void TestPartialPartialRef()
        {
            var text = @"
partial partial ref class C {}
partial partial ref struct S {}
";

            var comp = CreateCompilation(text, parseOptions: TestOptions.Regular7_3);
            comp.VerifyDiagnostics(
                // (2,27): error CS0106: The modifier 'ref' is not valid for this item
                // partial partial ref class C {}
                Diagnostic(ErrorCode.ERR_BadMemberFlag, "C").WithArguments("ref").WithLocation(2, 27),
                // (2,9): error CS1004: Duplicate 'partial' modifier
                // partial partial ref class C {}
                Diagnostic(ErrorCode.ERR_DuplicateModifier, "partial").WithArguments("partial").WithLocation(2, 9),
                // (3,9): error CS1004: Duplicate 'partial' modifier
                // partial partial ref struct S {}
                Diagnostic(ErrorCode.ERR_DuplicateModifier, "partial").WithArguments("partial").WithLocation(3, 9)
                );
        }

        [Fact]
        public void TestPartialRefRef()
        {
            var text = @"
partial ref ref class C {}
partial ref ref struct S {}
";

            var comp = CreateCompilation(text, parseOptions: TestOptions.Regular7_3);
            comp.VerifyDiagnostics(
                // (2,23): error CS0106: The modifier 'ref' is not valid for this item
                // partial ref ref class C {}
                Diagnostic(ErrorCode.ERR_BadMemberFlag, "C").WithArguments("ref").WithLocation(2, 23),
                // (2,13): error CS1004: Duplicate 'ref' modifier
                // partial ref ref class C {}
                Diagnostic(ErrorCode.ERR_DuplicateModifier, "ref").WithArguments("ref").WithLocation(2, 13),
                // (3,13): error CS1004: Duplicate 'ref' modifier
                // partial ref ref struct S {}
                Diagnostic(ErrorCode.ERR_DuplicateModifier, "ref").WithArguments("ref").WithLocation(3, 13)
                );
        }

        [Fact]
        public void TestPartialAfterIncompleteAttribute_PartialClass()
        {
            var test = @"
using System;
[Obsolete(
partial class Test {}
";

            var tree = SyntaxFactory.ParseSyntaxTree(test, options: TestOptions.Regular7_3);
            tree.GetDiagnostics().Verify(
                // (3,11): error CS1026: ) expected
                // [Obsolete(
                Diagnostic(ErrorCode.ERR_CloseParenExpected, "").WithLocation(3, 11),
                // (3,11): error CS1003: Syntax error, ']' expected
                // [Obsolete(
                Diagnostic(ErrorCode.ERR_SyntaxError, "").WithArguments("]", "").WithLocation(3, 11)
                );
        }

        [Fact]
        public void TestPartialAfterIncompleteAttribute_PartialPublicClass()
        {
            var test = @"
using System;
[Obsolete(
partial public class Test {}
";

            var tree = SyntaxFactory.ParseSyntaxTree(test, options: TestOptions.Regular7_3);
            tree.GetDiagnostics().Verify(
                // (3,11): error CS1026: ) expected
                // [Obsolete(
                Diagnostic(ErrorCode.ERR_CloseParenExpected, "").WithLocation(3, 11),
                // (3,11): error CS1003: Syntax error, ']' expected
                // [Obsolete(
                Diagnostic(ErrorCode.ERR_SyntaxError, "").WithArguments("]", "").WithLocation(3, 11)
                );
        }

        [Fact]
        public void TestNoRegressionOnRefPartialMember()
        {
            var text = @"
class partial
{
    ref partial M() => throw null;
}
";

            var comp = CreateCompilation(text, parseOptions: TestOptions.Regular7_3);
            comp.VerifyDiagnostics();
        }
    }
}

