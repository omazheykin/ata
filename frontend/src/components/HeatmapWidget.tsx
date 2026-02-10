import React, { useEffect, useState } from "react";
import { apiService } from "../services/apiService";
import type {
  StatsResponse,
  ArbitrageEvent,
  HeatmapCell,
} from "../types/types";
import HeatmapCellModal from "./HeatmapCellModal";

interface HeatmapWidgetProps {
  externalStats?: StatsResponse | null;
  externalLoading?: boolean;
}

const HeatmapWidget: React.FC<HeatmapWidgetProps> = ({
  externalStats,
  externalLoading,
}) => {
  const [stats, setStats] = useState<StatsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [isHeatmapModalOpen, setIsHeatmapModalOpen] = useState(false);
  const [selectedHeatmapCell, setSelectedHeatmapCell] = useState<{
    day: string;
    hour: number;
    events: ArbitrageEvent[];
    summary?: HeatmapCell;
  } | null>(null);

  // Sync with external props if provided
  useEffect(() => {
    if (externalStats !== undefined) {
      setStats(externalStats);
    }
  }, [externalStats]);

  useEffect(() => {
    if (externalLoading !== undefined) {
      setLoading(externalLoading);
    }
  }, [externalLoading]);

  const fetchStats = async () => {
    // Only fetch if not managed externally
    if (externalStats !== undefined) return;
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

  const handleHeatmapCellClick = async (
    displayDay: string,
    displayHour: number,
    utcDay: string,
    utcHour: number,
  ) => {
    try {
      const details = await apiService.getCellDetails(utcDay, utcHour);
      setSelectedHeatmapCell({
        day: displayDay,
        hour: displayHour,
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

  const [manualOffset, setManualOffset] = useState<number>(() => {
    const saved = localStorage.getItem("heatmap_tz_offset");
    return saved ? parseInt(saved) : 0;
  });

  const [localCalendar, setLocalCalendar] = useState<
    Record<string, Record<string, any>>
  >({});

  useEffect(() => {
    localStorage.setItem("heatmap_tz_offset", manualOffset.toString());
  }, [manualOffset]);

  useEffect(() => {
    if (!stats?.calendar) return;

    const newCal: any = {};
    const daysArr = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    const dayNamesShort = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    dayNamesShort.forEach((localDay) => {
      newCal[localDay] = {};
      for (let h = 0; h < 24; h++) {
        const hStr = h.toString().padStart(2, "0");

        const date = new Date();
        const localDayIndex = daysArr.indexOf(localDay);
        const daysToShift = (localDayIndex - date.getDay() + 7) % 7;
        date.setDate(date.getDate() + daysToShift);

        // Manual Shift
        date.setHours(h - manualOffset, 0, 0, 0);

        const utcDay = daysArr[date.getUTCDay()].substring(0, 3);
        const utcHour = date.getUTCHours();
        const utcHourStr = utcHour.toString().padStart(2, "0");

        const detail = stats?.calendar?.[utcDay]?.[utcHourStr];

        newCal[localDay][hStr] = {
          detail,
          utcDay,
          utcHour,
        };
      }
    });
    setLocalCalendar(newCal);
  }, [stats, manualOffset]);

  if (loading && !stats) {
    return (
      <div className="glass rounded-xl p-6 h-full flex items-center justify-center border border-white/5">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-500"></div>
      </div>
    );
  }

  if (!stats) return null;

  return (
    <div className="w-full h-full flex flex-col">
      <div className="flex justify-between items-center mb-3">
        <div className="flex items-center gap-4">
          <h3 className="text-sm font-bold text-white flex items-center gap-2">
            <span>📅</span> Calendar View
          </h3>

          <div className="flex items-center gap-2 px-2 py-0.5 bg-white/5 rounded-lg border border-white/10">
            <span className="text-[9px] text-gray-500 font-black uppercase tracking-wider">
              TZ
            </span>
            <select
              value={manualOffset}
              onChange={(e) => setManualOffset(parseInt(e.target.value))}
              className="bg-transparent text-blue-400 text-[10px] font-bold focus:outline-none cursor-pointer"
            >
              {[...Array(27)]
                .map((_, i) => i - 12)
                .map((off) => (
                  <option
                    key={off}
                    value={off}
                    className="bg-[#0f172a] text-white"
                  >
                    {off >= 0 ? `+${off}` : off}h
                  </option>
                ))}
            </select>
          </div>
        </div>
        <button
          onClick={fetchStats}
          className="p-1 rounded-lg hover:bg-white/5 text-gray-500 hover:text-white transition-colors"
          title="Refresh Heatmap"
        >
          <svg
            className={`w-3.5 h-3.5 ${loading ? "animate-spin" : ""}`}
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

      <div className="flex-1 min-h-0">
        <div className="h-full flex flex-col justify-between">
          {/* Header Row */}
          <div className="flex mb-0.5">
            <div className="w-8 flex-none"></div>
            {hours.map((h) => (
              <div
                key={h}
                className="flex-1 text-[7px] text-gray-500 text-center mx-[0.5px]"
              >
                {h}
              </div>
            ))}
          </div>

          {/* Rows */}
          {days.map((day) => (
            <div key={day} className="flex-1 flex mb-[1px] min-h-0">
              <div className="w-8 flex-none text-[9px] text-gray-500 font-bold flex items-center">
                {day}
              </div>
              {hours.map((h) => {
                const cell = localCalendar[day]?.[h];
                const detail = cell?.detail;
                const utcDay = cell?.utcDay;
                const utcHour = cell?.utcHour;

                const now = new Date();
                const shiftedNow = new Date(
                  now.getTime() + manualOffset * 3600000,
                );

                const isCurrentDay =
                  ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"][
                    shiftedNow.getDay()
                  ] === day;
                const isCurrentHour =
                  shiftedNow.getHours().toString().padStart(2, "0") === h;
                const isNow = isCurrentDay && isCurrentHour;

                return (
                  <div
                    key={h}
                    onClick={() =>
                      handleHeatmapCellClick(day, parseInt(h), utcDay, utcHour)
                    }
                    className={`flex-1 min-h-[14px] m-[0.5px] cursor-pointer transition-all ${
                      detail
                        ? getVolatilityColor(detail.volatilityScore)
                        : "bg-white/5 hover:bg-white/10"
                    } ${
                      isNow
                        ? "ring-1 ring-white ring-inset z-10 bg-white/20"
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
                          )}%\nFrequency: ${detail.avgOpportunitiesPerHour.toFixed(
                            1,
                          )} opps/hr\nVolatility: ${(
                            detail.volatilityScore * 100
                          ).toFixed(0)}%\nClick to view ${detail.count} events${
                            isNow ? " (CURRENT)" : ""
                          }`
                        : `No activity${isNow ? " (CURRENT)" : ""}`
                    }
                  >
                    {detail && detail.avgOpportunitiesPerHour > 0 && (
                      <div className="h-full flex items-center justify-center">
                        <span className="text-[7px] font-black opacity-60 leading-none">
                          {detail.avgOpportunitiesPerHour.toFixed(0)}
                        </span>
                      </div>
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
