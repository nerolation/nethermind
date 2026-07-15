// SPDX-FileCopyrightText: 2026 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Core.Test.Blockchain;
using Nethermind.Core.Test.Builders;
using Nethermind.Evm.GasPolicy;
using Nethermind.Evm.Test.Helpers;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.Specs;
using Nethermind.Specs.Forks;
using Nethermind.Specs.Test;
using Nethermind.State;
using NUnit.Framework;

namespace Nethermind.Evm.Test;

/// <summary>Pins the EIP-2780 gas-policy primitives and exercises the intrinsic path end-to-end.</summary>
[TestFixture]
public class Eip2780Tests
{
    private static readonly IReleaseSpec Eip2780Spec = new OverridableReleaseSpec(Prague.Instance) { IsEip2780Enabled = true };

    [Test]
    public void Call_value_cost_is_flat_under_eip2780()
    {
        EthereumGasPolicy gas = EthereumGasPolicy.FromULong(1_000_000);
        Assert.That(EthereumGasPolicy.ConsumeCallValueTransferEip2780(ref gas), Is.True);
        Assert.That(1_000_000 - EthereumGasPolicy.GetRemainingGas(in gas), Is.EqualTo(Eip8038Constants.CallValue));
    }

    private static ulong ChargeAccountAccess(IReleaseSpec spec, bool prewarm)
    {
        EthereumGasPolicy gas = EthereumGasPolicy.FromULong(1_000_000);
        using StackAccessTracker tracker = new();
        if (prewarm) tracker.WarmUp(TestItem.AddressB);
        Assert.That(EthereumGasPolicy.ConsumeAccountAccessGas(ref gas, spec, in tracker, isTracingAccess: false, TestItem.AddressB), Is.True);
        return 1_000_000 - EthereumGasPolicy.GetRemainingGas(in gas);
    }

    [Test]
    public void Cold_account_touch_is_flat()
    {
        Assert.That(ChargeAccountAccess(Eip2780Spec, prewarm: false), Is.EqualTo(GasCostOf.ColdAccountAccess), "cold account");
        Assert.That(ChargeAccountAccess(Eip2780Spec, prewarm: true), Is.EqualTo(GasCostOf.WarmStateRead), "warm account stays at WARM_STATE_READ");
    }

    private static Task<BasicTestBlockchain> CreateChain() =>
        BasicTestBlockchain.Create(b => b.AddSingleton<ISpecProvider>(
            new TestSpecProvider(new OverridableReleaseSpec(Prague.Instance) { IsEip2780Enabled = true, IsEip7708Enabled = true })));

    // Whole-transaction totals; recipient existence is irrelevant (state-independent intrinsic).
    [TestCase(false, 1ul, GasCostOf.TransactionEip2780 + GasCostOf.ColdAccountAccess + GasCostOf.TxValueCostEip2780 + GasCostOf.TransferLogEip2780, TestName = "value transfer to existing EOA (20600)")]
    [TestCase(true, 1ul, GasCostOf.TransactionEip2780 + GasCostOf.ColdAccountAccess + GasCostOf.TxValueCostEip2780 + GasCostOf.TransferLogEip2780, TestName = "value transfer to new account (20600)")]
    [TestCase(false, 0ul, GasCostOf.TransactionEip2780 + GasCostOf.ColdAccountAccess, TestName = "no-transfer to existing EOA (14600)")]
    [TestCase(true, 0ul, GasCostOf.TransactionEip2780 + GasCostOf.ColdAccountAccess, TestName = "no-transfer to empty account (14600)")]
    public async Task Simple_transfer_spends_eip2780_total_gas(bool recipientIsNew, ulong value, ulong expectedGas)
    {
        using BasicTestBlockchain chain = await CreateChain();
        ulong nonce = chain.StateReader.GetNonce(chain.BlockTree.Head!.Header, TestItem.AddressA);

        Address recipient = recipientIsNew ? TestItem.AddressF : TestItem.AddressB;
        Transaction tx = Build.A.Transaction
            .WithTo(recipient)
            .WithValue(value)
            .WithNonce(nonce)
            .WithGasLimit(60000)
            .SignedAndResolved(TestItem.PrivateKeyA)
            .TestObject;

        Block block = await chain.AddBlock(tx);

        Assert.That(chain.ReceiptStorage.Get(block)[0].GasUsed, Is.EqualTo(expectedGas));
    }

    [Test]
    public void Corrected_sender_recalculates_self_transfer_intrinsic_gas()
    {
        using EvmTestHarness harness = new(Eip2780Spec);
        harness.WorldState.CreateAccount(TestItem.AddressA, 1_000_000);
        Transaction tx = Build.A.Transaction
            .WithTo(TestItem.AddressA)
            .WithValue(0)
            .WithGasLimit(GasCostOf.TransactionEip2780)
            .Signed(harness.Ecdsa, TestItem.PrivateKeyA)
            .WithSenderAddress(TestItem.AddressF)
            .TestObject;
        Block block = harness.CreateBlock(tx);

        harness.ExecuteTx(tx, block);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tx.SenderAddress, Is.EqualTo(TestItem.AddressA));
            Assert.That(block.GasUsed, Is.EqualTo(GasCostOf.TransactionEip2780));
        }
    }

    [Test]
    public void Corrected_sender_rejects_underpriced_non_self_transfer()
    {
        using EvmTestHarness harness = new(Eip2780Spec);
        harness.WorldState.CreateAccount(TestItem.AddressA, 1_000_000);
        Transaction tx = Build.A.Transaction
            .WithTo(TestItem.AddressB)
            .WithValue(0)
            .WithGasLimit(GasCostOf.TransactionEip2780)
            .Signed(harness.Ecdsa, TestItem.PrivateKeyA)
            .WithSenderAddress(TestItem.AddressB)
            .TestObject;
        Block block = harness.CreateBlock(tx);

        TransactionResult result = harness.TxProcessor.Execute(
            tx,
            new BlockExecutionContext(block.Header, harness.SpecProvider.GetSpec(block.Header)),
            NullTxTracer.Instance);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tx.SenderAddress, Is.EqualTo(TestItem.AddressA));
            Assert.That(result.Error, Is.EqualTo(TransactionResult.ErrorType.GasLimitBelowIntrinsicGas));
            Assert.That(block.GasUsed, Is.Zero);
        }
    }

    [Test]
    public void Rejected_sender_recovery_retry_does_not_create_account()
    {
        using EvmTestHarness harness = new(Eip2780Spec);
        Transaction tx = Build.A.Transaction
            .WithTo(TestItem.AddressB)
            .WithValue(0)
            .WithGasLimit(GasCostOf.TransactionEip2780)
            .Signed(harness.Ecdsa, TestItem.PrivateKeyA)
            .WithSenderAddress(TestItem.AddressA)
            .TestObject;
        Block block = harness.CreateBlock(tx);

        TransactionResult result = harness.TxProcessor.BuildUp(
            tx,
            new BlockExecutionContext(block.Header, harness.SpecProvider.GetSpec(block.Header)),
            NullTxTracer.Instance);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error, Is.EqualTo(TransactionResult.ErrorType.GasLimitBelowIntrinsicGas));
            Assert.That(harness.WorldState.AccountExists(TestItem.AddressA), Is.False);
        }
    }
}
