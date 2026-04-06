// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Core;
using Nethermind.Core.Collections;
using Nethermind.Core.Crypto;
using Nethermind.Core.Eip2930;
using Nethermind.Core.Specs;
using Nethermind.Evm.State;
using Nethermind.Evm.Tracing.State;
using Nethermind.Int256;

namespace Nethermind.Consensus.Stateless;

/// <summary>
/// Decorator over <see cref="IWorldState"/> that records all accessed accounts, storage slots
/// and bytecodes into a <see cref="WitnessStore"/> for witness generation.
/// </summary>
public class WitnessGeneratingWorldState(IWorldState inner, WitnessStore witnessStore) : IWorldState
{
    public bool HasStateForBlock(BlockHeader? baseBlock) => inner.HasStateForBlock(baseBlock);

    public void Restore(Snapshot snapshot) => inner.Restore(snapshot);

    public bool TryGetAccount(Address address, out AccountStruct account)
    {
        witnessStore.RecordEmptySlots(address);
        return inner.TryGetAccount(address, out account);
    }

    public Hash256 StateRoot => inner.StateRoot;

    public bool IsInScope => inner.IsInScope;

    public IWorldStateScopeProvider ScopeProvider => inner.ScopeProvider;

    public byte[]? GetCode(Address address)
    {
        witnessStore.RecordEmptySlots(address);
        byte[] code = inner.GetCode(address);
        witnessStore.RecordBytecode(code);
        return code;
    }

    public byte[]? GetCode(in ValueHash256 codeHash)
    {
        byte[] code = inner.GetCode(in codeHash);
        witnessStore.RecordBytecode(code);
        return code;
    }

    public bool IsContract(Address address)
    {
        witnessStore.RecordEmptySlots(address);
        return inner.IsContract(address);
    }

    public bool AccountExists(Address address)
    {
        witnessStore.RecordEmptySlots(address);
        return inner.AccountExists(address);
    }

    public bool IsDeadAccount(Address address)
    {
        witnessStore.RecordEmptySlots(address);
        return inner.IsDeadAccount(address);
    }

    public ref readonly UInt256 GetBalance(Address address)
    {
        witnessStore.RecordEmptySlots(address);
        return ref inner.GetBalance(address);
    }

    public ref readonly ValueHash256 GetCodeHash(Address address)
    {
        witnessStore.RecordEmptySlots(address);
        return ref inner.GetCodeHash(address);
    }

    public byte[] GetOriginal(in StorageCell storageCell)
    {
        witnessStore.RecordSlot(storageCell);
        return inner.GetOriginal(in storageCell);
    }

    public ReadOnlySpan<byte> Get(in StorageCell storageCell)
    {
        witnessStore.RecordSlot(storageCell);
        return inner.Get(in storageCell);
    }

    public void Set(in StorageCell storageCell, byte[] newValue)
    {
        witnessStore.RecordSlot(storageCell);
        inner.Set(in storageCell, newValue);
    }

    // Transient state does not need trie node capture as it's purely in-memory storage, no trie representation whatsoever
    public ReadOnlySpan<byte> GetTransientState(in StorageCell storageCell)
        => inner.GetTransientState(in storageCell);

    // Transient state does not need trie node capture as it's purely in-memory storage, no trie representation whatsoever
    public void SetTransientState(in StorageCell storageCell, byte[] newValue)
        => inner.SetTransientState(in storageCell, newValue);

    public void Reset(bool resetBlockChanges = true) => inner.Reset(resetBlockChanges);

    public Snapshot TakeSnapshot(bool newTransactionStart = false) => inner.TakeSnapshot(newTransactionStart);

    public void WarmUp(AccessList? accessList) => inner.WarmUp(accessList);

    public void WarmUp(Address address) => inner.WarmUp(address);

    public void ClearStorage(Address address)
    {
        witnessStore.RecordEmptySlots(address);
        inner.ClearStorage(address);
    }

    public void RecalculateStateRoot() => inner.RecalculateStateRoot();

    public void DeleteAccount(Address address)
    {
        witnessStore.RecordEmptySlots(address);
        inner.DeleteAccount(address);
    }

    public void CreateAccount(Address address, in UInt256 balance, in UInt256 nonce = default)
    {
        witnessStore.RecordEmptySlots(address);
        inner.CreateAccount(address, in balance, in nonce);
    }

    public void CreateAccountIfNotExists(Address address, in UInt256 balance, in UInt256 nonce = default)
    {
        witnessStore.RecordEmptySlots(address);
        inner.CreateAccountIfNotExists(address, in balance, in nonce);
    }

    public bool InsertCode(Address address, in ValueHash256 codeHash, ReadOnlyMemory<byte> code, IReleaseSpec spec, bool isGenesis = false)
    {
        witnessStore.RecordEmptySlots(address);
        return inner.InsertCode(address, in codeHash, code, spec, isGenesis);
    }

    public void AddToBalance(Address address, in UInt256 balanceChange, IReleaseSpec spec)
    {
        witnessStore.RecordEmptySlots(address);
        inner.AddToBalance(address, in balanceChange, spec);
    }

    public void AddToBalance(Address address, in UInt256 balanceChange, IReleaseSpec spec, out UInt256 oldBalance)
    {
        witnessStore.RecordEmptySlots(address);
        inner.AddToBalance(address, in balanceChange, spec, out oldBalance);
    }

    public bool AddToBalanceAndCreateIfNotExists(Address address, in UInt256 balanceChange, IReleaseSpec spec, out UInt256 oldBalance)
    {
        witnessStore.RecordEmptySlots(address);
        return inner.AddToBalanceAndCreateIfNotExists(address, in balanceChange, spec, out oldBalance);
    }

    public void SubtractFromBalance(Address address, in UInt256 balanceChange, IReleaseSpec spec, out UInt256 oldBalance)
    {
        witnessStore.RecordEmptySlots(address);
        inner.SubtractFromBalance(address, in balanceChange, spec, out oldBalance);
    }

    public void IncrementNonce(Address address, UInt256 delta, out UInt256 oldNonce)
    {
        witnessStore.RecordEmptySlots(address);
        inner.IncrementNonce(address, delta, out oldNonce);
    }

    public void DecrementNonce(Address address, UInt256 delta)
    {
        witnessStore.RecordEmptySlots(address);
        inner.DecrementNonce(address, delta);
    }

    public void SetNonce(Address address, in UInt256 nonce)
    {
        witnessStore.RecordEmptySlots(address);
        inner.SetNonce(address, nonce);
    }

    public void Commit(IReleaseSpec releaseSpec, bool isGenesis = false, bool commitRoots = true) =>
        inner.Commit(releaseSpec, isGenesis, commitRoots);

    public void Commit(IReleaseSpec releaseSpec, IWorldStateTracer tracer, bool isGenesis = false, bool commitRoots = true) =>
        inner.Commit(releaseSpec, tracer, isGenesis, commitRoots);

    public void CommitTree(long blockNumber) => inner.CommitTree(blockNumber);

    public ArrayPoolList<AddressAsKey>? GetAccountChanges() => inner.GetAccountChanges();

    public void ResetTransient() => inner.ResetTransient();

    public IDisposable BeginScope(BlockHeader? baseBlock) => inner.BeginScope(baseBlock);

    public void CreateEmptyAccountIfDeleted(Address address)
    {
        witnessStore.RecordEmptySlots(address);
        inner.CreateEmptyAccountIfDeleted(address);
    }
}
