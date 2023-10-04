﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Serialization
{
    /// <summary>
    /// collection which children is checksum.
    /// </summary>
    internal class ChecksumCollection(ImmutableArray<IChecksummedObject> checksums) : ChecksumWithChildren(checksums), IReadOnlyCollection<Checksum>
    {
        public ChecksumCollection(ImmutableArray<Checksum> checksums) : this(checksums.CastArray<IChecksummedObject>())
        {
        }

        public int Count => Children.Length;
        public Checksum this[int index] => Children[index].Checksum;

        public IEnumerator<Checksum> GetEnumerator()
            => this.Children.Cast<Checksum>().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        [PerformanceSensitive("https://devdiv.visualstudio.com/DevDiv/_workitems/edit/1333566", AllowGenericEnumeration = false)]
        internal static async Task FindAsync<TState>(
            TextDocumentStates<TState> documentStates,
            HashSet<Checksum> searchingChecksumsLeft,
            Dictionary<Checksum, object> result,
            CancellationToken cancellationToken) where TState : TextDocumentState
        {
            foreach (var (_, state) in documentStates.States)
            {
                Contract.ThrowIfFalse(state.TryGetStateChecksums(out var stateChecksums));

                await stateChecksums.FindAsync(state, searchingChecksumsLeft, result, cancellationToken).ConfigureAwait(false);
                if (searchingChecksumsLeft.Count == 0)
                {
                    return;
                }
            }
        }

        internal static void Find<T>(
            IReadOnlyList<T> values,
            ChecksumWithChildren checksums,
            HashSet<Checksum> searchingChecksumsLeft,
            Dictionary<Checksum, object> result,
            CancellationToken cancellationToken)
        {
            Contract.ThrowIfFalse(values.Count == checksums.Children.Length);

            for (var i = 0; i < checksums.Children.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (searchingChecksumsLeft.Count == 0)
                {
                    return;
                }

                var checksum = (Checksum)checksums.Children[i];
                var value = values[i];

                if (searchingChecksumsLeft.Remove(checksum))
                {
                    result[checksum] = value;
                }
            }
        }

        public void WriteTo(ObjectWriter writer)
        {
            writer.WriteInt32(this.Count);
            foreach (var obj in this.Children)
                obj.Checksum.WriteTo(writer);
        }

        public static ChecksumCollection ReadFrom(ObjectReader reader)
        {
            var count = reader.ReadInt32();
            using var _ = ArrayBuilder<Checksum>.GetInstance(count, out var result);
            for (var i = 0; i < count; i++)
                result.Add(Checksum.ReadFrom(reader));

            return new(result.ToImmutableAndClear());
        }
    }
}
