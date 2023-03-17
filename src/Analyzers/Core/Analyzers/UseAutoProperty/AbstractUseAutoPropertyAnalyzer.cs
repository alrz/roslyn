﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.UseAutoProperty
{
    internal abstract class AbstractUseAutoPropertyAnalyzer<
        TSyntaxKind,
        TPropertyDeclaration,
        TFieldDeclaration,
        TVariableDeclarator,
        TExpression> : AbstractBuiltInCodeStyleDiagnosticAnalyzer
        where TSyntaxKind : struct, Enum
        where TPropertyDeclaration : SyntaxNode
        where TFieldDeclaration : SyntaxNode
        where TVariableDeclarator : SyntaxNode
        where TExpression : SyntaxNode
    {
        protected AbstractUseAutoPropertyAnalyzer()
            : base(IDEDiagnosticIds.UseAutoPropertyDiagnosticId,
                   EnforceOnBuildValues.UseAutoProperty,
                   CodeStyleOptions2.PreferAutoProperties,
                   new LocalizableResourceString(nameof(AnalyzersResources.Use_auto_property), AnalyzersResources.ResourceManager, typeof(AnalyzersResources)),
                   new LocalizableResourceString(nameof(AnalyzersResources.Use_auto_property), AnalyzersResources.ResourceManager, typeof(AnalyzersResources)))
        {
        }

        /// <summary>
        /// A method body edit anywhere in a type will force us to reanalyze the whole type.
        /// </summary>
        /// <returns></returns>
        public override DiagnosticAnalyzerCategory GetAnalyzerCategory()
            => DiagnosticAnalyzerCategory.SemanticDocumentAnalysis;

        protected abstract bool SupportsReadOnlyProperties(Compilation compilation);
        protected abstract bool SupportsPropertyInitializer(Compilation compilation);
        protected abstract bool CanExplicitInterfaceImplementationsBeFixed();
        protected abstract TExpression? GetFieldInitializer(TVariableDeclarator variable, CancellationToken cancellationToken);
        protected abstract TExpression? GetGetterExpression(IMethodSymbol getMethod, CancellationToken cancellationToken);
        protected abstract TExpression? GetSetterExpression(IMethodSymbol setMethod, SemanticModel semanticModel, CancellationToken cancellationToken);
        protected abstract SyntaxNode GetFieldNode(TFieldDeclaration fieldDeclaration, TVariableDeclarator variableDeclarator);

        protected abstract void RegisterIneligibleFieldsAction(
            ConcurrentSet<IFieldSymbol> ineligibleFields, SemanticModel semanticModel, SyntaxNode codeBlock, CancellationToken cancellationToken);

        protected sealed override void InitializeWorker(AnalysisContext context)
            => context.RegisterSymbolStartAction(context =>
            {
                var namedType = (INamedTypeSymbol)context.Symbol;
                if (namedType.TypeKind is not TypeKind.Class and not TypeKind.Struct and not TypeKind.Module)
                    return;

                var analysisResults = new List<AnalysisResult>();
                AnalyzeTypeProperties(context.Compilation, context.Options, namedType, analysisResults, context.CancellationToken);

                var ineligibleFields = new ConcurrentSet<IFieldSymbol>();
                context.RegisterCodeBlockStartAction<TSyntaxKind>(context =>
                    RegisterIneligibleFieldsAction(ineligibleFields, context.SemanticModel, context.CodeBlock, context.CancellationToken));

                context.RegisterSymbolEndAction(context =>
                    Process(analysisResults, ineligibleFields, context));
            }, SymbolKind.NamedType);

        private void AnalyzeTypeProperties(
            Compilation compilation, AnalyzerOptions options, INamedTypeSymbol namedType, List<AnalysisResult> analysisResults, CancellationToken cancellationToken)
        {
            foreach (var member in namedType.GetMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (member is IPropertySymbol property)
                    AnalyzeTypeProperty(compilation, options, namedType, property, analysisResults, cancellationToken);
            }
        }

        private void AnalyzeTypeProperty(
            Compilation compilation, AnalyzerOptions options, INamedTypeSymbol containingType, IPropertySymbol property, List<AnalysisResult> analysisResults, CancellationToken cancellationToken)
        {
            if (property.IsIndexer)
                return;

            // The property can't be virtual.  We don't know if it is overridden somewhere.  If it 
            // is, then calls to it may not actually assign to the field.
            if (property.IsVirtual || property.IsOverride || property.IsSealed)
                return;

            if (property.IsWithEvents)
                return;

            if (property.Parameters.Length > 0)
                return;

            // Need at least a getter.
            if (property.GetMethod == null)
                return;

            if (!CanExplicitInterfaceImplementationsBeFixed() && property.ExplicitInterfaceImplementations.Length != 0)
                return;

            // Serializable types can depend on fields (and their order).  Don't report these
            // properties in that case.
            if (containingType.IsSerializable)
                return;

            if (property.DeclaringSyntaxReferences is not [var propertyReference])
                return;

            if (propertyReference.GetSyntax(cancellationToken) is not TPropertyDeclaration propertyDeclaration)
                return;

            var preferAutoProps = options.GetAnalyzerOptions(propertyDeclaration.SyntaxTree).PreferAutoProperties;
            if (!preferAutoProps.Value)
                return;

            // Avoid reporting diagnostics when the feature is disabled. This primarily avoids reporting the hidden
            // helper diagnostic which is not otherwise influenced by the severity settings.
            var severity = preferAutoProps.Notification.Severity;
            if (severity == ReportDiagnostic.Suppress)
                return;

#pragma warning disable RS1030 // Do not invoke Compilation.GetSemanticModel() method within a diagnostic analyzer
            var semanticModel = compilation.GetSemanticModel(propertyDeclaration.SyntaxTree);
#pragma warning restore RS1030 // Do not invoke Compilation.GetSemanticModel() method within a diagnostic analyzer
            var getterField = GetGetterField(semanticModel, property.GetMethod, cancellationToken);
            if (getterField == null)
                return;

            // Only support this for private fields.  It limits the scope of hte program
            // we have to analyze to make sure this is safe to do.
            if (getterField.DeclaredAccessibility != Accessibility.Private)
                return;

            // If the user made the field readonly, we only want to convert it to a property if we
            // can keep it readonly.
            if (getterField.IsReadOnly && !SupportsReadOnlyProperties(compilation))
                return;

            // Field and property have to be in the same type.
            if (!containingType.Equals(getterField.ContainingType))
                return;

            // Property and field have to agree on type.
            if (!property.Type.Equals(getterField.Type))
                return;

            // Mutable value type fields are mutable unless they are marked read-only
            if (!getterField.IsReadOnly && getterField.Type.IsMutableValueType() != false)
                return;

            // Don't want to remove constants and volatile fields.
            if (getterField.IsConst || getterField.IsVolatile)
                return;

            // Field and property should match in static-ness
            if (getterField.IsStatic != property.IsStatic)
                return;

            var fieldReference = getterField.DeclaringSyntaxReferences[0];
            if (fieldReference.GetSyntax(cancellationToken) is not TVariableDeclarator variableDeclarator)
                return;

            if (variableDeclarator.Parent?.Parent is not TFieldDeclaration fieldDeclaration)
                return;

            // A setter is optional though.
            var setMethod = property.SetMethod;
            if (setMethod != null)
            {
                var setterField = GetSetterField(semanticModel, setMethod, cancellationToken);
                // If there is a getter and a setter, they both need to agree on which field they are 
                // writing to.
                if (setterField != getterField)
                    return;
            }

            var initializer = GetFieldInitializer(variableDeclarator, cancellationToken);
            if (initializer != null && !SupportsPropertyInitializer(compilation))
                return;

            // Can't remove the field if it has attributes on it.
            var attributes = getterField.GetAttributes();
            var suppressMessageAttributeType = compilation.SuppressMessageAttributeType();
            foreach (var attribute in attributes)
            {
                if (attribute.AttributeClass != suppressMessageAttributeType)
                    return;
            }

            if (!CanConvert(property))
                return;

            // Check if there are additional, language specific, reasons we think this field might be ineligible for 
            // replacing with an auto prop.
            if (!IsEligibleHeuristic(getterField, propertyDeclaration, semanticModel, cancellationToken))
                return;

            // Looks like a viable property/field to convert into an auto property.
            analysisResults.Add(new AnalysisResult(getterField, propertyDeclaration, fieldDeclaration, variableDeclarator, severity));
        }

        protected virtual bool CanConvert(IPropertySymbol property)
            => true;

        private IFieldSymbol? GetSetterField(SemanticModel semanticModel, IMethodSymbol setMethod, CancellationToken cancellationToken)
            => CheckFieldAccessExpression(semanticModel, GetSetterExpression(setMethod, semanticModel, cancellationToken), cancellationToken);

        private IFieldSymbol? GetGetterField(SemanticModel semanticModel, IMethodSymbol getMethod, CancellationToken cancellationToken)
            => CheckFieldAccessExpression(semanticModel, GetGetterExpression(getMethod, cancellationToken), cancellationToken);

        private static IFieldSymbol? CheckFieldAccessExpression(SemanticModel semanticModel, TExpression? expression, CancellationToken cancellationToken)
        {
            if (expression == null)
                return null;

            var symbolInfo = semanticModel.GetSymbolInfo(expression, cancellationToken);
            return symbolInfo.Symbol is IFieldSymbol { DeclaringSyntaxReferences.Length: 1 } field
                ? field
                : null;
        }

        private void Process(
            List<AnalysisResult> analysisResults,
            ConcurrentSet<IFieldSymbol> ineligibleFields,
            SymbolAnalysisContext context)
        {
            foreach (var result in analysisResults)
            {
                if (!ineligibleFields.Contains(result.Field))
                    Process(result, context);
            }
        }

        private void Process(AnalysisResult result, SymbolAnalysisContext context)
        {
            var propertyDeclaration = result.PropertyDeclaration;
            var variableDeclarator = result.VariableDeclarator;
            var fieldNode = GetFieldNode(result.FieldDeclaration, variableDeclarator);

            // Now add diagnostics to both the field and the property saying we can convert it to 
            // an auto property.  For each diagnostic store both location so we can easily retrieve
            // them when performing the code fix.
            var additionalLocations = ImmutableArray.Create(
                propertyDeclaration.GetLocation(),
                variableDeclarator.GetLocation());

            // Place the appropriate marker on the field depending on the user option.
            var diagnostic1 = DiagnosticHelper.Create(
                Descriptor,
                fieldNode.GetLocation(),
                result.Severity,
                additionalLocations: additionalLocations,
                properties: null);

            // Also, place a hidden marker on the property.  If they bring up a lightbulb
            // there, they'll be able to see that they can convert it to an auto-prop.
            var diagnostic2 = Diagnostic.Create(
                Descriptor, propertyDeclaration.GetLocation(),
                additionalLocations: additionalLocations);

            context.ReportDiagnostic(diagnostic1);
            context.ReportDiagnostic(diagnostic2);
        }

        protected virtual bool IsEligibleHeuristic(
            IFieldSymbol field, TPropertyDeclaration propertyDeclaration, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            return true;
        }

        private sealed record AnalysisResult(
            IFieldSymbol Field,
            TPropertyDeclaration PropertyDeclaration,
            TFieldDeclaration FieldDeclaration,
            TVariableDeclarator VariableDeclarator,
            ReportDiagnostic Severity);
    }
}
