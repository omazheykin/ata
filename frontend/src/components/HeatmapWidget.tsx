import React, { useEffect, useState } from "react";
import { apiService } from "../services/apiService";
import type {
  StatsResponse,
  ArbitrageEvent,
  HeatmapCell,
} from "../types/types";
import HeatmapCellModal from "./HeatmapCellModal";

const HeatmapWidget: React.FC = () => {
  const [stats, setStats] = useState<StatsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [isHeatmapModalOpen, setIsHeatmapModalOpen] = useState(false);
  const [selectedHeatmapCell, setSelectedHeatmapCell] = useState<{
    day: string;
    hour: number;
    events: ArbitrageEvent[];
    summary?: HeatmapCell;
  } | null>(null);

  const fetchStats = async () => {
    try {
      setLoading(true);
      const data = await apiService.getDetailedStats();
      setStats(data);
    } catch (err) {
      console.error("Error fetching heatmap stats:", err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchStats();
    // Refresh every minute
    const interval = setInterval(fetchStats, 60000);
    return () => clearInterval(interval);
  }, []);

  const handleHeatmapCellClick = async (day: string, hour: number) => {
    try {
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

  const getVolatilityColor = (score: number) => {
    if (score > 0.7) return "bg-red-500/40 text-red-200";
    if (score > 0.4) return "bg-yellow-500/40 text-yellow-200";
    return "bg-green-500/40 text-green-200";
  };

  const days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
  const hours = Array.from({ length: 24 }, (_, i) =>
    i.toString().padStart(2, "0"),
  );

  if (loading && !stats) {
    return (
      <div className="glass rounded-xl p-6 h-full flex items-center justify-center border border-white/5">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-500"></div>
      </div>
    );
  }

  if (!stats) return null;

  const { calendar } = stats;

  return (
    <div className="glass rounded-xl p-6 border border-white/5 h-full flex flex-col">
      <div className="flex justify-between items-center mb-4">
        <h3 className="text-lg font-bold text-white flex items-center gap-2">
          <span>📅</span> Calendar View
        </h3>
        <button
          onClick={fetchStats}
          className="p-1.5 rounded-lg hover:bg-white/5 text-gray-500 hover:text-white transition-colors"
          title="Refresh Heatmap"
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

      <div className="flex-1 overflow-x-auto">
        <div className="min-w-[300px]">
          {/* Header Row */}
          <div className="flex mb-1">
            <div className="w-8"></div>
            {hours
              .filter((_, i) => i % 4 === 0)
              .map((h) => (
                <div
                  key={h}
                  className="flex-1 text-[9px] text-gray-500 text-center"
                >
                  {h}
                </div>
              ))}
          </div>

          {/* Rows */}
          {days.map((day) => (
            <div key={day} className="flex mb-[2px]">
              <div className="w-8 text-[10px] text-gray-500 font-medium flex items-center">
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
                    className={`flex-1 h-6 m-[1px] rounded-sm cursor-pointer transition-all ${
                      detail
                        ? getVolatilityColor(detail.volatilityScore)
                        : "bg-white/5 hover:bg-white/10"
                    } ${
                      isNow
                        ? "ring-2 ring-white ring-offset-1 ring-offset-[#0f172a] z-10 scale-110 shadow-lg shadow-white/20"
                        : ""
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
                          ).toFixed(
                            0,
                          )}%\nClick to view ${detail.count} events${isNow ? " (CURRENT)" : ""}`
                        : `No activity${isNow ? " (CURRENT)" : ""}`
                    }
                  >
                    {detail && detail.avgOpportunitiesPerHour > 0 && (
                      <span className="text-[8px] font-bold opacity-80 group-hover:opacity-100">
                        {detail.avgOpportunitiesPerHour.toFixed(0)}
                      </span>
                    )}
                  </div>
                );
              })}
            </div>
          ))}
        </div>
      </div>

      <div className="mt-4 flex gap-4 text-[10px] justify-center text-gray-400">
        <div className="flex items-center gap-1.5">
          <div className="w-2 h-2 bg-green-500/40 rounded-sm"></div> Low
        </div>
        <div className="flex items-center gap-1.5">
          <div className="w-2 h-2 bg-yellow-500/40 rounded-sm"></div> Mod
        </div>
        <div className="flex items-center gap-1.5">
          <div className="w-2 h-2 bg-red-500/40 rounded-sm"></div> High
        </div>
        <div className="flex items-center gap-1.5 ml-2 border-l border-white/10 pl-4">
          <div className="w-2 h-2 ring-1 ring-white ring-offset-1 ring-offset-[#0f172a] rounded-sm bg-white/20"></div>{" "}
          Current
        </div>
      </div>

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

export default HeatmapWidget;
