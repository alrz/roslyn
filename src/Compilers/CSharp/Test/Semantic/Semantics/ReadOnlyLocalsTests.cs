// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.Semantic.UnitTests
{
    [CompilerTrait(CompilerFeature.ReadOnlyLocals)]
    public class ReadOnlyLocalsTests : CSharpTestBase
    {
        private static readonly string s_additionalTypes =
@"namespace System
{
    public struct ValueTuple<T1, T2>
    {
        public T1 Item1;
        public T2 Item2;

        public ValueTuple(T1 item1, T2 item2)
            => (Item1, Item2) = (item1, item2);
    }
    namespace Runtime.CompilerServices
    {
        [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue | AttributeTargets.Class | AttributeTargets.Struct )]
        public sealed class TupleElementNamesAttribute : Attribute
        {
            public TupleElementNamesAttribute(string[] transformNames) { }
        }
    }
}
struct S
{
    public int field;
}
class C
{
    public int field;
}";

        [Fact]
        public void TestReadOnlyLocals()
        {
            CreateCompilationWithMscorlib46(@"
class Test
{
    void M()
    {
        readonly int i = 42;
        readonly S s = new S();
        readonly C c = new C();
        readonly (int, int f) t = (1, 2);

        i = 5;
        s = default(S);
        s.field = 5;
        t.Item1 = 5;
        t.f = 5;
        c = null;
        c.field = 5; // OK
    }
}" + s_additionalTypes).VerifyDiagnostics(
                // (11,9): error CS8331: Cannot assign to variable 'i' because it is a readonly variable
                //         i = 5;
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField, "i").WithArguments("variable", "i").WithLocation(11, 9),
                // (12,9): error CS8331: Cannot assign to variable 's' because it is a readonly variable
                //         s = default(S);
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField, "s").WithArguments("variable", "s").WithLocation(12, 9),
                // (13,9): error CS8332: Cannot assign to a member of variable 's' because it is a readonly variable
                //         s.field = 5;
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField2, "s.field").WithArguments("variable", "s").WithLocation(13, 9),
                // (14,9): error CS8332: Cannot assign to a member of variable 't' because it is a readonly variable
                //         t.Item1 = 5;
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField2, "t.Item1").WithArguments("variable", "t").WithLocation(14, 9),
                // (15,9): error CS8332: Cannot assign to a member of variable 't' because it is a readonly variable
                //         t.f = 5;
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField2, "t.f").WithArguments("variable", "t").WithLocation(15, 9),
                // (16,9): error CS8331: Cannot assign to variable 'c' because it is a readonly variable
                //         c = null;
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField, "c").WithArguments("variable", "c").WithLocation(16, 9)
                );
        }

        [Fact]
        public void TestReadOnlyParameters()
        {
            CreateCompilationWithMscorlib46(@"
class Test
{
    void M(
        readonly int i,
        readonly S s,
        readonly C c,
        readonly (int, int f) t)
    {
        i = 5;
        s = default(S);
        s.field = 5;
        t.Item1 = 5;
        t.f = 5;
        c = null;
        c.field = 5; // OK
    }
}" + s_additionalTypes).VerifyDiagnostics(
                // (10,9): error CS8331: Cannot assign to variable 'int' because it is a readonly variable
                //         i = 5;
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField, "i").WithArguments("variable", "int").WithLocation(10, 9),
                // (11,9): error CS8331: Cannot assign to variable 'S' because it is a readonly variable
                //         s = default(S);
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField, "s").WithArguments("variable", "S").WithLocation(11, 9),
                // (12,9): error CS8332: Cannot assign to a member of variable 'S' because it is a readonly variable
                //         s.field = 5;
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField2, "s.field").WithArguments("variable", "S").WithLocation(12, 9),
                // (13,9): error CS8332: Cannot assign to a member of variable '(int, int f)' because it is a readonly variable
                //         t.Item1 = 5;
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField2, "t.Item1").WithArguments("variable", "(int, int f)").WithLocation(13, 9),
                // (14,9): error CS8332: Cannot assign to a member of variable '(int, int f)' because it is a readonly variable
                //         t.f = 5;
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField2, "t.f").WithArguments("variable", "(int, int f)").WithLocation(14, 9),
                // (15,9): error CS8331: Cannot assign to variable 'C' because it is a readonly variable
                //         c = null;
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField, "c").WithArguments("variable", "C").WithLocation(15, 9)
                );
        }

        [Fact]
        public void TestLet()
        {
            CreateCompilationWithMscorlib46(@"
class Test
{
    void M()
    {
        let i = 42;
        let s = new S();
        let c = new C();
        let t = (1, f: 2);

        i = 5;
        s = default(S);
        s.field = 5;
        t.Item1 = 5;
        t.f = 5;
        c = null;
        c.field = 5; // OK
    }
}" + s_additionalTypes).VerifyDiagnostics(
                // (11,9): error CS8331: Cannot assign to variable 'i' because it is a readonly variable
                //         i = 5;
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField, "i").WithArguments("variable", "i").WithLocation(11, 9),
                // (12,9): error CS8331: Cannot assign to variable 's' because it is a readonly variable
                //         s = default(S);
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField, "s").WithArguments("variable", "s").WithLocation(12, 9),
                // (13,9): error CS8332: Cannot assign to a member of variable 's' because it is a readonly variable
                //         s.field = 5;
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField2, "s.field").WithArguments("variable", "s").WithLocation(13, 9),
                // (14,9): error CS8332: Cannot assign to a member of variable 't' because it is a readonly variable
                //         t.Item1 = 5;
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField2, "t.Item1").WithArguments("variable", "t").WithLocation(14, 9),
                // (15,9): error CS8332: Cannot assign to a member of variable 't' because it is a readonly variable
                //         t.f = 5;
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField2, "t.f").WithArguments("variable", "t").WithLocation(15, 9),
                // (16,9): error CS8331: Cannot assign to variable 'c' because it is a readonly variable
                //         c = null;
                Diagnostic(ErrorCode.ERR_AssignReadonlyNotField, "c").WithArguments("variable", "c").WithLocation(16, 9)
                );
        }

        [Fact]
        public void TestBadReadOnlyParameterModifier()
        {
            CreateCompilationWithMscorlib46(@"
static class C 
{
    static void M1(readonly ref int i) {}
    static void M2(ref readonly int i) {}

    static void M3(readonly out int i) => throw null;
    static void M4(out readonly int i) => throw null;

    static void M5(readonly in int i) {}
    static void M6(in readonly int i) {}
}
").VerifyDiagnostics(
                // (11,23): error CS8328:  The parameter modifier 'readonly' cannot be used with 'in'
                //     static void M6(in readonly int i) {}
                Diagnostic(ErrorCode.ERR_BadParameterModifiers, "readonly").WithArguments("readonly", "in").WithLocation(11, 23),
                // (5,24): error CS9001: The parameter modifier 'readonly' cannot be used with 'ref'; use 'in' instead.
                //     static void M2(ref readonly int i) {}
                Diagnostic(ErrorCode.ERR_ReadOnlyParameterWithRef, "readonly").WithLocation(5, 24),
                // (7,29): error CS8328:  The parameter modifier 'out' cannot be used with 'readonly'
                //     static void M3(readonly out int i) => throw null;
                Diagnostic(ErrorCode.ERR_BadParameterModifiers, "out").WithArguments("out", "readonly").WithLocation(7, 29),
                // (8,24): error CS8328:  The parameter modifier 'readonly' cannot be used with 'out'
                //     static void M4(out readonly int i) => throw null;
                Diagnostic(ErrorCode.ERR_BadParameterModifiers, "readonly").WithArguments("readonly", "out").WithLocation(8, 24),
                // (10,29): error CS8328:  The parameter modifier 'in' cannot be used with 'readonly'
                //     static void M5(readonly in int i) {}
                Diagnostic(ErrorCode.ERR_BadParameterModifiers, "in").WithArguments("in", "readonly").WithLocation(10, 29),
                // (4,29): error CS9001: The parameter modifier 'readonly' cannot be used with 'ref'; use 'in' instead.
                //     static void M1(readonly ref int i) {}
                Diagnostic(ErrorCode.ERR_ReadOnlyParameterWithRef, "ref").WithLocation(4, 29)
                );
        }

        [Fact]
        public void TestGoodReadOnlyParameterModifier()
        {
            CreateCompilationWithMscorlib46(@"
static class C 
{
    static void M1(readonly this int[] i) {}
    static void M2(this readonly int[] i) {}
 
    static void M3(readonly params int[] i) {}
    static void M4(params readonly int[] i) {}
}
").VerifyDiagnostics();
        }

        [Fact]
        public void TestReadOnlyParameterInvalidOwner()
        {
            CreateCompilationWithMscorlib46(@"
interface I
{
    void M(readonly int i);
}
abstract partial class C
{
    protected abstract void Abstract(readonly int i);

    extern void Extern(readonly int i);

    partial void Partial(readonly int i);
}
").VerifyDiagnostics(
                // (10,24): error CS0106: The modifier 'readonly' is not valid for this item
                //     extern void Extern(readonly int i);
                Diagnostic(ErrorCode.ERR_BadMemberFlag, "readonly int i").WithArguments("readonly").WithLocation(10, 24),
                // (12,26): error CS0106: The modifier 'readonly' is not valid for this item
                //     partial void Partial(readonly int i);
                Diagnostic(ErrorCode.ERR_BadMemberFlag, "readonly int i").WithArguments("readonly").WithLocation(12, 26),
                // (8,38): error CS0106: The modifier 'readonly' is not valid for this item
                //     protected abstract void Abstract(readonly int i);
                Diagnostic(ErrorCode.ERR_BadMemberFlag, "readonly int i").WithArguments("readonly").WithLocation(8, 38),
                // (4,12): error CS0106: The modifier 'readonly' is not valid for this item
                //     void M(readonly int i);
                Diagnostic(ErrorCode.ERR_BadMemberFlag, "readonly int i").WithArguments("readonly").WithLocation(4, 12),
                // (10,17): warning CS0626: Method, operator, or accessor 'C.Extern(int)' is marked external and has no attributes on it. Consider adding a DllImport attribute to specify the external implementation.
                //     extern void Extern(readonly int i);
                Diagnostic(ErrorCode.WRN_ExternMethodNoImplementation, "Extern").WithArguments("C.Extern(int)").WithLocation(10, 17)
                );
        }

        [Fact(Skip = "TODO(readonly)"]
        public void TestReadOnlyWithLetAndVar()
        {
            CreateCompilationWithMscorlib46(@"
class Test
{
    void M()
    {
        readonly var x = 0;
        readonly let y = 0;
    }
}
").VerifyDiagnostics(
                );
        }

        [Fact(Skip = "TODO(readonly)")]
        public void TestDeconstruction()
        {
            CreateCompilationWithMscorlib46(@"
class Test
{
    void M()
    {
        let (x, y) = (1, 2);
        x = 42;
        y = 42;
    }
}
" + s_additionalTypes).VerifyDiagnostics(
                );
        }
    }
}
