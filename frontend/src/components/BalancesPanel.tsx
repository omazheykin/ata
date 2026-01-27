import React from "react";
import type { Balance, Transaction } from "../types/types";

interface BalancesPanelProps {
  balances: Record<string, Balance[]>;
  transactions: Transaction[];
  isAutoTradeEnabled: boolean;
  loading: boolean;
  error: string | null;
  onRefresh: () => void;
  onOpenDeposit: (exchange: string) => void;
  onToggleAutoTrade: (enabled: boolean) => void;
  isSandboxMode: boolean;
  onToggleSandbox: (enabled: boolean) => void;
}

const BalancesPanel: React.FC<BalancesPanelProps> = ({
  balances,
  transactions,
  isAutoTradeEnabled,
  loading,
  error,
  onRefresh,
  onOpenDeposit,
  onToggleAutoTrade,
  isSandboxMode,
  onToggleSandbox,
}) => {
  const totalProfit = transactions.reduce((sum, tx) => sum + tx.profit, 0);

  return (
    <div className="flex flex-col h-full bg-[#0f172a]/50 backdrop-blur-xl border-r border-white/10 w-80">
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

        <div className="grid grid-cols-2 gap-2">
          {/* Auto-Trade Toggle */}
          <div className="glass p-2 rounded-xl border border-white/5 flex items-center justify-between">
            <div className="flex items-center gap-2">
              <div
                className={`w-7 h-7 rounded-lg flex items-center justify-center transition-colors ${isAutoTradeEnabled ? "bg-primary-500/20 text-primary-400" : "bg-white/5 text-gray-500"}`}
              >
                <svg
                  className="w-4 h-4"
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth={2}
                    d="M13 10V3L4 14h7v7l9-11h-7z"
                  />
                </svg>
              </div>
              <div>
                <p className="text-[10px] font-bold text-white">Auto-Trade</p>
              </div>
            </div>
            <button
              onClick={() => onToggleAutoTrade(!isAutoTradeEnabled)}
              className={`relative inline-flex h-4 w-8 items-center rounded-full transition-colors focus:outline-none ${isAutoTradeEnabled ? "bg-primary-500" : "bg-white/10"}`}
            >
              <span
                className={`inline-block h-3 w-3 transform rounded-full bg-white transition-transform ${isAutoTradeEnabled ? "translate-x-4" : "translate-x-1"}`}
              />
            </button>
          </div>

          {/* Sandbox Mode Toggle */}
          <div className="glass p-2 rounded-xl border border-amber-500/20 flex items-center justify-between">
            <div className="flex items-center gap-2">
              <div
                className={`w-7 h-7 rounded-lg flex items-center justify-center transition-colors ${isSandboxMode ? "bg-amber-500/20 text-amber-400" : "bg-white/5 text-gray-500"}`}
              >
                <svg
                  className="w-4 h-4"
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth={2}
                    d="M19.428 15.428a2 2 0 00-1.022-.547l-2.387-.477a6 6 0 00-3.86.517l-.318.158a6 6 0 01-3.86.517L6.05 15.21a2 2 0 00-1.806.547M8 4h8l-1 1v5.172a2 2 0 00.586 1.414l5 5c1.26 1.26.367 3.414-1.415 3.414H4.828c-1.782 0-2.674-2.154-1.414-3.414l5-5A2 2 0 009 10.172V5L8 4z"
                  />
                </svg>
              </div>
              <div>
                <p className="text-[10px] font-bold text-white">Sandbox</p>
              </div>
            </div>
            <button
              onClick={() => onToggleSandbox(!isSandboxMode)}
              className={`relative inline-flex h-4 w-8 items-center rounded-full transition-colors focus:outline-none ${isSandboxMode ? "bg-amber-600" : "bg-white/10"}`}
            >
              <span
                className={`inline-block h-3 w-3 transform rounded-full bg-white transition-transform ${isSandboxMode ? "translate-x-4" : "translate-x-1"}`}
              />
            </button>
          </div>
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

              <div className="grid grid-cols-1 gap-1.5">
                {assetBalances.length === 0 ? (
                  <p className="text-[10px] text-gray-600 italic px-1">
                    No balances
                  </p>
                ) : (
                  assetBalances.map((balance) => (
                    <div
                      key={balance.asset}
                      className="glass px-2.5 py-1.5 rounded-lg border border-white/5 hover:border-white/10 transition-all group"
                    >
                      <div className="flex justify-between items-center">
                        <span className="text-xs font-bold text-white group-hover:text-primary-400 transition-colors">
                          {balance.asset}
                        </span>
                        <span className="text-[11px] font-mono text-gray-300">
                          {balance.total.toLocaleString(undefined, {
                            maximumFractionDigits: 4,
                          })}
                        </span>
                      </div>
                      <div className="flex justify-between text-[9px] mt-0.5">
                        <span className="text-gray-600">
                          Avail:{" "}
                          {balance.free.toLocaleString(undefined, {
                            maximumFractionDigits: 2,
                          })}
                        </span>
                        {balance.locked > 0 && (
                          <span className="text-orange-400/40">
                            Lock:{" "}
                            {balance.locked.toLocaleString(undefined, {
                              maximumFractionDigits: 2,
                            })}
                          </span>
                        )}
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
              transactions.map((tx) => (
                <div
                  key={tx.id}
                  className="glass p-2 rounded-lg border border-white/5 hover:border-white/10 transition-all"
                >
                  <div className="flex justify-between items-start mb-1">
                    <div>
                      <p className="text-[10px] font-bold text-white">
                        {tx.asset}
                      </p>
                      <p className="text-[9px] text-gray-600">{tx.exchange}</p>
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
              ))
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
