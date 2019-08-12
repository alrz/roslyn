using System;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests
{
    public class NullableEnhancedCommonTypeTests : CSharpTestBase
    {
        [Theory]
        [InlineData("new[] { 1d, null }", "System.Double?[]", "System.Double?")]
        [InlineData("new[] { 1d, (int?)null }", "System.Double?[]", "System.Double?")]
        [InlineData("cond switch { true => 1d, false => null }", "System.Double?")]
        [InlineData("cond switch { true => 1d, false => (int?)null }", "System.Double?")]
        [InlineData("cond ? 1d : null", "System.Double?")]
        [InlineData("cond ? 1d : (int?)null ", "System.Double?")]
        [InlineData("M1(null, 1d) ", null, "System.Double?", "System.Double?")]
        [InlineData("M1((int?)null, 1d) ", null, "System.Double?", "System.Double?")]
        [InlineData("M2(() => { if (cond) return 1d; else return null; }) ", null, "System.Func<System.Double?>")]
        [InlineData("M2(() => { if (cond) return 1d; else return (int?)null; }) ", null, "System.Func<System.Double?>")]
        [InlineData("M3(() => 1d, null) ", null, "System.Func<System.Double?>", "System.Double?")]
        [InlineData("M3(() => null, 1d) ", null, "System.Func<System.Double?>", "System.Double?")]
        [InlineData("M3(() => 1d, (int?)null) ", null, "System.Func<System.Double?>", "System.Double?")]
        [InlineData("M3(() => (int?)null, 1d) ", null, "System.Func<System.Double?>", "System.Double?")]
        public void TestExpression(string expression, string expectedType, params string[] childrenTypes)
        {
            string sourceTemplate = @"
using System;
class C
{
    static void Test(bool cond)
    {
        var x = EXPRESSION;
    }

    static int M1<T>(T a, T b) => 0;
    static int M2<T>(Func<T> a) => 0;
    static int M3<T>(Func<T> a, T b) => 0;
}";

            var source = sourceTemplate.Replace("EXPRESSION", expression);
            var tree = Parse(source, options: null);

            var comp = CreateCompilation(tree);
            comp.VerifyDiagnostics();

            var root = tree.GetCompilationUnitRoot();
            var localDeclaration = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>().Single();
            var expr = localDeclaration.Declaration.Variables.Single().Initializer.Value;
            var model = comp.GetSemanticModel(tree);

            switch (expr)
            {
                case ConditionalExpressionSyntax n:
                    Assert.Equal(expectedType, model.GetTypeInfo(expr).Type.ToTestDisplayString());
                    Assert.Equal(expectedType, model.GetTypeInfo(n.WhenTrue).ConvertedType.ToTestDisplayString());
                    Assert.Equal(expectedType, model.GetTypeInfo(n.WhenFalse).ConvertedType.ToTestDisplayString());
                    break;
                case SwitchExpressionSyntax n:
                    Assert.Equal(expectedType, model.GetTypeInfo(expr).Type.ToTestDisplayString());
                    foreach (var item in n.Arms)
                    {
                        Assert.Equal(expectedType, model.GetTypeInfo(item.Expression).ConvertedType.ToTestDisplayString());
                    }
                    break;
                case ImplicitArrayCreationExpressionSyntax n:
                    Assert.Equal(expectedType, model.GetTypeInfo(expr).Type.ToTestDisplayString());
                    foreach (var item in n.Initializer.Expressions)
                    {
                        Assert.Equal(childrenTypes[0], model.GetTypeInfo(item).ConvertedType.ToTestDisplayString());
                    }
                    break;
                case InvocationExpressionSyntax n:
                    Assert.Equal(childrenTypes.Length, n.ArgumentList.Arguments.Count);
                    foreach (var (arg, expectedArgType) in n.ArgumentList.Arguments.Zip(childrenTypes, (arg, type) => (arg, type)))
                    {
                        Assert.Equal(expectedArgType, model.GetTypeInfo(arg.Expression).ConvertedType.ToTestDisplayString());
                    }
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }
    }
}
