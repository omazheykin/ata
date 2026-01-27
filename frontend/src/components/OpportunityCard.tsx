import React from 'react';
import type { ArbitrageOpportunity } from '../types/types';

interface OpportunityCardProps {
    opportunity: ArbitrageOpportunity;
    isNew?: boolean;
    onClick?: () => void;
}

const OpportunityCard: React.FC<OpportunityCardProps> = ({ opportunity, isNew = false, onClick }) => {
    const getProfitColor = (profit: number) => {
        if (profit >= 2) return 'text-profit-high';
        if (profit >= 1) return 'text-profit-medium';
        return 'text-profit-low';
    };

    const getProfitBgColor = (profit: number) => {
        if (profit >= 2) return 'bg-profit-high/10 border-profit-high/30';
        if (profit >= 1) return 'bg-profit-medium/10 border-profit-medium/30';
        return 'bg-profit-low/10 border-profit-low/30';
    };

    return (
        <div
            onClick={onClick}
            className={`glass rounded-xl p-5 card-hover ${isNew ? 'pulse-glow' : ''} animate-slide-up cursor-pointer transition-all duration-300 hover:scale-[1.02] hover:shadow-xl hover:shadow-primary-500/10`}
        >
            <div className="flex justify-between items-start mb-4">
                <div>
                    <div className="flex items-center gap-2 mb-1">
                        <h3 className="text-2xl font-bold text-white">{opportunity.asset}</h3>
                        <span className={`text-[10px] px-1.5 py-0.5 rounded font-bold uppercase tracking-wider ${opportunity.isSandbox
                                ? 'bg-amber-500/20 text-amber-400 border border-amber-500/30'
                                : 'bg-emerald-500/20 text-emerald-400 border border-emerald-500/30'
                            }`}>
                            {opportunity.isSandbox ? 'Sandbox' : 'Real'}
                        </span>
                    </div>
                    <p className="text-sm text-gray-400">
                        {new Date(opportunity.timestamp).toLocaleTimeString()}
                    </p>
                </div>
                <div className={`px-3 py-1 rounded-full border ${getProfitBgColor(opportunity.profitPercentage)}`}>
                    <span className={`text-lg font-bold ${getProfitColor(opportunity.profitPercentage)}`}>
                        +{opportunity.profitPercentage.toFixed(2)}%
                    </span>
                </div>
            </div>

            <div className="space-y-3">
                <div className="flex items-center justify-between">
                    <div className="flex-1">
                        <p className="text-xs text-gray-400 mb-1">Buy from</p>
                        <p className="text-sm font-semibold text-primary-400">{opportunity.buyExchange}</p>
                        <p className="text-lg font-bold text-white">${opportunity.buyPrice.toLocaleString()}</p>
                    </div>
                    <div className="px-3">
                        <svg className="w-6 h-6 text-primary-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 7l5 5m0 0l-5 5m5-5H6" />
                        </svg>
                    </div>
                    <div className="flex-1 text-right">
                        <p className="text-xs text-gray-400 mb-1">Sell to</p>
                        <p className="text-sm font-semibold text-primary-400">{opportunity.sellExchange}</p>
                        <p className="text-lg font-bold text-white">${opportunity.sellPrice.toLocaleString()}</p>
                    </div>
                </div>

                <div className="pt-3 border-t border-gray-700">
                    <div className="flex justify-between items-center">
                        <span className="text-xs text-gray-400">Volume</span>
                        <span className="text-sm font-semibold text-gray-200">{opportunity.volume.toFixed(2)} {opportunity.asset}</span>
                    </div>
                </div>
            </div>
        </div>
    );
};

export default OpportunityCard;
