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
      className={`rounded-2xl p-4 border transition-all duration-500 ${
        (efficiencyScore ?? 1.0) > 0.7
          ? "glass border-blue-500/20 bg-blue-500/5 shadow-[0_0_20px_rgba(59,130,246,0.1)]"
          : "glass border-amber-500/20 bg-amber-500/5 shadow-[0_0_20px_rgba(245,158,11,0.1)]"
      }`}
    >
      <div className="flex items-center justify-between mb-4 border-b border-white/5 pb-3">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 bg-gradient-to-br from-indigo-500 to-purple-600 rounded-xl flex items-center justify-center text-xl shadow-lg">
            ⚖️
          </div>
          <div>
            <h4 className="text-sm font-black text-white uppercase tracking-tight">
              Rebalancing Recommendation
            </h4>
            <div className="flex items-center gap-2">
              <div
                className={`w-1.5 h-1.5 rounded-full animate-pulse ${(efficiencyScore ?? 1.0) > 0.7 ? "bg-blue-400" : "bg-amber-400"}`}
              ></div>
              <p className="text-[11px] text-gray-400 font-medium">
                {recommendation}
              </p>
            </div>
          </div>
        </div>
        <div className="text-right bg-white/5 px-3 py-1.5 rounded-xl border border-white/5">
          <p className="text-[9px] text-gray-500 uppercase font-black tracking-tighter mb-0.5">
            Avg. Quality
          </p>
          <p
            className={`text-lg font-black leading-none ${getEfficiencyColor(efficiencyScore ?? 0)}`}
          >
            {((efficiencyScore ?? 0) * 100).toFixed(1)}%
          </p>
        </div>
      </div>

      {/* Proposals List */}
      <div className="space-y-2">
        {proposals && proposals.length > 0 ? (
          proposals.map((prop) => (
            <div
              key={prop.asset}
              className={`p-3 rounded-xl border flex flex-col md:flex-row items-center justify-between gap-3 transition-all hover:bg-white/5 ${
                prop.isViable
                  ? "border-green-500/20 bg-green-500/5 shadow-[0_0_15px_rgba(34,197,94,0.05)]"
                  : "border-white/5 bg-white/2"
              }`}
            >
              <div className="flex items-center gap-3">
                <div className="bg-gradient-to-br from-gray-700 to-gray-800 px-2 py-1.5 rounded-lg font-black text-[10px] text-white shadow-sm border border-white/10 uppercase">
                  {prop.asset}
                </div>
                <div>
                  <div className="text-xs font-black text-white flex items-center gap-2 uppercase tracking-tight">
                    {prop.direction
                      .replace("Move from ", "From ")
                      .replace(" to ", " ➔ ")}
                    {prop.trendDescription && (
                      <span
                        className="text-[9px] bg-blue-500/10 text-blue-400 px-1.5 py-0.5 rounded-full border border-blue-500/20 font-black"
                        title="Aggregated 24h transaction trend"
                      >
                        📈 {prop.trendDescription}
                      </span>
                    )}
                  </div>
                  <div className="text-[10px] text-gray-400 font-medium">
                    Target Vol:{" "}
                    <span className="text-gray-200 font-bold">
                      {prop.amount.toLocaleString()} {prop.asset}
                    </span>
                  </div>
                </div>
              </div>

              <div className="flex items-center gap-4 text-right">
                <div className="hidden sm:block">
                  <div className="text-[9px] text-gray-500 font-black uppercase tracking-tighter">
                    Impact
                  </div>
                  <div
                    className={`text-xs font-black ${getCostColor(prop.costPercentage)}`}
                  >
                    {prop.costPercentage.toFixed(2)}%
                  </div>
                </div>

                <div className="pl-4 border-l border-white/10 flex items-center h-8">
                  {prop.isViable ? (
                    <div className="flex items-center gap-1.5 bg-green-500/10 text-green-400 px-2 py-1 rounded-lg border border-green-500/20 text-[9px] font-black uppercase tracking-widest shadow-sm">
                      <span className="w-1 h-1 bg-green-400 rounded-full animate-ping"></span>
                      Optimal
                    </div>
                  ) : (
                    <div
                      className="flex items-center gap-1.5 bg-red-500/10 text-red-400 px-2 py-1 rounded-lg border border-red-500/20 text-[9px] font-black uppercase tracking-widest opacity-60 grayscale"
                      title="Fee is too high relative to transfer amount"
                    >
                      Costly
                    </div>
                  )}
                </div>
              </div>
            </div>
          ))
        ) : (
          <div className="bg-white/5 border border-dashed border-white/10 rounded-xl py-6 flex flex-col items-center justify-center gap-2">
            <span className="text-2xl grayscale">🥗</span>
            <p className="text-[10px] text-gray-500 font-black uppercase tracking-widest">
              Inventories Optimized
            </p>
          </div>
        )}
      </div>
    </div>
  );
};

export default RebalancingPanel;
