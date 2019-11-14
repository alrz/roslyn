﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp.UseRecursivePatterns;
using Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.CodeRefactorings;
using Xunit;

namespace Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.UseRecursivePatterns
{
    public partial class UseRecursivePatternsTests : AbstractCSharpCodeActionTest
    {
        protected override CodeRefactoringProvider CreateCodeRefactoringProvider(Workspace workspace, TestParameters parameters)
           => new CSharpUseRecursivePatternsCodeRefactoringProvider();

        [Fact]
        public async Task Test1()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    bool M()
    {
        return b.P1 == 1 [||]&& b.P2 == 2;
    }
}",
@"class C
{
    bool M()
    {
        return b is
        {
            P1: 1, P2: 2
        };
    }
}");
        }

        [Fact]
        public async Task Test2()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    bool M()
    {
        return b.MetadataName == string.Empty &&
            b.ContainingType == null &&
            b.ContainingNamespace != null &&
            b.ContainingNamespace.Name == string.Empty [||]&&
            b.ContainingNamespace.ContainingNamespace == null;
    }
}",
@"class C
{
    bool M()
    {
        return b is
        {
            MetadataName: string.Empty, ContainingType: null, ContainingNamespace:
            {
                Name: string.Empty, ContainingNamespace: null
            }
        };
    }
}");
        }

        [Fact]
        public async Task Test3()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    bool M()
    {
        return type.Name == ""ValueTuple"" &&
            type.ContainingSymbol is var declContainer &&
            declContainer.Kind == SymbolKind.Namespace [||]&&
            declContainer.Name == ""System"";
    }
}",
@"class C
{
    bool M()
    {
        return type is
        {
            Name: ""ValueTuple"", ContainingSymbol:
            {
                Name: ""System"", Kind: SymbolKind.Namespace
            }

            declContainer
        };
    }
}");
        }

        [Fact]
        public async Task Test4()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    bool M()
    {
        return type.Name == ""ValueTuple"" &&
            type.ContainingSymbol is SomeType declContainer &&
            declContainer.Kind == SymbolKind.Namespace [||]&&
            declContainer.Name == ""System"";
    }
}",
@"class C
{
    bool M()
    {
        return type is
        {
            Name: ""ValueTuple"", ContainingSymbol: SomeType
            {
                Name: ""System"", Kind: SymbolKind.Namespace
            }

            declContainer
        };
    }
}");
        }

        [Fact]
        public async Task Test5()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    bool M()
    {
        return type.Name == ""ValueTuple"" [||]&& type.IsStruct;
    }
}",
@"class C
{
    bool M()
    {
        return type is
        {
            Name: ""ValueTuple"", IsStruct: true
        };
    }
}");
        }

        [Fact]
        public async Task Test6()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    bool M()
    {
        return type.Name == ""ValueTuple"" [||]&& !type.IsClass;
    }
}",
@"class C
{
    bool M()
    {
        return type is
        {
            Name: ""ValueTuple"", IsClass: false
        };
    }
}");
        }

        [Fact]
        public async Task Test7()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    bool M()
    {
        return type.ContainingSymbol is var declContainer &&
            declContainer.Kind == SymbolKind.Namespace &&
            declContainer.Name == ""System"" [||]&&
            (declContainer.ContainingSymbol as NamespaceSymbol)?.IsGlobalNamespace == true;
    }
}",
@"class C
{
    bool M()
    {
        return type is
        {
            ContainingSymbol:
            {
                ContainingSymbol: NamespaceSymbol
                {
                    IsGlobalNamespace: true
                }

                , Name: ""System"", Kind: SymbolKind.Namespace
            }

            declContainer
        };
    }
}");
        }

        [Fact]
        public async Task Test8()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    bool M()
    {
        return (declContainer.ContainingSymbol as NamespaceSymbol)?.IsGlobalNamespace [||]== true;
    }
}",
@"class C
{
    bool M()
    {
        return declContainer is
        {
            ContainingSymbol: NamespaceSymbol
            {
                IsGlobalNamespace: true
            }
        };
    }
}");
        }

        [Fact]
        public async Task Test9()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    void M()
    {
        switch (node)
        {
            case String s [||]when s.Length == 0:
                break;
        }
    }
}",
@"class C
{
    void M()
    {
        switch (node)
        {
            case String
            {
                Length: 0
            }

            s:
                break;
        }
    }
}");
        }

        [Fact]
        public async Task Test10()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    void M()
    {
        switch (node)
        {
            case String s [||]when s.Length == 0 && b:
                break;
        }
    }
}",
@"class C
{
    void M()
    {
        switch (node)
        {
            case String
            {
                Length: 0
            }

            s when b:
                break;
        }
    }
}");
        }

        [Fact]
        public async Task Test11()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    bool M()
    {
        return b.P1 == 1 && b.P2 == 2 [||]&& x;
    }
}",
@"class C
{
    bool M()
    {
        return b is { P1: 1, P2: 2 } && x;
    }
}");
        }

        [Fact]
        public async Task Test12()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    bool M()
    {
        return x && b.P1 == 1 [||]&& b.P2 == 2;
    }
}",
@"class C
{
    bool M()
    {
        return x && b is
        {
            P1: 1, P2: 2
        };
    }
}");
        }

        [Fact]
        public async Task Test13()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    bool M()
    {
        return x is var v [||]&& v != null;
    }
}",
@"class C
{
    bool M()
    {
        return x is { } v ;
    }
}");
        }

        [Fact]
        public async Task Test14()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    bool M(C x)
    {
        return x is var v [||]&& v is object;
    }
}",
@"class C
{
    bool M(C x)
    {
        return x is { } v ;
    }
}");
        }

        [Fact]
        public async Task Test15()
        {
            await TestInRegularAndScriptAsync(
@"class C
{
    bool M(C c)
    {
        return c is var (x, y) [||]&& x is SomeType && y != null;
    }
}",
@"class C
{
    bool M(C c)
    {
        return c is (SomeType
        {
        }

        x,
        {
        }

        y);
    }
}");
        }
    }
}
