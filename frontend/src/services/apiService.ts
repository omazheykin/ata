import axios from 'axios';
import type { ArbitrageOpportunity, Statistics, Balance, Transaction } from '../types/types';

const API_BASE_URL = 'https://localhost:5001/api';

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

    async getSandboxMode(): Promise<{ enabled: boolean }> {
        const response = await apiClient.get<{ enabled: boolean }>('/settings/sandbox');
        return response.data;
    },

    async toggleSandboxMode(enabled: boolean): Promise<{ enabled: boolean }> {
        const response = await apiClient.post<{ enabled: boolean }>(`/settings/sandbox?enabled=${enabled}`);
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
};

