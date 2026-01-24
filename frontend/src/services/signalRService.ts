import * as signalR from '@microsoft/signalr';
import type { ArbitrageOpportunity, Transaction } from '../types/types';

const HUB_URL = 'https://localhost:5001/arbitrageHub';

class SignalRService {
    private connection: signalR.HubConnection | null = null;
    private reconnectAttempts = 0;
    private maxReconnectAttempts = 5;

    async startConnection(): Promise<signalR.HubConnection> {
        if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
            return this.connection;
        }

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
        if (!this.connection) {
            throw new Error('Connection not established');
        }
        this.connection.on('ReceiveOpportunity', callback);
    }

    onReceiveTransaction(callback: (transaction: Transaction) => void): void {
        if (!this.connection) {
            throw new Error('Connection not established');
        }
        this.connection.on('ReceiveTransaction', callback);
    }

    onReceiveSandboxModeUpdate(callback: (enabled: boolean) => void): void {
        if (!this.connection) {
            throw new Error('Connection not established');
        }
        this.connection.on('ReceiveSandboxModeUpdate', callback);
    }


    async stopConnection(): Promise<void> {
        if (this.connection) {
            await this.connection.stop();
            console.log('SignalR Disconnected');
        }
    }

    getConnectionState(): signalR.HubConnectionState | null {
        return this.connection?.state || null;
    }
}

export const signalRService = new SignalRService();
