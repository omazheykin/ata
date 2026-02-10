import React, { useMemo } from "react";
import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  Cell,
} from "recharts";
import type { MarketPriceUpdate } from "../types/types";

interface PriceComparisonChartProps {
  data: MarketPriceUpdate | null;
}

const PriceComparisonChart: React.FC<PriceComparisonChartProps> = ({
  data,
}) => {
  const chartData = useMemo(() => {
    if (!data) return [];

    return Object.entries(data.prices)
      .map(([exchange, price]) => ({
        name: exchange,
        price: price,
        color:
          exchange === "Binance"
            ? "#F3BA2F"
            : exchange === "Coinbase"
              ? "#0052FF"
              : exchange === "OKX"
                ? "#FFFFFF"
                : "#8884d8",
      }))
      .sort((a, b) => a.price - b.price); // Sort to highlight spread
  }, [data]);

  if (!data) {
    return null;
  }

  // Calculate spread between min and max for the label
  const prices = Object.values(data.prices);
  const minPrice = Math.min(...prices);
  const maxPrice = Math.max(...prices);
  const spread = ((maxPrice - minPrice) / minPrice) * 100;

  return (
    <div className="glass rounded-xl p-6 animate-fade-in">
      <div className="flex justify-between items-center mb-4">
        <h2 className="text-lg font-bold text-white flex items-center gap-2">
          <span className="text-xl">📊</span>
          {data.asset} Price Comparison
        </h2>
        <div className="text-[10px] px-2 py-1 bg-blue-500/20 text-blue-400 rounded-full border border-blue-500/30">
          Spread: {spread.toFixed(3)}%
        </div>
      </div>

      <ResponsiveContainer width="100%" height={120}>
        <BarChart
          data={chartData}
          layout="vertical"
          margin={{ left: -20, right: 20 }}
        >
          <CartesianGrid
            strokeDasharray="3 3"
            stroke="rgba(255,255,255,0.05)"
            horizontal={false}
          />
          <XAxis type="number" domain={["dataMin - 1", "dataMax + 1"]} hide />
          <YAxis
            dataKey="name"
            type="category"
            stroke="rgba(255,255,255,0.5)"
            style={{ fontSize: "10px" }}
            width={70}
          />
          <Tooltip
            contentStyle={{
              backgroundColor: "rgba(15, 23, 42, 0.9)",
              border: "1px solid rgba(255, 255, 255, 0.1)",
              borderRadius: "8px",
              color: "#fff",
              fontSize: "10px",
            }}
            formatter={(val: number | undefined) => [
              `$${(val || 0).toLocaleString()}`,
              "Price",
            ]}
          />
          <Bar dataKey="price" radius={[0, 4, 4, 0]} barSize={15}>
            {chartData.map((entry, index) => (
              <Cell
                key={`cell-${index}`}
                fill={entry.color}
                fillOpacity={0.8}
              />
            ))}
          </Bar>
        </BarChart>
      </ResponsiveContainer>

      <div className="flex justify-between mt-2 px-1">
        <div className="text-[10px] text-green-400 flex items-center gap-1">
          <div className="w-1.5 h-1.5 rounded-full bg-green-400"></div>
          Buy: {chartData[0]?.name}
        </div>
        <div className="text-[10px] text-red-400 flex items-center gap-1">
          Sell: {chartData[chartData.length - 1]?.name}
          <div className="w-1.5 h-1.5 rounded-full bg-red-400"></div>
        </div>
      </div>
    </div>
  );
};

export default PriceComparisonChart;
