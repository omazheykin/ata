import React, { useState, useEffect, useMemo } from "react";
import { HubConnectionState } from "@microsoft/signalr";
import type {
  ArbitrageOpportunity,
  Balance,
  Transaction,
  StrategyUpdate,
  MarketPriceUpdate,
  StatsResponse,
  ArbitrageEvent,
} from "../types/types";
import { signalRService } from "../services/signalRService";
import { apiService } from "../services/apiService";
import OpportunityCard from "./OpportunityCard";
import StatsPanel from "./StatsPanel";
import PriceComparisonChart from "./PriceComparisonChart";
import ProfitPanel from "./ProfitPanel";
import OpportunityList from "./OpportunityList";
import BalancesPanel from "./BalancesPanel";
import DepositModal from "./DepositModal";
import ConnectionLostModal from "./ConnectionLostModal";
import HeatmapWidget from "./HeatmapWidget";
import SettingsView from "./SettingsView";
import RebalancingPanel from "./RebalancingPanel";
import EventsModal from "./EventsModal";
import ConnectionStatus from "./ConnectionStatus";
import { PieChart, Pie, Cell, Tooltip, ResponsiveContainer } from "recharts";

interface DashboardProps {
  connectionState: HubConnectionState | null;
}

const Dashboard: React.FC<DashboardProps> = ({ connectionState }) => {
  const [hasAttemptedInitially, setHasAttemptedInitially] = useState(false);
  const [opportunities, setOpportunities] = useState<ArbitrageOpportunity[]>(
    [],
  );
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
  const [isLeftPanelExpanded, setIsLeftPanelExpanded] = useState(true);

  // Balance state
  const [balances, setBalances] = useState<Record<string, Balance[]>>({});
  const [loadingBalances, setLoadingBalances] = useState(true);
  const [balanceError, setBalanceError] = useState<string | null>(null);
  const [isDepositModalOpen, setIsDepositModalOpen] = useState(false);
  const [selectedExchangeForDeposit, setSelectedExchangeForDeposit] =
    useState<string>("Binance");

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

  // Full Stats State (merged from StatsView)
  const [detailedStats, setDetailedStats] = useState<StatsResponse | null>(
    null,
  );
  const [loadingStats, setLoadingStats] = useState(true);
  const [selectedPair, setSelectedPair] = useState<string | null>(null);
  const [pairEvents, setPairEvents] = useState<ArbitrageEvent[]>([]);
  const [isEventsModalOpen, setIsEventsModalOpen] = useState(false);
  const [loadingEvents, setLoadingEvents] = useState(false);

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

  const fetchStats = async () => {
    try {
      setLoadingStats(true);
      const data = await apiService.getDetailedStats();
      const smartStatus = await apiService.getSmartStrategy();
      setDetailedStats(data);
      setIsSmartStrategy(smartStatus.enabled);
    } catch (err) {
      console.error("Error fetching stats:", err);
    } finally {
      setLoadingStats(false);
    }
  };

  const fetchEvents = async (pair: string) => {
    try {
      setLoadingEvents(true);
      setSelectedPair(pair);
      setIsEventsModalOpen(true);
      const events = await apiService.getEventsByPair(pair);
      setPairEvents(events);
    } catch (err) {
      console.error("Error fetching events:", err);
    } finally {
      setLoadingEvents(false);
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

  const handleToggleSmartStrategy = async () => {
    try {
      const newStatus = !isSmartStrategy;
      await apiService.toggleSmartStrategy(newStatus);
      setIsSmartStrategy(newStatus);

      // If we turned it off, fetch the manual status to show the right threshold
      if (!newStatus) {
        const manualStatus = await apiService.getStrategyStatus();
        setStrategyUpdate(manualStatus);
        setManualValue(manualStatus.threshold.toFixed(2));
      }
    } catch (err) {
      console.error("Error toggling smart strategy:", err);
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
    fetchStats();

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
            // setNewOpportunityId(opportunity.id);
            // Clear the "new" indicator after 2 seconds
            // setTimeout(() => setNewOpportunityId(null), 2000);
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

  const summaryStats = {
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
    <div className="flex flex-col gap-4 animate-fade-in max-w-[1600px] mx-auto h-screen p-4 overflow-hidden">
      {/* High-Density Header: Branding + Global Controls */}
      <div className="glass rounded-2xl p-4 flex flex-wrap items-center justify-between gap-6 border border-white/10 shadow-2xl overflow-hidden flex-shrink-0">
        <div className="flex items-center gap-4">
          <div className="w-12 h-12 bg-gradient-to-br from-blue-500 to-purple-600 rounded-xl flex items-center justify-center text-3xl shadow-lg">
            🤖
          </div>
          <div>
            <h1 className="text-2xl font-black gradient-text leading-tight tracking-tighter">
              Antigravity
            </h1>
            <div className="flex items-center gap-2 leading-none">
              <div className="w-2 h-2 bg-green-500 rounded-full animate-pulse"></div>
              <span className="text-[10px] text-gray-500 uppercase tracking-widest font-black">
                Terminal • L2 High-Freq
              </span>
            </div>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-4">
          {/* Main Controls Group */}
          <div className="flex items-center gap-3 bg-white/5 px-4 py-2 rounded-xl border border-white/5 shadow-inner">
            {/* Auto-Trade */}
            <div className="flex items-center gap-2" title="Global Auto-Trade">
              <span className="text-[9px] text-gray-500 font-black uppercase tracking-tighter">
                Auto
              </span>
              <button
                onClick={() => handleToggleAutoTrade(!isAutoTradeEnabled)}
                className={`relative inline-flex h-5 w-10 items-center rounded-full transition-all duration-300 ${isAutoTradeEnabled ? "bg-primary-500 shadow-[0_0_10px_rgba(59,130,246,0.3)]" : "bg-white/10"}`}
              >
                <span
                  className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform duration-300 ${isAutoTradeEnabled ? "translate-x-5" : "translate-x-1"}`}
                />
              </button>
            </div>

            <div className="w-px h-4 bg-white/10"></div>

            {/* Sandbox */}
            <div
              className="flex items-center gap-2"
              title="Sandbox Mode (Simulated Trades)"
            >
              <span className="text-[9px] text-gray-500 font-black uppercase tracking-tighter">
                Sandbox
              </span>
              <button
                onClick={() => handleToggleSandbox(!isSandboxMode)}
                className={`relative inline-flex h-5 w-10 items-center rounded-full transition-all duration-300 ${isSandboxMode ? "bg-amber-500 shadow-[0_0_10px_rgba(245,158,11,0.3)]" : "bg-white/10"}`}
              >
                <span
                  className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform duration-300 ${isSandboxMode ? "translate-x-5" : "translate-x-1"}`}
                />
              </button>
            </div>

            <div className="w-px h-4 bg-white/10"></div>

            {/* Strategy Select */}
            <div className="flex items-center gap-2" title="Execution Strategy">
              <span className="text-[9px] text-gray-500 font-black uppercase tracking-tighter">
                {executionStrategy}
              </span>
              <button
                onClick={handleToggleStrategy}
                className={`relative inline-flex h-5 w-10 items-center rounded-full transition-all duration-300 ${executionStrategy === "Concurrent" ? "bg-purple-500 shadow-[0_0_10px_rgba(168,85,247,0.3)]" : "bg-blue-500 shadow-[0_0_10px_rgba(59,130,246,0.3)]"}`}
              >
                <span
                  className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform duration-300 ${executionStrategy === "Sequential" ? "translate-x-5" : "translate-x-1"}`}
                />
              </button>
            </div>

            <div className="w-px h-4 bg-white/10"></div>

            {/* Fee Mode */}
            <div
              className="flex items-center gap-2"
              title={`Fee Mode: ${useTakerFees ? "Taker" : "Maker"}`}
            >
              <span className="text-[9px] text-gray-500 font-black uppercase tracking-tighter">
                {useTakerFees ? "Taker" : "Maker"}
              </span>
              <button
                onClick={() => handleToggleFeeMode(!useTakerFees)}
                className={`relative inline-flex h-5 w-10 items-center rounded-full transition-all duration-300 ${useTakerFees ? "bg-green-500 shadow-[0_0_10px_rgba(34,197,94,0.3)]" : "bg-orange-500 shadow-[0_0_10px_rgba(249,115,22,0.3)]"}`}
              >
                <span
                  className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform duration-300 ${useTakerFees ? "translate-x-5" : "translate-x-1"}`}
                />
              </button>
            </div>
          </div>

          {/* Strategy Threshold Group */}
          <div className="flex items-center gap-4 bg-white/5 px-4 py-2 rounded-xl border border-white/5">
            <div className="flex items-center gap-2">
              <span className="text-[9px] text-gray-500 font-black uppercase">
                Threshold
              </span>
              <div className="flex items-center gap-1 group relative">
                <input
                  type="number"
                  step="0.01"
                  value={strategyUpdate?.threshold ?? 0.1}
                  onChange={(e) =>
                    setStrategyUpdate((prev) =>
                      prev
                        ? { ...prev, threshold: parseFloat(e.target.value) }
                        : null,
                    )
                  }
                  className="w-14 bg-black/40 border border-white/10 rounded px-2 py-0.5 text-xs font-bold text-blue-400 focus:outline-none focus:border-blue-500 transition-all font-mono"
                />
                <button
                  onClick={handleUpdateManualThreshold}
                  className="p-1 hover:text-blue-400 transition-colors text-xs"
                  title="Apply Manual"
                >
                  💾
                </button>
              </div>
            </div>

            <div className="w-px h-4 bg-white/10"></div>

            <div className="flex items-center gap-3">
              <span className="text-[9px] text-gray-500 font-black uppercase">
                Smart
              </span>
              <button
                onClick={handleToggleSmartStrategy}
                className={`relative inline-flex h-5 w-10 items-center rounded-full transition-all duration-300 ${
                  isSmartStrategy
                    ? "bg-blue-500 shadow-[0_0_10px_rgba(59,130,246,0.5)]"
                    : "bg-white/10"
                }`}
              >
                <span
                  className={`inline-block h-4 w-4 transform rounded-full bg-white shadow-md transition-transform duration-300 ${isSmartStrategy ? "translate-x-5" : "translate-x-1"}`}
                />
              </button>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <button
              onClick={() => setIsSettingsOpen(true)}
              className="p-2 rounded-lg text-gray-400 hover:text-white hover:bg-white/5 transition-all group"
              title="Settings"
            >
              <svg
                className="w-5 h-5 group-hover:rotate-45 transition-transform"
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
            <ConnectionStatus connectionState={connectionState} />
          </div>
        </div>
      </div>

      {/* Unified Terminal Grid */}
      {/* Unified Terminal Main Container */}
      <div className="flex gap-4 h-[calc(100vh-120px)] min-h-[800px]">
        {/* Left Col: Balances & Controls - Expandable */}
        <div
          className={`flex-shrink-0 transition-all duration-300 ease-in-out ${
            isLeftPanelExpanded ? "w-80" : "w-16"
          }`}
        >
          <div className="h-full glass rounded-xl border border-white/5 overflow-hidden flex flex-col relative">
            <button
              onClick={() => setIsLeftPanelExpanded(!isLeftPanelExpanded)}
              className="absolute top-2 -right-3 z-50 bg-blue-500 text-white p-1 rounded-full shadow-lg hover:scale-110 transition-transform"
              title={
                isLeftPanelExpanded ? "Collapse Sidebar" : "Expand Sidebar"
              }
            >
              <span className="text-[10px]">
                {isLeftPanelExpanded ? "◀" : "▶"}
              </span>
            </button>
            <BalancesPanel
              isExpanded={isLeftPanelExpanded}
              balances={balances}
              transactions={transactions}
              loading={loadingBalances}
              error={balanceError}
              onRefresh={fetchBalances}
              onOpenDeposit={(ex) => {
                setSelectedExchangeForDeposit(ex);
                setIsDepositModalOpen(true);
              }}
            />
          </div>
        </div>

        {/* Dynamic Center & Right Columns */}
        <div className="flex-1 flex flex-col gap-4 min-w-0">
          {/* Top Row: Standardized Height (500px) */}
          <div className="grid grid-cols-12 gap-4 h-[500px]">
            {/* Center Col: Insights & Monitor */}
            <div className="col-span-12 lg:col-span-8 flex flex-col gap-4 min-w-0">
              <div className="grid grid-cols-2 gap-4 h-[140px] flex-shrink-0">
                <div className="glass rounded-xl border border-white/5 p-3 overflow-hidden">
                  <StatsPanel
                    totalOpportunities={summaryStats.totalOpportunities}
                    averageProfit={summaryStats.averageProfit}
                    bestProfit={summaryStats.bestProfit}
                    totalVolume={summaryStats.totalVolume}
                    volatilityScore={
                      detailedStats?.summary.globalVolatilityScore
                    }
                    efficiencyScore={detailedStats?.rebalancing.efficiencyScore}
                  />
                </div>
                <div className="glass rounded-xl border border-white/5 p-3 overflow-hidden flex flex-col">
                  {priceChartData ? (
                    <PriceComparisonChart data={priceChartData} />
                  ) : (
                    detailedStats && (
                      <div className="flex-1 animate-fade-in">
                        <RebalancingPanel
                          rebalancing={detailedStats.rebalancing}
                        />
                      </div>
                    )
                  )}
                </div>
              </div>

              <div className="glass rounded-xl p-4 border border-white/5 flex flex-col gap-3 flex-1 min-h-0">
                <div className="flex items-center justify-between border-b border-white/5 pb-2">
                  <h3 className="text-md font-bold text-white flex items-center gap-2">
                    <span className="text-blue-400">⚡</span> Live Monitor
                  </h3>
                  <div className="flex bg-white/5 p-0.5 rounded-lg border border-white/5">
                    <button
                      onClick={() => setViewMode("card")}
                      className={`px-3 py-1 rounded text-[10px] font-black uppercase transition-all ${
                        viewMode === "card"
                          ? "bg-white/10 text-white"
                          : "text-gray-500"
                      }`}
                    >
                      Cards
                    </button>
                    <button
                      onClick={() => setViewMode("list")}
                      className={`px-3 py-1 rounded text-[10px] font-black uppercase transition-all ${
                        viewMode === "list"
                          ? "bg-white/10 text-white"
                          : "text-gray-500"
                      }`}
                    >
                      List
                    </button>
                  </div>
                </div>

                <div className="flex-1 overflow-y-auto overflow-x-hidden pr-1">
                  {opportunities.length === 0 ? (
                    <div className="h-full flex flex-col items-center justify-center opacity-30 text-center py-20">
                      <div className="text-4xl mb-4 animate-bounce">🛰️</div>
                      <p className="text-sm italic uppercase tracking-widest font-black">
                        Syncing Stream...
                      </p>
                    </div>
                  ) : viewMode === "list" ? (
                    <div className="scale-[0.98] origin-top">
                      <OpportunityList
                        opportunities={opportunities}
                        onSelect={setSelectedOpportunity}
                        selectedId={selectedOpportunity?.id}
                        threshold={strategyUpdate?.threshold ?? 0.1}
                        balances={balances}
                      />
                    </div>
                  ) : (
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-3 pb-4">
                      {opportunities
                        .slice()
                        .reverse()
                        .slice(0, 8)
                        .map((o) => (
                          <div
                            className="scale-[0.98] transition-transform hover:scale-100"
                            key={o.id}
                          >
                            <OpportunityCard
                              opportunity={o}
                              onClick={() => setSelectedOpportunity(o)}
                            />
                          </div>
                        ))}
                    </div>
                  )}
                </div>
              </div>
            </div>

            {/* Right Col: Asset Stats & Heatmap (Top Row) */}
            <div className="col-span-12 lg:col-span-4 flex flex-col gap-4 min-w-0">
              <div className="flex-1 glass rounded-xl p-4 border border-white/5 flex flex-col gap-3 min-h-0">
                <h3 className="text-md font-bold text-white flex items-center gap-2">
                  <span className="text-purple-400">📊</span> Pair Activity
                </h3>
                <div className="flex-1 overflow-y-auto pr-1 font-mono">
                  <table className="w-full text-[11px]">
                    <thead className="sticky top-0 bg-[#020617] z-10">
                      <tr className="text-gray-500 font-black uppercase border-b border-white/5">
                        <th className="pb-1 text-left">Pair</th>
                        <th className="pb-1 text-right">Opps</th>
                        <th className="pb-1 text-right">Spread</th>
                      </tr>
                    </thead>
                    <tbody>
                      {Object.entries(detailedStats?.summary.pairs || {}).map(
                        ([name, data]) => (
                          <tr
                            key={name}
                            onClick={() => fetchEvents(name)}
                            className="border-b border-white/5 last:border-0 hover:bg-white/5 cursor-pointer group"
                          >
                            <td className="py-1 font-bold group-hover:text-blue-400 transition-colors">
                              {name}
                            </td>
                            <td className="py-1 text-right text-gray-400">
                              {data.count}
                            </td>
                            <td className="py-1 text-right text-green-400 font-black">
                              {(data.avgSpread * 100).toFixed(2)}%
                            </td>
                          </tr>
                        ),
                      )}
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          </div>

          {/* Bottom Row: Standardized Height (400px) */}
          <div className="grid grid-cols-12 gap-4 h-[350px]">
            <div className="col-span-12 lg:col-span-8 glass rounded-xl p-4 border border-white/5 flex flex-col gap-3 min-h-0">
              <HeatmapWidget
                externalStats={detailedStats}
                externalLoading={loadingStats}
              />
            </div>

            <div className="col-span-12 lg:col-span-4 glass rounded-xl p-4 border border-white/5 flex flex-col gap-3 min-h-0">
              <h3 className="text-md font-bold text-white flex items-center gap-2">
                <span className="text-orange-400">🔁</span> Flow Distribution
              </h3>
              <div className="flex-1 min-h-0">
                <ResponsiveContainer width="100%" height="100%">
                  <PieChart>
                    <Pie
                      data={Object.entries(
                        detailedStats?.summary.directionDistribution || {},
                      ).map(([name, value]) => ({
                        name: name.includes("B→C") ? "Bin→Cb" : "Cb→Bin",
                        value,
                      }))}
                      innerRadius={40}
                      outerRadius={60}
                      paddingAngle={5}
                      dataKey="value"
                    >
                      <Cell fill="#3bb2f6" />
                      <Cell fill="#a855f7" />
                    </Pie>
                    <Tooltip
                      contentStyle={{
                        backgroundColor: "#0f172a",
                        border: "none",
                        borderRadius: "8px",
                        fontSize: "10px",
                      }}
                    />
                  </PieChart>
                </ResponsiveContainer>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Modals & Overlays */}
      {selectedOpportunity && (
        <ProfitPanel
          opportunity={selectedOpportunity}
          balances={balances}
          onClose={() => setSelectedOpportunity(null)}
        />
      )}
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
      {isEventsModalOpen && (
        <EventsModal
          pair={selectedPair || ""}
          events={pairEvents}
          isOpen={isEventsModalOpen}
          onClose={() => setIsEventsModalOpen(false)}
          loading={loadingEvents}
        />
      )}

      {showThresholdHelp && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-md z-[100] flex items-center justify-center p-4">
          <div className="glass max-w-lg w-full rounded-2xl border border-white/10 shadow-2xl p-8">
            <div className="flex items-center gap-3 mb-6">
              <div className="p-3 rounded-xl bg-purple-500/20 text-purple-400">
                🛡️
              </div>
              <h3 className="text-2xl font-bold text-white">Strategy Help</h3>
            </div>
            <div className="space-y-4 text-sm text-gray-400">
              <p>
                <strong className="text-white">Smart Mode:</strong> Dynamic
                threshold adjustment.
              </p>
              <p>
                <strong className="text-white">Manual:</strong> Fixed minimum
                profit requirement.
              </p>
            </div>
            <button
              onClick={() => setShowThresholdHelp(false)}
              className="mt-8 w-full py-3 bg-blue-500 text-white font-bold rounded-xl shadow-lg"
            >
              Understood
            </button>
          </div>
        </div>
      )}

      {showFeeModeHelp && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-md z-[100] flex items-center justify-center p-4">
          <div className="glass max-w-lg w-full rounded-2xl border border-white/10 shadow-2xl p-8">
            <div className="flex items-center gap-3 mb-6">
              <div className="p-3 rounded-xl bg-blue-500/20 text-blue-400">
                🛡️
              </div>
              <h3 className="text-2xl font-bold text-white">Fee Safety</h3>
            </div>
            <div className="space-y-4 text-sm text-gray-400">
              <p>
                <strong className="text-white">Taker:</strong> Safety first.
              </p>
              <p>
                <strong className="text-white">Maker:</strong> Max profit, high
                risk.
              </p>
            </div>
            <button
              onClick={() => setShowFeeModeHelp(false)}
              className="mt-8 w-full py-3 bg-blue-500 text-white font-bold rounded-xl shadow-lg"
            >
              Understood
            </button>
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
