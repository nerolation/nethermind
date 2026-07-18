// SPDX-FileCopyrightText: 2026 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Runtime.CompilerServices;
using Nethermind.Core;
using Nethermind.Core.BlockAccessLists;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Int256;

namespace Nethermind.JsonRpc.Modules.Eth;

/// <summary>
/// Per-account entry of an <c>eth_getBlockAccessList</c> response as defined by the
/// execution-apis EIP-7928 schema.
/// </summary>
public class AccountAccessForRpc
{
    public required Address Address { get; init; }
    public required SlotChangesForRpc[] StorageChanges { get; init; }
    public required ValueHash256[] StorageReads { get; init; }
    public required BalanceChangeForRpc[] BalanceChanges { get; init; }
    public required NonceChangeForRpc[] NonceChanges { get; init; }
    public required CodeChangeForRpc[] CodeChanges { get; init; }

    public static AccountAccessForRpc[] FromBlockAccessList(ReadOnlyBlockAccessList blockAccessList)
    {
        AccountAccessForRpc[] accounts = new AccountAccessForRpc[blockAccessList.AccountChanges.Count];
        int i = 0;
        foreach (ReadOnlyAccountChanges account in blockAccessList.AccountChanges)
        {
            accounts[i++] = FromAccountChanges(account);
        }

        return accounts;
    }

    private static AccountAccessForRpc FromAccountChanges(ReadOnlyAccountChanges account)
    {
        SlotChangesForRpc[] storageChanges = new SlotChangesForRpc[account.StorageChanges.Length];
        for (int i = 0; i < storageChanges.Length; i++)
        {
            ReadOnlySlotChanges slotChanges = account.StorageChanges[i];
            StorageChangeForRpc[] changes = new StorageChangeForRpc[slotChanges.Changes.Length];
            for (int j = 0; j < changes.Length; j++)
            {
                StorageChange change = slotChanges.Changes[j];
                changes[j] = new StorageChangeForRpc { Index = change.Index, Value = ToValueHash(change.Value) };
            }

            storageChanges[i] = new SlotChangesForRpc { Key = slotChanges.Key.ToValueHash(), Changes = changes };
        }

        ValueHash256[] storageReads = new ValueHash256[account.StorageReads.Length];
        for (int i = 0; i < storageReads.Length; i++)
        {
            storageReads[i] = account.StorageReads[i].ToValueHash();
        }

        BalanceChangeForRpc[] balanceChanges = new BalanceChangeForRpc[account.BalanceChanges.Length];
        for (int i = 0; i < balanceChanges.Length; i++)
        {
            BalanceChange change = account.BalanceChanges[i];
            balanceChanges[i] = new BalanceChangeForRpc { Index = change.Index, Value = change.Value };
        }

        NonceChangeForRpc[] nonceChanges = new NonceChangeForRpc[account.NonceChanges.Length];
        for (int i = 0; i < nonceChanges.Length; i++)
        {
            NonceChange change = account.NonceChanges[i];
            nonceChanges[i] = new NonceChangeForRpc { Index = change.Index, Value = change.Value };
        }

        CodeChangeForRpc[] codeChanges = new CodeChangeForRpc[account.CodeChanges.Length];
        for (int i = 0; i < codeChanges.Length; i++)
        {
            CodeChange change = account.CodeChanges[i];
            codeChanges[i] = new CodeChangeForRpc { Index = change.Index, Code = change.Code };
        }

        return new AccountAccessForRpc
        {
            Address = account.Address,
            StorageChanges = storageChanges,
            StorageReads = storageReads,
            BalanceChanges = balanceChanges,
            NonceChanges = nonceChanges,
            CodeChanges = codeChanges,
        };
    }

    /// <summary>Reinterprets 32 big-endian bytes as a hash so they serialize zero-padded.</summary>
    private static ValueHash256 ToValueHash(in EvmWord value)
        => Unsafe.As<EvmWord, ValueHash256>(ref Unsafe.AsRef(in value));
}

public class SlotChangesForRpc
{
    public required ValueHash256 Key { get; init; }
    public required StorageChangeForRpc[] Changes { get; init; }
}

public class StorageChangeForRpc
{
    public required ulong Index { get; init; }
    public required ValueHash256 Value { get; init; }
}

public class BalanceChangeForRpc
{
    public required ulong Index { get; init; }
    public required UInt256 Value { get; init; }
}

public class NonceChangeForRpc
{
    public required ulong Index { get; init; }
    public required ulong Value { get; init; }
}

public class CodeChangeForRpc
{
    public required ulong Index { get; init; }
    public required byte[] Code { get; init; }
}
