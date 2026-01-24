import React from 'react';
import { HubConnectionState } from '@microsoft/signalr';

interface ConnectionStatusProps {
    connectionState: HubConnectionState | null;
}

const ConnectionStatus: React.FC<ConnectionStatusProps> = ({ connectionState }) => {
    const getStatusInfo = () => {
        switch (connectionState) {
            case HubConnectionState.Connected:
                return { text: 'Connected', color: 'bg-green-500', pulse: true };
            case HubConnectionState.Connecting:
            case HubConnectionState.Reconnecting:
                return { text: 'Connecting...', color: 'bg-yellow-500', pulse: true };
            case HubConnectionState.Disconnected:
                return { text: 'Disconnected', color: 'bg-red-500', pulse: false };
            default:
                return { text: 'Unknown', color: 'bg-gray-500', pulse: false };
        }
    };

    const status = getStatusInfo();

    return (
        <div className="flex items-center gap-2 px-4 py-2 glass rounded-lg">
            <div className="relative">
                <div className={`w-3 h-3 rounded-full ${status.color}`}></div>
                {status.pulse && (
                    <div className={`absolute inset-0 w-3 h-3 rounded-full ${status.color} animate-ping opacity-75`}></div>
                )}
            </div>
            <span className="text-sm font-medium text-gray-200">{status.text}</span>
        </div>
    );
};

export default ConnectionStatus;
