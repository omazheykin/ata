import * as signalR from '@microsoft/signalr';
import type { ArbitrageOpportunity, Transaction, StrategyUpdate, ConnectionStatus, MarketPriceUpdate } from '../types/types';

const HUB_URL = 'http://localhost:5000/arbitrageHub';

class SignalRService {
    private connection: signalR.HubConnection;
    private reconnectAttempts = 0;
    private maxReconnectAttempts = 5;

    constructor() {
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl(HUB_URL, {
                skipNegotiation: false,
                transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.ServerSentEvents | signalR.HttpTransportType.LongPolling,
            })
            .withAutomaticReconnect({
                nextRetryDelayInMilliseconds: (retryContext) => {
                    if (retryContext.previousRetryCount >= this.maxReconnectAttempts) {
                        return null; // Stop reconnecting
                    }
                    return Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000);
                },
            })
            .configureLogging(signalR.LogLevel.Information)
            .build();

        // Connection event handlers
        this.connection.onreconnecting((error) => {
            console.warn('SignalR reconnecting...', error);
            this.reconnectAttempts++;
        });

        this.connection.onreconnected((connectionId) => {
            console.log('SignalR reconnected:', connectionId);
            this.reconnectAttempts = 0;
        });

        this.connection.onclose((error) => {
            console.error('SignalR connection closed:', error);
            if (this.reconnectAttempts < this.maxReconnectAttempts) {
                setTimeout(() => this.startConnection(), 5000);
            }
        });
    }

    async startConnection(): Promise<signalR.HubConnection> {
        if (this.connection.state === signalR.HubConnectionState.Connected) {
            return this.connection;
        }

        try {
            await this.connection.start();
            console.log('✅ SignalR Connected');
            return this.connection;
        } catch (error) {
            console.error('❌ SignalR Connection Error:', error);
            throw error;
        }
    }

    onReceiveOpportunity(callback: (opportunity: ArbitrageOpportunity) => void): void {
        this.connection.on('ReceiveOpportunity', callback);
    }

    onReceiveTransaction(callback: (transaction: Transaction) => void): void {
        this.connection.on('ReceiveTransaction', callback);
    }

    onReceiveSandboxModeUpdate(callback: (enabled: boolean) => void): void {
        this.connection.on('ReceiveSandboxModeUpdate', callback);
    }

    onReceiveStrategyUpdate(callback: (update: StrategyUpdate) => void): void {
        this.connection.on('ReceiveStrategyUpdate', callback);
    }

    onReceiveSmartStrategyUpdate(callback: (enabled: boolean) => void): void {
        this.connection.on('ReceiveSmartStrategyUpdate', callback);
    }

    onReceiveMarketPrices(callback: (update: MarketPriceUpdate) => void): void {
        this.connection.on('ReceiveMarketPrices', callback);
    }

    onReceiveConnectionStatus(callback: (status: ConnectionStatus[]) => void): void {
        this.connection.on('ReceiveConnectionStatus', callback);
    }

    onReceivePairThresholdsUpdate(callback: (thresholds: Record<string, number>) => void): void {
        this.connection.on('ReceivePairThresholdsUpdate', callback);
    }

    onReceiveSafeBalanceMultiplierUpdate(callback: (multiplier: number) => void): void {
        this.connection.on('ReceiveSafeBalanceMultiplierUpdate', callback);
    }

    onReceiveFeeModeUpdate(callback: (enabled: boolean) => void): void {
        this.connection.on('ReceiveFeeModeUpdate', callback);
    }


    async stopConnection(): Promise<void> {
        if (this.connection.state !== signalR.HubConnectionState.Disconnected) {
            await this.connection.stop();
            console.log('SignalR Disconnected');
        }
    }

    getConnectionState(): signalR.HubConnectionState {
        return this.connection.state;
    }
}

export const signalRService = new SignalRService();
