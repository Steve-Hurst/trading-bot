using System.Threading.Tasks;
using Broker;
using Core.Models;
using Logging;

namespace Strategies
{
    public interface IStrategy
    {
        string StrategyName { get; }
        string TargetSymbol { get; }
        
        Task OnTickAsync(TickData tick, AccountSummary account, ITradingBroker broker, TelemetryLogger logger);
        Task OnBarAsync(BarData bar, AccountSummary account, ITradingBroker broker, TelemetryLogger logger);
    }
}
