import React from "react";
import type { ArbitrageEvent } from "../types/types";

interface EventsModalProps {
  pair: string;
  events: ArbitrageEvent[];
  isOpen: boolean;
  onClose: () => void;
  loading: boolean;
}

const EventsModal: React.FC<EventsModalProps> = ({
  pair,
  events,
  isOpen,
  onClose,
  loading,
}) => {
  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-[110] flex items-center justify-center p-4">
      <div
        className="absolute inset-0 bg-black/80 backdrop-blur-sm"
        onClick={onClose}
      />
      <div className="glass rounded-2xl p-6 border border-white/10 max-w-4xl w-full relative z-10 animate-fade-in flex flex-col max-h-[90vh]">
        <div className="flex justify-between items-center mb-6">
          <h3 className="text-2xl font-bold text-white flex items-center gap-3">
            <span>📋</span> {pair} Opportunity History
          </h3>
          <button
            onClick={onClose}
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

        <div className="flex-1 overflow-y-auto">
          {loading ? (
            <div className="flex items-center justify-center py-12">
              <div className="animate-spin rounded-full h-10 w-10 border-b-2 border-blue-500"></div>
            </div>
          ) : events.length === 0 ? (
            <div className="text-center py-12 text-gray-500">
              No historical events found for {pair}.
            </div>
          ) : (
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="bg-white/5 text-gray-400 text-xs uppercase tracking-wider sticky top-0">
                  <th className="p-3 font-medium">Time</th>
                  <th className="p-3 font-medium">Direction</th>
                  <th className="p-3 font-medium text-right">Spread</th>
                  <th className="p-3 font-medium text-right">Depth Buy</th>
                  <th className="p-3 font-medium text-right">Depth Sell</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-white/5">
                {events.map((event) => (
                  <tr
                    key={event.id}
                    className="hover:bg-white/5 transition-colors"
                  >
                    <td className="p-1.5 text-xs text-gray-300 whitespace-nowrap">
                      {new Date(event.timestamp).toLocaleString()}
                    </td>
                    <td className="p-1.5">
                      <span
                        className={`text-[10px] px-2 py-0.5 rounded-full ${
                          event.direction.includes("B→C")
                            ? "bg-blue-500/20 text-blue-400"
                            : "bg-purple-500/20 text-purple-400"
                        }`}
                      >
                        {event.direction}
                      </span>
                    </td>
                    <td
                      className={`p-1.5 text-right text-xs font-bold ${
                        event.spread > 0 ? "text-green-400" : "text-red-400"
                      }`}
                    >
                      {(event.spread * 100).toFixed(2)}%
                    </td>
                    <td className="p-1.5 text-right text-xs text-gray-400">
                      {event.depthBuy.toLocaleString()}
                    </td>
                    <td className="p-1.5 text-right text-xs text-gray-400">
                      {event.depthSell.toLocaleString()}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>

        <div className="mt-6 pt-6 border-t border-white/5 flex justify-end">
          <button
            onClick={onClose}
            className="px-6 py-2 bg-white/5 text-white font-bold rounded-xl hover:bg-white/10 transition-all border border-white/10"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
};

export default EventsModal;
