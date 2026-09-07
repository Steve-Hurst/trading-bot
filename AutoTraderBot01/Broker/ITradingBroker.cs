using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Models;

namespace Broker
{
    public interface ITradingBroker
    {
        string BrokerName { get; }
        bool IsConnected { get; }

        event Action<TickData>? OnTickReceived;
        event Action<BarData>? OnBarReceived;

        Task<bool> ConnectAsync();
        Task DisconnectAsync();

        Task<AccountSummary> GetAccountSummaryAsync();
        Task<IReadOnlyList<Position>> GetOpenPositionsAsync();

        Task<OrderResult> ExecuteOrderAsync(OrderRequest request);
        Task<bool> ClosePositionAsync(long positionId);
        Task<bool> ModifyPositionAsync(long positionId, double? stopLoss, double? takeProfit);
    }
}
