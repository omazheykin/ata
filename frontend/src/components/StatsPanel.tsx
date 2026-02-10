import React from "react";

interface StatsPanelProps {
  totalOpportunities: number;
  averageProfit: number;
  bestProfit: number;
  totalVolume: number;
  volatilityScore?: number;
  efficiencyScore?: number;
}

const StatsPanel: React.FC<StatsPanelProps> = ({
  totalOpportunities,
  averageProfit,
  bestProfit,
  totalVolume,
  volatilityScore = 0,
  efficiencyScore = 0,
}) => {
  const stats = [
    {
      label: "Opportunities",
      value: totalOpportunities,
      icon: "📊",
      tip: "Total arbitrage opportunities detected this session",
    },
    {
      label: "Avg Profit",
      value: averageProfit.toFixed(2) + "%",
      icon: "💰",
      tip: "Average net profit percentage across all opportunities",
    },
    {
      label: "Volatility",
      value: (volatilityScore * 100).toFixed(1) + "%",
      icon: "🌪️",
      tip: "Market volatility score based on price fluctuations",
    },
    {
      label: "Efficiency",
      value: (efficiencyScore * 100).toFixed(1) + "%",
      icon: "⚡",
      tip: "System execution efficiency and success rate",
    },
    {
      label: "Volume",
      value: totalVolume.toFixed(2),
      icon: "📈",
      tip: "Total trading volume detected (base currency)",
    },
    {
      label: "Best Trade",
      value: bestProfit.toFixed(2) + "%",
      icon: "🚀",
      tip: "Highest profit percentage recorded this session",
    },
  ];

  return (
    <div className="grid grid-cols-2 sm:grid-cols-3 gap-2">
      {stats.map((stat, index) => (
        <div
          key={index}
          className="glass rounded-xl px-2 py-1.5 border border-white/5 transition-all hover:bg-white/10 group cursor-help animate-fade-in"
          style={{ animationDelay: `${index * 50}ms` }}
          title={stat.tip}
        >
          <div className="flex items-center gap-2">
            <span className="text-base group-hover:scale-110 transition-transform">
              {stat.icon}
            </span>
            <div className="min-w-0">
              <p className="text-[8px] text-gray-500 uppercase font-black tracking-tighter leading-none mb-0.5">
                {stat.label}
              </p>
              <p className="text-xs font-bold text-white truncate">
                {stat.value}
              </p>
            </div>
          </div>
        </div>
      ))}
    </div>
  );
};

export default StatsPanel;
