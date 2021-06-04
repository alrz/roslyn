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
        private static Task VerifyAsync(string source, string expected)
        {
            return VerifyCS.VerifyCodeFixAsync(source, expected);
        }
        private static Task VerifyMissingAsync(string source)
        {
            return VerifyCS.VerifyCodeFixAsync(source, source);
        }

        [Fact]
        public async Task Test_NotNull_Parameter()
        {
            await VerifyAsync(
@"
#nullable enable
class C
{
    void M(object o)
    {
        var x = o[|!|];
    }
}
",
@"
#nullable enable
class C
{
    void M(object o)
    {
        var x = o;
    }
}
");
        }

        [Fact]
        public async Task Test_NotNull_Property()
        {
            await VerifyAsync(
@"
#nullable enable
class C
{
    public object Prop { get; } = 0;
    void M(C o)
    {
        var x = o.Prop[|!|];
    }
}
",
@"
#nullable enable
class C
{
    public object Prop { get; } = 0;
    void M(C o)
    {
        var x = o.Prop;
    }
}
");
        }

        [Fact]
        public async Task Test_NotNull_Method()
        {
            await VerifyAsync(
@"
#nullable enable
class C
{
    public object Method() => new object();
    void M(C o)
    {
        var x = Method()[|!|];
    }
}
",
@"
#nullable enable
class C
{
    public object Method() => new object();
    void M(C o)
    {
        var x = Method();
    }
}
");
        }

        [Fact]
        public async Task Test_NullCheck()
        {
            await VerifyAsync(
@"
#nullable enable
class C
{
    void M(object? o)
    {
        if (o != null)
        {
            var x = o[|!|];
        }
    }
}
",
@"
#nullable enable
class C
{
    void M(object? o)
    {
        if (o != null)
        {
            var x = o;
        }
    }
}
");
        }

        [Fact]
        public async Task TestMissing_NullableContext_01()
        {
            await VerifyMissingAsync(
@"
class C
{
    void M(object o)
    {
        var x = o!;
    }
}
");
        }

        [Fact]
        public async Task TestMissing_NullableContext_02()
        {
            await VerifyMissingAsync(
                @"
#nullable enable
class C
{
    void M(object o)
    {
#nullable disable
        var x = o!;
#nullable enable
    }
}
");
        }

        [Fact]
        public async Task TestMissing_Null()
        {
            await VerifyMissingAsync(
@"
#nullable enable
class C
{
    void M(object o)
    {
        string x = null!;
    }
}
");
        }

        [Fact]
        public async Task TestMissing_Method()
        {
            await VerifyMissingAsync(
@"
#nullable enable
class C
{
    public object? Method() => null;
    object M()
    {
        return Method()!;
    }
}
");
        }
    }
}
