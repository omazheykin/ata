import React from "react";
import {
  ScatterChart,
  Scatter,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  ZAxis,
} from "recharts";
import type { ArbitrageOpportunity } from "../types/types";

interface ProfitScatterChartProps {
  opportunities: ArbitrageOpportunity[];
}

const ProfitScatterChart: React.FC<ProfitScatterChartProps> = ({
  opportunities,
}) => {
  // Use last 100 opportunities
  const chartData = opportunities.slice(-100).map((opp) => ({
    x: opp.volume, // Volume on X
    y: opp.profitPercentage, // Profit on Y
    z: 1, // Size z-axis
    name: opp.asset,
    time: new Date(opp.timestamp).toLocaleTimeString([], {
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
    }),
    rawTime: opp.timestamp,
    buyExchange: opp.buyExchange,
    sellExchange: opp.sellExchange,
    buyPrice: opp.buyPrice,
    sellPrice: opp.sellPrice,
    buyFee: opp.buyFee,
    sellFee: opp.sellFee,
  }));

  return (
    <div className="glass rounded-xl p-6 animate-fade-in">
      <h2 className="text-xl font-bold text-white mb-4 flex items-center gap-2">
        <span className="text-2xl">💎</span>
        Value Discovery (Profit vs Volume)
      </h2>
      <ResponsiveContainer width="100%" height={300}>
        <ScatterChart
          margin={{
            top: 20,
            right: 20,
            bottom: 20,
            left: 20,
          }}
        >
          <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.1)" />
          <XAxis
            type="number"
            dataKey="x"
            name="Volume"
            unit=""
            stroke="rgba(255,255,255,0.5)"
            style={{ fontSize: "12px" }}
            label={{
              value: "Volume",
              position: "insideBottomRight",
              offset: -10,
              fill: "rgba(255,255,255,0.5)",
            }}
          />
          <YAxis
            type="number"
            dataKey="y"
            name="Profit"
            unit="%"
            stroke="rgba(255,255,255,0.5)"
            style={{ fontSize: "12px" }}
            label={{
              value: "Profit %",
              angle: -90,
              position: "insideLeft",
              fill: "rgba(255,255,255,0.7)",
            }}
          />
          <ZAxis type="number" dataKey="z" range={[60, 400]} name="score" />
          <Tooltip
            cursor={{ strokeDasharray: "3 3" }}
            contentStyle={{
              backgroundColor: "rgba(30, 41, 59, 0.95)",
              border: "1px solid rgba(139, 92, 246, 0.3)",
              borderRadius: "8px",
              color: "#fff",
              minWidth: 220,
            }}
            content={({ active, payload }) => {
              if (active && payload && payload.length > 0) {
                const d = payload[0].payload;
                return (
                  <div style={{ padding: 10 }}>
                    <div style={{ fontWeight: 600, marginBottom: 4 }}>
                      {d.name} ({d.time})
                    </div>
                    <div>
                      <b>Profit:</b> {d.y.toFixed(2)}%
                    </div>
                    <div>
                      <b>Volume:</b> {d.x}
                    </div>
                    <div className="mt-2 text-xs opacity-75">
                      {d.buyExchange} &rarr; {d.sellExchange}
                    </div>
                  </div>
                );
              }
              return null;
            }}
          />
          <Scatter name="Opportunities" data={chartData} fill="#8b5cf6" />
        </ScatterChart>
      </ResponsiveContainer>
    </div>
  );
};

export default ProfitScatterChart;
