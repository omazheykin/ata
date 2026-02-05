import React from "react";

interface StatsPanelProps {
  totalOpportunities: number;
  averageProfit: number;
  bestProfit: number;
  totalVolume: number;
}

const StatsPanel: React.FC<StatsPanelProps> = ({
  totalOpportunities,
  averageProfit,
  bestProfit,
  totalVolume,
}) => {
  const stats = [
    {
      label: "Total Opportunities",
      value: totalOpportunities,
      icon: "📊",
      suffix: "",
    },
    {
      label: "Average Profit",
      value: averageProfit.toFixed(2),
      icon: "💰",
      suffix: "%",
    },
    {
      label: "Best Profit",
      value: bestProfit.toFixed(2),
      icon: "🚀",
      suffix: "%",
    },
    {
      label: "Total Volume",
      value: totalVolume.toFixed(2),
      icon: "📈",
      suffix: "",
    },
  ];

  return (
    <div className="grid grid-cols-2 gap-2">
      {stats.map((stat, index) => (
        <div
          key={index}
          className="glass rounded-xl p-4 card-hover animate-fade-in"
          style={{ animationDelay: `${index * 100}ms` }}
        >
          <div className="flex items-center justify-between mb-2">
            <span className="text-3xl">{stat.icon}</span>
            <div className="text-right">
              <p className="text-xs text-gray-400 mb-1">{stat.label}</p>
              <p className="text-2xl font-bold gradient-text">
                {stat.value}
                {stat.suffix}
              </p>
            </div>
          </div>
        </div>
      ))}
    </div>
  );
};

export default StatsPanel;
