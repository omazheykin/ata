import React, { useState } from "react";
import { apiService } from "../services/apiService";
import type { ArbitrageOpportunity, Balance } from "../types/types";

interface OpportunityListProps {
  opportunities: ArbitrageOpportunity[];
  onSelect: (opportunity: ArbitrageOpportunity) => void;
  selectedId?: string;
  threshold: number;
  balances: Record<string, Balance[]>;
}

const OpportunityList: React.FC<OpportunityListProps> = ({
  opportunities,
  onSelect,
  selectedId,
  threshold,
  balances,
}) => {
  const [showRealOnly, setShowRealOnly] = useState(false);
  const [loadingId, setLoadingId] = useState<string | null>(null);

  const handleTrade = async (
    e: React.MouseEvent,
    opp: ArbitrageOpportunity,
    executableVol: number,
  ) => {
    e.stopPropagation(); // Prevent row selection
    if (executableVol <= 0) return;

    if (
      !confirm(`Are you sure you want to trade ${executableVol} ${opp.asset}?`)
    )
      return;

    setLoadingId(opp.id.toString());
    try {
      const result = await apiService.executeTrade(opp);
      if (result.success) {
        alert("Trade request sent! 🚀");
      } else {
        alert("Trade failed request. Check logs.");
      }
    } catch (error) {
      console.error(error);
      alert("Trade failed to send.");
    } finally {
      setLoadingId(null);
    }
  };

  const filteredOpportunities = opportunities.filter((opp) => {
    if (showRealOnly) return opp.profitPercentage >= threshold;
    return true;
  });

  const getWalletCapacity = (opp: ArbitrageOpportunity) => {
    // Heuristic: Binance uses USDT, Coinbase uses USD, OKX uses USDT
    const quoteAsset =
      opp.buyExchange === "Binance" || opp.buyExchange === "OKX"
        ? "USDT"
        : "USD";

    // 2. Buy Side Capacity: How much can I buy with my Quote Asset on the Buy Exchange?
    const buyExBalances = balances[opp.buyExchange] || [];
    const quoteBalance =
      buyExBalances.find((b) => b.asset === quoteAsset)?.free ||
      (quoteAsset === "USD"
        ? buyExBalances.find((b) => b.asset === "USDC")?.free
        : 0) ||
      0;

    const maxBuyVol = quoteBalance / opp.buyPrice;

    // 3. Sell Side Capacity: How much Base Asset do I have on the Sell Exchange?
    const sellExBalances = balances[opp.sellExchange] || [];
    const baseBalance =
      sellExBalances.find((b) => b.asset === opp.asset)?.free || 0;
    const maxSellVol = baseBalance;

    // 4. Executable is the minimum of:
    //    a) Opportunity Volume (Liquidity)
    //    b) My Buy Capacity
    //    c) My Sell Capacity (Assume simultaneous execution)
    const userLimit = Math.min(maxBuyVol, maxSellVol);
    const executableVol = Math.min(opp.volume, userLimit);

    let limitingFactor = "Available";
    let isLimited = false;

    if (userLimit < opp.volume) {
      isLimited = true;
      if (maxBuyVol < maxSellVol) {
        limitingFactor = `Limited by ${quoteAsset} on ${opp.buyExchange}`;
      } else {
        limitingFactor = `Limited by ${opp.asset} on ${opp.sellExchange}`;
      }
    }

    return {
      executableVol,
      userLimit,
      quoteBalance,
      baseBalance,
      quoteAsset,
      limitingFactor,
      isLimited,
    };
  };

  return (
    <div className="glass rounded-xl overflow-hidden border border-white/5 animate-fade-in">
      {/* ... keeping existing header ... */}
      <div className="p-4 flex justify-between items-center border-b border-white/5">
        <h3 className="text-white font-bold flex items-center gap-2">
          <span>Live Opportunities</span>
          <span className="px-2 py-0.5 rounded-full bg-blue-500/20 text-blue-400 text-xs shadow-[0_0_10px_rgba(59,130,246,0.2)]">
            {filteredOpportunities.length}
          </span>
        </h3>
        <label className="flex items-center gap-2 cursor-pointer hover:opacity-80 transition-opacity">
          <span className="text-sm text-gray-400 font-medium">
            Hide Below Threshold (&lt;{threshold.toFixed(2)}%)
          </span>
          <div className="relative">
            <input
              type="checkbox"
              checked={showRealOnly}
              onChange={(e) => setShowRealOnly(e.target.checked)}
              className="sr-only peer"
            />
            <div className="w-10 h-5 bg-gray-700 rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-green-500"></div>
          </div>
        </label>
      </div>

      <div className="overflow-x-auto max-h-[500px] overflow-y-auto custom-scrollbar">
        <table className="w-full text-left border-collapse relative">
          <thead>
            <tr className="bg-gray-900/95 backdrop-blur-sm text-gray-400 text-xs uppercase tracking-wider sticky top-0 z-20 shadow-sm">
              {/* ... existing columns ... */}
              <th className="p-3 font-medium">
                <div className="flex items-center gap-1">Time</div>
              </th>
              <th className="p-3 font-medium">
                <div className="flex items-center gap-1">Asset</div>
              </th>
              <th className="p-3 font-medium text-right">
                <div className="flex items-center justify-end gap-1">
                  Liquidity
                </div>
              </th>
              {/* NEW COLUMN */}
              <th className="p-3 font-medium text-right bg-blue-500/5">
                <div className="flex items-center justify-end gap-1 text-blue-300">
                  Your Cap
                  <span
                    className="cursor-help"
                    title="Max executable volume based on your wallet balances on both exchanges"
                  >
                    ?
                  </span>
                </div>
              </th>
              <th className="p-3 font-medium text-right">
                <div className="flex items-center justify-end gap-1">
                  Spread
                </div>
              </th>
              <th className="p-3 font-medium text-right">
                <div className="flex items-center justify-end gap-1">
                  Est. Profit
                </div>
              </th>
              <th className="p-3 font-medium">Buy At</th>
              <th className="p-3 font-medium">Sell At</th>
              <th className="p-3 font-medium text-center">Action</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-white/5">
            {filteredOpportunities.map((opp) => {
              const {
                executableVol,
                isLimited,
                limitingFactor,
                quoteAsset,
                quoteBalance,
                baseBalance,
              } = getWalletCapacity(opp);
              const potentialProfit =
                executableVol * opp.buyPrice * (opp.profitPercentage / 100);

              return (
                <tr
                  key={opp.id}
                  onClick={() => onSelect(opp)}
                  className={`group transition-colors cursor-pointer ${
                    opp.id === selectedId
                      ? "bg-blue-500/10"
                      : "hover:bg-white/5"
                  }`}
                >
                  <td className="p-3 text-xs text-gray-400 whitespace-nowrap">
                    {new Date(opp.timestamp).toLocaleTimeString([], {
                      hour: "2-digit",
                      minute: "2-digit",
                      second: "2-digit",
                    })}
                  </td>
                  <td className="p-3">
                    <div className="flex items-center gap-2">
                      <div className="w-6 h-6 rounded-full bg-white/10 flex items-center justify-center text-xs font-bold text-white">
                        {opp.asset.charAt(0)}
                      </div>
                      <span className="font-bold text-white text-sm">
                        {opp.asset}
                      </span>
                    </div>
                  </td>
                  <td className="p-3 text-right">
                    <div className="flex flex-col items-end">
                      <span className="text-sm font-bold text-white">
                        {opp.volume.toLocaleString(undefined, {
                          maximumFractionDigits: 4,
                        })}
                      </span>
                      <span className="text-[10px] text-gray-500 font-mono">
                        ≈$
                        {(opp.volume * opp.buyPrice).toLocaleString(undefined, {
                          maximumFractionDigits: 0,
                        })}
                      </span>
                    </div>
                  </td>
                  {/* MY CAP CELL */}
                  <td className="p-3 text-right bg-blue-500/5">
                    <div className="flex flex-col items-end">
                      <div className="flex items-center gap-1">
                        <span
                          className={`text-sm font-bold ${executableVol > 0 ? "text-blue-300" : "text-gray-500"}`}
                        >
                          {executableVol.toLocaleString(undefined, {
                            maximumFractionDigits: 4,
                          })}
                        </span>
                        <span className="text-[10px] text-gray-500 font-bold">
                          {opp.asset}
                        </span>
                      </div>
                      <span
                        className={`text-[9px] font-mono whitespace-nowrap cursor-help ${isLimited ? "text-amber-400/80 dashed-underline" : "text-blue-400/60"}`}
                        title={
                          isLimited
                            ? limitingFactor.includes(quoteAsset)
                              ? `The calculated opportunity required ${(opp.volume * opp.buyPrice).toLocaleString(undefined, { maximumFractionDigits: 2 })} ${quoteAsset}, you have ${quoteBalance.toLocaleString(undefined, { maximumFractionDigits: 2 })} ${quoteAsset}`
                              : `The calculated opportunity required ${opp.volume.toLocaleString(undefined, { maximumFractionDigits: 4 })} ${opp.asset}, you have ${baseBalance.toLocaleString(undefined, { maximumFractionDigits: 4 })} ${opp.asset}`
                            : ""
                        }
                      >
                        {isLimited
                          ? limitingFactor
                          : `≈$${(executableVol * opp.buyPrice).toLocaleString(
                              undefined,
                              { maximumFractionDigits: 0 },
                            )} capacity`}
                      </span>
                    </div>
                  </td>
                  <td className="p-3 text-right">
                    <div className="flex flex-col items-end">
                      <span
                        className={`text-sm font-bold ${
                          opp.profitPercentage >= 0
                            ? "text-green-400"
                            : "text-red-400"
                        }`}
                      >
                        +{opp.profitPercentage.toFixed(3)}%
                      </span>
                    </div>
                  </td>
                  <td className="p-3 text-right">
                    <div
                      className="flex flex-col items-end cursor-help"
                      title={`Based on executable volume: ${executableVol.toFixed(4)} ${opp.asset}`}
                    >
                      <span
                        className={`text-sm font-bold ${executableVol > 0 ? "text-green-400" : "text-gray-500"}`}
                      >
                        +$
                        {potentialProfit.toLocaleString(undefined, {
                          maximumFractionDigits: 2,
                          minimumFractionDigits: 2,
                        })}
                      </span>
                      {isLimited && (
                        <span className="text-[9px] text-gray-500 line-through">
                          Theoretical: $
                          {(
                            opp.volume *
                            opp.buyPrice *
                            (opp.profitPercentage / 100)
                          ).toFixed(2)}
                        </span>
                      )}
                    </div>
                  </td>
                  <td className="p-3">
                    <div className="flex flex-col">
                      <span className="text-xs text-white">
                        {opp.buyExchange}
                      </span>
                      <span className="text-[10px] text-gray-500 font-mono">
                        ${opp.buyPrice.toFixed(2)}
                      </span>
                    </div>
                  </td>
                  <td className="p-3">
                    <div className="flex flex-col">
                      <span className="text-xs text-white">
                        {opp.sellExchange}
                      </span>
                      <span className="text-[10px] text-gray-500 font-mono">
                        ${opp.sellPrice.toFixed(2)}
                      </span>
                    </div>
                  </td>
                  <td className="p-3 text-center">
                    <button
                      onClick={(e) => handleTrade(e, opp, executableVol)}
                      disabled={
                        loadingId === opp.id.toString() || executableVol <= 0
                      }
                      className={`px-3 py-1 rounded-lg text-xs font-bold border transition-colors ${
                        executableVol > 0
                          ? "bg-green-500/20 text-green-400 border-green-500/30 hover:bg-green-500/30"
                          : "bg-gray-500/10 text-gray-500 border-gray-500/20 cursor-not-allowed"
                      }`}
                    >
                      {loadingId === opp.id.toString() ? "..." : "Trade"}
                    </button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {opportunities.length === 0 && (
        <div className="p-8 text-center text-gray-500 text-sm">
          No opportunities found
        </div>
      )}
    </div>
  );
};

export default OpportunityList;
