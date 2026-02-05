using System.Threading.Channels;
using ArbitrageApi.Models;

namespace ArbitrageApi.Services;

public class ChannelProvider
{
    public Channel<ArbitrageOpportunity> TradeChannel { get; } = Channel.CreateUnbounded<ArbitrageOpportunity>();
    public Channel<ArbitrageEvent> EventChannel { get; } = Channel.CreateUnbounded<ArbitrageEvent>();
    public Channel<Transaction> TransactionChannel { get; } = Channel.CreateUnbounded<Transaction>();
    public Channel<string> MarketUpdateChannel { get; } = Channel.CreateUnbounded<string>();
    public Channel<StrategyUpdate> StrategyUpdateChannel { get; } = Channel.CreateUnbounded<StrategyUpdate>();
    public Channel<RebalancingProposal> RebalanceChannel { get; } = Channel.CreateUnbounded<RebalancingProposal>();
    public Channel<ArbitrageOpportunity> PassiveRebalanceChannel { get; } = Channel.CreateUnbounded<ArbitrageOpportunity>();
}
