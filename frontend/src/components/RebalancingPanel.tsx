import React from "react";
import type { RebalancingInfo } from "../types/types";

interface RebalancingPanelProps {
  rebalancing: RebalancingInfo;
}

const RebalancingPanel: React.FC<RebalancingPanelProps> = ({ rebalancing }) => {
  const { recommendation, efficiencyScore, proposals } = rebalancing;

  const getEfficiencyColor = (score: number) => {
    if (score > 0.9) return "text-green-400";
    if (score > 0.7) return "text-yellow-400";
    return "text-red-400";
  };

  const getCostColor = (percentage: number) => {
    if (percentage < 0.5) return "text-green-400";
    if (percentage < 1.0) return "text-yellow-400";
    return "text-red-400";
  };

  return (
    <div
      className={`rounded-xl p-6 border transition-all ${
        (efficiencyScore ?? 1.0) > 0.7
          ? "bg-blue-500/10 border-blue-500/30"
          : "bg-yellow-500/10 border-yellow-500/30"
      }`}
    >
      <div className="flex items-start justify-between mb-4">
        <div className="flex items-center gap-4">
          <div className="text-3xl">⚖️</div>
          <div>
            <h4 className="text-lg font-bold text-white mb-1">
              Rebalancing Recommendation
            </h4>
            <p className="text-gray-200">{recommendation}</p>
          </div>
        </div>
        <div className="text-right">
          <p className="text-xs text-gray-400 uppercase font-bold">
            Efficiency Score
          </p>
          <p
            className={`text-2xl font-bold ${getEfficiencyColor(efficiencyScore ?? 0)}`}
          >
            {((efficiencyScore ?? 0) * 100).toFixed(1)}%
          </p>
        </div>
      </div>

      {/* Proposals List */}
      <div className="space-y-3">
        {proposals && proposals.length > 0 ? (
          proposals.map((prop) => (
            <div
              key={prop.asset}
              className={`glass p-3 rounded-lg border flex flex-col md:flex-row items-center justify-between gap-4 ${
                prop.isViable
                  ? "border-green-500/30 bg-green-500/50"
                  : "border-white/5"
              }`}
            >
              <div className="flex items-center gap-3">
                <div className="bg-white/10 p-2 rounded-lg font-bold text-white">
                  {prop.asset}
                </div>
                <div>
                  <div className="text-sm font-bold text-white flex items-center gap-2">
                    {prop.direction}
                    {prop.trendDescription && (
                      <span
                        className="text-[10px] bg-blue-500/20 text-blue-300 px-1.5 py-0.5 rounded border border-blue-500/30 font-normal"
                        title="Aggregated 24h transaction trend"
                      >
                        📈 {prop.trendDescription}
                      </span>
                    )}
                  </div>
                  <div className="text-xs text-gray-400">
                    Transfer:{" "}
                    <span className="text-white">
                      {prop.amount} {prop.asset}
                    </span>
                  </div>
                </div>
              </div>

              <div className="flex items-center gap-6 text-right">
                <div>
                  <div className="text-xs text-gray-400">Est. Fee</div>
                  <div className="text-sm font-mono text-gray-300">
                    {prop.estimatedFee} {prop.asset}
                  </div>
                </div>

                <div>
                  <div className="text-xs text-gray-400">Cost Impact</div>
                  <div
                    className={`text-sm font-bold ${getCostColor(prop.costPercentage)}`}
                  >
                    {prop.costPercentage.toFixed(2)}%
                  </div>
                </div>

                <div className="pl-4 border-l border-white/10">
                  {prop.isViable ? (
                    <div className="flex items-center gap-1 text-green-400 text-xs font-bold uppercase tracking-wider">
                      <span>✅ Recommended</span>
                    </div>
                  ) : (
                    <div
                      className="flex items-center gap-1 text-red-400 text-xs font-bold uppercase tracking-wider tooltip"
                      data-tip="Fee is too high relative to transfer amount"
                    >
                      <span>❌ High Cost</span>
                    </div>
                  )}
                </div>
              </div>
            </div>
          ))
        ) : (
          <div className="text-center py-4 text-gray-500 text-sm italic">
            No active rebalancing proposals. Inventories are balanced.
          </div>
        )}
      </div>
    </div>
  );
};

export default RebalancingPanel;
