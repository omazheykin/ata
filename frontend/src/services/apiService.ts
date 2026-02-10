import axios from 'axios';
import type { 
    ArbitrageOpportunity, 
    Statistics, 
    Balance, 
    Transaction, 
    StatsResponse, 
    StrategyUpdate, 
    ArbitrageEvent,
    HeatmapCellDetail,
    AppState
} from '../types/types';

const API_BASE_URL = 'http://localhost:5000/api';

const apiClient = axios.create({
    baseURL: API_BASE_URL,
    timeout: 10000,
    headers: {
        'Content-Type': 'application/json',
    },
});

export const apiService = {
    async getRecentOpportunities(): Promise<ArbitrageOpportunity[]> {
        const response = await apiClient.get<ArbitrageOpportunity[]>('/arbitrage/recent');
        return response.data;
    },

    async getStatistics(): Promise<Statistics> {
        const response = await apiClient.get<Statistics>('/arbitrage/statistics');
        return response.data;
    },

    async getDetailedStats(): Promise<StatsResponse> {
        const response = await apiClient.get<StatsResponse>('/statistics', { timeout: 30000 });
        return response.data;
    },

    async getExchanges(): Promise<string[]> {
        const response = await apiClient.get<string[]>('/arbitrage/exchanges');
        return response.data;
    },

    async getBalances(): Promise<Record<string, Balance[]>> {
        const response = await apiClient.get<Record<string, Balance[]>>('/balances/balances');
        return response.data;
    },

    async getTransactions(): Promise<Transaction[]> {
        const response = await apiClient.get<Transaction[]>('/trade/transactions');
        return response.data;
    },

    async toggleAutoTrade(enabled: boolean): Promise<{ enabled: boolean }> {
        const response = await apiClient.post<{ enabled: boolean }>(`/trade/autotrade?enabled=${enabled}`);
        return response.data;
    },

    async setAutoTradeThreshold(threshold: number): Promise<{ threshold: number }> {
        const response = await apiClient.post<{ threshold: number }>(`/trade/autotrade/threshold?threshold=${threshold}`);
        return response.data;
    },

    async getAutoTradeStatus(): Promise<{ enabled: boolean, threshold: number }> {
        const response = await apiClient.get<{ enabled: boolean, threshold: number }>('/trade/autotrade/status');
        return response.data;
    },

    async getExecutionStrategy(): Promise<{ strategy: string }> {
        const response = await apiClient.get<{ strategy: string }>('/trade/strategy');
        return response.data;
    },

    async setExecutionStrategy(strategy: string): Promise<{ strategy: string }> {
        const response = await apiClient.post<{ strategy: string }>(`/trade/strategy?strategy=${strategy}`);
        return response.data;
    },

    async getSandboxMode(): Promise<{ enabled: boolean }> {
        const response = await apiClient.get<{ enabled: boolean }>('/settings/sandbox');
        return response.data;
    },

    async toggleSandboxMode(enabled: boolean): Promise<{ enabled: boolean }> {
        const response = await apiClient.post<{ enabled: boolean }>(`/settings/sandbox?enabled=${enabled}`);
        return response.data;
    },

    async getSmartStrategy(): Promise<{ enabled: boolean }> {
        const response = await apiClient.get<{ enabled: boolean }>('/settings/smart-strategy');
        return response.data;
    },

    async toggleSmartStrategy(enabled: boolean): Promise<{ enabled: boolean }> {
        const response = await apiClient.post<{ enabled: boolean }>(`/settings/smart-strategy?enabled=${enabled}`);
        return response.data;
    },

    async getStrategyStatus(): Promise<StrategyUpdate> {
        const response = await apiClient.get<StrategyUpdate>('/settings/strategy-status');
        return response.data;
    },

    async getFullState(): Promise<AppState> {
        const response = await apiClient.get<AppState>('/settings/state');
        return response.data;
    },

    async setPairThresholds(thresholds: Record<string, number>): Promise<Record<string, number>> {
        const response = await apiClient.post<Record<string, number>>('/settings/pair-thresholds', thresholds);
        return response.data;
    },

    async setSafeMultiplier(multiplier: number): Promise<{ multiplier: number }> {
        const response = await apiClient.post<{ multiplier: number }>(`/settings/safe-multiplier?multiplier=${multiplier}`);
        return response.data;
    },

    async setUseTakerFees(enabled: boolean): Promise<{ enabled: boolean }> {
        const response = await apiClient.post<{ enabled: boolean }>(`/settings/taker-fees?enabled=${enabled}`);
        return response.data;
    },
    
    async toggleAutoRebalance(enabled: boolean): Promise<{ enabled: boolean }> {
        const response = await apiClient.post<{ enabled: boolean }>(`/settings/auto-rebalance?enabled=${enabled}`);
        return response.data;
    },

    async resetSafetyKillSwitch(): Promise<{ success: boolean }> {
        const response = await apiClient.post<{ success: boolean }>('/settings/safety-reset');
        return response.data;
    },

    async setSafetyLimits(drawdown: number, losses: number): Promise<{ drawdown: number, losses: number }> {
        const response = await apiClient.post<{ drawdown: number, losses: number }>(`/settings/safety-limits?drawdown=${drawdown}&losses=${losses}`);
        return response.data;
    },

    async setRebalanceThreshold(threshold: number): Promise<{ threshold: number }> {
        const response = await apiClient.post<{ threshold: number }>(`/settings/rebalance-threshold?threshold=${threshold}`);
        return response.data;
    },

    async setWalletOverride(asset: string, exchange: string, address: string): Promise<{ asset: string, exchange: string, address: string }> {
        const response = await apiClient.post<{ asset: string, exchange: string, address: string }>(`/settings/wallet-override?asset=${asset}&exchange=${exchange}&address=${address}`);
        return response.data;
    },

    async setWalletOverrides(overrides: Record<string, Record<string, string>>): Promise<Record<string, Record<string, string>>> {
        const response = await apiClient.post<Record<string, Record<string, string>>>('/settings/wallet-overrides', overrides);
        return response.data;
    },

    async deposit(exchange: string, asset: string, amount: number): Promise<{ success: boolean }> {
        const response = await apiClient.post<{ success: boolean }>('/balances/deposit', {
            exchange,
            asset,
            amount
        });
        return response.data;
    },

    async executeTrade(opportunity: ArbitrageOpportunity): Promise<{ success: boolean }> {
        const response = await apiClient.post<{ success: boolean }>('/trade/execute', opportunity);
        return response.data;
    },

    async getEventsByPair(pair: string): Promise<ArbitrageEvent[]> {
        const response = await apiClient.get<ArbitrageEvent[]>(`/statistics/events/${pair}`);
        return response.data;
    },

    async getCellDetails(day: string, hour: number): Promise<HeatmapCellDetail> {
        const response = await apiClient.get<HeatmapCellDetail>(`/statistics/cell-details?day=${day}&hour=${hour}`);
        return response.data;
    },

    async downloadZippedExport(day: string, hour: number): Promise<void> {
        const response = await apiClient.get(`/statistics/export-zipped?day=${day}&hour=${hour}`, {
            responseType: 'blob'
        });
        
        const url = window.URL.createObjectURL(new Blob([response.data]));
        const link = document.createElement('a');
        link.href = url;
        link.setAttribute('download', `Arbitrage_Activity_${day}_${hour.toString().padStart(2, "0")}-00.zip`);
        document.body.appendChild(link);
        link.click();
        link.remove();
        window.URL.revokeObjectURL(url);
    },
};

