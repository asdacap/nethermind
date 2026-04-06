// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using Autofac;
using Autofac.Features.AttributeFilters;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Headers;
using Nethermind.Blockchain.Receipts;
using Nethermind.Core;
using Nethermind.Core.Container;
using Nethermind.Db;
using Nethermind.Evm.State;
using Nethermind.Logging;
using Nethermind.State;

namespace Nethermind.Consensus.Stateless;

public interface IWitnessGeneratingBlockProcessingEnvScope : IDisposable
{
    IWitnessGeneratingBlockProcessingEnv Env { get; }
}

public interface IWitnessGeneratingBlockProcessingEnvFactory
{
    IWitnessGeneratingBlockProcessingEnvScope CreateScope();
}

public sealed class ExecutionRecordingScope(ILifetimeScope envLifetimeScope) : IWitnessGeneratingBlockProcessingEnvScope
{
    public IWitnessGeneratingBlockProcessingEnv Env { get; } = envLifetimeScope.Resolve<IWitnessGeneratingBlockProcessingEnv>();

    public void Dispose() => envLifetimeScope.Dispose();
}

public class WitnessGeneratingBlockProcessingEnvFactory(
    ILifetimeScope rootLifetimeScope,
    IWorldStateManager worldStateManager,
    [KeyFilter(DbNames.Code)] IDb codeDb,
    IReadOnlyList<IBlockValidationModule> validationModules,
    ILogManager logManager) : IWitnessGeneratingBlockProcessingEnvFactory
{
    public IWitnessGeneratingBlockProcessingEnvScope CreateScope()
    {
        WitnessCapturingTrieStore trieStore = new(worldStateManager.CreateReadOnlyTrieStore());
        IStateReader stateReader = new StateReader(trieStore, codeDb, logManager);
        WitnessStore witnessStore = new(stateReader, trieStore);

        ILifetimeScope envLifetimeScope = rootLifetimeScope.BeginLifetimeScope(builder => builder
            .AddScoped<WitnessCapturingTrieStore>(trieStore)
            .AddScoped<IStateReader>(stateReader)
            .AddScoped<IWorldStateScopeProvider>(new TrieStoreScopeProvider(trieStore, codeDb, logManager))
            .AddScoped<WitnessStore>(witnessStore)
            .AddDecorator<IHeaderFinder>((_, inner) =>
                new WitnessGeneratingHeaderFinder(inner, witnessStore))
            .AddScoped<IBlockhashCache, BlockhashCache>()
            .AddDecorator<IWorldState, WitnessGeneratingWorldState>()
            .AddModule(validationModules)
            .AddScoped<IReceiptStorage>(NullReceiptStorage.Instance)
            .AddScoped<IWitnessGeneratingBlockProcessingEnv, WitnessGeneratingBlockProcessingEnv>());

        return new ExecutionRecordingScope(envLifetimeScope);
    }
}
