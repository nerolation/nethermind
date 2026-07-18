// SPDX-FileCopyrightText: 2026 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core.BlockAccessLists;
using Nethermind.Core.Test.Builders;
using Nethermind.Int256;
using Nethermind.JsonRpc.Modules.Eth;
using Nethermind.Serialization.Json;
using NUnit.Framework;

namespace Nethermind.JsonRpc.Test.Modules.Eth;

public class BlockAccessListForRpcTests
{
    [Test]
    public void Account_with_all_change_families_serializes_to_execution_apis_shape()
    {
        ReadOnlyAccountChanges account = new(
            TestItem.AddressA,
            [new ReadOnlySlotChanges((UInt256)1, [new StorageChange(0, (UInt256)0xff)])],
            [(UInt256)2],
            [new BalanceChange(1, 1000)],
            [new NonceChange(2, 1)],
            [new CodeChange(3, [0x60, 0xff])]);
        ReadOnlyBlockAccessList blockAccessList = new([account], itemCount: 4);

        string serialized = new EthereumJsonSerializer().Serialize(AccountAccessForRpc.FromBlockAccessList(blockAccessList));

        Assert.That(serialized, Is.EqualTo(
            "[{\"address\":\"0xb7705ae4c6f81b66cdb323c65f4e8133690fc099\","
            + "\"storageChanges\":[{\"key\":\"0x0000000000000000000000000000000000000000000000000000000000000001\",\"changes\":[{\"index\":\"0x0\",\"value\":\"0x00000000000000000000000000000000000000000000000000000000000000ff\"}]}],"
            + "\"storageReads\":[\"0x0000000000000000000000000000000000000000000000000000000000000002\"],"
            + "\"balanceChanges\":[{\"index\":\"0x1\",\"value\":\"0x3e8\"}],"
            + "\"nonceChanges\":[{\"index\":\"0x2\",\"value\":\"0x1\"}],"
            + "\"codeChanges\":[{\"index\":\"0x3\",\"code\":\"0x60ff\"}]}]"));
    }
}
