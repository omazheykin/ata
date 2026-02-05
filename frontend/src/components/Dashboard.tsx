import React, { useState, useEffect, useMemo } from "react";
import { HubConnectionState } from "@microsoft/signalr";
import type {
  ArbitrageOpportunity,
  Balance,
  Transaction,
  StrategyUpdate,
  MarketPriceUpdate,
} from "../types/types";
import { signalRService } from "../services/signalRService";
import { apiService } from "../services/apiService";
import OpportunityCard from "./OpportunityCard";
import StatsPanel from "./StatsPanel";
// import ProfitChart from "./ProfitChart";
// import ProfitScatterChart from "./ProfitScatterChart";
import PriceComparisonChart from "./PriceComparisonChart";
import ProfitPanel from "./ProfitPanel";
import OpportunityList from "./OpportunityList";
import BalancesPanel from "./BalancesPanel";
import DepositModal from "./DepositModal";
import ConnectionLostModal from "./ConnectionLostModal";
import HeatmapWidget from "./HeatmapWidget";
import SettingsView from "./SettingsView";

interface DashboardProps {
  connectionState: HubConnectionState | null;
  activeView: "live" | "stats";
  onViewChange: (view: "live" | "stats") => void;
}

const Dashboard: React.FC<DashboardProps> = ({
  connectionState,
  activeView,
  onViewChange,
}) => {
  const [hasAttemptedInitially, setHasAttemptedInitially] = useState(false);
  const [opportunities, setOpportunities] = useState<ArbitrageOpportunity[]>(
    [],
  );
  const [newOpportunityId, setNewOpportunityId] = useState<string | null>(null);
  const [selectedOpportunity, setSelectedOpportunity] =
    useState<ArbitrageOpportunity | null>(null);
  /* Temporarily removed
  const [activeChart, setActiveChart] = useState<"trend" | "scatter">(
    "scatter",
  ); */
  const [viewMode, setViewMode] = useState<"card" | "list">("list");
  const [marketPrices, setMarketPrices] = useState<
    Record<string, MarketPriceUpdate>
  >({});
  const [lastUpdatedAsset, setLastUpdatedAsset] = useState<string | null>(null);

  // Balance state
  const [balances, setBalances] = useState<Record<string, Balance[]>>({});
  const [loadingBalances, setLoadingBalances] = useState(true);
  const [balanceError, setBalanceError] = useState<string | null>(null);
  const [isDepositModalOpen, setIsDepositModalOpen] = useState(false);
  const [selectedExchangeForDeposit, setSelectedExchangeForDeposit] =
    useState<string>("Binance");
  const [filterText, setFilterText] = useState<string>("");

  // Auto-Trade & Transactions state
  const [transactions, setTransactions] = useState<Transaction[]>([]);
  const [isAutoTradeEnabled, setIsAutoTradeEnabled] = useState(false);
  const [isSandboxMode, setIsSandboxMode] = useState(false);
  const [executionStrategy, setExecutionStrategy] =
    useState<string>("Sequential");
  const [strategyUpdate, setStrategyUpdate] = useState<StrategyUpdate | null>(
    null,
  );
  const [isSmartStrategy, setIsSmartStrategy] = useState(false);
  const [showThresholdHelp, setShowThresholdHelp] = useState(false);
  const [showFeeModeHelp, setShowFeeModeHelp] = useState(false);
  const [manualValue, setManualValue] = useState("");
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const [useTakerFees, setUseTakerFees] = useState(true);

  const fetchBalances = async () => {
    try {
      setLoadingBalances(true);
      const data = await apiService.getBalances();
      setBalances(data);
      setBalanceError(null);
    } catch (err) {
      console.error("Error fetching balances:", err);
      setBalanceError("Failed to load balances");
    } finally {
      setLoadingBalances(false);
    }
  };

  const handleDepositSuccess = async (
    amount: number,
    exchange: string,
    asset: string,
  ) => {
    console.log(`Successfully deposited ${amount} ${asset} to ${exchange}`);
    try {
      // In Sandbox mode, we simulate the deposit in the backend
      if (isSandboxMode) {
        await apiService.deposit(exchange, asset, amount);
      }
      // Refresh balances after a short delay
      setTimeout(fetchBalances, 1000);
    } catch (err) {
      console.error("Error processing simulated deposit:", err);
    }
  };

  const fetchTransactions = async () => {
    try {
      const data = await apiService.getTransactions();
      setTransactions(data);
    } catch (err) {
      console.error("Error fetching transactions:", err);
    }
  };

  const fetchAutoTradeStatus = async () => {
    try {
      const status = await apiService.getAutoTradeStatus();
      setIsAutoTradeEnabled(status.enabled);
    } catch (err) {
      console.error("Error fetching auto-trade status:", err);
    }
  };

  const handleToggleAutoTrade = async (enabled: boolean) => {
    try {
      const result = await apiService.toggleAutoTrade(enabled);
      setIsAutoTradeEnabled(result.enabled);
    } catch (err) {
      console.error("Error toggling auto-trade:", err);
    }
  };

  const fetchExecutionStrategy = async () => {
    try {
      const data = await apiService.getExecutionStrategy();
      setExecutionStrategy(data.strategy);
    } catch (err) {
      console.error("Error fetching execution strategy:", err);
    }
  };

  const handleToggleStrategy = async () => {
    const newStrategy =
      executionStrategy === "Sequential" ? "Concurrent" : "Sequential";
    try {
      const result = await apiService.setExecutionStrategy(newStrategy);
      setExecutionStrategy(result.strategy);
    } catch (err) {
      console.error("Error toggling execution strategy:", err);
    }
  };

  const fetchSandboxStatus = async () => {
    try {
      const status = await apiService.getSandboxMode();
      setIsSandboxMode(status.enabled);
    } catch (err) {
      console.error("Error fetching sandbox status:", err);
    }
  };

  const fetchFullState = async () => {
    try {
      const state = await apiService.getFullState();
      setUseTakerFees(state.useTakerFees);
    } catch (err) {
      console.error("Error fetching full state:", err);
    }
  };

  const handleUpdateManualThreshold = async () => {
    try {
      const threshold = parseFloat(manualValue.replace(",", "."));
      if (isNaN(threshold)) return;
      await apiService.setAutoTradeThreshold(threshold);
      // Fetch update to sync UI
      const manualStatus = await apiService.getStrategyStatus();
      setStrategyUpdate(manualStatus);
    } catch (err) {
      console.error("Error updating manual threshold:", err);
    }
  };

  const handleToggleSandbox = async (enabled: boolean) => {
    console.log("Toggling sandbox mode to:", enabled);
    // Optimistic update
    setIsSandboxMode(enabled);

    try {
      await apiService.toggleSandboxMode(enabled);
      console.log("Sandbox mode toggled successfully, reloading...");
    } catch (err) {
      console.error("Error toggling sandbox mode:", err);
    } finally {
      // Reload the page immediately to ensure all components and services are in sync
      window.location.reload();
    }
  };

  const handleToggleFeeMode = async (enabled: boolean) => {
    try {
      await apiService.setUseTakerFees(enabled);
      setUseTakerFees(enabled);
    } catch (err) {
      console.error("Error toggling fee mode:", err);
    }
  };

  const handleReconnect = async () => {
    try {
      await signalRService.startConnection();
    } catch (error) {
      console.error("Manual reconnection failed:", error);
    } finally {
      setHasAttemptedInitially(true);
    }
  };

  useEffect(() => {
    fetchBalances();
    fetchTransactions();
    fetchAutoTradeStatus();
    fetchSandboxStatus();
    fetchExecutionStrategy();
    fetchFullState();

    // Fetch initial smart strategy status
    apiService
      .getSmartStrategy()
      .then((status) => {
        setIsSmartStrategy(status.enabled);
      })
      .catch((err) =>
        console.error("Error fetching smart strategy status:", err),
      );

    // Fetch current threshold and reason
    apiService
      .getStrategyStatus()
      .then((status) => {
        setStrategyUpdate(status);
        setManualValue(status.threshold.toFixed(2));
      })
      .catch((err) => console.error("Error fetching strategy status:", err));

    const interval = setInterval(() => {
      fetchBalances();
      fetchTransactions();
    }, 5000);
    return () => clearInterval(interval);
  }, []);

  useEffect(() => {
    let mounted = true;

    const initializeDataAndSubscriptions = async () => {
      try {
        // Load historical data first
        const historical = await apiService.getRecentOpportunities();
        if (mounted) {
          setOpportunities(historical);
        }

        // Subscribe to new opportunities
        signalRService.onReceiveOpportunity((opportunity) => {
          if (mounted) {
            setOpportunities((prev) => {
              const updated = [...prev, opportunity];
              // Keep only last 100 opportunities in memory
              return updated.slice(-100);
            });
            setNewOpportunityId(opportunity.id);
            // Clear the "new" indicator after 2 seconds
            setTimeout(() => setNewOpportunityId(null), 2000);
          }
        });

        signalRService.onReceiveMarketPrices((update: MarketPriceUpdate) => {
          if (mounted) {
            setMarketPrices((prev) => ({ ...prev, [update.asset]: update }));

            // Only update the chart automatically if this asset has a profitable opportunity
            setOpportunities((prevOpps) => {
              const hasProfitableOpp = prevOpps.some(
                (opp) => opp.asset === update.asset && opp.profitPercentage > 0,
              );

              if (hasProfitableOpp) {
                setLastUpdatedAsset(update.asset);
              }
              return prevOpps;
            });
          }
        });

        // Subscribe to new transactions
        signalRService.onReceiveTransaction((transaction) => {
          if (mounted) {
            setTransactions((prev) => {
              const updated = [transaction, ...prev];
              return updated.slice(0, 50);
            });
            // Also refresh balances when a trade happens
            fetchBalances();
          }
        });

        // Subscribe to sandbox mode updates
        signalRService.onReceiveSandboxModeUpdate((enabled) => {
          if (mounted) {
            console.log("📢 SignalR: Sandbox mode update received:", enabled);
            setIsSandboxMode(enabled);
            setOpportunities([]);
            // Refresh balances when switching modes
            fetchBalances();
          }
        });

        // Subscribe to strategy updates
        signalRService.onReceiveStrategyUpdate((update) => {
          if (mounted) {
            setStrategyUpdate(update);
          }
        });

        signalRService.onReceiveSmartStrategyUpdate((enabled) => {
          if (mounted) {
            setIsSmartStrategy(enabled);
            if (!enabled) setStrategyUpdate(null);
          }
        });

        signalRService.onReceiveFeeModeUpdate((enabled) => {
          if (mounted) setUseTakerFees(enabled);
        });
      } catch (error) {
        console.error("Failed to initialize subscriptions:", error);
      }
    };

    initializeDataAndSubscriptions().then(() => {
      // Delay setting the 'attempted' flag slightly to allow SignalR state to settle
      // This prevents the "Disconnected" modal from popping up while still negotiating
      setTimeout(() => {
        if (mounted) setHasAttemptedInitially(true);
      }, 2000);
    });

    return () => {
      mounted = false;
    };
  }, []);

  const stats = {
    totalOpportunities: opportunities.length,
    averageProfit:
      opportunities.length > 0
        ? opportunities.reduce((sum, opp) => sum + opp.profitPercentage, 0) /
          opportunities.length
        : 0,
    bestProfit:
      opportunities.length > 0
        ? Math.max(...opportunities.map((opp) => opp.profitPercentage))
        : 0,
    totalVolume: opportunities.reduce((sum, opp) => sum + opp.volume, 0),
  };

  // Prepare data for PriceComparisonChart
  const priceChartData = useMemo(() => {
    // 1. If we have a selected opportunity, try to get its asset's live prices
    if (selectedOpportunity) {
      const liveData = marketPrices[selectedOpportunity.asset];
      if (liveData) return liveData;

      // 2. Fallback: Create partial market data from the opportunity itself
      return {
        asset: selectedOpportunity.asset,
        prices: {
          [selectedOpportunity.buyExchange]: selectedOpportunity.buyPrice,
          [selectedOpportunity.sellExchange]: selectedOpportunity.sellPrice,
        },
        timestamp: selectedOpportunity.timestamp,
      } as MarketPriceUpdate;
    }

    // 3. No selection: show most recently updated asset
    if (lastUpdatedAsset) {
      return marketPrices[lastUpdatedAsset];
    }

    return null;
  }, [selectedOpportunity, marketPrices, lastUpdatedAsset]);

  return (
    <div className="flex gap-4">
      {/* Left Panel - Balances */}
      <div className="hidden lg:flex flex-col gap-4 w-80 sticky top-24 h-[calc(100vh-8rem)]">
        <div className="flex-1 min-h-0">
          <BalancesPanel
            balances={balances}
            transactions={transactions}
            isAutoTradeEnabled={isAutoTradeEnabled}
            loading={loadingBalances}
            error={balanceError}
            onRefresh={fetchBalances}
            onOpenDeposit={(ex) => {
              setSelectedExchangeForDeposit(ex);
              setIsDepositModalOpen(true);
            }}
            onToggleAutoTrade={handleToggleAutoTrade}
            isSandboxMode={isSandboxMode}
            onToggleSandbox={handleToggleSandbox}
            executionStrategy={executionStrategy}
            onToggleStrategy={handleToggleStrategy}
            useTakerFees={useTakerFees}
            onToggleFeeMode={handleToggleFeeMode}
            onShowFeeHelp={() => setShowFeeModeHelp(true)}
          />
        </div>
      </div>

      {/* Main Content Area */}
      <div className="flex-1 min-w-0">
        {/* Price Comparison & Stats Summary */}
        <div className="mb-4 grid grid-cols-1 lg:grid-cols-2 gap-3">
          <PriceComparisonChart data={priceChartData} />
          <StatsPanel
            totalOpportunities={stats.totalOpportunities}
            averageProfit={stats.averageProfit}
            bestProfit={stats.bestProfit}
            totalVolume={stats.totalVolume}
          />
        </div>

        {/* Chart Section - Temporarily removed per user request
        {opportunities.length > 0 && (
          <div className="mb-6">
            <div className="flex justify-end mb-2 gap-2">
              <button
                onClick={() => setActiveChart("trend")}
                className={`px-3 py-1 rounded-lg text-xs font-bold transition-all ${
                  activeChart === "trend"
                    ? "bg-blue-500/20 text-blue-400 border border-blue-500/30"
                    : "bg-white/5 text-gray-400 border border-white/5 hover:bg-white/10"
                }`}
              >
                📈 Trend
              </button>
              <button
                onClick={() => setActiveChart("scatter")}
                className={`px-3 py-1 rounded-lg text-xs font-bold transition-all ${
                  activeChart === "scatter"
                    ? "bg-purple-500/20 text-purple-400 border border-purple-500/30"
                    : "bg-white/5 text-gray-400 border border-white/5 hover:bg-white/10"
                }`}
              >
                💎 Value
              </button>
            </div>
            {activeChart === "trend" ? (
              <ProfitChart
                opportunities={opportunities}
                threshold={strategyUpdate?.threshold}
              />
            ) : (
              <ProfitScatterChart opportunities={opportunities} />
            )}
          </div>
        )} */}

        {/* Opportunities Header & strategy toggle */}
        <div className="mb-4 flex justify-between items-center gap-4">
          <div className="flex items-center gap-4 flex-1 min-w-0">
            <div className="flex items-center gap-1">
              <span className="text-3xl">⚡</span>
              <span className="text-2xl font-bold text-white">Dashboard</span>
            </div>

            {/* Monitor Screen Toggle */}
            <div className="flex gap-2 bg-white/5 p-1 rounded-xl border border-white/5">
              <button
                onClick={() => onViewChange("live")}
                className={`px-4 py-1.5 rounded-lg text-xs font-bold transition-all duration-300 flex items-center gap-2 ${
                  activeView === "live"
                    ? "bg-blue-500 text-white shadow-lg shadow-blue-500/20"
                    : "text-gray-400 hover:text-white hover:bg-white/5"
                }`}
              >
                <span>⚡</span> Live
              </button>
              <button
                onClick={() => onViewChange("stats")}
                className={`px-4 py-1.5 rounded-lg text-xs font-bold transition-all duration-300 flex items-center gap-2 ${
                  activeView === "stats"
                    ? "bg-purple-500 text-white shadow-lg shadow-purple-500/20"
                    : "text-gray-400 hover:text-white hover:bg-white/5"
                }`}
              >
                <span>📊</span> Stats
              </button>
            </div>

            {/* View Switcher - MOVED BEFORE THRESHOLD */}
            <div className="flex items-center gap-2 glass rounded-xl p-1 border border-white/5">
              <button
                onClick={() => setViewMode("card")}
                className={`p-1.5 rounded-lg transition-all ${
                  viewMode === "card"
                    ? "bg-white/10 text-white"
                    : "text-gray-500 hover:text-gray-300"
                }`}
                title="Card View"
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
                    d="M4 6a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2H6a2 2 0 01-2-2V6zM14 6a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2h-2a2 2 0 01-2-2V6zM4 16a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2H6a2 2 0 01-2-2v-2zM14 16a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2h-2a2 2 0 01-2-2v-2z"
                  />
                </svg>
              </button>
              <button
                onClick={() => setViewMode("list")}
                className={`p-1.5 rounded-lg transition-all ${
                  viewMode === "list"
                    ? "bg-white/10 text-white"
                    : "text-gray-500 hover:text-gray-300"
                }`}
                title="List View"
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
                    d="M4 6h16M4 12h16M4 18h16"
                  />
                </svg>
              </button>
            </div>

            {/* Settings Button */}
            <button
              onClick={() => setIsSettingsOpen(true)}
              className="p-2.5 rounded-xl bg-white/5 border border-white/10 hover:bg-white/10 text-gray-400 hover:text-white transition-all group"
              title="Safety Settings"
            >
              <svg
                className="w-5 h-5 group-hover:rotate-45 transition-transform duration-500"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={2}
                  d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z"
                />
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={2}
                  d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"
                />
              </svg>
            </button>

            {/* Threshold Indicator - GENEROUS SPACING */}
            <div className="flex items-center gap-4 glass rounded-xl px-5 py-2.5 border border-white/10 flex-1 min-w-0 max-w-2xl">
              <div className="flex flex-col items-end min-w-[200px]">
                <span className="text-[10px] text-gray-400 uppercase font-black tracking-widest mb-1.5 opacity-80">
                  Profit Threshold
                </span>
                <div className="flex items-center gap-2">
                  {!isSmartStrategy ? (
                    <div className="flex items-center gap-2 p-1.5 bg-white/5 rounded-lg border border-white/10 focus-within:border-blue-500/50 transition-all w-full group">
                      <input
                        type="number"
                        step="0.01"
                        min="0"
                        value={manualValue}
                        onChange={(e) => setManualValue(e.target.value)}
                        className="bg-transparent px-2 py-0.5 text-sm font-bold text-blue-400 w-16 outline-none"
                      />
                      <button
                        onClick={handleUpdateManualThreshold}
                        className="px-3 py-1.5 bg-blue-500 text-white text-[10px] font-black rounded-md hover:bg-blue-600 active:scale-95 transition-all whitespace-nowrap shadow-lg shadow-blue-500/20"
                      >
                        SET
                      </button>
                    </div>
                  ) : (
                    <span
                      className={`text-sm font-bold ${isSmartStrategy ? "text-purple-400" : "text-blue-400"}`}
                    >
                      {strategyUpdate
                        ? strategyUpdate.threshold.toFixed(2)
                        : "0.10"}
                      %
                    </span>
                  )}
                  {isSmartStrategy && (
                    <div className="flex items-center gap-1">
                      <span className="text-[9px] bg-purple-500/20 text-purple-400 px-1.5 py-0.5 rounded uppercase font-bold tracking-tight">
                        Smart
                      </span>
                      <button
                        onClick={() => setShowThresholdHelp(true)}
                        className="text-purple-400/60 hover:text-purple-400 transition-colors"
                        title="How thresholds work"
                      >
                        <svg
                          className="w-3 h-3"
                          fill="currentColor"
                          viewBox="0 0 20 20"
                        >
                          <path
                            fillRule="evenodd"
                            d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-8-3a1 1 0 00-.867.5 1 1 0 11-1.731-1A3 3 0 0113 8a3.001 3.001 0 01-2 2.83V11a1 1 0 11-2 0v-1a1 1 0 011-1 1 1 0 100-2zm0 8a1 1 0 100-2 1 1 0 000 2z"
                            clipRule="evenodd"
                          />
                        </svg>
                      </button>
                    </div>
                  )}
                </div>
              </div>
              {strategyUpdate?.reason && (
                <div className="h-6 w-px bg-white/10 hidden sm:block flex-shrink-0"></div>
              )}
              {strategyUpdate?.reason && (
                <span
                  className="text-xs text-gray-500 italic hidden sm:block truncate flex-1 min-w-0"
                  title={strategyUpdate.reason}
                >
                  {strategyUpdate.reason}
                </span>
              )}
            </div>
          </div>
          <div className="flex items-center">
            <input
              type="text"
              value={filterText}
              onChange={(e) => setFilterText(e.target.value)}
              placeholder="Filter assets..."
              className="glass rounded-xl p-2 px-4 border border-white/5 text-sm"
            />
          </div>
        </div>

        {opportunities.length === 0 ? (
          <div className="glass rounded-xl p-12 text-center mb-6">
            <div className="text-6xl mb-4">🔍</div>
            <p className="text-xl text-gray-400">
              Waiting for arbitrage opportunities...
            </p>
            <p className="text-sm text-gray-500 mt-2">
              {connectionState === HubConnectionState.Connected
                ? "Connected and monitoring exchanges"
                : "Connecting to server..."}
            </p>
          </div>
        ) : (
          <div className="grid grid-cols-1 xl:grid-cols-3 gap-4 mb-4">
            <div className="xl:col-span-2 min-w-0">
              {viewMode === "list" ? (
                <OpportunityList
                  opportunities={
                    filterText?.length > 0
                      ? opportunities.filter((opportunity) =>
                          opportunity.asset
                            .toLowerCase()
                            .includes(filterText.toLowerCase()),
                        )
                      : opportunities
                  }
                  onSelect={setSelectedOpportunity}
                  selectedId={selectedOpportunity?.id}
                  threshold={strategyUpdate?.threshold ?? 0.1}
                  balances={balances}
                />
              ) : (
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  {
                    // add filtering here
                    (filterText?.length > 0
                      ? opportunities.filter((opportunity) =>
                          opportunity.asset
                            .toLowerCase()
                            .includes(filterText.toLowerCase()),
                        )
                      : opportunities
                    )
                      .slice()
                      .reverse()
                      .slice(0, 12)
                      .map((opportunity) => (
                        <OpportunityCard
                          key={opportunity.id}
                          opportunity={opportunity}
                          isNew={opportunity.id === newOpportunityId}
                          onClick={() => setSelectedOpportunity(opportunity)}
                        />
                      ))
                  }
                </div>
              )}
            </div>

            {/* Calendar Widget */}
            <div className="xl:col-span-1 h-full min-h-[400px]">
              <HeatmapWidget />
            </div>
          </div>
        )}
      </div>

      {/* Profit Calculator Panel */}
      <ProfitPanel
        opportunity={selectedOpportunity}
        balances={balances}
        onClose={() => setSelectedOpportunity(null)}
      />

      {/* Deposit Modal */}
      <DepositModal
        isOpen={isDepositModalOpen}
        onClose={() => setIsDepositModalOpen(false)}
        onSuccess={handleDepositSuccess}
        initialExchange={selectedExchangeForDeposit}
      />

      <SettingsView
        isOpen={isSettingsOpen}
        onClose={() => setIsSettingsOpen(false)}
      />

      {/* Threshold Help Modal */}
      {showThresholdHelp && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-md z-[100] flex items-center justify-center p-4 overflow-y-auto">
          <div
            className="glass max-w-2xl w-full rounded-2xl border border-white/10 shadow-2xl animate-fade-in"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="p-6 md:p-8">
              <div className="flex justify-between items-start mb-6">
                <div className="flex items-center gap-3">
                  <div className="p-3 rounded-xl bg-purple-500/20 text-purple-400">
                    <svg
                      className="w-6 h-6"
                      fill="none"
                      stroke="currentColor"
                      viewBox="0 0 24 24"
                    >
                      <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        strokeWidth={2}
                        d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
                      />
                    </svg>
                  </div>
                  <h3 className="text-2xl font-bold text-white">
                    How Thresholds Work
                  </h3>
                </div>
                <button
                  onClick={() => setShowThresholdHelp(false)}
                  className="p-2 rounded-lg hover:bg-white/5 text-gray-400 hover:text-white transition-colors"
                >
                  <svg
                    className="w-6 h-6"
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                  >
                    <path
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      strokeWidth={2}
                      d="M6 18L18 6M6 6l12 12"
                    />
                  </svg>
                </button>
              </div>

              <div className="space-y-6 text-gray-300">
                <p className="bg-blue-500/10 border-l-4 border-blue-500 p-4 rounded-r-lg italic text-blue-100/90 text-sm">
                  "When you see 'No opportunities' while a threshold is active,
                  it means the system is filtering out market data that doesn't
                  meet your profit criteria."
                </p>

                <div className="grid gap-6">
                  <section>
                    <h4 className="text-white font-bold mb-2 flex items-center gap-2">
                      <span className="text-blue-400">1.</span> The "Fee Hurdle"
                    </h4>
                    <p className="text-sm leading-relaxed">
                      The difference between the Buy and Sell price must be
                      larger than the total trading fees on both exchanges.
                    </p>
                    <div className="mt-2 text-xs bg-white/5 p-3 rounded-lg border border-white/5 text-gray-400">
                      <strong className="text-gray-300">Example:</strong> If
                      Binance fee is 0.1% and Coinbase fee is 0.1%, a raw price
                      difference of 0.15% results in a{" "}
                      <span className="text-red-400">-0.05% loss</span> and is
                      hidden.
                    </div>
                  </section>

                  <section>
                    <h4 className="text-white font-bold mb-2 flex items-center gap-2">
                      <span className="text-purple-400">2.</span> The "Threshold
                      Filter"
                    </h4>
                    <p className="text-sm leading-relaxed">
                      Even if the trade is profitable (e.g., +0.08%), it will be
                      hidden if your threshold is set higher (e.g., 0.15%).
                    </p>
                    <p className="mt-2 text-xs text-purple-300/70 italic">
                      <strong>Why?</strong> Small profits are often eaten by
                      "slippage" (prices changing while execution happens). The
                      threshold acts as a safety margin.
                    </p>
                  </section>

                  <div className="grid md:grid-cols-2 gap-4">
                    <section className="bg-white/5 p-4 rounded-xl border border-white/5">
                      <h4 className="text-white font-bold mb-2 flex items-center gap-2">
                        <span className="text-green-400">3.</span> Liquidity
                        (Depth)
                      </h4>
                      <p className="text-xs leading-relaxed text-gray-400">
                        The system checks the Order Book. If volume at the
                        profitable price is too low, the trade is ignored as it
                        wouldn't be execution-efficient.
                      </p>
                    </section>
                    <section className="bg-white/5 p-4 rounded-xl border border-white/5">
                      <h4 className="text-white font-bold mb-2 flex items-center gap-2">
                        <span className="text-yellow-400">4.</span> Market
                        Efficiency
                      </h4>
                      <p className="text-xs leading-relaxed text-gray-400">
                        Often, prices across exchanges are perfectly synced. If
                        the difference is zero or near-zero, no data is
                        displayed.
                      </p>
                    </section>
                  </div>
                </div>
              </div>

              <div className="mt-8">
                <button
                  onClick={() => setShowThresholdHelp(false)}
                  className="w-full py-3 bg-white/5 hover:bg-white/10 text-white font-bold rounded-xl border border-white/10 transition-all font-bold"
                >
                  Got it
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Fee Mode Help Modal */}
      {showFeeModeHelp && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-md z-[100] flex items-center justify-center p-4 overflow-y-auto">
          <div
            className="glass max-w-2xl w-full rounded-2xl border border-white/10 shadow-2xl animate-fade-in"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="p-6 md:p-8">
              <div className="flex justify-between items-start mb-6">
                <div className="flex items-center gap-3">
                  <div className="p-3 rounded-xl bg-green-500/20 text-green-400">
                    <svg
                      className="w-6 h-6"
                      fill="none"
                      stroke="currentColor"
                      viewBox="0 0 24 24"
                    >
                      <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        strokeWidth={2}
                        d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
                      />
                    </svg>
                  </div>
                  <h3 className="text-2xl font-bold text-white">
                    Fee Calculation Modes
                  </h3>
                </div>
                <button
                  onClick={() => setShowFeeModeHelp(false)}
                  className="p-2 rounded-lg hover:bg-white/5 text-gray-400 hover:text-white transition-colors"
                >
                  <svg
                    className="w-6 h-6"
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                  >
                    <path
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      strokeWidth={2}
                      d="M6 18L18 6M6 6l12 12"
                    />
                  </svg>
                </button>
              </div>

              <div className="space-y-6 text-gray-300">
                <p className="bg-green-500/10 border-l-4 border-green-500 p-4 rounded-r-lg italic text-green-100/90 text-sm leading-relaxed">
                  In crypto trading, you pay different fees depending on how you
                  trade.
                </p>

                <div className="grid gap-6">
                  <section className="glass p-4 rounded-xl border border-white/5">
                    <h4 className="text-white font-bold mb-2 flex items-center gap-2">
                      <span className="text-green-400">🛡️</span> Taker
                      (Pessimistic/Safe)
                    </h4>
                    <p className="text-sm leading-relaxed text-gray-400">
                      You use a{" "}
                      <strong className="text-gray-300">Market Order</strong> to
                      buy or sell immediately. These fees are higher (e.g.,
                      0.1%).
                    </p>
                    <p className="mt-2 text-xs text-gray-500 italic">
                      By defaulting to this, the bot only shows you trades that
                      are profitable even after paying these high fees. It's
                      "pessimistic" because it assumes the most expensive
                      scenario.
                    </p>
                  </section>

                  <section className="glass p-4 rounded-xl border border-white/5">
                    <h4 className="text-white font-bold mb-2 flex items-center gap-2">
                      <span className="text-orange-400">⚔️</span> Maker
                      (Optimistic/Aggressive)
                    </h4>
                    <p className="text-sm leading-relaxed text-gray-400">
                      You use a{" "}
                      <strong className="text-gray-300">Limit Order</strong> and
                      wait for the market to hit your price. These fees are much
                      lower (e.g., 0.02%).
                    </p>
                    <p className="mt-2 text-xs text-gray-500 italic">
                      This looks more profitable on paper, but you risk the
                      price moving away before your order is filled.
                    </p>
                  </section>

                  <div className="p-4 bg-blue-500/10 rounded-xl border border-blue-500/20 text-sm text-blue-100/80 leading-relaxed">
                    <strong>The Toggle</strong> allows you to tell the bot:
                    <div className="mt-2 italic">
                      "Only show me the sure things (
                      <span className="text-green-400">Pessimistic</span>)"
                    </div>
                    <div className="mt-1">or</div>
                    <div className="mt-1 italic">
                      "Show me everything that could be profitable if I execute
                      perfectly (
                      <span className="text-orange-400">Optimistic</span>)."
                    </div>
                  </div>
                </div>
              </div>

              <div className="mt-8">
                <button
                  onClick={() => setShowFeeModeHelp(false)}
                  className="w-full py-3 bg-white/5 hover:bg-white/10 text-white font-bold rounded-xl border border-white/10 transition-all uppercase tracking-widest text-xs"
                >
                  Understood
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      <ConnectionLostModal
        isOpen={
          hasAttemptedInitially &&
          (connectionState === HubConnectionState.Disconnected ||
            connectionState === HubConnectionState.Reconnecting)
        }
        connectionState={connectionState}
        onReconnect={handleReconnect}
      />
    </div>
  );
};

export default Dashboard;
