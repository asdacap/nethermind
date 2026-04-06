// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Blockchain.Headers;
using Nethermind.Core;
using Nethermind.Core.Crypto;

namespace Nethermind.Consensus.Stateless;

/// <summary>
/// Decorator over <see cref="IHeaderFinder"/> that records the lowest block number accessed
/// (from BLOCKHASH opcode) into <see cref="WitnessStore"/> for witness header generation.
/// </summary>
public class WitnessGeneratingHeaderFinder(IHeaderFinder inner, WitnessStore witnessStore) : IHeaderFinder
{
    public BlockHeader? Get(Hash256 blockHash, long? blockNumber = null)
    {
        BlockHeader? header = inner.Get(blockHash, blockNumber);
        if (header is not null)
        {
            witnessStore.RecordHeaderAccess(header.Number);
        }
        return header;
    }
}
