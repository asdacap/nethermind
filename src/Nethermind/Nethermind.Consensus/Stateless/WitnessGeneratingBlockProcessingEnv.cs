// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Blockchain.Headers;
using Nethermind.Consensus.Processing;
using Nethermind.Core.Specs;
using Nethermind.Evm.State;

namespace Nethermind.Consensus.Stateless;

public interface IWitnessGeneratingBlockProcessingEnv
{
    IExistingBlockWitnessCollector CreateExistingBlockWitnessCollector();
}

public class WitnessGeneratingBlockProcessingEnv(
    IWorldState worldState,
    WitnessStore witnessStore,
    IHeaderFinder headerFinder,
    IBlockProcessor blockProcessor,
    ISpecProvider specProvider) : IWitnessGeneratingBlockProcessingEnv
{
    public IExistingBlockWitnessCollector CreateExistingBlockWitnessCollector()
        => new WitnessCollector(worldState, witnessStore, headerFinder, blockProcessor, specProvider);
}
