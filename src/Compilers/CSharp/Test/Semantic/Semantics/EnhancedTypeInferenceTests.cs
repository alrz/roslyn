
// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;
using System.Threading;
using System.Linq;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests
{
    public class EnhancedTypeInferenceTests : CSharpTestBase
    {
        [Fact]
        public void TestConstructorArguments_01()
        {
            var source = @"
using System;
class C
{
    string M()
    {
        new Lazy(M);
        new System.Lazy(M);
        new global::System.Lazy(M);
        return null;
    }
}";

            var compilation = CreateCompilation(source);
            compilation.VerifyDiagnostics();
            var tree = compilation.SyntaxTrees[0];
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                var info = model.GetTypeInfo(node);
                Assert.Equal("System.Lazy<string>", info.Type.ToDisplayString());
            }
        }

        [Fact]
        public void TestConstructorArguments_02()
        {
            var source = @"
class C<T, U>
{
    C(T i) {}
    void M()
    {
        new C(123);
    }
}";

            var compilation = CreateCompilation(source);
            compilation.VerifyDiagnostics(
                // (7,13): error CS0411: The type arguments for method 'C<T, U>.C(T)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
                //         new C(123);
                Diagnostic(ErrorCode.ERR_CantInferMethTypeArgs, "C").WithArguments("C<T, U>.C(T)").WithLocation(7, 13)
                );
        }

        [Fact]
        public void TestConstructorArguments_03()
        {
            var source = @"
class C {}
class C<T, U>
{
    void M()
    {
        new C(123);
    }
}";

            var compilation = CreateCompilation(source);
            compilation.VerifyDiagnostics(
                // (7,13): error CS1729: 'C' does not contain a constructor that takes 1 arguments
                //         new C(123);
                Diagnostic(ErrorCode.ERR_BadCtorArgCount, "C").WithArguments("C", "1").WithLocation(7, 13)
                );
        }

        [Fact]
        public void TestConstructorArguments_04()
        {
            var source = @"
class C<T> {}
class C<T, U>
{
    void M()
    {
        new C(123);
    }
}";

            var compilation = CreateCompilation(source);
            compilation.VerifyDiagnostics(
                // (7,13): error CS0104: 'C' is an ambiguous reference between 'C<T>' and 'C<T, U>'
                //         new C(123);
                Diagnostic(ErrorCode.ERR_AmbigContext, "C").WithArguments("C", "C<T>", "C<T, U>").WithLocation(7, 13)
                );
        }

        [Fact]
        public void TestConstructorArguments_05()
        {
            var source = @"
class C<T>
{
    class D<U>
    {
        public D(C<T> t, U i) {}
    }
    void M()
    {
        new C<T>.D(this, 123);
    }
}";

            var compilation = CreateCompilation(source);
            compilation.VerifyDiagnostics();
        }

        [Fact]
        public void MethodGroup_01()
        {
            var source = @"
using System;
class Program
{
    public static void Main()
    {
        Program.Test(Program.IsEven);
    }

    public static bool IsEven(int x)
    {
        return true;
    }

    public static void Test<T>(Func<T, bool> predicate)
    {
        Console.Write(predicate(default(T)));
    }
}";

            var compilation = CreateCompilation(source, options: TestOptions.DebugExe);
            compilation.VerifyDiagnostics();
            CompileAndVerify(compilation, expectedOutput: "True");
        }
    }
}
