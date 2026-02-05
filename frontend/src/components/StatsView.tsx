import React, { useEffect, useState } from "react";
import {
  Tooltip,
  ResponsiveContainer,
  Cell,
  PieChart,
  Pie,
  Legend,
} from "recharts";
import { apiService } from "../services/apiService";
import type {
  StatsResponse,
  StrategyUpdate,
  ArbitrageEvent,
  HeatmapCell,
} from "../types/types";
import * as signalR from "@microsoft/signalr";
import EventsModal from "./EventsModal";
import HeatmapCellModal from "./HeatmapCellModal";

interface StatsViewProps {
  activeView: "live" | "stats";
  onViewChange: (view: "live" | "stats") => void;
}

const StatsView: React.FC<StatsViewProps> = ({ activeView, onViewChange }) => {
  const [stats, setStats] = useState<StatsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isSmartStrategy, setIsSmartStrategy] = useState(false);
  const [strategyUpdate, setStrategyUpdate] = useState<StrategyUpdate | null>(
    null,
  );
  const [manualValue, setManualValue] = useState("");
  const [showHeatmapHelp, setShowHeatmapHelp] = useState(false);
  const [selectedPair, setSelectedPair] = useState<string | null>(null);
  const [pairEvents, setPairEvents] = useState<ArbitrageEvent[]>([]);
  const [isEventsModalOpen, setIsEventsModalOpen] = useState(false);
  const [loadingEvents, setLoadingEvents] = useState(false);
  const [isHeatmapModalOpen, setIsHeatmapModalOpen] = useState(false);
  const [selectedHeatmapCell, setSelectedHeatmapCell] = useState<{
    day: string;
    hour: number;
    events: ArbitrageEvent[];
    summary?: HeatmapCell;
  } | null>(null);

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

  const fetchStats = async () => {
    try {
      setLoading(true);
      const data = await apiService.getDetailedStats();
      const smartStatus = await apiService.getSmartStrategy();
      setStats(data);
      setIsSmartStrategy(smartStatus.enabled);
      setError(null);
    } catch (err) {
      console.error("Error fetching stats:", err);
      setError("Failed to load detailed statistics");
    } finally {
      setLoading(false);
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

  const handleUpdateManualThreshold = async () => {
    try {
      const threshold = parseFloat(manualValue);
      if (isNaN(threshold)) return;
      await apiService.setAutoTradeThreshold(threshold);
      // Fetch update to sync UI
      const manualStatus = await apiService.getStrategyStatus();
      setStrategyUpdate(manualStatus);
    } catch (err) {
      console.error("Error updating manual threshold:", err);
    }
  };

  const handleHeatmapCellClick = async (day: string, hour: number) => {
    try {
      // Use the new backend-driven endpoint
      const details = await apiService.getCellDetails(day, hour);

      setSelectedHeatmapCell({
        day,
        hour,
        events: details.events,
        summary: details.summary,
      });
      setIsHeatmapModalOpen(true);
    } catch (err) {
      console.error("Error fetching heatmap cell details:", err);
    }
  };

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl("http://localhost:5000/arbitrageHub")
      .withAutomaticReconnect()
      .build();

    let isMounted = true;

    const startConnection = async () => {
      try {
        if (connection.state === signalR.HubConnectionState.Disconnected) {
          await connection.start();
          if (isMounted) {
            console.log("SignalR connected to Statistics Hub");
          }
        }
      } catch (err) {
        if (isMounted) {
          console.error("SignalR Connection Error: ", err);
        }
      }
    };

    startConnection();

    connection.on(
      "ReceiveStrategyUpdate",
      (update: { threshold: number; reason: string }) => {
        if (isMounted) setStrategyUpdate(update);
      },
    );

    connection.on("ReceiveSmartStrategyUpdate", (enabled: boolean) => {
      if (isMounted) setIsSmartStrategy(enabled);
    });

    return () => {
      isMounted = false;
      // Only stop if connected or connecting
      if (connection.state !== signalR.HubConnectionState.Disconnected) {
        connection
          .stop()
          .catch((err) =>
            console.error("Error stopping SignalR connection:", err),
          );
      }
    };
  }, []);

  useEffect(() => {
    fetchStats();
    // Initialize manual value if status is fetched
    apiService.getStrategyStatus().then((status) => {
      setManualValue(status.threshold.toFixed(2));
      setStrategyUpdate(status);
    });
  }, []);

  if (loading && !stats)
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-500"></div>
      </div>
    );

  if (error || !stats)
    return (
      <div className="glass rounded-xl p-8 text-center text-red-400">
        <p className="text-xl">{error || "No statistics available"}</p>
        <button
          onClick={fetchStats}
          className="mt-4 px-4 py-2 bg-blue-500/20 text-blue-400 border border-blue-500/30 rounded-lg hover:bg-blue-500/30"
        >
          Retry
        </button>
      </div>
    );

  const { summary, calendar, rebalancing } = stats;

  const pairData = Object.entries(summary.pairs).map(([name, data]) => ({
    name,
    count: data.count,
    avgSpread: (data.avgSpread * 100).toFixed(2),
    maxSpread: (data.maxSpread * 100).toFixed(2),
  }));

  const directionData = Object.entries(summary.directionDistribution).map(
    ([name, value]) => ({
      name:
        name.includes("B→C") || name.startsWith("b")
          ? "Binance → Coinbase"
          : "Coinbase → Binance",
      value,
    }),
  );

  const COLORS = ["#3bb2f6", "#a855f7", "#10b981", "#f59e0b"];

  const days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
  const hours = Array.from({ length: 24 }, (_, i) =>
    i.toString().padStart(2, "0"),
  );

  const getVolatilityColor = (score: number) => {
    if (score > 0.7) return "bg-red-500/40 text-red-200";
    if (score > 0.4) return "bg-yellow-500/40 text-yellow-200";
    return "bg-green-500/40 text-green-200";
  };

  return (
    <div className="space-y-6 animate-fade-in">
      {/* Strategy Control & Rebalancing Row */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Navigation Control */}
        <div className="glass rounded-xl p-6 border border-white/5 flex flex-col justify-between">
          <div className="flex items-center gap-4">
            <div className="p-4 rounded-full bg-blue-500/20 text-blue-400 shadow-lg">
              🖥️
            </div>
            <div>
              <h2 className="text-xl font-bold text-white">Monitor Screen</h2>
              <p className="text-gray-400 text-sm">Switch between views</p>
            </div>
          </div>
          <div className="flex gap-2 bg-white/5 p-1 rounded-xl border border-white/5 mt-4">
            <button
              onClick={() => onViewChange("live")}
              className={`flex-1 py-3 rounded-lg text-sm font-bold transition-all duration-300 flex items-center justify-center gap-2 ${
                activeView === "live"
                  ? "bg-blue-500 text-white shadow-lg shadow-blue-500/20"
                  : "text-gray-400 hover:text-white hover:bg-white/5"
              }`}
            >
              <span>⚡</span> Live Dashboard
            </button>
            <button
              onClick={() => onViewChange("stats")}
              className={`flex-1 py-3 rounded-lg text-sm font-bold transition-all duration-300 flex items-center justify-center gap-2 ${
                activeView === "stats"
                  ? "bg-purple-500 text-white shadow-lg shadow-purple-500/20"
                  : "text-gray-400 hover:text-white hover:bg-white/5"
              }`}
            >
              <span>📊</span> Statistics
            </button>
          </div>
        </div>

        {/* Strategy Header */}
        <div className="glass rounded-xl p-6 border border-white/5 flex flex-col gap-6">
          <div className="flex items-center gap-4">
            <div
              className={`p-4 rounded-full ${isSmartStrategy ? "bg-purple-500/20 text-purple-400" : "bg-gray-500/20 text-gray-400"} shadow-lg`}
            >
              {isSmartStrategy ? "🧠" : "⚙️"}
            </div>
            <div>
              <h2 className="text-xl font-bold text-white flex items-center gap-2">
                Smart Strategy Control
                {isSmartStrategy && (
                  <span className="text-[10px] bg-green-500/20 text-green-400 px-2 py-0.5 rounded-full uppercase tracking-wider animate-pulse">
                    Live
                  </span>
                )}
              </h2>
              <p className="text-gray-400 text-sm">
                {isSmartStrategy
                  ? "Dynamically adjusting thresholds based on historical volatility."
                  : "Using static thresholds. Enable smart strategy for optimized performance."}
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-4">
            <button
              onClick={() => {
                console.log("Refresh clicked");
                fetchStats();
              }}
              disabled={loading}
              className={`p-2.5 glass rounded-xl transition-all border border-white/10 active:scale-90 group flex items-center gap-2 ${
                loading
                  ? "opacity-50 cursor-wait"
                  : "hover:text-white hover:bg-white/10 hover:border-white/20"
              } text-gray-400`}
              title="Refresh statistics"
            >
              <span
                className={`text-lg leading-none inline-block ${loading ? "animate-spin" : "group-hover:rotate-180 transition-transform duration-700 ease-out"}`}
              >
                🔄
              </span>
              <span className="text-[10px] uppercase font-black tracking-widest hidden sm:inline">
                Refresh
              </span>
            </button>

            {strategyUpdate && isSmartStrategy && (
              <div className="text-right hidden lg:block">
                <p className="text-xs text-gray-500 uppercase font-bold tracking-widest mb-1">
                  Current Focus
                </p>
                <div className="flex items-center justify-end gap-2">
                  <span className="text-sm font-bold text-purple-400">
                    {strategyUpdate.threshold.toFixed(2)}% Min Profit
                  </span>
                  <span
                    className="text-xs text-gray-400 border-l border-white/10 pl-2 max-w-[200px] truncate"
                    title={strategyUpdate.reason}
                  >
                    {strategyUpdate.reason}
                  </span>
                </div>
              </div>
            )}

            {!isSmartStrategy && (
              <div className="flex flex-col items-end gap-1">
                <p className="text-[10px] text-gray-500 uppercase font-bold tracking-widest">
                  Manual Threshold (%)
                </p>
                <div className="flex items-center gap-1.5 p-1 bg-white/5 rounded-lg border border-white/10 focus-within:border-blue-500/50 transition-colors">
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
                    className="px-2.5 py-1 bg-blue-500 text-white text-[10px] font-bold rounded-md hover:bg-blue-600 transition-colors shadow-lg shadow-blue-500/20"
                  >
                    SET
                  </button>
                </div>
              </div>
            )}

            <button
              onClick={handleToggleSmartStrategy}
              className={`px-6 py-2.5 rounded-xl font-bold transition-all duration-300 flex items-center gap-2 shadow-lg ${
                isSmartStrategy
                  ? "bg-purple-500 text-white hover:bg-purple-600 ring-4 ring-purple-500/20"
                  : "bg-white/5 text-gray-400 hover:bg-white/10 border border-white/10"
              }`}
            >
              {isSmartStrategy ? "Disable Smart Mode" : "Enable Smart Mode"}
            </button>
          </div>
        </div>

        {/* Rebalancing Recommendation */}
        <div
          className={`rounded-xl p-6 border transition-all ${
            rebalancing.recommendation.includes("balanced")
              ? "bg-blue-500/10 border-blue-500/30"
              : "bg-yellow-500/10 border-yellow-500/30 animate-pulse-glow"
          }`}
        >
          <div className="flex items-center gap-4">
            <div className="text-3xl">⚖️</div>
            <div>
              <h4 className="text-lg font-bold text-white mb-1">
                Rebalancing Recommendation
              </h4>
              <p className="text-gray-200">{rebalancing.recommendation}</p>
            </div>
          </div>
          <div className="mt-4 grid grid-cols-2 md:grid-cols-4 lg:grid-cols-6 gap-4">
            {Object.entries(rebalancing.assetSkews).map(([asset, skew]) => (
              <div
                key={asset}
                className="glass rounded-lg p-2 text-center border border-white/5"
              >
                <p className="text-xs text-gray-400 uppercase">{asset}</p>
                <div className="h-1.5 w-full bg-white/10 rounded-full my-2 overflow-hidden flex">
                  <div
                    className={`h-full ${skew < 0 ? "bg-blue-500" : "bg-purple-500"}`}
                    style={{
                      width: `${Math.abs(skew) * 100}%`,
                      marginLeft: skew < 0 ? "auto" : "0",
                    }}
                  />
                </div>
                <p
                  className={`text-xs font-bold ${Math.abs(skew) > 0.3 ? "text-yellow-400" : "text-gray-400"}`}
                >
                  {skew > 0 ? "↑ Binance" : "↓ Coinbase"}
                </p>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Summary Row */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <div className="glass rounded-xl p-5 border border-white/5">
          <p className="text-gray-400 text-sm mb-1">Global Volatility Score</p>
          <div className="flex items-end gap-2">
            <span className="text-3xl font-bold gradient-text">
              {(summary.globalVolatilityScore * 100).toFixed(1)}%
            </span>
            <span className="text-xs text-gray-500 mb-1">Activity Density</span>
          </div>
        </div>
        <div className="glass rounded-xl p-5 border border-white/5">
          <p className="text-gray-400 text-sm mb-1">Avg Series Duration</p>
          <div className="flex items-end gap-2">
            <span className="text-3xl font-bold gradient-text">
              {summary.avgSeriesDuration.toFixed(1)}
            </span>
            <span className="text-xs text-gray-500 mb-1">
              Events / Direction Change
            </span>
          </div>
        </div>
        <div className="glass rounded-xl p-5 border border-white/5">
          <p className="text-gray-400 text-sm mb-1">System Efficiency</p>
          <div className="flex items-end gap-2">
            <span className="text-3xl font-bold gradient-text">
              {(rebalancing.efficiencyScore * 100).toFixed(1)}%
            </span>
            <span className="text-xs text-gray-500 mb-1">
              Rebalancing Score
            </span>
          </div>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Pair Statistics */}
        <div className="glass rounded-xl p-6 border border-white/5">
          <h3 className="text-xl font-bold text-white mb-4 flex items-center gap-2">
            <span>📊</span> Asset Pairs Activity
          </h3>
          <div className="overflow-x-auto">
            <table className="w-full text-left">
              <thead>
                <tr className="text-gray-400 text-sm border-b border-white/5">
                  <th className="pb-2">Pair</th>
                  <th className="pb-2 text-right">Events</th>
                  <th className="pb-2 text-right">Avg Spread</th>
                  <th className="pb-2 text-right">Max Spread</th>
                </tr>
              </thead>
              <tbody className="text-gray-200">
                {pairData.map((row) => (
                  <tr
                    key={row.name}
                    onClick={() => fetchEvents(row.name)}
                    className="border-b border-white/5 last:border-0 hover:bg-white/5 transition-colors cursor-pointer group"
                  >
                    <td className="py-1.5 text-sm font-semibold group-hover:text-blue-400 transition-colors">
                      {row.name}
                    </td>
                    <td className="py-1.5 text-sm text-right">{row.count}</td>
                    <td className="py-1.5 text-sm text-right text-green-400">
                      {row.avgSpread}%
                    </td>
                    <td className="py-1.5 text-right text-sm text-blue-400 font-bold">
                      {row.maxSpread}%
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        {/* Direction Distribution */}
        <div className="glass rounded-xl p-6 border border-white/5">
          <h3 className="text-xl font-bold text-white mb-4 flex items-center gap-2">
            <span>🔁</span> Direction Distribution
          </h3>
          <div className="h-[300px]">
            <ResponsiveContainer width="100%" height="100%">
              <PieChart>
                <Pie
                  data={directionData}
                  cx="50%"
                  cy="50%"
                  innerRadius={60}
                  outerRadius={100}
                  paddingAngle={5}
                  dataKey="value"
                >
                  {directionData.map((_, index) => (
                    <Cell
                      key={`cell-${index}`}
                      fill={COLORS[index % COLORS.length]}
                    />
                  ))}
                </Pie>
                <Tooltip
                  contentStyle={{
                    background: "#1e293b",
                    border: "1px solid rgba(255,255,255,0.1)",
                    borderRadius: "8px",
                  }}
                  itemStyle={{ color: "#fff" }}
                />
                <Legend verticalAlign="bottom" height={36} />
              </PieChart>
            </ResponsiveContainer>
          </div>
        </div>
      </div>

      {/* Activity Heatmap */}
      <div className="glass rounded-xl p-6 border border-white/5">
        <h3 className="text-xl font-bold text-white mb-6 flex items-center gap-2">
          <span>🔥</span> Activity Heatmap (Volatility & Spread)
          <button
            onClick={() => setShowHeatmapHelp(true)}
            className="text-gray-400/60 hover:text-white transition-colors p-1"
            title="What is this?"
          >
            <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
              <path
                fillRule="evenodd"
                d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-8-3a1 1 0 00-.867.5 1 1 0 11-1.731-1A3 3 0 0113 8a3.001 3.001 0 01-2 2.83V11a1 1 0 11-2 0v-1a1 1 0 011-1 1 1 0 100-2zm0 8a1 1 0 100-2 1 1 0 000 2z"
                clipRule="evenodd"
              />
            </svg>
          </button>
        </h3>
        <div className="overflow-x-auto">
          <div className="min-w-[800px]">
            {/* Header Row */}
            <div className="flex">
              <div className="w-12"></div>
              {hours.map((h) => (
                <div
                  key={h}
                  className="flex-1 text-center text-[10px] text-gray-500 py-1"
                >
                  {h}
                </div>
              ))}
            </div>
            {/* Rows */}
            {days.map((day) => (
              <div key={day} className="flex group">
                <div className="w-12 text-xs text-gray-400 flex items-center">
                  {day}
                </div>
                {hours.map((h) => {
                  const detail = calendar[day]?.[h];
                  const now = new Date();
                  const isCurrentDay =
                    ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"][
                      now.getDay()
                    ] === day;
                  const isCurrentHour =
                    now.getHours().toString().padStart(2, "0") === h;
                  const isNow = isCurrentDay && isCurrentHour;

                  return (
                    <div
                      key={h}
                      onClick={() => handleHeatmapCellClick(day, parseInt(h))}
                      className={`flex-1 h-6 m-[1px] rounded-sm transition-all cursor-pointer flex items-center justify-center text-[8px] font-bold ${
                        detail
                          ? getVolatilityColor(detail.volatilityScore)
                          : "bg-white/5 text-gray-700"
                      } ${
                        isNow
                          ? "ring-2 ring-white ring-offset-2 ring-offset-[#0f172a] z-10 scale-110 shadow-lg shadow-white/20"
                          : "hover:scale-110"
                      }`}
                      title={
                        detail
                          ? `${day} ${h}:00\nAvg Spread: ${(
                              detail.avgSpread * 100
                            ).toFixed(3)}%\nMax Spread: ${(
                              detail.maxSpread * 100
                            ).toFixed(
                              3,
                            )}%\nFrequency: ${detail.avgOpportunitiesPerHour.toFixed(1)} opps/hr\nVolatility: ${(
                              detail.volatilityScore * 100
                            ).toFixed(0)}%\nBias: ${
                              detail.directionBias
                            }\nClick to view ${detail.count} events${isNow ? " (CURRENT)" : ""}`
                          : `No data${isNow ? " (CURRENT)" : ""}`
                      }
                    >
                      {detail && detail.avgOpportunitiesPerHour > 0 && (
                        <span>{detail.avgOpportunitiesPerHour.toFixed(0)}</span>
                      )}
                    </div>
                  );
                })}
              </div>
            ))}
          </div>
        </div>
        <div className="mt-4 flex gap-6 text-xs justify-center">
          <div className="flex items-center gap-2">
            <div className="w-3 h-3 bg-white/5 border border-white/10 rounded-sm"></div>
            <span className="text-gray-400 font-medium">Inactive</span>
          </div>
          <div className="flex items-center gap-2">
            <div className="w-3 h-3 bg-green-500/40 border border-green-500/30 rounded-sm"></div>
            <span className="text-gray-400 font-medium">Low Activity</span>
          </div>
          <div className="flex items-center gap-2">
            <div className="w-3 h-3 bg-yellow-500/40 border border-yellow-500/30 rounded-sm"></div>
            <span className="text-gray-400 font-medium">Moderate Activity</span>
          </div>
          <div className="flex items-center gap-2">
            <div className="w-3 h-3 bg-red-500/40 border border-red-500/30 rounded-sm"></div>
            <span className="text-gray-400 font-medium">High Activity</span>
          </div>
          <div className="flex items-center gap-2 ml-4 border-l border-white/10 pl-6">
            <div className="w-3 h-3 ring-2 ring-white ring-offset-2 ring-offset-[#0f172a] rounded-sm bg-white/20"></div>
            <span className="text-gray-400 font-medium font-bold text-white">
              Current
            </span>
          </div>
        </div>
      </div>

      {/* Heatmap Help Modal */}
      {showHeatmapHelp && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center p-4">
          <div
            className="absolute inset-0 bg-black/80 backdrop-blur-sm"
            onClick={() => setShowHeatmapHelp(false)}
          />
          <div className="glass rounded-2xl p-8 border border-white/10 max-w-xl w-full relative z-10 animate-fade-in">
            <div className="flex justify-between items-center mb-6">
              <h3 className="text-2xl font-bold text-white flex items-center gap-3">
                <span>📊</span> Understanding the Heatmap
              </h3>
              <button
                onClick={() => setShowHeatmapHelp(false)}
                className="text-gray-400 hover:text-white transition-colors"
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
              <section>
                <h4 className="text-blue-400 font-bold mb-2 flex items-center gap-2">
                  <span className="w-2 h-2 rounded-full bg-blue-400"></span>
                  Volatility Score
                </h4>
                <p className="text-sm leading-relaxed">
                  Measures the frequency of price movements and spread
                  opportunities. A high volatility score means prices are moving
                  fast, which often creates profitable arbitrage windows between
                  exchanges.
                </p>
              </section>

              <section>
                <h4 className="text-purple-400 font-bold mb-2 flex items-center gap-2">
                  <span className="w-2 h-2 rounded-full bg-purple-400"></span>
                  Spread Intensity
                </h4>
                <p className="text-sm leading-relaxed">
                  Shows how wide the average profit margin was during those
                  price movements. Red zones indicate "peak activity" where both
                  high volatility and wide spreads occur simultaneously.
                </p>
              </section>

              <div className="bg-white/5 rounded-xl p-4 border border-white/5">
                <p className="text-xs italic text-gray-400">
                  Tip: These scores help the "Smart Strategy" determine whether
                  to lower the profit threshold (during high activity) or raise
                  it (during quiet periods) to maximize trade reliability.
                </p>
              </div>
            </div>

            <button
              onClick={() => setShowHeatmapHelp(false)}
              className="mt-8 w-full py-3 bg-blue-500 text-white font-bold rounded-xl hover:bg-blue-600 transition-all active:scale-95"
            >
              Got it
            </button>
          </div>
        </div>
      )}

      {/* Events Modal */}
      <EventsModal
        pair={selectedPair || ""}
        events={pairEvents}
        isOpen={isEventsModalOpen}
        onClose={() => setIsEventsModalOpen(false)}
        loading={loadingEvents}
      />

      {/* Heatmap Cell Modal */}
      {selectedHeatmapCell && (
        <HeatmapCellModal
          isOpen={isHeatmapModalOpen}
          onClose={() => setIsHeatmapModalOpen(false)}
          day={selectedHeatmapCell?.day || ""}
          hour={selectedHeatmapCell?.hour || 0}
          events={selectedHeatmapCell?.events || []}
          summary={selectedHeatmapCell?.summary}
        />
      )}
    </div>
  );
};

export default StatsView;
