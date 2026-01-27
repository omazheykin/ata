import React from 'react';
import { HubConnectionState } from '@microsoft/signalr';

interface ConnectionLostModalProps {
    isOpen: boolean;
    connectionState: HubConnectionState | null;
    onReconnect: () => void;
}

const ConnectionLostModal: React.FC<ConnectionLostModalProps> = ({ isOpen, connectionState, onReconnect }) => {
    if (!isOpen) return null;

    const isReconnecting = connectionState === HubConnectionState.Reconnecting;
    const isDisconnected = connectionState === HubConnectionState.Disconnected;

    return (
        <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 bg-black/80 backdrop-blur-sm animate-in fade-in duration-300">
            <div className="glass w-full max-w-md rounded-3xl border border-white/10 overflow-hidden shadow-2xl animate-in zoom-in-95 duration-300">
                <div className="p-8 text-center">
                    {/* Icon */}
                    <div className="mb-6 relative inline-block">
                        <div className={`w-20 h-20 rounded-full flex items-center justify-center ${isReconnecting ? 'bg-amber-500/20 text-amber-400' : 'bg-red-500/20 text-red-400'}`}>
                            {isReconnecting ? (
                                <svg className="w-10 h-10 animate-spin" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                                </svg>
                            ) : (
                                <svg className="w-10 h-10" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                                </svg>
                            )}
                        </div>
                        {isDisconnected && (
                            <div className="absolute -top-1 -right-1 w-6 h-6 bg-red-500 rounded-full border-4 border-[#020617] animate-pulse" />
                        )}
                    </div>

                    <h2 className="text-2xl font-bold text-white mb-2">
                        {isReconnecting ? 'Connection Lost' : 'Disconnected'}
                    </h2>
                    <p className="text-gray-400 mb-8">
                        {isReconnecting
                            ? 'Attempting to restore connection to the arbitrage server...'
                            : 'The connection to the server was lost. Please check your internet or try to reconnect manually.'}
                    </p>

                    <div className="space-y-3">
                        <button
                            onClick={onReconnect}
                            disabled={isReconnecting}
                            className={`w-full py-4 px-6 rounded-2xl font-bold transition-all flex items-center justify-center gap-2 ${isReconnecting
                                    ? 'bg-white/5 text-gray-500 cursor-not-allowed'
                                    : 'bg-primary-600 hover:bg-primary-500 text-white shadow-lg shadow-primary-600/20'
                                }`}
                        >
                            {isReconnecting ? (
                                <>
                                    <svg className="w-5 h-5 animate-spin" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                                    </svg>
                                    Reconnecting...
                                </>
                            ) : (
                                'Try Reconnect'
                            )}
                        </button>

                        <button
                            onClick={() => window.location.reload()}
                            className="w-full py-3 px-6 rounded-2xl font-medium text-gray-400 hover:text-white hover:bg-white/5 transition-all"
                        >
                            Reload Dashboard
                        </button>
                    </div>
                </div>

                {/* Status Bar */}
                <div className="bg-white/5 px-8 py-4 flex items-center justify-between border-t border-white/5">
                    <span className="text-xs text-gray-500 uppercase tracking-wider font-semibold">Server Status</span>
                    <div className="flex items-center gap-2">
                        <div className={`w-2 h-2 rounded-full ${isReconnecting ? 'bg-amber-500 animate-pulse' : 'bg-red-500'}`} />
                        <span className={`text-xs font-bold ${isReconnecting ? 'text-amber-500' : 'text-red-500'}`}>
                            {isReconnecting ? 'RECONNECTING' : 'OFFLINE'}
                        </span>
                    </div>
                </div>
            </div>
        </div>
    );
};

export default ConnectionLostModal;
