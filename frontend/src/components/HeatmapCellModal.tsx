import type { ArbitrageEvent, HeatmapCell } from "../types/types";
import React, { useState } from "react";
import { excelService } from "../services/excelService";
import { apiService } from "../services/apiService";

interface HeatmapCellModalProps {
  isOpen: boolean;
  onClose: () => void;
  day: string;
  hour: number;
  events: ArbitrageEvent[];
  summary?: HeatmapCell;
}

const HeatmapCellModal: React.FC<HeatmapCellModalProps> = ({
  isOpen,
  onClose,
  day,
  hour,
  events,
  summary,
}) => {
  const [isExportingAll, setIsExportingAll] = useState(false);

  if (!isOpen) return null;

  // Format hour range
  const hourEnd = hour + 1;
  const timeRange = `${hour.toString().padStart(2, "0")}:00 - ${hourEnd.toString().padStart(2, "0")}:00`;

  const handleExportRecent = () => {
    const filename = `Recent_Events_${day}_${hour.toString().padStart(2, "0")}-00.xlsx`;
    excelService.exportEventsToExcel(events, filename);
  };

  const handleFullExport = async () => {
    try {
      setIsExportingAll(true);
      await apiService.downloadZippedExport(day, hour);
    } catch (err) {
      console.error("Failed to export full history:", err);
      alert("Failed to generate zipped export on server.");
    } finally {
      setIsExportingAll(false);
    }
  };

  return (
    <div
      className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 animate-fade-in"
      onClick={onClose}
    >
      <div
        className="glass rounded-2xl p-6 max-w-3xl w-full mx-4 max-h-[80vh] overflow-hidden flex flex-col border border-white/10 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between mb-4 pb-4 border-b border-white/10">
          <div>
            <h2 className="text-2xl font-bold text-white flex items-center gap-3">
              <span>📋</span> Activity Details
            </h2>
            <p className="text-gray-400 text-sm mt-1">
              {day} • {timeRange}
            </p>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={handleExportRecent}
              disabled={events.length === 0 || isExportingAll}
              title="Export the 100 most recent events shown below"
              className="px-3 py-2 bg-white/5 text-gray-300 border border-white/10 rounded-xl hover:bg-white/10 transition-all active:scale-95 flex items-center gap-2 text-xs font-bold disabled:opacity-50"
            >
              Recent (100)
            </button>
            <button
              onClick={handleFullExport}
              disabled={
                isExportingAll ||
                (summary?.eventCount === 0 && events.length === 0)
              }
              className="px-4 py-2 bg-green-500/20 text-green-400 border border-green-500/30 rounded-xl hover:bg-green-500/30 transition-all active:scale-95 flex items-center gap-2 text-sm font-bold disabled:opacity-50 disabled:cursor-not-allowed min-w-[140px] justify-center"
            >
              {isExportingAll ? (
                <>
                  <span className="animate-spin text-lg">⏳</span> Loading...
                </>
              ) : (
                <>
                  <span>📁</span> Export All History
                </>
              )}
            </button>
            <button
              onClick={onClose}
              className="text-gray-400 hover:text-white transition-colors p-2 hover:bg-white/5 rounded-lg"
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
        </div>

        {/* Summary Stats */}
        <div className="grid grid-cols-3 gap-4 mb-6">
          <div className="glass rounded-xl p-4 border border-white/5">
            <p className="text-gray-400 text-xs mb-1 uppercase tracking-wider font-black opacity-40">
              Total Events
            </p>
            <p className="text-2xl font-bold gradient-text">
              {summary?.eventCount || events.length}
            </p>
          </div>
          <div className="glass rounded-xl p-4 border border-white/5">
            <p className="text-gray-400 text-xs mb-1 uppercase tracking-wider font-black opacity-40">
              Avg Spread
            </p>
            <p className="text-2xl font-bold text-blue-400">
              {summary?.avgSpread?.toFixed(3) || "0.000"}%
            </p>
          </div>
          <div className="glass rounded-xl p-4 border border-white/5">
            <p className="text-gray-400 text-xs mb-1 uppercase tracking-wider font-black opacity-40">
              Max Spread
            </p>
            <p className="text-2xl font-bold text-green-400">
              {summary?.maxSpread?.toFixed(3) || "0.000"}%
            </p>
          </div>
        </div>

        {/* Events List - Standardized Table */}
        <div className="flex-1 overflow-y-auto custom-scrollbar">
          {events.length === 0 ? (
            <div className="text-center py-12 text-gray-500">
              <p className="text-lg">No events in this time period</p>
            </div>
          ) : (
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="bg-white/5 text-gray-400 text-[10px] uppercase tracking-widest font-black sticky top-0 z-10">
                  <th className="p-3">Time</th>
                  <th className="p-3">Pair</th>
                  <th className="p-3">Direction</th>
                  <th className="p-3 text-right">Spread</th>
                  <th className="p-3 text-right">Depth Buy</th>
                  <th className="p-3 text-right">Depth Sell</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-white/5">
                {events.map((event, index) => (
                  <tr
                    key={index}
                    className="hover:bg-white/5 transition-colors group"
                  >
                    <td className="p-2 text-[10px] text-gray-400 font-mono">
                      {new Date(event.timestamp).toLocaleTimeString()}
                    </td>
                    <td className="p-2">
                      <div className="flex items-center gap-2">
                        <div className="w-6 h-6 rounded-lg bg-white/5 flex items-center justify-center text-[10px] text-white font-bold border border-white/10">
                          {event.pair.charAt(0)}
                        </div>
                        <span className="text-xs font-bold text-white group-hover:text-primary-400 transition-colors">
                          {event.pair}
                        </span>
                      </div>
                    </td>
                    <td className="p-2">
                      <span
                        className={`text-[9px] px-2 py-0.5 rounded-full font-black uppercase tracking-tighter ${
                          event.direction.includes("B→C")
                            ? "bg-blue-500/20 text-blue-400 border border-blue-500/20"
                            : "bg-purple-500/20 text-purple-400 border border-purple-500/20"
                        }`}
                      >
                        {event.direction}
                      </span>
                    </td>
                    <td
                      className={`p-2 text-right text-xs font-bold font-mono ${
                        (event.spreadPercent ?? event.spread) > 0
                          ? "text-green-400"
                          : "text-red-400"
                      }`}
                    >
                      {(event.spreadPercent ?? event.spread * 100).toFixed(3)}%
                    </td>
                    <td className="p-2 text-right text-xs text-gray-500 font-mono">
                      {event.depthBuy.toLocaleString()}
                    </td>
                    <td className="p-2 text-right text-xs text-gray-500 font-mono">
                      {event.depthSell.toLocaleString()}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>
    </div>
  );
};

export default HeatmapCellModal;
