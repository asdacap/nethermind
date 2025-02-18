// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nethermind.Blockchain;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Test.Builders;
using Nethermind.Db;
using Nethermind.Logging;
using Nethermind.State;
using Nethermind.State.Proofs;
using Nethermind.State.Snap;
using Nethermind.Synchronization.SnapSync;
using Nethermind.Trie;
using Nethermind.Trie.Pruning;
using NUnit.Framework;

namespace Nethermind.Store.Test;

public class RecreateStateFromAccountRangesTests
{
    private StateTree _inputTree;

    [OneTimeSetUp]
    public void Setup()
    {
        _inputTree = TestItem.Tree.GetStateTree();
    }

    //[Test]
    public void Test01()
    {
        Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

        AccountProofCollector accountProofCollector = new(TestItem.Tree.AccountsWithPaths[0].Path.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        byte[][] firstProof = accountProofCollector.BuildResult().Proof;

        accountProofCollector = new(TestItem.Tree.AccountsWithPaths[5].Path.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        byte[][] lastProof = accountProofCollector.BuildResult().Proof;

        MemDb db = new();
        IScopedTrieStore store = new TrieStore(db, LimboLogs.Instance).GetTrieStore(null);
        StateTree tree = new(store, LimboLogs.Instance);

        IList<TrieNode> nodes = new List<TrieNode>();

        for (int i = 0; i < (firstProof!).Length; i++)
        {
            byte[] nodeBytes = (firstProof!)[i];
            var node = new TrieNode(NodeType.Unknown, nodeBytes);
            TreePath emptyPath = TreePath.Empty;
            node.ResolveKey(store, ref emptyPath, i == 0);

            nodes.Add(node);
            if (i < (firstProof!).Length - 1)
            {
                //IBatch batch = store.GetOrStartNewBatch();
                //batch[node.Keccak!.Bytes] = nodeBytes;
                //db.Set(node.Keccak!, nodeBytes);
            }
        }

        for (int i = 0; i < (lastProof!).Length; i++)
        {
            byte[] nodeBytes = (lastProof!)[i];
            var node = new TrieNode(NodeType.Unknown, nodeBytes);
            TreePath emptyPath = TreePath.Empty;
            node.ResolveKey(store, ref emptyPath, i == 0);

            nodes.Add(node);
            if (i < (lastProof!).Length - 1)
            {
                //IBatch batch = store.GetOrStartNewBatch();
                //batch[node.Keccak!.Bytes] = nodeBytes;
                //db.Set(node.Keccak!, nodeBytes);
            }
        }

        tree.RootRef = nodes[0];

        tree.Set(TestItem.Tree.AccountsWithPaths[0].Path, TestItem.Tree.AccountsWithPaths[0].Account);
        tree.Set(TestItem.Tree.AccountsWithPaths[1].Path, TestItem.Tree.AccountsWithPaths[1].Account);
        tree.Set(TestItem.Tree.AccountsWithPaths[2].Path, TestItem.Tree.AccountsWithPaths[2].Account);
        tree.Set(TestItem.Tree.AccountsWithPaths[3].Path, TestItem.Tree.AccountsWithPaths[3].Account);
        tree.Set(TestItem.Tree.AccountsWithPaths[4].Path, TestItem.Tree.AccountsWithPaths[4].Account);
        tree.Set(TestItem.Tree.AccountsWithPaths[5].Path, TestItem.Tree.AccountsWithPaths[5].Account);

        tree.Commit(0);

        Assert.That(tree.RootHash, Is.EqualTo(_inputTree.RootHash));
        Assert.That(db.Keys.Count, Is.EqualTo(6));  // we don't persist proof nodes (boundary nodes)
        Assert.That(db.KeyExists(rootHash), Is.False); // the root node is a part of the proof nodes
    }

    [Test]
    public void RecreateAccountStateFromOneRangeWithNonExistenceProof()
    {
        Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

        AccountProofCollector accountProofCollector = new(Keccak.Zero.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        byte[][] firstProof = accountProofCollector.BuildResult().Proof;
        accountProofCollector = new(TestItem.Tree.AccountsWithPaths[5].Path.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        byte[][] lastProof = accountProofCollector.BuildResult().Proof;

        MemDb db = new();
        DbProvider dbProvider = new();
        dbProvider.RegisterDb(DbNames.State, db);
        using ProgressTracker progressTracker = new(null, dbProvider.GetDb<IDb>(DbNames.State), LimboLogs.Instance);
        SnapProvider snapProvider = CreateSnapProvider(progressTracker, dbProvider);
        AddRangeResult result = snapProvider.AddAccountRange(1, rootHash, Keccak.Zero, TestItem.Tree.AccountsWithPaths, firstProof!.Concat(lastProof!).ToArray());

        Assert.That(result, Is.EqualTo(AddRangeResult.OK));
        Assert.That(db.Keys.Count, Is.EqualTo(10));  // we persist proof nodes (boundary nodes) via stitching
        Assert.That(db.KeyExists(rootHash), Is.False);
    }

    [Test]
    public void RecreateAccountStateFromOneRangeWithExistenceProof()
    {
        Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

        AccountProofCollector accountProofCollector = new(TestItem.Tree.AccountsWithPaths[0].Path.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        byte[][] firstProof = accountProofCollector.BuildResult().Proof;
        accountProofCollector = new(TestItem.Tree.AccountsWithPaths[5].Path.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        byte[][] lastProof = accountProofCollector.BuildResult().Proof;

        MemDb db = new();
        DbProvider dbProvider = new();
        dbProvider.RegisterDb(DbNames.State, db);
        ProgressTracker progressTracker = new(null, dbProvider.GetDb<IDb>(DbNames.State), LimboLogs.Instance);
        SnapProvider snapProvider = CreateSnapProvider(progressTracker, dbProvider);
        var result = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[0].Path, TestItem.Tree.AccountsWithPaths, firstProof!.Concat(lastProof!).ToArray());

        Assert.That(result, Is.EqualTo(AddRangeResult.OK));
        Assert.That(db.Keys.Count, Is.EqualTo(10));  // we persist proof nodes (boundary nodes) via stitching
        Assert.That(db.KeyExists(rootHash), Is.False);
    }

    [Test]
    public void RecreateAccountStateFromOneRangeWithoutProof()
    {
        Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

        MemDb db = new();
        DbProvider dbProvider = new();
        dbProvider.RegisterDb(DbNames.State, db);
        using ProgressTracker progressTracker = new(null, dbProvider.GetDb<IDb>(DbNames.State), LimboLogs.Instance);
        SnapProvider snapProvider = CreateSnapProvider(progressTracker, dbProvider);
        var result = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[0].Path, TestItem.Tree.AccountsWithPaths);

        Assert.That(result, Is.EqualTo(AddRangeResult.OK));
        Assert.That(db.Keys.Count, Is.EqualTo(10));  // we don't have the proofs so we persist all nodes
        Assert.That(db.KeyExists(rootHash), Is.False); // the root node is NOT a part of the proof nodes
    }

    [Test]
    public void RecreateAccountStateFromMultipleRange()
    {
        Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

        // output state
        MemDb db = new();
        DbProvider dbProvider = new();
        dbProvider.RegisterDb(DbNames.State, db);
        using ProgressTracker progressTracker = new(null, dbProvider.GetDb<IDb>(DbNames.State), LimboLogs.Instance);
        SnapProvider snapProvider = CreateSnapProvider(progressTracker, dbProvider);

        AccountProofCollector accountProofCollector = new(Keccak.Zero.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        byte[][] firstProof = accountProofCollector.BuildResult().Proof;
        accountProofCollector = new(TestItem.Tree.AccountsWithPaths[1].Path.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        byte[][] lastProof = accountProofCollector.BuildResult().Proof;

        var result1 = snapProvider.AddAccountRange(1, rootHash, Keccak.Zero, TestItem.Tree.AccountsWithPaths[0..2], firstProof!.Concat(lastProof!).ToArray());

        Assert.That(db.Keys.Count, Is.EqualTo(2));

        accountProofCollector = new(TestItem.Tree.AccountsWithPaths[2].Path.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        firstProof = accountProofCollector.BuildResult().Proof;
        accountProofCollector = new(TestItem.Tree.AccountsWithPaths[3].Path.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        lastProof = accountProofCollector.BuildResult().Proof;

        var result2 = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[2].Path, TestItem.Tree.AccountsWithPaths[2..4], firstProof!.Concat(lastProof!).ToArray());

        Assert.That(db.Keys.Count, Is.EqualTo(5));  // we don't persist proof nodes (boundary nodes)

        accountProofCollector = new(TestItem.Tree.AccountsWithPaths[4].Path.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        firstProof = accountProofCollector.BuildResult().Proof;
        accountProofCollector = new(TestItem.Tree.AccountsWithPaths[5].Path.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        lastProof = accountProofCollector.BuildResult().Proof;

        var result3 = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[4].Path, TestItem.Tree.AccountsWithPaths[4..6], firstProof!.Concat(lastProof!).ToArray());

        Assert.That(result1, Is.EqualTo(AddRangeResult.OK));
        Assert.That(result2, Is.EqualTo(AddRangeResult.OK));
        Assert.That(result3, Is.EqualTo(AddRangeResult.OK));
        Assert.That(db.Keys.Count, Is.EqualTo(10));  // we persist proof nodes (boundary nodes) via stitching
        Assert.That(db.KeyExists(rootHash), Is.False);
    }

    [Test]
    public void MissingAccountFromRange()
    {
        Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

        // output state
        MemDb db = new();
        DbProvider dbProvider = new();
        dbProvider.RegisterDb(DbNames.State, db);
        using ProgressTracker progressTracker = new(null, dbProvider.GetDb<IDb>(DbNames.State), LimboLogs.Instance);
        SnapProvider snapProvider = CreateSnapProvider(progressTracker, dbProvider);

        AccountProofCollector accountProofCollector = new(Keccak.Zero.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        byte[][] firstProof = accountProofCollector.BuildResult().Proof;
        accountProofCollector = new(TestItem.Tree.AccountsWithPaths[1].Path.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        byte[][] lastProof = accountProofCollector.BuildResult().Proof;

        var result1 = snapProvider.AddAccountRange(1, rootHash, Keccak.Zero, TestItem.Tree.AccountsWithPaths[0..2], firstProof!.Concat(lastProof!).ToArray());

        Assert.That(db.Keys.Count, Is.EqualTo(2));

        accountProofCollector = new(TestItem.Tree.AccountsWithPaths[2].Path.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        firstProof = accountProofCollector.BuildResult().Proof;
        accountProofCollector = new(TestItem.Tree.AccountsWithPaths[3].Path.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        lastProof = accountProofCollector.BuildResult().Proof;

        // missing TestItem.Tree.AccountsWithHashes[2]
        var result2 = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[2].Path, TestItem.Tree.AccountsWithPaths[3..4], firstProof!.Concat(lastProof!).ToArray());

        Assert.That(db.Keys.Count, Is.EqualTo(2));

        accountProofCollector = new(TestItem.Tree.AccountsWithPaths[4].Path.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        firstProof = accountProofCollector.BuildResult().Proof;
        accountProofCollector = new(TestItem.Tree.AccountsWithPaths[5].Path.Bytes);
        _inputTree.Accept(accountProofCollector, _inputTree.RootHash);
        lastProof = accountProofCollector.BuildResult().Proof;

        var result3 = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[4].Path, TestItem.Tree.AccountsWithPaths[4..6], firstProof!.Concat(lastProof!).ToArray());

        Assert.That(result1, Is.EqualTo(AddRangeResult.OK));
        Assert.That(result2, Is.EqualTo(AddRangeResult.DifferentRootHash));
        Assert.That(result3, Is.EqualTo(AddRangeResult.OK));
        Assert.That(db.Keys.Count, Is.EqualTo(6));
        Assert.That(db.KeyExists(rootHash), Is.False);
    }

    [Test]
    public void Will_not_redownload_persisted_code()
    {
        MemDb db = new();
        MemDb codeDb = new();
        DbProvider dbProvider = new();
        dbProvider.RegisterDb(DbNames.State, db);
        dbProvider.RegisterDb(DbNames.Code, codeDb);

        BlockTree tree = Build.A.BlockTree().OfChainLength(5).TestObject;
        using ProgressTracker progressTracker = new(tree, dbProvider.GetDb<IDb>(DbNames.State), LimboLogs.Instance,
            accountRangePartitionCount: 1);
        SnapProvider snapProvider = CreateSnapProvider(progressTracker, dbProvider);

        PathWithAccount[] accountsWithPath =
        [
            new PathWithAccount(new Hash256("0000000000000000000000000000000000000000000000000000000001112345"),
                new Account(0, 0, Keccak.EmptyTreeHash, TestItem.Keccaks[0])),
            new PathWithAccount(new Hash256("0000000000000000000000000000000000000000000000000000000001113456"),
                new Account(0, 0, Keccak.EmptyTreeHash, TestItem.Keccaks[1])),
            new PathWithAccount(new Hash256("0000000000000000000000000000000000000000000000000000000001114567"),
                new Account(0, 0, Keccak.EmptyTreeHash, TestItem.Keccaks[2])),
            new PathWithAccount(new Hash256("0000000000000000000000000000000000000000000000000000000001123456"),
                new Account(0, 0, Keccak.EmptyTreeHash, TestItem.Keccaks[3])),
            new PathWithAccount(new Hash256("0000000000000000000000000000000000000000000000000000000001123457"),
                new Account(0, 0, Keccak.EmptyTreeHash, TestItem.Keccaks[4]))
        ];

        codeDb[TestItem.Keccaks[1].Bytes] = [1];
        codeDb[TestItem.Keccaks[2].Bytes] = [1];

        StateTree stateTree = new StateTree();
        foreach (PathWithAccount pathWithAccount in accountsWithPath)
        {
            stateTree.Set(pathWithAccount.Path, pathWithAccount.Account);
        }

        stateTree.UpdateRootHash();

        snapProvider.AddAccountRange(1,
            stateTree.RootHash,
            accountsWithPath[0].Path,
            accountsWithPath);

        progressTracker.IsFinished(out SnapSyncBatch nextRequest).Should().BeFalse();
        progressTracker.IsFinished(out nextRequest).Should().BeFalse();
        nextRequest.CodesRequest.Count.Should().Be(3);
    }

    private SnapProvider CreateSnapProvider(ProgressTracker progressTracker, IDbProvider dbProvider)
    {
        try
        {
            IDb _ = dbProvider.CodeDb;
        }
        catch (ArgumentException)
        {
            dbProvider.RegisterDb(DbNames.Code, new MemDb());
        }
        return new(progressTracker, dbProvider.CodeDb, new NodeStorage(dbProvider.StateDb), LimboLogs.Instance);
    }
}
