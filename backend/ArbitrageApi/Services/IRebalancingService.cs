using ArbitrageApi.Models;

namespace ArbitrageApi.Services;

public interface IRebalancingService
{
    decimal GetDeviation(string asset, string exchange);
    Dictionary<string, Dictionary<string, decimal>> GetAllDeviations();
    List<RebalancingProposal> GetProposals();
    ITrendAnalysisService GetTrendAnalysisService();
    Task<bool> ExecuteRebalanceAsync(RebalancingProposal proposal, CancellationToken ct = default);
}
