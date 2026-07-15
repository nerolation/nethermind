// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Core;
using Nethermind.Core.Messages;
using Nethermind.Core.Specs;
using Nethermind.Logging;

namespace Nethermind.TxPool.Filters
{
    /// <summary>
    /// Filters out transactions that are not well formed (not conforming with the yellowpaper and EIPs)
    /// </summary>
    internal sealed class MalformedTxFilter(
        IChainHeadSpecProvider specProvider,
        ITxValidator txValidator,
        ILogger logger,
        bool deferredOnly = false)
        : IIncomingTxFilter
    {
        public AcceptTxResult Accept(Transaction tx, ref TxFilteringState state, TxHandlingOptions txHandlingOptions)
        {
            if (deferredOnly != state.ValidateTransactionAfterSenderRecovery)
            {
                return AcceptTxResult.Accepted;
            }

            IReleaseSpec spec = specProvider.GetCurrentHeadSpec();
            ValidationResult result = txValidator.IsWellFormed(tx, spec);
            if (!result)
            {
                if (!deferredOnly
                    && spec.IsEip2780Enabled
                    && tx.IsMessageCall
                    && tx.SenderAddress is null
                    && result.Error!.StartsWith(TxErrorMessages.IntrinsicGasTooLow, StringComparison.Ordinal))
                {
                    state.ValidateTransactionAfterSenderRecovery = true;
                    return AcceptTxResult.Accepted;
                }

                Metrics.PendingTransactionsMalformed++;
                // It may happen that other nodes send us transactions that were signed for another chain or don't have enough gas.
                if (logger.IsTrace) logger.Trace($"Skipped adding transaction {tx.ToString("  ")}, invalid transaction: {result}");
                return AcceptTxResult.Invalid.WithMessage($"{result}");
            }

            return AcceptTxResult.Accepted;
        }
    }
}
