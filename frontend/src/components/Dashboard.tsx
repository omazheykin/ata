import React, { useState, useEffect } from "react";
import { HubConnectionState } from "@microsoft/signalr";
import type {
  ArbitrageOpportunity,
  Balance,
  Transaction,
} from "../types/types";
import { signalRService } from "../services/signalRService";
import { apiService } from "../services/apiService";
import ConnectionStatus from "./ConnectionStatus";
import StatsPanel from "./StatsPanel";
import OpportunityCard from "./OpportunityCard";
import ProfitChart from "./ProfitChart";
import ProfitPanel from "./ProfitPanel";
import BalancesPanel from "./BalancesPanel";
import DepositModal from "./DepositModal";
import ConnectionLostModal from "./ConnectionLostModal";

const Dashboard: React.FC = () => {
  const [opportunities, setOpportunities] = useState<ArbitrageOpportunity[]>(
    [],
  );
  const [connectionState, setConnectionState] =
    useState<HubConnectionState | null>(null);
  const [newOpportunityId, setNewOpportunityId] = useState<string | null>(null);
  const [selectedOpportunity, setSelectedOpportunity] =
    useState<ArbitrageOpportunity | null>(null);

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

  const handleReconnect = async () => {
    try {
      await signalRService.startConnection();
      setConnectionState(signalRService.getConnectionState());
    } catch (error) {
      console.error("Manual reconnection failed:", error);
    }
  };

  useEffect(() => {
    fetchBalances();
    fetchTransactions();
    fetchAutoTradeStatus();
    fetchSandboxStatus();
    fetchExecutionStrategy();
    const interval = setInterval(() => {
      fetchBalances();
      fetchTransactions();
    }, 5000);
    return () => clearInterval(interval);
  }, []);

  useEffect(() => {
    let mounted = true;

    const initializeConnection = async () => {
      try {
        // Load historical data first
        const historical = await apiService.getRecentOpportunities();
        if (mounted) {
          setOpportunities(historical);
        }

        // Start SignalR connection
        const connection = await signalRService.startConnection();
        if (mounted) {
          setConnectionState(connection.state);
        }

        // Subscribe to new opportunities
        signalRService.onReceiveOpportunity((opportunity) => {
          if (mounted) {
            setOpportunities((prev) => {
              const updated = [...prev, opportunity];
              // Keep only last 10 opportunities in memory
              return updated.slice(-10);
            });
            setNewOpportunityId(opportunity.id);
            // Clear the "new" indicator after 3 seconds
            setTimeout(() => setNewOpportunityId(null), 3000);
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

        // Update connection state periodically
        const stateInterval = setInterval(() => {
          if (mounted) {
            setConnectionState(signalRService.getConnectionState());
          }
        }, 1000);

        return () => {
          clearInterval(stateInterval);
        };
      } catch (error) {
        console.error("Failed to initialize connection:", error);
      }
    };

    initializeConnection();

    return () => {
      mounted = false;
      signalRService.stopConnection();
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

  return (
    <div className="flex min-h-screen bg-[#020617]">
      {/* Left Panel - Balances */}
      <div className="hidden lg:block sticky top-0 h-screen">
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
        />
      </div>

      {/* Main Content */}
      <div className="flex-1 p-4 overflow-y-auto">
        <div className="max-w-8xl mx-auto">
          {/* Header */}
          <div className="flex justify-between items-center mb-8">
            <div>
              <h1 className="text-4xl font-bold gradient-text mb-2">
                Arbitrage Opportunity Dashboard
              </h1>
              <p className="text-gray-400">
                Real-time cryptocurrency arbitrage monitoring
              </p>
            </div>
            <div className="flex items-center gap-4">
              <div className="flex items-center gap-2 glass rounded-xl px-3 py-1.5 border border-white/5">
                <span className="text-xs font-medium text-gray-400 uppercase tracking-wider">
                  Strategy:
                </span>
                <button
                  onClick={handleToggleStrategy}
                  className={`px-3 py-1 rounded-lg text-sm font-bold transition-all duration-300 ${
                    executionStrategy === "Sequential"
                      ? "bg-blue-500/20 text-blue-400 border border-blue-500/30 shadow-[0_0_15px_rgba(59,130,246,0.2)]"
                      : "bg-purple-500/20 text-purple-400 border border-purple-500/30 shadow-[0_0_15px_rgba(168,85,247,0.2)]"
                  }`}
                >
                  {executionStrategy}
                </button>
              </div>
              <ConnectionStatus connectionState={connectionState} />
            </div>
          </div>

          {/* Stats Panel */}
          <StatsPanel
            totalOpportunities={stats.totalOpportunities}
            averageProfit={stats.averageProfit}
            bestProfit={stats.bestProfit}
            totalVolume={stats.totalVolume}
          />

          {/* Chart */}
          {opportunities.length > 0 && (
            <div className="mb-6">
              <ProfitChart opportunities={opportunities} />
            </div>
          )}

          {/* Opportunities Grid */}
          <div className="mb-4">
            <div className="flex items-center gap-1">
              <span className="text-3xl">⚡</span>
              <span className="text-2xl font-bold text-white">
                Live Opportunities
              </span>
              <div className="flex items-center">
                <input
                  type="text"
                  value={filterText}
                  onChange={(e) => setFilterText(e.target.value)}
                  placeholder="Filter..."
                  className="glass rounded-xl p-2"
                />
              </div>
            </div>
          </div>

          {opportunities.length === 0 ? (
            <div className="glass rounded-xl p-12 text-center">
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
            <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-4 gap-4">
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

      {/* Connection Lost Modal */}
      <ConnectionLostModal
        isOpen={
          connectionState === HubConnectionState.Disconnected ||
          connectionState === HubConnectionState.Reconnecting
        }
        connectionState={connectionState}
        onReconnect={handleReconnect}
      />
    </div>
  );
};

export default Dashboard;
