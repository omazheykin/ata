import React, { useState, useEffect } from 'react';
import type { ArbitrageOpportunity, Balance } from '../types/types';
import { apiService } from '../services/apiService';

interface ProfitPanelProps {
    opportunity: ArbitrageOpportunity | null;
    balances: Record<string, Balance[]>;
    onClose: () => void;
}

const ProfitPanel: React.FC<ProfitPanelProps> = ({ opportunity, balances, onClose }) => {
    const [investmentAmount, setInvestmentAmount] = useState<number>(1000);
    const [isVisible, setIsVisible] = useState(false);
    const [isExecuting, setIsExecuting] = useState(false);
    const [executionError, setExecutionError] = useState<string | null>(null);
    const [executionSuccess, setExecutionSuccess] = useState(false);

    useEffect(() => {
        if (opportunity) {
            setIsVisible(true);
            setExecutionError(null);
            setExecutionSuccess(false);
        } else {
            setIsVisible(false);
        }
    }, [opportunity]);

    if (!opportunity) return null;

    // 1. Check USD Balance on Buy Exchange
    const buyExchangeBalances = balances[opportunity.buyExchange] || [];
    const quoteAssetBalance = buyExchangeBalances.find(b => b.asset === 'USD' || b.asset === 'USDT' || b.asset === 'USDC');
    const availableUsd = quoteAssetBalance?.free || 0;
    const isOverUsdBalance = investmentAmount > availableUsd;

    // 2. Check Asset Balance on Sell Exchange (Crucial for Arbitrage)
    const sellExchangeBalances = balances[opportunity.sellExchange] || [];
    const baseAssetBalance = sellExchangeBalances.find(b => b.asset === opportunity.asset);
    const availableAsset = baseAssetBalance?.free || 0;

    // Calculate required asset volume based on investment amount
    const requiredAssetVolume = investmentAmount / opportunity.buyPrice;
    const isOverAssetBalance = requiredAssetVolume > availableAsset;

    const buyFeeAmount = investmentAmount * (opportunity.buyFee / 100);
    const sellFeeAmount = (investmentAmount + (investmentAmount * (opportunity.sellPrice - opportunity.buyPrice) / opportunity.buyPrice)) * (opportunity.sellFee / 100);
    const totalFees = buyFeeAmount + sellFeeAmount;

    const grossProfit = investmentAmount * ((opportunity.sellPrice - opportunity.buyPrice) / opportunity.buyPrice);
    const netProfit = grossProfit - totalFees;
    const totalReturn = investmentAmount + netProfit;

    const presetAmounts = [100, 500, 1000, 5000, 10000];

    const handleMax = () => {
        // Max is limited by both USD on Buy exchange and Asset on Sell exchange
        const maxFromUsd = availableUsd;
        const maxFromAsset = availableAsset * opportunity.buyPrice;
        const maxInvestment = Math.min(maxFromUsd, maxFromAsset);

        setInvestmentAmount(Math.floor(maxInvestment * 100) / 100);
    };

    const handleExecuteTrade = async () => {
        if (!opportunity || isOverUsdBalance || isOverAssetBalance || investmentAmount <= 0) return;

        try {
            setIsExecuting(true);
            setExecutionError(null);

            // Create a custom opportunity with the selected volume
            const manualOpportunity = {
                ...opportunity,
                volume: investmentAmount / opportunity.buyPrice
            };

            const result = await apiService.executeTrade(manualOpportunity);

            if (result.success) {
                setExecutionSuccess(true);
                setTimeout(() => {
                    onClose();
                }, 2000);
            } else {
                setExecutionError('Trade execution failed. Check logs for details.');
            }
        } catch (err) {
            console.error('Error executing manual trade:', err);
            setExecutionError('An error occurred during trade execution.');
        } finally {
            setIsExecuting(false);
        }
    };

    return (
        <>
            {/* Backdrop */}
            <div
                className={`fixed inset-0 bg-black/50 backdrop-blur-sm z-40 transition-opacity duration-300 ${isVisible ? 'opacity-100' : 'opacity-0 pointer-events-none'
                    }`}
                onClick={onClose}
            />

            {/* Panel */}
            <div
                className={`fixed top-0 right-0 h-full w-full md:w-[480px] bg-[#0f172a]/95 backdrop-blur-xl border-l border-white/10 shadow-2xl z-50 transform transition-transform duration-300 ease-out ${isVisible ? 'translate-x-0' : 'translate-x-full'
                    }`}
            >
                <div className="h-full flex flex-col p-6 overflow-y-auto">
                    {/* Header */}
                    <div className="flex justify-between items-start mb-8">
                        <div>
                            <h2 className="text-3xl font-bold text-white mb-2 flex items-center gap-3">
                                {opportunity.asset}
                                <span className="text-sm font-normal px-3 py-1 rounded-full bg-primary-500/20 text-primary-400 border border-primary-500/30">
                                    Arbitrage
                                </span>
                            </h2>
                            <p className="text-gray-400">Profit Calculator</p>
                        </div>
                        <button
                            onClick={onClose}
                            className="p-2 rounded-lg hover:bg-white/10 text-gray-400 hover:text-white transition-colors"
                        >
                            <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </button>
                    </div>

                    {/* Key Stats */}
                    <div className="grid grid-cols-2 gap-4 mb-8">
                        <div className="glass p-4 rounded-xl border border-green-500/20 bg-green-500/5">
                            <p className="text-sm text-gray-400 mb-1">Net Profit</p>
                            <p className="text-2xl font-bold text-green-400">+{opportunity.profitPercentage.toFixed(2)}%</p>
                        </div>
                        <div className="glass p-4 rounded-xl">
                            <p className="text-sm text-gray-400 mb-1">Volume Available</p>
                            <p className="text-2xl font-bold text-white">{opportunity.volume.toFixed(4)}</p>
                        </div>
                    </div>

                    {/* Calculator Section */}
                    <div className="space-y-6 mb-8">
                        <div>
                            <div className="flex justify-between items-center mb-3">
                                <label className="block text-sm font-medium text-gray-300">
                                    Investment Amount (USD)
                                </label>
                                <div className="text-xs text-gray-500">
                                    Available: <span className={availableUsd > 0 ? 'text-primary-400' : 'text-red-400'}>
                                        ${availableUsd.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
                                    </span>
                                </div>
                            </div>
                            <div className="relative">
                                <span className="absolute left-4 top-1/2 -translate-y-1/2 text-gray-400">$</span>
                                <input
                                    type="number"
                                    value={investmentAmount}
                                    onChange={(e) => setInvestmentAmount(Number(e.target.value))}
                                    className={`w-full bg-white/5 border ${isOverUsdBalance || isOverAssetBalance ? 'border-red-500/50' : 'border-white/10'} rounded-xl py-4 pl-10 pr-20 text-xl font-bold text-white focus:ring-2 ${isOverUsdBalance || isOverAssetBalance ? 'focus:ring-red-500' : 'focus:ring-primary-500'} focus:border-transparent transition-all outline-none`}
                                />
                                <button
                                    onClick={handleMax}
                                    className="absolute right-3 top-1/2 -translate-y-1/2 px-3 py-1.5 rounded-lg bg-primary-500/20 text-primary-400 text-xs font-bold hover:bg-primary-500/30 transition-colors"
                                >
                                    MAX
                                </button>
                            </div>

                            {/* Validation Messages */}
                            <div className="mt-2 space-y-1">
                                {isOverUsdBalance && (
                                    <p className="text-red-400 text-xs flex items-center gap-1">
                                        <svg className="w-3 h-3" fill="currentColor" viewBox="0 0 20 20">
                                            <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
                                        </svg>
                                        Insufficient USD on {opportunity.buyExchange}
                                    </p>
                                )}
                                {isOverAssetBalance && (
                                    <p className="text-red-400 text-xs flex items-center gap-1">
                                        <svg className="w-3 h-3" fill="currentColor" viewBox="0 0 20 20">
                                            <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
                                        </svg>
                                        Insufficient {opportunity.asset} on {opportunity.sellExchange} (Available: {availableAsset.toFixed(4)})
                                    </p>
                                )}
                            </div>
                        </div>

                        {/* Preset Amounts */}
                        <div className="flex flex-wrap gap-2">
                            {presetAmounts.map((amount) => (
                                <button
                                    key={amount}
                                    onClick={() => setInvestmentAmount(amount)}
                                    className={`px-4 py-2 rounded-lg text-sm font-medium transition-all ${investmentAmount === amount
                                        ? 'bg-primary-500 text-white shadow-lg shadow-primary-500/25'
                                        : 'bg-white/5 text-gray-400 hover:bg-white/10 hover:text-white'
                                        }`}
                                >
                                    ${amount.toLocaleString()}
                                </button>
                            ))}
                        </div>
                    </div>

                    {/* Results Card */}
                    <div className="glass rounded-2xl p-6 border border-white/10 relative overflow-hidden group">
                        <div className="absolute inset-0 bg-gradient-to-br from-primary-500/10 to-purple-500/10 opacity-50 group-hover:opacity-100 transition-opacity" />

                        <div className="relative space-y-4">
                            <div className="flex justify-between items-center">
                                <span className="text-gray-400 text-sm">Gross Profit</span>
                                <div className="flex items-center gap-1">
                                    <span className="text-primary-400 font-bold">+</span>
                                    <span className="text-white font-semibold">
                                        ${grossProfit.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
                                    </span>
                                </div>
                            </div>

                            <div className="flex justify-between items-center text-red-400/80 text-sm">
                                <span>Total Fees ({(opportunity.buyFee + opportunity.sellFee).toFixed(2)}%)</span>
                                <div className="flex items-center gap-1">
                                    <span className="font-bold">-</span>
                                    <span>
                                        ${totalFees.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
                                    </span>
                                </div>
                            </div>

                            <div className="pt-4 border-t border-white/10 flex justify-between items-center">
                                <span className="text-gray-300 font-medium">Net Profit</span>
                                <div className="flex items-center gap-1">
                                    <span className="text-green-400 font-bold">+</span>
                                    <span className="text-2xl font-bold text-green-400">
                                        ${netProfit.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
                                    </span>
                                </div>
                            </div>

                            <div className="flex justify-between items-center pt-1">
                                <span className="text-gray-400 text-sm">Total Return</span>
                                <span className="text-xl font-bold text-white">
                                    ${totalReturn.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
                                </span>
                            </div>
                        </div>
                    </div>

                    {/* Exchange Details */}
                    <div className="mt-8 space-y-4">
                        <h3 className="text-lg font-semibold text-white mb-4">Execution Path</h3>

                        <div className="relative pl-8 border-l-2 border-white/10 space-y-8">
                            {/* Buy Step */}
                            <div className="relative">
                                <div className="absolute -left-[33px] top-0 w-4 h-4 rounded-full bg-blue-500 border-4 border-[#0f172a]" />
                                <div className="glass p-4 rounded-xl">
                                    <div className="flex justify-between mb-1">
                                        <span className="text-blue-400 font-bold">Buy on {opportunity.buyExchange}</span>
                                        <span className="text-white font-mono">${opportunity.buyPrice.toLocaleString()}</span>
                                    </div>
                                    <div className="flex justify-between text-xs text-gray-500">
                                        <span>Fee: {opportunity.buyFee.toFixed(2)}%</span>
                                        <span>-${buyFeeAmount.toFixed(2)}</span>
                                    </div>
                                </div>
                            </div>

                            {/* Sell Step */}
                            <div className="relative">
                                <div className="absolute -left-[33px] top-0 w-4 h-4 rounded-full bg-purple-500 border-4 border-[#0f172a]" />
                                <div className="glass p-4 rounded-xl">
                                    <div className="flex justify-between mb-1">
                                        <span className="text-purple-400 font-bold">Sell on {opportunity.sellExchange}</span>
                                        <span className="text-white font-mono">${opportunity.sellPrice.toLocaleString()}</span>
                                    </div>
                                    <div className="flex justify-between text-xs text-gray-500">
                                        <span>Fee: {opportunity.sellFee.toFixed(2)}%</span>
                                        <span>-${sellFeeAmount.toFixed(2)}</span>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>

                    {/* Action Button */}
                    <div className="mt-auto pt-8">
                        {executionError && (
                            <div className="mb-4 p-3 rounded-lg bg-red-500/10 border border-red-500/20 text-red-400 text-sm flex items-center gap-2">
                                <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
                                    <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
                                </svg>
                                {executionError}
                            </div>
                        )}
                        {executionSuccess && (
                            <div className="mb-4 p-3 rounded-lg bg-green-500/10 border border-green-500/20 text-green-400 text-sm flex items-center gap-2">
                                <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
                                    <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
                                </svg>
                                Trade executed successfully!
                            </div>
                        )}

                        <button
                            onClick={handleExecuteTrade}
                            disabled={isOverUsdBalance || isOverAssetBalance || investmentAmount <= 0 || isExecuting || executionSuccess}
                            className={`w-full py-4 bg-gradient-to-r ${isOverUsdBalance || isOverAssetBalance || investmentAmount <= 0 || isExecuting || executionSuccess ? 'from-gray-700 to-gray-600 cursor-not-allowed opacity-50' : 'from-primary-600 to-primary-500 hover:from-primary-500 hover:to-primary-400'} text-white font-bold rounded-xl shadow-lg ${isOverUsdBalance || isOverAssetBalance || investmentAmount <= 0 || isExecuting || executionSuccess ? '' : 'shadow-primary-500/25'} transition-all transform ${isOverUsdBalance || isOverAssetBalance || investmentAmount <= 0 || isExecuting || executionSuccess ? '' : 'hover:scale-[1.02] active:scale-[0.98]'} flex items-center justify-center gap-2`}
                        >
                            {isExecuting ? (
                                <>
                                    <svg className="animate-spin h-5 w-5 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                                    </svg>
                                    Executing...
                                </>
                            ) : executionSuccess ? (
                                'Success!'
                            ) : isOverUsdBalance || isOverAssetBalance ? (
                                'Insufficient Balance'
                            ) : (
                                'Execute Trade'
                            )}
                        </button>
                        <p className="text-center text-xs text-gray-500 mt-3">
                            *Calculations include estimated fees. Actual results may vary.
                        </p>
                    </div>
                </div>
            </div>
        </>
    );
};

export default ProfitPanel;
