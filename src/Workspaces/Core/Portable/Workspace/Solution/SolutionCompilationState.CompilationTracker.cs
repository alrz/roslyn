﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.ErrorReporting;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Internal.Log;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Collections;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis
{
    internal partial class SolutionCompilationState
    {
        /// <summary>
        /// Tracks the changes made to a project and provides the facility to get a lazily built
        /// compilation for that project.  As the compilation is being built, the partial results are
        /// stored as well so that they can be used in the 'in progress' workspace snapshot.
        /// </summary>
        private partial class CompilationTracker : ICompilationTracker
        {
            private static readonly Func<ProjectState, string> s_logBuildCompilationAsync =
                state => string.Join(",", state.AssemblyName, state.DocumentStates.Count);

            public ProjectState ProjectState { get; }

            /// <summary>
            /// Access via the <see cref="ReadState"/> and <see cref="WriteState"/> methods.
            /// </summary>
            private CompilationTrackerState? _stateDoNotAccessDirectly;

            // guarantees only one thread is building at a time
            private SemaphoreSlim? _buildLock;

            public SkeletonReferenceCache SkeletonReferenceCache { get; }

            /// <summary>
            /// Set via a feature flag to enable strict validation of the compilations that are produced, in that they match the original states. This validation is expensive, so we don't want it
            /// running in normal production scenarios.
            /// </summary>
            private readonly bool _validateStates;

            private CompilationTracker(
                ProjectState project,
                CompilationTrackerState? state,
                SkeletonReferenceCache cachedSkeletonReferences)
            {
                Contract.ThrowIfNull(project);

                this.ProjectState = project;
                _stateDoNotAccessDirectly = state;
                this.SkeletonReferenceCache = cachedSkeletonReferences;

                _validateStates = project.LanguageServices.SolutionServices.GetRequiredService<IWorkspaceConfigurationService>().Options.ValidateCompilationTrackerStates;

                ValidateState(state);
            }

            /// <summary>
            /// Creates a tracker for the provided project.  The tracker will be in the 'empty' state
            /// and will have no extra information beyond the project itself.
            /// </summary>
            public CompilationTracker(ProjectState project)
                : this(project, state: null, cachedSkeletonReferences: new())
            {
            }

            private CompilationTrackerState? ReadState()
                => Volatile.Read(ref _stateDoNotAccessDirectly);

            private void WriteState(CompilationTrackerState state)
            {
                Volatile.Write(ref _stateDoNotAccessDirectly, state);
                ValidateState(state);
            }

            public GeneratorDriver? GeneratorDriver
            {
                get
                {
                    var state = this.ReadState();
                    return state?.GeneratorInfo.Driver;
                }
            }

            public bool ContainsAssemblyOrModuleOrDynamic(ISymbol symbol, bool primary, out MetadataReferenceInfo? referencedThrough)
            {
                Debug.Assert(symbol.Kind is SymbolKind.Assembly or
                             SymbolKind.NetModule or
                             SymbolKind.DynamicType);
                var state = this.ReadState();

                var unrootedSymbolSet = (state as FinalCompilationTrackerState)?.UnrootedSymbolSet;
                if (unrootedSymbolSet == null)
                {
                    // this was not a tracker that has handed out a compilation (all compilations handed out must be
                    // owned by a 'FinalState').  So this symbol could not be from us.
                    referencedThrough = null;
                    return false;
                }

                return unrootedSymbolSet.Value.ContainsAssemblyOrModuleOrDynamic(symbol, primary, out referencedThrough);
            }

            /// <summary>
            /// Creates a new instance of the compilation info, retaining any already built
            /// compilation state as the now 'old' state
            /// </summary>
            public ICompilationTracker Fork(
                ProjectState newProjectState,
                TranslationAction? translate)
            {
                var forkedTrackerState = ForkTrackerState();

                // We should never fork into a FinalCompilationTrackerState.  We must always be at some state prior to
                // it since some change has happened, and we may now need to run generators.
                Contract.ThrowIfTrue(forkedTrackerState is FinalCompilationTrackerState);
                Contract.ThrowIfFalse(forkedTrackerState is null or InProgressState);
                return new CompilationTracker(
                    newProjectState,
                    forkedTrackerState,
                    this.SkeletonReferenceCache.Clone());

                CompilationTrackerState? ForkTrackerState()
                {
                    var state = this.ReadState();
                    if (state is null)
                        return null;

                    var (compilationWithoutGeneratedDocuments, staleCompilationWithGeneratedDocuments) = state switch
                    {
                        InProgressState inProgressState => (inProgressState.CompilationWithoutGeneratedDocuments, inProgressState.StaleCompilationWithGeneratedDocuments),
                        FinalCompilationTrackerState finalState => (finalState.CompilationWithoutGeneratedDocuments, finalState.FinalCompilationWithGeneratedDocuments),
                        _ => throw ExceptionUtilities.UnexpectedValue(state.GetType()),
                    };

                    var finalSteps = UpdatePendingTranslationActions(
                        state switch
                        {
                            InProgressState inProgressState => inProgressState.PendingTranslationActions,
                            FinalCompilationTrackerState => [],
                            _ => throw ExceptionUtilities.UnexpectedValue(state.GetType()),
                        });

                    var newState = InProgressState.Create(
                        state.IsFrozen,
                        compilationWithoutGeneratedDocuments,
                        state.GeneratorInfo,
                        staleCompilationWithGeneratedDocuments,
                        finalSteps);

                    return newState;
                }

                ImmutableList<TranslationAction> UpdatePendingTranslationActions(
                    ImmutableList<TranslationAction> pendingTranslationActions)
                {
                    if (translate is null)
                        return pendingTranslationActions;

                    // We have a translation action; are we able to merge it with the prior one?
                    if (!pendingTranslationActions.IsEmpty)
                    {
                        var priorAction = pendingTranslationActions.Last();
                        var mergedTranslation = translate.TryMergeWithPrior(priorAction);
                        if (mergedTranslation != null)
                        {
                            // We can replace the prior action with this new one
                            return pendingTranslationActions.SetItem(
                                pendingTranslationActions.Count - 1,
                                mergedTranslation);
                        }
                    }

                    // Just add it to the end
                    return pendingTranslationActions.Add(translate);
                }
            }

            /// <summary>
            /// Gets the final compilation if it is available.
            /// </summary>
            public bool TryGetCompilation([NotNullWhen(true)] out Compilation? compilation)
            {
                var state = ReadState();
                if (state is FinalCompilationTrackerState finalState)
                {
                    compilation = finalState.FinalCompilationWithGeneratedDocuments;
                    Contract.ThrowIfNull(compilation);
                    return true;
                }
                else
                {
                    compilation = null;
                    return false;
                }
            }

            public Task<Compilation> GetCompilationAsync(SolutionCompilationState compilationState, CancellationToken cancellationToken)
            {
                if (this.TryGetCompilation(out var compilation))
                {
                    return Task.FromResult(compilation);
                }
                else if (cancellationToken.IsCancellationRequested)
                {
                    // Handle early cancellation here to avoid throwing/catching cancellation exceptions in the async
                    // state machines. This helps reduce the total number of First Chance Exceptions occurring in IDE
                    // typing scenarios.
                    return Task.FromCanceled<Compilation>(cancellationToken);
                }
                else
                {
                    return GetCompilationSlowAsync(compilationState, cancellationToken);
                }
            }

            private async Task<Compilation> GetCompilationSlowAsync(
                SolutionCompilationState compilationState, CancellationToken cancellationToken)
            {
                var finalState = await GetOrBuildFinalStateAsync(compilationState, cancellationToken: cancellationToken).ConfigureAwait(false);
                return finalState.FinalCompilationWithGeneratedDocuments;
            }

            private async Task<FinalCompilationTrackerState> GetOrBuildFinalStateAsync(
                SolutionCompilationState compilationState,
                CancellationToken cancellationToken)
            {
                try
                {
                    using (Logger.LogBlock(FunctionId.Workspace_Project_CompilationTracker_BuildCompilationAsync,
                                           s_logBuildCompilationAsync, ProjectState, cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var state = ReadState();

                        // Try to get the built compilation.  If it exists, then we can just return that.
                        if (state is FinalCompilationTrackerState finalState)
                            return finalState;

                        var buildLock = InterlockedOperations.Initialize(
                            ref _buildLock,
                            static () => new SemaphoreSlim(initialCount: 1));

                        // Otherwise, we actually have to build it.  Ensure that only one thread is trying to
                        // build this compilation at a time.
                        using (await buildLock.DisposableWaitAsync(cancellationToken).ConfigureAwait(false))
                        {
                            return await BuildFinalStateAsync().ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception e) when (FatalError.ReportAndPropagateUnlessCanceled(e, cancellationToken, ErrorSeverity.Critical))
                {
                    throw ExceptionUtilities.Unreachable();
                }

                // <summary>
                // Builds the compilation matching the project state. In the process of building, also
                // produce in progress snapshots that can be accessed from other threads.
                // </summary>
                async Task<FinalCompilationTrackerState> BuildFinalStateAsync()
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var state = ReadState();

                    // if we already have a compilation, we must be already done!  This can happen if two
                    // threads were waiting to build, and we came in after the other succeeded.
                    if (state is FinalCompilationTrackerState finalState)
                        return finalState;

                    // Transition from wherever we're currently at to the 'all trees parsed' state.
                    var expandedInProgressState = state switch
                    {
                        InProgressState inProgressState => inProgressState,

                        // We've got nothing.  Build it from scratch :(
                        null => await BuildInProgressStateFromNoCompilationStateAsync().ConfigureAwait(false),

                        _ => throw ExceptionUtilities.UnexpectedValue(state.GetType())
                    };

                    // Now do the final step of transitioning from the 'all trees parsed' state to the final state.
                    var collapsedInProgressState = await CollapseInProgressStateAsync(expandedInProgressState).ConfigureAwait(false);
                    return await FinalizeCompilationAsync(collapsedInProgressState).ConfigureAwait(false);
                }

                [PerformanceSensitive(
                    "https://github.com/dotnet/roslyn/issues/23582",
                    Constraint = "Avoid calling " + nameof(Compilation.AddSyntaxTrees) + " in a loop due to allocation overhead.")]

                async Task<InProgressState> BuildInProgressStateFromNoCompilationStateAsync()
                {
                    try
                    {
                        var compilation = CreateEmptyCompilation();

                        var trees = await GetAllSyntaxTreesAsync(
                            this.ProjectState.DocumentStates.GetStatesInCompilationOrder(),
                            this.ProjectState.DocumentStates.Count,
                            cancellationToken).ConfigureAwait(false);

                        compilation = compilation.AddSyntaxTrees(trees);

                        // We only got here when we had no compilation state at all.  So we couldn't have gotten
                        // here from a frozen state (as a frozen state always ensures we have a
                        // WithCompilationTrackerState).  As such, we can safely still preserve that we're not
                        // frozen here.
                        var allSyntaxTreesParsedState = InProgressState.Create(
                            isFrozen: false, compilation, CompilationTrackerGeneratorInfo.Empty, staleCompilationWithGeneratedDocuments: null,
                            pendingTranslationActions: []);

                        WriteState(allSyntaxTreesParsedState);
                        return allSyntaxTreesParsedState;
                    }
                    catch (Exception e) when (FatalError.ReportAndPropagateUnlessCanceled(e, cancellationToken, ErrorSeverity.Critical))
                    {
                        throw ExceptionUtilities.Unreachable();
                    }
                }

                async Task<InProgressState> CollapseInProgressStateAsync(InProgressState initialState)
                {
                    try
                    {
                        var currentState = initialState;
                        while (currentState.PendingTranslationActions.Count > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            // We have a list of transformations to get to our final compilation; take the first transformation and apply it.
                            var (compilationWithoutGeneratedDocuments, staleCompilationWithGeneratedDocuments, generatorInfo) =
                                await ApplyFirstTransformationAsync(currentState).ConfigureAwait(false);

                            // We have updated state, so store this new result; this allows us to drop the intermediate state we already processed
                            // even if we were to get cancelled at a later point.
                            //
                            // As long as we have intermediate projects, we'll still keep creating InProgressStates.  But
                            // once it becomes empty we'll produce an AllSyntaxTreesParsedState and we'll break the loop.
                            //
                            // Preserve the current frozen bit.  Specifically, once states become frozen, we continually make
                            // all states forked from those states frozen as well.  This ensures we don't attempt to move
                            // generator docs back to the uncomputed state from that point onwards.  We'll just keep
                            // whateverZ generated docs we have.
                            currentState = InProgressState.Create(
                                currentState.IsFrozen,
                                compilationWithoutGeneratedDocuments,
                                generatorInfo,
                                staleCompilationWithGeneratedDocuments,
                                currentState.PendingTranslationActions.RemoveAt(0));
                            this.WriteState(currentState);
                        }

                        return currentState;
                    }
                    catch (Exception e) when (FatalError.ReportAndPropagateUnlessCanceled(e, cancellationToken, ErrorSeverity.Critical))
                    {
                        throw ExceptionUtilities.Unreachable();
                    }

                    async Task<(Compilation compilationWithoutGeneratedDocuments, Compilation? staleCompilationWithGeneratedDocuments, CompilationTrackerGeneratorInfo generatorInfo)>
                        ApplyFirstTransformationAsync(InProgressState inProgressState)
                    {
                        Contract.ThrowIfTrue(inProgressState.PendingTranslationActions.IsEmpty);
                        var translationAction = inProgressState.PendingTranslationActions[0];

                        var compilationWithoutGeneratedDocuments = inProgressState.CompilationWithoutGeneratedDocuments;
                        var staleCompilationWithGeneratedDocuments = inProgressState.StaleCompilationWithGeneratedDocuments;

                        // If staleCompilationWithGeneratedDocuments is the same as compilationWithoutGeneratedDocuments,
                        // then it means a prior run of generators didn't produce any files. In that case, we'll just make
                        // staleCompilationWithGeneratedDocuments null so we avoid doing any transformations of it multiple
                        // times. Otherwise the transformations below and in FinalizeCompilationAsync will try to update
                        // both at once, which is functionally fine but just unnecessary work. This function is always
                        // allowed to return null for AllSyntaxTreesParsedState.StaleCompilationWithGeneratedDocuments in
                        // the end, so there's no harm there.
                        if (staleCompilationWithGeneratedDocuments == compilationWithoutGeneratedDocuments)
                            staleCompilationWithGeneratedDocuments = null;

                        compilationWithoutGeneratedDocuments = await translationAction.TransformCompilationAsync(compilationWithoutGeneratedDocuments, cancellationToken).ConfigureAwait(false);

                        if (staleCompilationWithGeneratedDocuments != null)
                        {
                            // Also transform the compilation that has generated files; we won't do that though if the transformation either would cause problems with
                            // the generated documents, or if don't have any source generators in the first place.
                            if (translationAction.CanUpdateCompilationWithStaleGeneratedTreesIfGeneratorsGiveSameOutput &&
                                translationAction.OldProjectState.SourceGenerators.Any())
                            {
                                staleCompilationWithGeneratedDocuments = await translationAction.TransformCompilationAsync(staleCompilationWithGeneratedDocuments, cancellationToken).ConfigureAwait(false);
                            }
                            else
                            {
                                staleCompilationWithGeneratedDocuments = null;
                            }
                        }

                        var generatorInfo = inProgressState.GeneratorInfo;
                        if (generatorInfo.Driver != null)
                            generatorInfo = generatorInfo with { Driver = translationAction.TransformGeneratorDriver(generatorInfo.Driver) };

                        return (compilationWithoutGeneratedDocuments, staleCompilationWithGeneratedDocuments, generatorInfo);
                    }
                }

                // <summary>
                // Add all appropriate references to the compilation and set it as our final compilation state.
                // </summary>
                async Task<FinalCompilationTrackerState> FinalizeCompilationAsync(InProgressState inProgressState)
                {
                    try
                    {
                        return await FinalizeCompilationWorkerAsync(inProgressState).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // Explicitly force a yield point here.  This addresses a problem on .net framework where it's
                        // possible that cancelling this task chain ends up stack overflowing as the TPL attempts to
                        // synchronously recurse through the tasks to execute antecedent work.  This will force continuations
                        // here to run asynchronously preventing the stack overflow.
                        // See https://github.com/dotnet/roslyn/issues/56356 for more details.
                        // note: this can be removed if this code only needs to run on .net core (as the stack overflow issue
                        // does not exist there).
                        await Task.Yield().ConfigureAwait(false);
                        throw;
                    }
                    catch (Exception e) when (FatalError.ReportAndPropagateUnlessCanceled(e, cancellationToken, ErrorSeverity.Critical))
                    {
                        throw ExceptionUtilities.Unreachable();
                    }
                }

                async Task<FinalCompilationTrackerState> FinalizeCompilationWorkerAsync(InProgressState inProgressState)
                {
                    // Caller should collapse the in progress state first.
                    Contract.ThrowIfTrue(inProgressState.PendingTranslationActions.Count > 0);

                    // The final state we produce will be frozen or not depending on if a frozen state was passed into it.
                    var isFrozen = inProgressState.IsFrozen;
                    var generatorInfo = inProgressState.GeneratorInfo;
                    var compilationWithoutGeneratedDocuments = inProgressState.CompilationWithoutGeneratedDocuments;
                    var staleCompilationWithGeneratedDocuments = inProgressState.StaleCompilationWithGeneratedDocuments;

                    // Project is complete only if the following are all true:
                    //  1. HasAllInformation flag is set for the project
                    //  2. Either the project has non-zero metadata references OR this is the corlib project.
                    //     For the latter, we use a heuristic if the underlying compilation defines "System.Object" type.
                    var hasSuccessfullyLoaded = this.ProjectState.HasAllInformation &&
                        (this.ProjectState.MetadataReferences.Count > 0 ||
                         compilationWithoutGeneratedDocuments.GetTypeByMetadataName("System.Object") != null);

                    var newReferences = new List<MetadataReference>();
                    var metadataReferenceToProjectId = new Dictionary<MetadataReference, ProjectId>();
                    newReferences.AddRange(this.ProjectState.MetadataReferences);

                    foreach (var projectReference in this.ProjectState.ProjectReferences)
                    {
                        var referencedProject = compilationState.SolutionState.GetProjectState(projectReference.ProjectId);

                        // Even though we're creating a final compilation (vs. an in progress compilation),
                        // it's possible that the target project has been removed.
                        if (referencedProject is null)
                            continue;

                        // If both projects are submissions, we'll count this as a previous submission link
                        // instead of a regular metadata reference
                        if (referencedProject.IsSubmission)
                        {
                            // if the referenced project is a submission project must be a submission as well:
                            Debug.Assert(this.ProjectState.IsSubmission);

                            // We now need to (potentially) update the prior submission compilation. That Compilation is held in the
                            // ScriptCompilationInfo that we need to replace as a unit.
                            var previousSubmissionCompilation =
                                await compilationState.GetCompilationAsync(
                                    projectReference.ProjectId, cancellationToken).ConfigureAwait(false);

                            if (compilationWithoutGeneratedDocuments.ScriptCompilationInfo!.PreviousScriptCompilation != previousSubmissionCompilation)
                            {
                                compilationWithoutGeneratedDocuments = compilationWithoutGeneratedDocuments.WithScriptCompilationInfo(
                                    compilationWithoutGeneratedDocuments.ScriptCompilationInfo!.WithPreviousScriptCompilation(previousSubmissionCompilation!));

                                staleCompilationWithGeneratedDocuments = staleCompilationWithGeneratedDocuments?.WithScriptCompilationInfo(
                                    staleCompilationWithGeneratedDocuments.ScriptCompilationInfo!.WithPreviousScriptCompilation(previousSubmissionCompilation!));
                            }
                        }
                        else
                        {
                            // Not a submission.  Add as a metadata reference.

                            if (isFrozen)
                            {
                                // In the frozen case, attempt to get a partial reference, or fallback to the last
                                // successful reference for this project if we can find one. 
                                var metadataReference = compilationState.GetPartialMetadataReference(projectReference, this.ProjectState);

                                if (metadataReference is null)
                                {
                                    // if we failed to get the metadata and we were frozen, check to see if we
                                    // previously had existing metadata and reuse it instead.
                                    var inProgressCompilationNotRef = staleCompilationWithGeneratedDocuments ?? compilationWithoutGeneratedDocuments;
                                    metadataReference = inProgressCompilationNotRef.ExternalReferences.FirstOrDefault(
                                        r => GetProjectId(inProgressCompilationNotRef.GetAssemblyOrModuleSymbol(r) as IAssemblySymbol) == projectReference.ProjectId);
                                }

                                AddMetadataReference(projectReference, metadataReference);
                            }
                            else
                            {
                                // For the non-frozen case, attempt to get the full metadata reference.
                                var metadataReference = await compilationState.GetMetadataReferenceAsync(projectReference, this.ProjectState, cancellationToken).ConfigureAwait(false);
                                AddMetadataReference(projectReference, metadataReference);
                            }
                        }
                    }

                    // Now that we know the set of references this compilation should have, update them if they're not already.
                    // Generators cannot add references, so we can use the same set of references both for the compilation
                    // that doesn't have generated files, and the one we're trying to reuse that has generated files.
                    // Since we updated both of these compilations together in response to edits, we only have to check one
                    // for a potential mismatch.
                    if (!Enumerable.SequenceEqual(compilationWithoutGeneratedDocuments.ExternalReferences, newReferences))
                    {
                        compilationWithoutGeneratedDocuments = compilationWithoutGeneratedDocuments.WithReferences(newReferences);
                        staleCompilationWithGeneratedDocuments = staleCompilationWithGeneratedDocuments?.WithReferences(newReferences);
                    }

                    // We will finalize the compilation by adding full contents here.
                    var (compilationWithGeneratedDocuments, generatedDocuments, generatorDriver) = await AddExistingOrComputeNewGeneratorInfoAsync(
                        isFrozen,
                        compilationState,
                        compilationWithoutGeneratedDocuments,
                        generatorInfo,
                        staleCompilationWithGeneratedDocuments,
                        cancellationToken).ConfigureAwait(false);

                    // After producing the sg documents, we must always be in the final state for the generator data.
                    var nextGeneratorInfo = new CompilationTrackerGeneratorInfo(generatedDocuments, generatorDriver);

                    var finalState = FinalCompilationTrackerState.Create(
                        isFrozen,
                        compilationWithGeneratedDocuments,
                        compilationWithoutGeneratedDocuments,
                        hasSuccessfullyLoaded,
                        nextGeneratorInfo,
                        this.ProjectState.Id,
                        metadataReferenceToProjectId);

                    this.WriteState(finalState);

                    return finalState;

                    void AddMetadataReference(ProjectReference projectReference, MetadataReference? metadataReference)
                    {
                        // A reference can fail to be created if a skeleton assembly could not be constructed.
                        if (metadataReference != null)
                        {
                            newReferences.Add(metadataReference);
                            metadataReferenceToProjectId.Add(metadataReference, projectReference.ProjectId);
                        }
                        else
                        {
                            hasSuccessfullyLoaded = false;
                        }
                    }
                }
            }

            private Compilation CreateEmptyCompilation()
            {
                var compilationFactory = this.ProjectState.LanguageServices.GetRequiredService<ICompilationFactoryService>();

                if (this.ProjectState.IsSubmission)
                {
                    return compilationFactory.CreateSubmissionCompilation(
                        this.ProjectState.AssemblyName,
                        this.ProjectState.CompilationOptions!,
                        this.ProjectState.HostObjectType);
                }
                else
                {
                    return compilationFactory.CreateCompilation(
                        this.ProjectState.AssemblyName,
                        this.ProjectState.CompilationOptions!);
                }
            }

            /// <summary>
            /// Attempts to get (without waiting) a metadata reference to a possibly in progress
            /// compilation. Only actual compilation references are returned. Could potentially 
            /// return null if nothing can be provided.
            /// </summary>
            public MetadataReference? GetPartialMetadataReference(ProjectState fromProject, ProjectReference projectReference)
            {
                if (ProjectState.LanguageServices == fromProject.LanguageServices)
                {
                    // if we have a compilation and its the correct language, use a simple compilation reference in any
                    // state it happens to be in right now
                    if (ReadState() is CompilationTrackerState compilationState)
                        return compilationState.CompilationWithoutGeneratedDocuments.ToMetadataReference(projectReference.Aliases, projectReference.EmbedInteropTypes);
                }
                else
                {
                    // Cross project reference.  We need a skeleton reference.  Skeletons are too expensive to
                    // generate on demand.  So just try to see if we can grab the last generated skeleton for that
                    // project.
                    var properties = new MetadataReferenceProperties(aliases: projectReference.Aliases, embedInteropTypes: projectReference.EmbedInteropTypes);
                    return this.SkeletonReferenceCache.TryGetAlreadyBuiltMetadataReference(properties);
                }

                return null;
            }

            public Task<bool> HasSuccessfullyLoadedAsync(
                SolutionCompilationState compilationState, CancellationToken cancellationToken)
            {
                return this.ReadState() is FinalCompilationTrackerState finalState
                    ? finalState.HasSuccessfullyLoaded ? SpecializedTasks.True : SpecializedTasks.False
                    : HasSuccessfullyLoadedSlowAsync(compilationState, cancellationToken);
            }

            private async Task<bool> HasSuccessfullyLoadedSlowAsync(
                SolutionCompilationState compilationState, CancellationToken cancellationToken)
            {
                var finalState = await GetOrBuildFinalStateAsync(
                    compilationState, cancellationToken: cancellationToken).ConfigureAwait(false);
                return finalState.HasSuccessfullyLoaded;
            }

            public ICompilationTracker FreezePartialState(CancellationToken cancellationToken)
            {
                var state = this.ReadState();

                var clonedCache = this.SkeletonReferenceCache.Clone();
                if (state is FinalCompilationTrackerState finalState)
                {
                    // If we're finalized and already frozen, we can just use ourselves. Otherwise, flip the frozen bit
                    // so that any future forks keep things frozen.
                    return finalState.IsFrozen
                        ? this
                        : new CompilationTracker(this.ProjectState, finalState.WithIsFrozen(), clonedCache);
                }

                // Non-final state currently.  Produce an in-progress-state containing the forked change. Note: we
                // transition to in-progress-state here (and not final-state) as we still want to leverage all the
                // final-state-transition logic contained in FinalizeCompilationAsync (for example, properly setting
                // up all references).
                if (state is null)
                {
                    // We may have already parsed some of the documents in this compilation.  For example, if we're
                    // partway through the logic in BuildInProgressStateFromNoCompilationStateAsync.  If so, move those
                    // parsed documents over to the new project state so we can preserve as much information as
                    // possible.

                    using var _1 = ArrayBuilder<DocumentState>.GetInstance(out var documentsWithTrees);
                    using var _2 = ArrayBuilder<SyntaxTree>.GetInstance(out var alreadyParsedTrees);

                    foreach (var documentState in this.ProjectState.DocumentStates.GetStatesInCompilationOrder())
                    {
                        if (documentState.TryGetSyntaxTree(out var alreadyParsedTree))
                        {
                            documentsWithTrees.Add(documentState);
                            alreadyParsedTrees.Add(alreadyParsedTree);
                        }
                    }

                    // Transition us to a state that only has documents for the files we've already parsed.
                    var frozenProjectState = this.ProjectState
                        .RemoveAllDocuments()
                        .AddDocuments(documentsWithTrees.ToImmutableAndClear());

                    var compilationWithoutGeneratedDocuments = this.CreateEmptyCompilation().AddSyntaxTrees(alreadyParsedTrees);
                    var compilationWithGeneratedDocuments = compilationWithoutGeneratedDocuments;

                    return new CompilationTracker(
                        frozenProjectState,
                        InProgressState.Create(
                            isFrozen: true,
                            compilationWithoutGeneratedDocuments,
                            CompilationTrackerGeneratorInfo.Empty,
                            compilationWithGeneratedDocuments,
                            pendingTranslationActions: []),
                        clonedCache);
                }
                else if (state is InProgressState inProgressState)
                {
                    // If we have an in progress state with no steps, then we're just at the current project state.
                    // Otherwise, reset us to whatever state the InProgressState had currently transitioned to.

                    var frozenProjectState = inProgressState.PendingTranslationActions.IsEmpty
                        ? this.ProjectState
                        : inProgressState.PendingTranslationActions.First().OldProjectState;

                    // Grab whatever is in the in-progress-state so far, add any generated docs, and snap 
                    // us to a frozen state with that information.
                    var generatorInfo = inProgressState.GeneratorInfo;
                    var compilationWithoutGeneratedDocuments = inProgressState.CompilationWithoutGeneratedDocuments;
                    var compilationWithGeneratedDocuments = compilationWithoutGeneratedDocuments.AddSyntaxTrees(
                        generatorInfo.Documents.States.Values.Select(state => state.GetSyntaxTree(cancellationToken)));

                    return new CompilationTracker(
                        frozenProjectState,
                        InProgressState.Create(
                            isFrozen: true,
                            compilationWithoutGeneratedDocuments,
                            generatorInfo,
                            compilationWithGeneratedDocuments,
                            pendingTranslationActions: []),
                        clonedCache);
                }
                else
                {
                    throw ExceptionUtilities.UnexpectedValue(state.GetType());
                }
            }

            public async ValueTask<TextDocumentStates<SourceGeneratedDocumentState>> GetSourceGeneratedDocumentStatesAsync(
                SolutionCompilationState compilationState, CancellationToken cancellationToken)
            {
                // If we don't have any generators, then we know we have no generated files, so we can skip the computation entirely.
                if (!this.ProjectState.SourceGenerators.Any())
                {
                    return TextDocumentStates<SourceGeneratedDocumentState>.Empty;
                }

                var finalState = await GetOrBuildFinalStateAsync(
                    compilationState, cancellationToken: cancellationToken).ConfigureAwait(false);
                return finalState.GeneratorInfo.Documents;
            }

            public async ValueTask<ImmutableArray<Diagnostic>> GetSourceGeneratorDiagnosticsAsync(
                SolutionCompilationState compilationState, CancellationToken cancellationToken)
            {
                if (!this.ProjectState.SourceGenerators.Any())
                {
                    return [];
                }

                var finalState = await GetOrBuildFinalStateAsync(
                    compilationState, cancellationToken: cancellationToken).ConfigureAwait(false);

                var driverRunResult = finalState.GeneratorInfo.Driver?.GetRunResult();
                if (driverRunResult is null)
                {
                    return [];
                }

                using var _ = ArrayBuilder<Diagnostic>.GetInstance(capacity: driverRunResult.Diagnostics.Length, out var builder);

                foreach (var result in driverRunResult.Results)
                {
                    if (!IsGeneratorRunResultToIgnore(result))
                    {
                        builder.AddRange(result.Diagnostics);
                    }
                }

                return builder.ToImmutableAndClear();
            }

            public SourceGeneratedDocumentState? TryGetSourceGeneratedDocumentStateForAlreadyGeneratedId(DocumentId documentId)
            {
                var state = ReadState();

                // If we are in FinalState, then we have correctly ran generators and then know the final contents of the
                // Compilation. The GeneratedDocuments can be filled for intermediate states, but those aren't guaranteed to be
                // correct and can be re-ran later.
                return state is FinalCompilationTrackerState finalState ? finalState.GeneratorInfo.Documents.GetState(documentId) : null;
            }

            // HACK HACK HACK HACK around a problem introduced by https://github.com/dotnet/sdk/pull/24928. The Razor generator is
            // controlled by a flag that lives in an .editorconfig file; in the IDE we generally don't run the generator and instead use
            // the design-time files added through the legacy IDynamicFileInfo API. When we're doing Hot Reload we then
            // remove those legacy files and remove the .editorconfig file that is supposed to disable the generator, for the Hot
            // Reload pass we then are running the generator. This is done in the CompileTimeSolutionProvider.
            //
            // https://github.com/dotnet/sdk/pull/24928 introduced an issue where even though the Razor generator is being told to not
            // run, it still runs anyways. As a tactical fix rather than reverting that PR, for Visual Studio 17.3 Preview 2 we are going
            // to do a hack here which is to rip out generated files.

            private bool IsGeneratorRunResultToIgnore(GeneratorRunResult result)
            {
                var globalOptions = this.ProjectState.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions;

                // This matches the implementation in https://github.com/chsienki/sdk/blob/4696442a24e3972417fb9f81f182420df0add107/src/RazorSdk/SourceGenerators/RazorSourceGenerator.RazorProviders.cs#L27-L28
                var suppressGenerator = globalOptions.TryGetValue("build_property.SuppressRazorSourceGenerator", out var option) && option == "true";

                if (!suppressGenerator)
                    return false;

                var generatorType = result.Generator.GetGeneratorType();
                return generatorType.FullName == "Microsoft.NET.Sdk.Razor.SourceGenerators.RazorSourceGenerator" &&
                       generatorType.Assembly.GetName().Name is "Microsoft.NET.Sdk.Razor.SourceGenerators" or
                            "Microsoft.CodeAnalysis.Razor.Compiler.SourceGenerators" or
                            "Microsoft.CodeAnalysis.Razor.Compiler";
            }

            // END HACK HACK HACK HACK, or the setup of it at least; once this hack is removed the calls to IsGeneratorRunResultToIgnore
            // need to be cleaned up.

            /// <summary>
            /// Validates the compilation is consistent and we didn't have a bug in producing it. This only runs under a feature flag.
            /// </summary>
            private void ValidateState(CompilationTrackerState? state)
            {
                if (state is null)
                    return;

                if (!_validateStates)
                    return;

                if (state is FinalCompilationTrackerState finalState)
                {
                    ValidateCompilationTreesMatchesProjectState(finalState.FinalCompilationWithGeneratedDocuments, ProjectState, finalState.GeneratorInfo);
                }
                else if (state is InProgressState inProgressState)
                {
                    var projectState = inProgressState.PendingTranslationActions is [var translationAction, ..]
                        ? translationAction.OldProjectState
                        : this.ProjectState;

                    ValidateCompilationTreesMatchesProjectState(inProgressState.CompilationWithoutGeneratedDocuments, projectState, generatorInfo: null);

                    if (inProgressState.StaleCompilationWithGeneratedDocuments != null)
                    {
                        ValidateCompilationTreesMatchesProjectState(inProgressState.StaleCompilationWithGeneratedDocuments, projectState, inProgressState.GeneratorInfo);
                    }
                }
                else
                {
                    throw ExceptionUtilities.UnexpectedValue(state.GetType());
                }
            }

            private static void ValidateCompilationTreesMatchesProjectState(Compilation compilation, ProjectState projectState, CompilationTrackerGeneratorInfo? generatorInfo)
            {
                // We'll do this all in a try/catch so it makes validations easy to do with ThrowExceptionIfFalse().
                try
                {
                    // Assert that all the trees we expect to see are in the Compilation...
                    var syntaxTreesInWorkspaceStates = new HashSet<SyntaxTree>(
#if NET
                        capacity: projectState.DocumentStates.Count + generatorInfo?.Documents.Count ?? 0
#endif
                        );

                    foreach (var documentInProjectState in projectState.DocumentStates.States)
                    {
                        ThrowExceptionIfFalse(documentInProjectState.Value.TryGetSyntaxTree(out var tree), "We should have a tree since we have a compilation that should contain it.");
                        syntaxTreesInWorkspaceStates.Add(tree);
                        ThrowExceptionIfFalse(compilation.ContainsSyntaxTree(tree), "The tree in the ProjectState should have been in the compilation.");
                    }

                    if (generatorInfo != null)
                    {
                        foreach (var generatedDocument in generatorInfo.Value.Documents.States)
                        {
                            ThrowExceptionIfFalse(generatedDocument.Value.TryGetSyntaxTree(out var tree), "We should have a tree since we have a compilation that should contain it.");
                            syntaxTreesInWorkspaceStates.Add(tree);
                            ThrowExceptionIfFalse(compilation.ContainsSyntaxTree(tree), "The tree for the generated document should have been in the compilation.");
                        }
                    }

                    // ...and that the reverse is true too.
                    foreach (var tree in compilation.SyntaxTrees)
                        ThrowExceptionIfFalse(syntaxTreesInWorkspaceStates.Contains(tree), "The tree in the Compilation should have been from the workspace.");
                }
                catch (Exception e) when (FatalError.ReportWithDumpAndCatch(e, ErrorSeverity.Critical))
                {
                }
            }

            /// <summary>
            /// This is just the same as <see cref="Contract.ThrowIfFalse(bool, string, int)"/> but throws a custom exception type to make this easier to find in telemetry since the exception type
            /// is easily seen in telemetry.
            /// </summary>
            private static void ThrowExceptionIfFalse([DoesNotReturnIf(parameterValue: false)] bool condition, string message)
            {
                if (!condition)
                {
                    throw new CompilationTrackerValidationException(message);
                }
            }

            public class CompilationTrackerValidationException : Exception
            {
                public CompilationTrackerValidationException() { }
                public CompilationTrackerValidationException(string message) : base(message) { }
                public CompilationTrackerValidationException(string message, Exception inner) : base(message, inner) { }
            }

            #region Versions and Checksums

            // Dependent Versions are stored on compilation tracker so they are more likely to survive when unrelated solution branching occurs.

            private AsyncLazy<VersionStamp>? _lazyDependentVersion;
            private AsyncLazy<VersionStamp>? _lazyDependentSemanticVersion;
            private AsyncLazy<Checksum>? _lazyDependentChecksum;

            public Task<VersionStamp> GetDependentVersionAsync(
                SolutionCompilationState compilationState, CancellationToken cancellationToken)
            {
                if (_lazyDependentVersion == null)
                {
                    // temp. local to avoid a closure allocation for the fast path
                    // note: solution is captured here, but it will go away once GetValueAsync executes.
                    var compilationStateCapture = compilationState;
                    Interlocked.CompareExchange(ref _lazyDependentVersion, AsyncLazy.Create(
                        c => ComputeDependentVersionAsync(compilationStateCapture, c)), null);
                }

                return _lazyDependentVersion.GetValueAsync(cancellationToken);
            }

            private async Task<VersionStamp> ComputeDependentVersionAsync(
                SolutionCompilationState compilationState, CancellationToken cancellationToken)
            {
                var projectState = this.ProjectState;
                var projVersion = projectState.Version;
                var docVersion = await projectState.GetLatestDocumentVersionAsync(cancellationToken).ConfigureAwait(false);

                var version = docVersion.GetNewerVersion(projVersion);
                foreach (var dependentProjectReference in projectState.ProjectReferences)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (compilationState.SolutionState.ContainsProject(dependentProjectReference.ProjectId))
                    {
                        var dependentProjectVersion = await compilationState.GetDependentVersionAsync(dependentProjectReference.ProjectId, cancellationToken).ConfigureAwait(false);
                        version = dependentProjectVersion.GetNewerVersion(version);
                    }
                }

                return version;
            }

            public Task<VersionStamp> GetDependentSemanticVersionAsync(
                SolutionCompilationState compilationState, CancellationToken cancellationToken)
            {
                if (_lazyDependentSemanticVersion == null)
                {
                    // temp. local to avoid a closure allocation for the fast path
                    // note: solution is captured here, but it will go away once GetValueAsync executes.
                    var compilationStateCapture = compilationState;
                    Interlocked.CompareExchange(ref _lazyDependentSemanticVersion, AsyncLazy.Create(
                        c => ComputeDependentSemanticVersionAsync(compilationStateCapture, c)), null);
                }

                return _lazyDependentSemanticVersion.GetValueAsync(cancellationToken);
            }

            private async Task<VersionStamp> ComputeDependentSemanticVersionAsync(
                SolutionCompilationState compilationState, CancellationToken cancellationToken)
            {
                var projectState = this.ProjectState;
                var version = await projectState.GetSemanticVersionAsync(cancellationToken).ConfigureAwait(false);

                foreach (var dependentProjectReference in projectState.ProjectReferences)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (compilationState.SolutionState.ContainsProject(dependentProjectReference.ProjectId))
                    {
                        var dependentProjectVersion = await compilationState.GetDependentSemanticVersionAsync(
                            dependentProjectReference.ProjectId, cancellationToken).ConfigureAwait(false);
                        version = dependentProjectVersion.GetNewerVersion(version);
                    }
                }

                return version;
            }

            public Task<Checksum> GetDependentChecksumAsync(
                SolutionCompilationState compilationState, CancellationToken cancellationToken)
            {
                if (_lazyDependentChecksum == null)
                {
                    var tmp = compilationState.SolutionState; // temp. local to avoid a closure allocation for the fast path
                    // note: solution is captured here, but it will go away once GetValueAsync executes.
                    Interlocked.CompareExchange(ref _lazyDependentChecksum, AsyncLazy.Create(c => ComputeDependentChecksumAsync(tmp, c)), null);
                }

                return _lazyDependentChecksum.GetValueAsync(cancellationToken);
            }

            private async Task<Checksum> ComputeDependentChecksumAsync(SolutionState solution, CancellationToken cancellationToken)
            {
                using var _ = ArrayBuilder<Checksum>.GetInstance(out var tempChecksumArray);

                // Get the checksum for the project itself.
                var projectChecksum = await this.ProjectState.GetChecksumAsync(cancellationToken).ConfigureAwait(false);
                tempChecksumArray.Add(projectChecksum);

                // Calculate a checksum this project and for each dependent project that could affect semantics for
                // this project. Ensure that the checksum calculation orders the projects consistently so that we get
                // the same checksum across sessions of VS.  Note: we use the project filepath+name as a unique way
                // to reference a project.  This matches the logic in our persistence-service implemention as to how
                // information is associated with a project.
                var transitiveDependencies = solution.GetProjectDependencyGraph().GetProjectsThatThisProjectTransitivelyDependsOn(this.ProjectState.Id);
                var orderedProjectIds = transitiveDependencies.OrderBy(id =>
                {
                    var depProject = solution.GetRequiredProjectState(id);
                    return (depProject.FilePath, depProject.Name);
                });

                foreach (var projectId in orderedProjectIds)
                {
                    var referencedProject = solution.GetRequiredProjectState(projectId);

                    // Note that these checksums should only actually be calculated once, if the project is unchanged
                    // the same checksum will be returned.
                    var referencedProjectChecksum = await referencedProject.GetChecksumAsync(cancellationToken).ConfigureAwait(false);
                    tempChecksumArray.Add(referencedProjectChecksum);
                }

                return Checksum.Create(tempChecksumArray);
            }

            #endregion
        }
    }
}
