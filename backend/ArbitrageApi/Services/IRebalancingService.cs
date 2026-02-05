using ArbitrageApi.Models;

namespace ArbitrageApi.Services;

public interface IRebalancingService
{
    decimal GetSkew(string asset);
    Dictionary<string, decimal> GetAllSkews();
    List<RebalancingProposal> GetProposals();
    ITrendAnalysisService GetTrendAnalysisService();
    Task<bool> ExecuteRebalanceAsync(RebalancingProposal proposal, CancellationToken ct = default);
}
