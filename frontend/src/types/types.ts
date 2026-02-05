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
    grossProfitPercentage: number;
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

export interface PairStats {
    count: number;
    avgSpread: number;
    maxSpread: number;
}

export interface HourStats {
    count: number;
    avgSpread: number;
    maxSpread: number;
    avgDepth: number;
}

export interface DayStats {
    count: number;
    avgSpread: number;
}

export interface HourDetail {
    avgOpportunitiesPerHour: number;
    count: number;
    avgSpread: number;
    maxSpread: number;
    avgDepth: number;
    directionBias: string;
    volatilityScore: number;
    zone: string;
}

export interface RebalancingProposal {
    asset: string;
    skew: number;
    direction: string;
    amount: number;
    estimatedFee: number;
    costPercentage: number;
    isViable: boolean;
    trendDescription?: string;
}

export interface RebalancingInfo {
    assetSkews: Record<string, number>;
    recommendation: string;
    efficiencyScore: number;
    proposals?: RebalancingProposal[];
}

export interface StatsSummary {
    pairs: Record<string, PairStats>;
    hours: Record<number, HourStats>;
    days: Record<string, DayStats>;
    globalVolatilityScore: number;
    directionDistribution: Record<string, number>;
    avgSeriesDuration: number;
}

export interface StatsResponse {
    summary: StatsSummary;
    calendar: Record<string, Record<string, HourDetail>>;
    rebalancing: RebalancingInfo;
}

export interface StrategyUpdate {
    threshold: number;
    reason: string;
    volatilityScore?: number;
    countScore?: number;
    spreadScore?: number;
}

export interface ConnectionStatus {
    exchangeName: string;
    status: string;
    latencyMs?: number;
    lastUpdate: string;
    errorMessage?: string;
}

export interface ArbitrageEvent {
    id: string;
    pair: string;
    direction: string;
    spread: number;
    spreadPercent: number;
    depthBuy: number;
    depthSell: number;
    timestamp: string;
}

export interface HeatmapCell {
    id: string;
    day: string;
    hour: number;
    eventCount: number;
    avgSpread: number;
    maxSpread: number;
    directionBias: string;
    volatilityScore: number;
}

export interface HeatmapCellDetail {
    summary: HeatmapCell;
    events: ArbitrageEvent[];
}

export interface MarketPriceUpdate {
    asset: string;
    prices: Record<string, number>;
    timestamp: string;
}

export interface AppState {
    isSandboxMode: boolean;
    isAutoTradeEnabled: boolean;
    isAutoRebalanceEnabled: boolean;
    minProfitThreshold: number;
    isSmartStrategyEnabled: boolean;
    safeBalanceMultiplier: number;
    useTakerFees: boolean;
    pairThresholds: Record<string, number>;
    maxDrawdownUsd: number;
    maxConsecutiveLosses: number;
    isSafetyKillSwitchTriggered: boolean;
    globalKillSwitchReason: string;
    minRebalanceSkewThreshold: number;
    walletOverrides: Record<string, Record<string, string>>;
}
