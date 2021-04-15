// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests
{
    [CompilerTrait(CompilerFeature.Patterns)]
    public class PatternMatchingTests_Variables : PatternMatchingTestBase
    {
        [Fact]
        public void OrPattern_01()
        {
            var program = @"
using static System.Console;
class C
{
    static void Main()
    {
        Test(5, 1);
        Test(1, 6);
    }
    static void Test(int a, int b)
    {
        if ((a, b) is (int x, 1) or (1, int x)) 
            Write(x);
    }
}
";
            var compilation = CreateCompilation(program, parseOptions: TestOptions.RegularWithPatternCombinators, options: TestOptions.ReleaseExe);
            compilation.VerifyDiagnostics();
            var verifier = CompileAndVerify(compilation, expectedOutput: "56");
        }

        [Fact]
        public void OrPattern_02()
        {
            var program = @"
using static System.Console;
class C
{
    static void Main()
    {
        Test(5, 1);
        Test(1, 6);
        Test(5, 2);
        Test(2, 6);
    }
    static void Test(int a, int b)
    {
        if ((a, b) is (int x, 1) or (1, int x) or (int x, 2) or (2, int x))
            Write(x);
    }
}
";
            var compilation = CreateCompilation(program, parseOptions: TestOptions.RegularWithPatternCombinators, options: TestOptions.ReleaseExe);
            compilation.VerifyDiagnostics();
            var verifier = CompileAndVerify(compilation, expectedOutput: "5656");
        }
    }
}
