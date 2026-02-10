import { useState, useEffect } from "react";
import { HubConnectionState } from "@microsoft/signalr";
import "./index.css";
import Dashboard from "./components/Dashboard";
import { signalRService } from "./services/signalRService";

function App() {
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
      <main className="p-4">
        <Dashboard connectionState={connectionState} />
      </main>
    </div>
  );
}

export default App;
