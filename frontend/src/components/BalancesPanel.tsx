import React from "react";
import type { Balance, Transaction } from "../types/types";

interface BalancesPanelProps {
  isExpanded?: boolean;
  balances: Record<string, Balance[]>;
  transactions: Transaction[];
  loading: boolean;
  error: string | null;
  onRefresh: () => void;
  onOpenDeposit: (exchange: string) => void;
}

const BalancesPanel: React.FC<BalancesPanelProps> = ({
  isExpanded = true,
  balances,
  transactions,
  loading,
  error,
  onRefresh,
  onOpenDeposit,
}) => {
  const [visibleCount, setVisibleCount] = React.useState(10);
  const totalProfit = transactions.reduce((sum, tx) => sum + tx.profit, 0);

  if (!isExpanded) {
    return (
      <div className="flex flex-col h-full bg-[#0f172a]/50 backdrop-blur-xl items-center py-4 gap-6">
        <div className="text-xl">💰</div>
        <div className="flex flex-col gap-4 text-xs font-black opacity-40 [writing-mode:vertical-lr] rotate-180">
          BALANCES
        </div>
        <div className="mt-auto text-xl">🔄</div>
      </div>
    );
  }

  return (
    <div className="flex flex-col min-h-0 h-full bg-[#0f172a]/50 backdrop-blur-xl w-full">
      <div className="p-4 border-b border-white/10">
        <div className="flex justify-between items-center mb-4">
          <h2 className="text-lg font-bold text-white flex items-center gap-2">
            <svg
              className="w-5 h-5 text-primary-400"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M3 10h18M7 15h1m4 0h1m-7 4h12a3 3 0 003-3V8a3 3 0 00-3-3H6a3 3 0 00-3 3v8a3 3 0 003 3z"
              />
            </svg>
            My Balances
          </h2>
          <button
            onClick={onRefresh}
            className="p-1.5 rounded-lg hover:bg-white/5 text-gray-400 hover:text-white transition-colors"
            title="Refresh Balances"
          >
            <svg
              className={`w-4 h-4 ${loading ? "animate-spin" : ""}`}
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"
              />
            </svg>
          </button>
        </div>
      </div>

      <div className="flex-1 flex flex-col min-h-0">
        {/* Balances Section - Scrollable */}
        <div className="flex-1 overflow-y-auto p-3 space-y-4 scrollbar-thin scrollbar-thumb-white/10 scrollbar-track-transparent">
          {error && (
            <div className="p-2 rounded-lg bg-red-500/10 border border-red-500/20 text-red-400 text-[10px]">
              {error}
            </div>
          )}

          {Object.entries(balances).map(([exchange, assetBalances]) => (
            <div key={exchange} className="space-y-2">
              <div className="flex items-center justify-between px-1">
                <div className="flex items-center gap-2">
                  <h3 className="text-[10px] font-semibold text-gray-500 uppercase tracking-wider">
                    {exchange}
                  </h3>
                  <button
                    onClick={() => onOpenDeposit(exchange)}
                    className="p-0.5 rounded bg-primary-500/10 text-primary-400 hover:bg-primary-500/20 transition-colors"
                    title="Deposit Funds"
                  >
                    <svg
                      className="w-2.5 h-2.5"
                      fill="none"
                      stroke="currentColor"
                      viewBox="0 0 24 24"
                    >
                      <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        strokeWidth={2}
                        d="M12 4v16m8-8H4"
                      />
                    </svg>
                  </button>
                </div>
                <span className="text-[9px] px-1.5 py-0.5 rounded-full bg-white/5 text-gray-600">
                  {assetBalances.length}
                </span>
              </div>

              <div className="grid grid-cols-2 gap-2">
                {assetBalances.length === 0 ? (
                  <p className="col-span-2 text-[10px] text-gray-600 italic px-1 py-4 text-center">
                    No balances
                  </p>
                ) : (
                  assetBalances.map((balance) => (
                    <div
                      key={balance.asset}
                      className="glass rounded-xl px-2.5 py-2 border border-white/5 transition-all hover:bg-white/10 group cursor-default"
                      title={`Available: ${balance.free.toLocaleString()} | Locked: ${balance.locked.toLocaleString()}`}
                    >
                      <div className="flex items-center gap-2">
                        <div className="w-7 h-7 bg-white/5 rounded-lg flex items-center justify-center text-xs group-hover:scale-110 transition-transform shadow-inner border border-white/5">
                          {balance.asset === "ETH"
                            ? "🔹"
                            : balance.asset === "BTC"
                              ? "₿"
                              : balance.asset === "USDT"
                                ? "💵"
                                : balance.asset === "BNB"
                                  ? "🔸"
                                  : "💰"}
                        </div>
                        <div className="min-w-0">
                          <p className="text-[8px] text-gray-500 uppercase font-black tracking-tighter leading-none mb-0.5">
                            {balance.asset}
                          </p>
                          <p className="text-[11px] font-bold text-white truncate">
                            {balance.total.toLocaleString(undefined, {
                              maximumFractionDigits: 3,
                            })}
                          </p>
                        </div>
                      </div>
                    </div>
                  ))
                )}
              </div>
            </div>
          ))}

          {loading && Object.keys(balances).length === 0 && (
            <div className="flex flex-col items-center justify-center py-8 space-y-3">
              <div className="w-6 h-6 border-2 border-primary-500/20 border-t-primary-500 rounded-full animate-spin" />
              <p className="text-[10px] text-gray-600">Loading balances...</p>
            </div>
          )}
        </div>

        {/* Recent Transactions - Fixed at bottom, also scrollable if needed */}
        <div className="border-t border-white/10 bg-black/40 flex flex-col max-h-[40%]">
          <div className="p-3 border-b border-white/5 flex items-center justify-between">
            <h3 className="text-[10px] font-semibold text-gray-400 uppercase tracking-wider flex items-center gap-2">
              <svg
                className="w-3.5 h-3.5"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={2}
                  d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z"
                />
              </svg>
              Recent Activity
            </h3>
            <div className="text-right">
              <p className="text-[9px] text-gray-500 uppercase tracking-tighter">
                Total Profit
              </p>
              <p
                className={`text-xs font-bold ${totalProfit >= 0 ? "text-green-400" : "text-red-400"}`}
              >
                ${totalProfit.toFixed(2)}
              </p>
            </div>
          </div>

          <div className="flex-1 overflow-y-auto p-2 space-y-2 scrollbar-thin scrollbar-thumb-white/10 scrollbar-track-transparent">
            {transactions.length === 0 ? (
              <div className="text-center py-6 px-4">
                <p className="text-[10px] text-gray-600">No transactions yet</p>
              </div>
            ) : (
              <>
                {transactions.slice(0, visibleCount).map((tx) => (
                  <div
                    key={tx.id}
                    className="glass p-2 rounded-lg border border-white/5 hover:border-white/10 transition-all"
                  >
                    <div className="flex justify-between items-start mb-1">
                      <div>
                        <p className="text-[10px] font-bold text-white">
                          {tx.asset}
                        </p>
                        <p className="text-[9px] text-gray-600">
                          {tx.exchange}
                        </p>
                      </div>
                      <div className="text-right">
                        <p
                          className={`text-[10px] font-bold ${tx.profit >= 0 ? "text-green-400" : "text-red-400"}`}
                        >
                          {tx.profit >= 0 ? "+" : ""}${tx.profit.toFixed(2)}
                        </p>
                        <p className="text-[9px] text-gray-700">
                          {new Date(tx.timestamp).toLocaleTimeString([], {
                            hour: "2-digit",
                            minute: "2-digit",
                          })}
                        </p>
                      </div>
                    </div>
                    <div className="flex justify-between items-center pt-1.5 border-t border-white/5">
                      <span
                        className={`text-[8px] px-1 py-0.5 rounded uppercase font-bold tracking-wider ${
                          tx.status === "Success"
                            ? "bg-green-500/10 text-green-400"
                            : tx.status === "Partial Fill"
                              ? "bg-amber-500/10 text-amber-400"
                              : "bg-red-500/10 text-red-400"
                        }`}
                      >
                        {tx.status}
                      </span>
                      <span className="text-[9px] text-gray-600">
                        Vol: {tx.amount.toFixed(4)}
                      </span>
                    </div>
                  </div>
                ))}

                {transactions.length > visibleCount && (
                  <button
                    onClick={() => setVisibleCount((prev) => prev + 10)}
                    className="w-full py-2 mt-2 text-[10px] font-black uppercase tracking-widest text-gray-500 hover:text-white bg-white/5 hover:bg-white/10 rounded-lg transition-all border border-white/5"
                  >
                    Show More ({transactions.length - visibleCount} remaining)
                  </button>
                )}
              </>
            )}
          </div>
        </div>
      </div>

      <div className="px-3 py-1.5 border-t border-white/10 bg-black/60">
        <p className="text-[9px] text-gray-600 text-center">
          Auto-refreshing every 30s
        </p>
      </div>
    </div>
  );
};

export default BalancesPanel;
