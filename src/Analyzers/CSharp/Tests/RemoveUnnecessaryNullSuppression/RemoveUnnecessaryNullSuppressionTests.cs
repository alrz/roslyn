// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.RemoveUnnecessaryNullSuppression;
using Microsoft.CodeAnalysis.Editor.UnitTests.CodeActions;
using Xunit;

namespace Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.RemoveUnnecessaryNullSuppression
{
    using VerifyCS = CSharpCodeFixVerifier<CSharpRemoveUnnecessaryNullSuppressionDiagnosticAnalyzer, CSharpRemoveUnnecessaryNullSuppressionCodeFixProvider>;

    public class RemoveUnnecessaryNullSuppressionTests
    {
        private static Task Verify(string source, string expected)
        {
            return VerifyCS.VerifyCodeFixAsync(source, expected);
        }

        [Fact]
        public async Task TestRemoveSuppression_01()
        {
            await Verify(
@"
#nullable enable
class C
{
    void M(object o)
    {
        if (o [|!|]is string)
        {
        }
    }
}",
@"
#nullable enable
class C
{
    void M(object o)
    {
        if (o is string)
        {
        }
    }
}");
        }
    }
}
