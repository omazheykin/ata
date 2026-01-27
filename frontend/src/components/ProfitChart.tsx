import React, { useEffect, useRef } from "react";
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
} from "recharts";
import type { ArbitrageOpportunity } from "../types/types";

interface ProfitChartProps {
  opportunities: ArbitrageOpportunity[];
}

const ProfitChart: React.FC<ProfitChartProps> = ({ opportunities }) => {
  const chartRef = useRef<HTMLDivElement>(null);

  // Prepare data for chart - show last 20 opportunities
  const chartData = opportunities.slice(-20).map((opp, index) => ({
    index: index + 1,
    profit: opp.profitPercentage,
    time: new Date(opp.timestamp).toLocaleTimeString([], {
      hour: "2-digit",
      minute: "2-digit",
    }),
    asset: opp.asset,
    buyExchange: opp.buyExchange,
    sellExchange: opp.sellExchange,
    buyPrice: opp.buyPrice,
    sellPrice: opp.sellPrice,
    volume: opp.volume,
    buyFee: opp.buyFee,
    sellFee: opp.sellFee,
  }));

  useEffect(() => {
    // Scroll to latest data when new opportunity arrives
    if (chartRef.current) {
      chartRef.current.scrollIntoView({ behavior: "smooth", block: "nearest" });
    }
  }, [opportunities.length]);

  return (
    <div className="glass rounded-xl p-6 animate-fade-in" ref={chartRef}>
      <h2 className="text-xl font-bold text-white mb-4 flex items-center gap-2">
        <span className="text-2xl">📈</span>
        Profit Trend
      </h2>
      <ResponsiveContainer width="100%" height={300}>
        <LineChart data={chartData}>
          <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.1)" />
          <XAxis
            dataKey="time"
            stroke="rgba(255,255,255,0.5)"
            style={{ fontSize: "12px" }}
          />
          <YAxis
            stroke="rgba(255,255,255,0.5)"
            style={{ fontSize: "12px" }}
            label={{
              value: "Profit %",
              angle: -90,
              position: "insideLeft",
              fill: "rgba(255,255,255,0.7)",
            }}
          />
          <Tooltip
            contentStyle={{
              backgroundColor: "rgba(30, 41, 59, 0.95)",
              border: "1px solid rgba(56, 189, 248, 0.3)",
              borderRadius: "8px",
              color: "#fff",
              minWidth: 220,
            }}
            cursor={{ stroke: '#38bdf8', strokeWidth: 2, opacity: 0.2 }}
            formatter={(value, name) => {
              if (name === 'profit') {
                return [`${(value as number).toFixed(2)}%`, 'Profit'];
              }
              return [value, name];
            }}
            content={({ active, payload }) => {
              if (active && payload && payload.length > 0) {
                const d = payload[0].payload;
                return (
                  <div style={{ padding: 10 }}>
                    <div style={{ fontWeight: 600, marginBottom: 4 }}>{d.asset} ({d.time})</div>
                    <div><b>Buy:</b> {d.buyExchange} @ ${d.buyPrice.toFixed(2)} (fee: {d.buyFee})</div>
                    <div><b>Sell:</b> {d.sellExchange} @ ${d.sellPrice.toFixed(2)} (fee: {d.sellFee})</div>
                    <div><b>Volume:</b> {d.volume}</div>
                    <div><b>Profit:</b> {d.profit.toFixed(2)}%</div>
                  </div>
                );
              }
              return null;
            }}
          />
          <Line
            type="monotone"
            dataKey="profit"
            stroke="#38bdf8"
            strokeWidth={3}
            dot={{ fill: "#0ea5e9", r: 4 }}
            activeDot={{ r: 6, fill: "#38bdf8" }}
            animationDuration={300}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
};

export default ProfitChart;
