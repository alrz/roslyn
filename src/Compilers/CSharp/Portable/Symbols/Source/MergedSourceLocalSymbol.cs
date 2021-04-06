// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using Roslyn.Utilities;
using System.Linq;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    internal sealed class MergedSourceLocalSymbol : SourceLocalSymbol
    {
        private readonly ImmutableArray<SourceLocalSymbol> _locals;
        private SourceLocalSymbol First => _locals[0];

        public MergedSourceLocalSymbol(ImmutableArray<SourceLocalSymbol> locals)
            : base(locals[0]._containingSymbol, locals[0]._scopeBinder, locals.SelectMany(v => v.Locations).ToImmutableArray())
        {
            _locals = locals;
        }

        internal override LocalDeclarationKind DeclarationKind => LocalDeclarationKind.PatternVariable;

        public override string Name => First.Name;

        internal override SyntaxToken IdentifierToken => throw ExceptionUtilities.Unreachable;

        public override TypeWithAnnotations TypeWithAnnotations => First.TypeWithAnnotations;

        public override bool IsVar => false;

        internal override void SetTypeWithAnnotations(TypeWithAnnotations newType)
        {
            // TODO(alrz) check identical types and return false if not matching
            foreach (var item in _locals)
                item.SetTypeWithAnnotations(newType);
        }

        internal override SyntaxNode GetDeclaratorSyntax()
        {
            return First.GetDeclaratorSyntax();
        }

        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences
            => _locals.SelectMany(local => local.DeclaringSyntaxReferences).ToImmutableArray();

        public override RefKind RefKind => RefKind.None;

        public override int GetHashCode()
        {
            var hash = Hash.Combine(Hash.CombineValues(this._locals.Select(l => l.IdentifierToken)), _containingSymbol.GetHashCode());
            return hash;
        }

        public sealed override bool Equals(Symbol obj, TypeCompareKind compareKind)
        {
            if (obj == (object)this)
            {
                return true;
            }

            // If we're comparing against a symbol that was wrapped and updated for nullable,
            // delegate to its handling of equality, rather than our own.
            if (obj is UpdatedContainingSymbolAndNullableAnnotationLocal updated)
            {
                return updated.Equals(this, compareKind);
            }

            return obj is MergedSourceLocalSymbol symbol
                   && symbol._locals.SequenceEqual(_locals)
                   && symbol._containingSymbol.Equals(_containingSymbol, compareKind);
        }
    }
}
