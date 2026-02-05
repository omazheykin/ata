import React, { useEffect, useState } from "react";
import { signalRService } from "../services/signalRService";
import type { ConnectionStatus } from "../types/types";

const ExchangeStatusPanel: React.FC = () => {
  const [statuses, setStatuses] = useState<ConnectionStatus[]>([]);

  useEffect(() => {
    signalRService.onReceiveConnectionStatus((newStatuses) => {
      setStatuses(newStatuses);
    });
  }, []);

  const getStatusColor = (status: string) => {
    switch (status) {
      case "Connected":
        return "text-green-400";
      case "Connecting":
        return "text-yellow-400";
      case "Error":
        return "text-red-400";
      default:
        return "text-gray-400";
    }
  };

  const getStatusBg = (status: string) => {
    switch (status) {
      case "Connected":
        return "bg-green-500/20";
      case "Connecting":
        return "bg-yellow-500/20";
      case "Error":
        return "bg-red-500/20";
      default:
        return "bg-gray-500/20";
    }
  };

  const getStatusDot = (status: string) => {
    switch (status) {
      case "Connected":
        return "bg-green-500";
      case "Connecting":
        return "bg-yellow-500";
      case "Error":
        return "bg-red-500";
      default:
        return "bg-gray-500";
    }
  };

  return (
    <div className="glass rounded-2xl p-4 border border-white/5">
      <h3 className="text-[10px] text-gray-500 uppercase tracking-widest font-black mb-4 flex items-center gap-2">
        <span>📡</span> Exchange Connectivity
      </h3>
      <div className="flex flex-col gap-3">
        {statuses.length === 0 ? (
          <div className="text-xs text-gray-500 italic">
            Waiting for status updates...
          </div>
        ) : (
          statuses.map((status) => (
            <div
              key={status.exchangeName}
              className="flex items-center justify-between group"
            >
              <div className="flex items-center gap-3">
                <div
                  className={`w-8 h-8 rounded-lg ${getStatusBg(status.status)} flex items-center justify-center text-xs font-bold border border-white/5`}
                >
                  {status.exchangeName.substring(0, 1)}
                </div>
                <div>
                  <div className="text-xs font-bold text-gray-200">
                    {status.exchangeName}
                  </div>
                  <div
                    className={`text-[10px] font-medium ${getStatusColor(status.status)}`}
                  >
                    {status.status}
                  </div>
                </div>
              </div>
              <div className="flex flex-col items-end">
                <div className="relative flex items-center gap-2">
                  <div
                    className={`w-1.5 h-1.5 rounded-full ${getStatusDot(status.status)} ${status.status === "Connected" ? "animate-pulse" : ""}`}
                  ></div>
                  <span className="text-[10px] font-mono text-gray-500">
                    {status.latencyMs ? `${status.latencyMs}ms` : "--"}
                  </span>
                </div>
                <div className="text-[9px] text-gray-600 font-medium">
                  {new Date(status.lastUpdate).toLocaleTimeString()}
                </div>
              </div>
            </div>
          ))
        )}
      </div>

      {statuses.some((s) => s.status === "Error") && (
        <div className="mt-4 p-2 rounded-lg bg-red-500/10 border border-red-500/20">
          <p className="text-[9px] text-red-400 leading-tight">
            ⚠️ Integration Error:{" "}
            {statuses.find((s) => s.status === "Error")?.errorMessage}
          </p>
        </div>
      )}
    </div>
  );
};

export default ExchangeStatusPanel;
