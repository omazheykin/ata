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
            <h2 className="text-2xl font-bold text-white flex items-center gap-2">
              <span>🔥</span> Activity Details
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
            <p className="text-gray-400 text-xs mb-1">Total Events</p>
            <p className="text-2xl font-bold gradient-text">
              {summary?.eventCount || events.length}
            </p>
          </div>
          <div className="glass rounded-xl p-4 border border-white/5">
            <p className="text-gray-400 text-xs mb-1">Avg Spread</p>
            <p className="text-2xl font-bold text-blue-400">
              {summary?.avgSpread?.toFixed(3) || "0.000"}%
            </p>
          </div>
          <div className="glass rounded-xl p-4 border border-white/5">
            <p className="text-gray-400 text-xs mb-1">Max Spread</p>
            <p className="text-2xl font-bold text-green-400">
              {summary?.maxSpread?.toFixed(3) || "0.000"}%
            </p>
          </div>
        </div>

        {/* Events Headers */}
        <div className="grid grid-cols-[80px_1fr_100px_140px] gap-4 px-4 pb-2 text-xs font-semibold text-gray-400 border-b border-white/5 mb-2 mr-2">
          <div
            className="flex items-center gap-1 cursor-help"
            title="Fund flow: Buy Exchange → Sell Exchange"
          >
            Direction <span className="opacity-50">?</span>
          </div>
          <div>Asset / Time</div>
          <div
            className="text-right flex items-center justify-end gap-1 cursor-help"
            title="Net profit percentage (3 decimal precision)"
          >
            Spread <span className="opacity-50">?</span>
          </div>
          <div
            className="text-right flex items-center justify-end gap-1 cursor-help"
            title="Liquidity units: Buy Exchange Depth / Sell Exchange Depth"
          >
            Depth <span className="opacity-50">?</span>
          </div>
        </div>

        {/* Events List */}
        <div className="flex-1 overflow-y-auto pr-2 custom-scrollbar">
          {events.length === 0 ? (
            <div className="text-center py-12 text-gray-500">
              <p className="text-lg">No events in this time period</p>
            </div>
          ) : (
            <div className="space-y-2">
              {events.map((event, index) => (
                <div
                  key={index}
                  className="glass rounded-lg p-4 border border-white/5 hover:bg-white/5 transition-colors"
                >
                  <div className="grid grid-cols-[80px_1fr_100px_140px] gap-4 items-center">
                    <div>
                      <p className="text-sm font-bold text-white">
                        {event.direction}
                      </p>
                    </div>

                    <div className="flex items-center gap-3 min-w-0">
                      <div className="w-10 h-10 rounded-full bg-gradient-to-br from-blue-500 to-purple-500 flex items-center justify-center text-white font-bold flex-shrink-0">
                        {event.pair.charAt(0)}
                      </div>
                      <div className="min-w-0">
                        <p className="text-white font-bold truncate">
                          {event.pair}
                        </p>
                        <p className="text-xs text-gray-400">
                          {new Date(event.timestamp).toLocaleTimeString()}
                        </p>
                      </div>
                    </div>

                    <div className="text-right">
                      <p className="text-lg font-bold text-green-400">
                        {(event.spreadPercent ?? event.spread * 100).toFixed(3)}
                        %
                      </p>
                    </div>

                    <div className="text-right">
                      <p className="text-sm text-white font-mono">
                        {event.depthBuy.toLocaleString(undefined, {
                          maximumFractionDigits: 2,
                          minimumFractionDigits: 0,
                        })}{" "}
                        /{" "}
                        {event.depthSell.toLocaleString(undefined, {
                          maximumFractionDigits: 2,
                          minimumFractionDigits: 0,
                        })}
                      </p>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default HeatmapCellModal;
