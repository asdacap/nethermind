// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System;
using Collections.Pooled;
using Nethermind.Blockchain.Headers;
using Nethermind.Core;
using Nethermind.Core.Collections;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Int256;
using Nethermind.Serialization.Rlp;
using Nethermind.State;
using Nethermind.State.Proofs;
using Nethermind.Trie;
using Nethermind.Trie.Pruning;

namespace Nethermind.Consensus.Stateless;

/// <summary>
/// Collects storage slots, bytecodes, and trie nodes accessed during block execution
/// for witness generation. <see cref="WitnessGeneratingWorldState"/> delegates recording
/// to this class, and <see cref="WitnessCollector"/> calls <see cref="GetWitness"/> to
/// produce the final witness.
/// </summary>
public class WitnessStore(
    IStateReader stateReader,
    WitnessCapturingTrieStore trieStore)
{
    private static readonly HeaderDecoder _headerDecoder = new();
    private readonly Dictionary<Address, HashSet<UInt256>> _storageSlots = new();
    private readonly Dictionary<ValueHash256, byte[]> _bytecodes = new();
    private long _lowestRequestedHeader = long.MaxValue;

    public void RecordHeaderAccess(long blockNumber)
    {
        if (blockNumber < _lowestRequestedHeader)
            _lowestRequestedHeader = blockNumber;
    }

    public void RecordSlot(in StorageCell storageCell) => RecordEmptySlots(storageCell.Address).Add(storageCell.Index);

    public HashSet<UInt256> RecordEmptySlots(Address address)
    {
        ref HashSet<UInt256>? slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_storageSlots, address, out _);
        slot ??= new HashSet<UInt256>();
        return slot;
    }

    public void RecordBytecode(byte[]? code)
    {
        // Unnecessary to record empty code
        if (code?.Length > 0)
        {
            Hash256 codeHash = Keccak.Compute(code);
            _bytecodes.TryAdd(codeHash, code);
        }
    }

    public Witness GetWitness(BlockHeader parentHeader, IHeaderFinder headerFinder)
    {
        // Build state nodes
        //
        // The purpose of adding this tree visitor over the captured keys is for capturing trie nodes
        // for slots that were never read and yet written to (writes are cached) but transaction reverted.
        // Transaction reverting implies that cached writes got discarded and trie never got traversed
        // for those keys, hence the associated trie nodes never got captured.
        //
        // We could potentially enforce read-before-write for every function called within this file,
        // but this tree visitor solution is safer, more defensive and maintainable.
        //
        // Notes:
        // - We wouldn't need to capture those trie nodes for nethermind stateless execution, but we need to
        // if we want to be compatible with other clients (such as geth, for example) so that our witness
        // can be used for their stateless execution.
        // - Trie nodes captured using this additional tree visitor pattern should not add unnecessary trie nodes
        // as anyway all keys recorded in this file should either be read or written to. In both cases, we want
        // trie traversal with trie nodes capture along the path to be compatible with other clients.
        //

        if (!trieStore.TouchedNodesRlp.Any())
        {
            // When there are no storage slots or account read at all, because of the lazy optimization of the TrieNode
            // and skipping recording when node is of unknown type, the root node is not recorded at all.
            // So we explicitly resolve it here. This may seems to work in most case as the TrieNode tend to be cached
            // especially the root node,
            ITrieNodeResolver stateResolver = trieStore.GetTrieStore(null);
            TreePath path = TreePath.Empty;
            TrieNode node = stateResolver.FindCachedOrUnknown(path, parentHeader.StateRoot!);
            node.ResolveNode(stateResolver, path);
        }

        using PooledSet<byte[]> stateNodes = new(trieStore.TouchedNodesRlp, Bytes.EqualityComparer);
        foreach ((Address account, HashSet<UInt256> slots) in _storageSlots)
        {
            AccountProofCollector accountProofCollector = new(account, slots);
            stateReader.RunTreeVisitor(accountProofCollector, parentHeader);
            (IReadOnlyList<byte[]> accountProof, IReadOnlyList<byte[]>[] storageProof) = accountProofCollector.GetRawResult();
            stateNodes.AddRange(accountProof);
            stateNodes.AddRange(storageProof.SelectMany(p => p));
        }

        ArrayPoolList<byte[]> codes = new(_bytecodes.Count);
        foreach (byte[] code in _bytecodes.Values)
            codes.Add(code);

        ArrayPoolList<byte[]> state = new(stateNodes.Count);
        foreach (byte[] nodeRlp in stateNodes)
            state.Add(nodeRlp);

        // Build keys
        int totalKeysCount = 0;
        foreach (KeyValuePair<Address, HashSet<UInt256>> kvp in _storageSlots)
        {
            totalKeysCount++;
            totalKeysCount += kvp.Value.Count;
        }

        ArrayPoolList<byte[]> keys = new(totalKeysCount);

        // Keys should be ordered like: <address1><address2><slot1-address2><slot2-address2><address3><slot1-address3>
        foreach (KeyValuePair<Address, HashSet<UInt256>> kvp in _storageSlots)
        {
            keys.Add(kvp.Key.Bytes);
            foreach (UInt256 slot in kvp.Value)
                keys.Add(slot.ToBigEndian());
        }

        return new Witness
        {
            Codes = codes,
            State = state,
            Keys = keys,
            Headers = GetWitnessHeaders(parentHeader, headerFinder)
        };
    }

    private IOwnedReadOnlyList<byte[]> GetWitnessHeaders(BlockHeader parentHeader, IHeaderFinder headerFinder)
    {
        ArrayPoolList<byte[]> headers = new(capacity: 16);

        Hash256 currentHash = parentHeader.Hash!;
        BlockHeader childHeader = headerFinder.Get(currentHash) ?? throw new ArgumentException($"Parent {currentHash} is not found");
        headers.Add(_headerDecoder.Encode(childHeader).Bytes);

        if (_lowestRequestedHeader < long.MaxValue)
        {
            for (long i = childHeader.Number - 1; i >= _lowestRequestedHeader; i--)
            {
                currentHash = childHeader.ParentHash!;
                childHeader = headerFinder.Get(currentHash, i) ?? throw new ArgumentException($"Unable to get requested header at hash {currentHash} and number {i} during witness generation");
                headers.Add(_headerDecoder.Encode(childHeader).Bytes);
            }
        }

        return headers;
    }
}
