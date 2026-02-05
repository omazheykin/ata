import React, { useMemo, useRef, useEffect } from "react";
import {
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  ReferenceLine,
  Area,
  AreaChart,
} from "recharts";
import type { ArbitrageOpportunity } from "../types/types";

interface ProfitChartProps {
  opportunities: ArbitrageOpportunity[];
  threshold?: number;
}

const ProfitChart: React.FC<ProfitChartProps> = ({
  opportunities,
  threshold,
}) => {
  const chartRef = useRef<HTMLDivElement>(null);

  // Group by second and find MAX profit per second
  const aggregatedData = useMemo(() => {
    const grouped = opportunities.reduce(
      (acc, opp) => {
        // Use second-level precision for grouping
        const timestamp = new Date(opp.timestamp).getTime();
        const secondBucket = Math.floor(timestamp / 1000) * 1000;

        if (
          !acc[secondBucket] ||
          opp.profitPercentage > acc[secondBucket].profit
        ) {
          acc[secondBucket] = {
            rawTime: secondBucket,
            profit: opp.profitPercentage,
            grossProfit: opp.grossProfitPercentage,
            asset: opp.asset,
            buyExchange: opp.buyExchange,
            sellExchange: opp.sellExchange,
            buyPrice: opp.buyPrice,
            sellPrice: opp.sellPrice,
            volume: opp.volume,
            buyFee: opp.buyFee,
            sellFee: opp.sellFee,
          };
        }
        return acc;
      },
      {} as Record<number, any>,
    );

    return Object.values(grouped)
      .sort((a: any, b: any) => a.rawTime - b.rawTime)
      .slice(-50);
  }, [opportunities]);

  useEffect(() => {
    if (chartRef.current) {
      chartRef.current.scrollIntoView({ behavior: "smooth", block: "nearest" });
    }
  }, [aggregatedData.length]);

  return (
    <div className="glass rounded-xl p-6 animate-fade-in" ref={chartRef}>
      <div className="flex justify-between items-center mb-4">
        <h2 className="text-xl font-bold text-white flex items-center gap-2">
          <span className="text-2xl">📈</span>
          Profit Potential Trend
        </h2>
        <div className="flex gap-4 text-xs">
          <div className="flex items-center gap-1.5 text-blue-400">
            <div className="w-2 h-2 rounded-full bg-blue-400"></div>
            <span>Max Net %</span>
          </div>
          <div className="flex items-center gap-1.5 text-white/30">
            <div className="w-2 h-2 rounded-full bg-white/30"></div>
            <span>0% Anchor</span>
          </div>
        </div>
      </div>

      <ResponsiveContainer width="100%" height={300}>
        <AreaChart data={aggregatedData}>
          <defs>
            <linearGradient id="colorProfit" x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor="#38bdf8" stopOpacity={0.3} />
              <stop offset="95%" stopColor="#38bdf8" stopOpacity={0} />
            </linearGradient>
          </defs>
          <CartesianGrid
            strokeDasharray="3 3"
            stroke="rgba(255,255,255,0.05)"
            vertical={false}
          />
          <XAxis
            dataKey="rawTime"
            stroke="rgba(255,255,255,0.3)"
            style={{ fontSize: "10px" }}
            minTickGap={30}
            tickFormatter={(value) => {
              return new Date(value).toLocaleTimeString([], {
                hour: "2-digit",
                minute: "2-digit",
                second: "2-digit",
              });
            }}
          />
          <YAxis
            stroke="rgba(255,255,255,0.3)"
            style={{ fontSize: "10px" }}
            tickFormatter={(val) => `${val.toFixed(2)}%`}
          />
          <Tooltip
            contentStyle={{
              backgroundColor: "rgba(15, 23, 42, 0.9)",
              border: "1px solid rgba(56, 189, 248, 0.2)",
              borderRadius: "12px",
              color: "#fff",
              backdropFilter: "blur(8px)",
            }}
            content={({ active, payload }) => {
              if (active && payload && payload.length > 0) {
                const d = payload[0].payload;
                return (
                  <div className="p-2 text-xs">
                    <div className="font-bold border-b border-white/10 pb-1 mb-2 flex justify-between gap-4">
                      <span>{d.asset} Peak</span>
                      <span className="text-white/40">
                        {new Date(d.rawTime).toLocaleTimeString()}
                      </span>
                    </div>
                    <div className="space-y-1">
                      <div className="flex justify-between gap-4">
                        <span className="text-white/60">Profit (Net):</span>
                        <span
                          className={`font-mono ${d.profit >= 0 ? "text-green-400" : "text-red-400"}`}
                        >
                          {d.profit.toFixed(3)}%
                        </span>
                      </div>
                      <div className="flex justify-between gap-4">
                        <span className="text-white/60">Gross Spread:</span>
                        <span className="font-mono text-blue-400">
                          {d.grossProfit.toFixed(3)}%
                        </span>
                      </div>
                      <div className="flex justify-between gap-4 pt-1 border-t border-white/5 mt-1">
                        <span className="text-white/60">Route:</span>
                        <span>
                          {d.buyExchange} ➔ {d.sellExchange}
                        </span>
                      </div>
                    </div>
                  </div>
                );
              }
              return null;
            }}
          />
          <ReferenceLine
            y={0}
            stroke="rgba(255,255,255,0.2)"
            strokeDasharray="3 3"
            label={{
              value: "0%",
              fill: "rgba(255,255,255,0.2)",
              position: "right",
              fontSize: 10,
            }}
          />
          {threshold && (
            <ReferenceLine
              y={threshold}
              stroke="#f59e0b"
              strokeDasharray="5 5"
              label={{
                value: `Target: ${threshold}%`,
                fill: "#f59e0b",
                position: "insideTopRight",
                fontSize: 10,
                fontWeight: "bold",
              }}
            />
          )}
          <Area
            type="monotone"
            dataKey="profit"
            stroke="#38bdf8"
            strokeWidth={2}
            fillOpacity={1}
            fill="url(#colorProfit)"
            animationDuration={500}
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
};

export default ProfitChart;
