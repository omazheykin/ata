import React from "react";
import type { ArbitrageOpportunity } from "../types/types";

interface OpportunityCardProps {
  opportunity: ArbitrageOpportunity;
  isNew?: boolean;
  onClick?: () => void;
}

const OpportunityCard: React.FC<OpportunityCardProps> = ({
  opportunity,
  isNew = false,
  onClick,
}) => {
  return (
    <div
      onClick={onClick}
      className={`glass rounded-xl p-5 card-hover ${isNew ? "pulse-glow" : ""} animate-slide-up cursor-pointer transition-all duration-300 hover:scale-[1.02] hover:shadow-xl hover:shadow-primary-500/10`}
    >
      <div className="flex justify-between items-start mb-4">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <h3 className="text-2xl font-bold text-white">
              {opportunity.asset}
            </h3>
            <span
              className={`text-[10px] px-1.5 py-0.5 rounded font-bold uppercase tracking-wider ${
                opportunity.isSandbox
                  ? "bg-amber-500/20 text-amber-400 border border-amber-500/30"
                  : "bg-emerald-500/20 text-emerald-400 border border-emerald-500/30"
              }`}
            >
              {opportunity.isSandbox ? "Sandbox" : "Real"}
            </span>
          </div>
          <p className="text-sm text-gray-400">
            {new Date(opportunity.timestamp).toLocaleTimeString()}
          </p>
        </div>
        <div className="flex flex-col gap-2 items-end">
          <div className={`p-2 rounded-lg bg-black/20 border border-white/5`}>
            <div
              className={`text-xl font-black ${
                opportunity.profitPercentage >= 0
                  ? "text-green-400"
                  : "text-red-400"
              }`}
            >
              {opportunity.profitPercentage >= 0 ? "+" : ""}
              {opportunity.profitPercentage.toFixed(2)}%
            </div>
            <div className="text-[10px] text-gray-500 font-bold uppercase tracking-wider mt-0.5">
              NET SPREAD
            </div>
          </div>
          <div className="p-2 rounded-lg bg-white/5 border border-white/5">
            <div className="text-sm font-black text-blue-400">
              +{opportunity.grossProfitPercentage.toFixed(2)}%
            </div>
            <div className="text-[10px] text-gray-400 font-bold uppercase tracking-wider mt-0.5">
              GROSS
            </div>
          </div>
        </div>
      </div>

      <div className="space-y-3">
        <div className="flex items-center justify-between">
          <div className="flex-1">
            <p className="text-xs text-gray-400 mb-1">Buy from</p>
            <p className="text-sm font-semibold text-primary-400">
              {opportunity.buyExchange}
            </p>
            <p className="text-lg font-bold text-white">
              $
              {opportunity.buyPrice.toLocaleString(undefined, {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2,
              })}
            </p>
            <p className="text-[10px] text-gray-500 mt-0.5">
              Fee: {(opportunity.buyFee * 100).toFixed(2)}%
            </p>
          </div>
          <div className="px-3">
            <svg
              className="w-6 h-6 text-primary-500"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M13 7l5 5m0 0l-5 5m5-5H6"
              />
            </svg>
          </div>
          <div className="flex-1 text-right">
            <p className="text-xs text-gray-400 mb-1">Sell to</p>
            <p className="text-sm font-semibold text-primary-400">
              {opportunity.sellExchange}
            </p>
            <p className="text-lg font-bold text-white">
              $
              {opportunity.sellPrice.toLocaleString(undefined, {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2,
              })}
            </p>
            <p className="text-[10px] text-gray-500 mt-0.5">
              Fee: {(opportunity.sellFee * 100).toFixed(2)}%
            </p>
          </div>
        </div>

        <div className="pt-3 border-t border-gray-700">
          <div className="flex justify-between items-center">
            <span className="text-xs text-gray-400">Volume</span>
            <span className="text-sm font-semibold text-gray-200">
              {opportunity.volume.toFixed(2)} {opportunity.asset}
            </span>
          </div>
        </div>
      </div>
    </div>
  );
};

export default OpportunityCard;
