import { useState, useEffect } from "react";
import { HubConnectionState } from "@microsoft/signalr";
import "./index.css";
import Dashboard from "./components/Dashboard";
import StatsView from "./components/StatsView";
import ConnectionStatus from "./components/ConnectionStatus";
import { signalRService } from "./services/signalRService";

function App() {
  const [view, setView] = useState<"live" | "stats">("live");
  const [connectionState, setConnectionState] = useState<HubConnectionState>(
    signalRService.getConnectionState(),
  );

  useEffect(() => {
    let mounted = true;

    // Start polling state immediately and consistently
    const stateInterval = setInterval(() => {
      if (mounted) {
        setConnectionState(signalRService.getConnectionState());
      }
    }, 1000);

    const initConnection = async () => {
      try {
        await signalRService.startConnection();
      } catch (error) {
        console.error("SignalR initial connection failed:", error);
      }
    };

    initConnection();

    return () => {
      mounted = false;
      clearInterval(stateInterval);
    };
  }, []);

  return (
    <div className="min-h-screen bg-[#020617] text-white">
      {/* Global Navigation Bar */}
      <nav className="glass sticky top-0 z-50 px-6 py-4 flex justify-between items-center border-b border-white/5">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 bg-gradient-to-br from-blue-500 to-purple-600 rounded-xl flex items-center justify-center text-2xl shadow-lg shadow-blue-500/20">
            🤖
          </div>
          <div>
            <h1 className="text-xl font-bold gradient-text leading-tight">
              Antigravity
            </h1>
            <p className="text-[10px] text-gray-500 uppercase tracking-widest font-bold">
              Arbitrage Terminal
            </p>
          </div>
        </div>

        <div className="flex items-center gap-6">
          <ConnectionStatus connectionState={connectionState} />
          <div className="flex flex-col items-end">
            <span className="text-[10px] text-gray-500 uppercase font-black">
              Market Depth
            </span>
            <div className="flex items-center gap-2">
              <div className="w-2 h-2 bg-green-500 rounded-full animate-pulse"></div>
              <span className="text-sm font-bold text-green-400">
                L2 Streaming
              </span>
            </div>
          </div>
        </div>
      </nav>

      <main className="p-4">
        {view === "live" ? (
          <Dashboard
            connectionState={connectionState}
            activeView={view}
            onViewChange={setView}
          />
        ) : (
          <StatsView activeView={view} onViewChange={setView} />
        )}
      </main>
    </div>
  );
}

export default App;
