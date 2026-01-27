export interface ArbitrageOpportunity {
    id: string;
    asset: string;
    buyExchange: string;
    sellExchange: string;
    buyPrice: number;
    sellPrice: number;
    buyFee: number;
    sellFee: number;
    profitPercentage: number;
    volume: number;
    timestamp: string;
    status: string;
    isSandbox: boolean;
}

export interface Statistics {
    totalOpportunities: number;
    averageProfitPercentage: number;
    bestOpportunity: ArbitrageOpportunity | null;
    totalVolume: number;
    activeExchanges: number;
}

export interface Balance {
    asset: string;
    free: number;
    locked: number;
    total: number;
}

export interface Transaction {
    id: string;
    timestamp: string;
    type: string;
    asset: string;
    amount: number;
    exchange: string;
    price: number;
    fee: number;
    profit: number;
    status: string;
}

