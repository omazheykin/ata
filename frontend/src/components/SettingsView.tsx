import React, { useState, useEffect } from "react";
import { apiService } from "../services/apiService";
import type { AppState } from "../types/types";

interface SettingsViewProps {
  isOpen: boolean;
  onClose: () => void;
}

const SettingsView: React.FC<SettingsViewProps> = ({ isOpen, onClose }) => {
  const [state, setState] = useState<AppState | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [newPair, setNewPair] = useState("");
  const [newThreshold, setNewThreshold] = useState("0.1");

  useEffect(() => {
    if (isOpen) {
      fetchSettings();
    }
  }, [isOpen]);

  const fetchSettings = async () => {
    try {
      setLoading(true);
      const data = await apiService.getFullState();
      setState(data);
    } catch (error) {
      console.error("Error fetching settings:", error);
    } finally {
      setLoading(false);
    }
  };

  const handleUpdateThreshold = (pair: string, value: string) => {
    if (!state) return;
    const numValue = parseFloat(value);
    if (isNaN(numValue)) return;

    setState({
      ...state,
      pairThresholds: {
        ...state.pairThresholds,
        [pair]: numValue,
      },
    });
  };

  const handleRemovePair = (pair: string) => {
    if (!state) return;
    const newThresholds = { ...state.pairThresholds };
    delete newThresholds[pair];
    setState({
      ...state,
      pairThresholds: newThresholds,
    });
  };

  const handleAddPair = () => {
    if (!state || !newPair) return;
    const numValue = parseFloat(newThreshold);
    if (isNaN(numValue)) return;

    setState({
      ...state,
      pairThresholds: {
        ...state.pairThresholds,
        [newPair.toUpperCase()]: numValue,
      },
    });
    setNewPair("");
  };

  const handleSave = async () => {
    if (!state) return;
    try {
      setSaving(true);
      await apiService.setPairThresholds(state.pairThresholds);
      await apiService.setSafeMultiplier(state.safeBalanceMultiplier);
      await apiService.setUseTakerFees(state.useTakerFees);
      onClose();
    } catch (error) {
      console.error("Error saving settings:", error);
    } finally {
      setSaving(false);
    }
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-black/60 backdrop-blur-md z-[100] flex items-center justify-center p-4">
      <div className="glass max-w-2xl w-full rounded-2xl border border-white/10 shadow-2xl animate-fade-in flex flex-col max-h-[90vh]">
        {/* Header */}
        <div className="p-6 border-b border-white/5 flex justify-between items-center">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg bg-blue-500/20 text-blue-400">
              <svg
                className="w-6 h-6"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={2}
                  d="M12 6V4m0 2a2 2 0 100 4m0-4a2 2 0 110 4m-6 8a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4m6 6v10m6-2a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4"
                />
              </svg>
            </div>
            <h2 className="text-xl font-bold">Safety Settings</h2>
          </div>
          <button
            onClick={onClose}
            className="p-2 hover:bg-white/5 rounded-lg transition-colors"
          >
            <svg
              className="w-6 h-6"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M6 18L18 6M6 6l12 12"
              />
            </svg>
          </button>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto p-6 space-y-8">
          {loading ? (
            <div className="flex justify-center p-12">
              <div className="w-8 h-8 border-4 border-blue-500/30 border-t-blue-500 rounded-full animate-spin"></div>
            </div>
          ) : (
            state && (
              <>
                {/* Risk Management */}
                <section>
                  <h3 className="text-sm font-black text-gray-500 uppercase tracking-widest mb-4">
                    Risk Management
                  </h3>
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                    <div className="glass p-4 rounded-xl border border-white/5">
                      <label className="block text-xs font-bold text-gray-400 mb-2 uppercase">
                        Safe Balance Multiplier
                      </label>
                      <div className="flex items-center gap-3">
                        <input
                          type="range"
                          min="0.1"
                          max="1.0"
                          step="0.1"
                          value={state.safeBalanceMultiplier}
                          onChange={(e) =>
                            setState({
                              ...state,
                              safeBalanceMultiplier: parseFloat(e.target.value),
                            })
                          }
                          className="flex-1 h-1.5 bg-white/10 rounded-lg appearance-none cursor-pointer accent-blue-500"
                        />
                        <span className="text-sm font-black text-blue-400 w-12 text-right">
                          {(state.safeBalanceMultiplier * 100).toFixed(0)}%
                        </span>
                      </div>
                      <p className="text-[10px] text-gray-500 mt-2 italic">
                        Limits max trade size to % of free balance.
                      </p>
                    </div>

                    <div className="glass p-4 rounded-xl border border-white/5">
                      <label className="block text-xs font-bold text-gray-400 mb-2 uppercase">
                        Fee Calculation Mode
                      </label>
                      <div className="flex p-1 bg-black/20 rounded-lg border border-white/5">
                        <button
                          onClick={() =>
                            setState({ ...state, useTakerFees: true })
                          }
                          className={`flex-1 py-1.5 text-[10px] font-black uppercase rounded-md transition-all ${state.useTakerFees ? "bg-blue-500 text-white shadow-lg shadow-blue-500/20" : "text-gray-500 hover:text-gray-300"}`}
                        >
                          Pessimistic (Taker)
                        </button>
                        <button
                          onClick={() =>
                            setState({ ...state, useTakerFees: false })
                          }
                          className={`flex-1 py-1.5 text-[10px] font-black uppercase rounded-md transition-all ${!state.useTakerFees ? "bg-orange-500 text-white shadow-lg shadow-orange-500/20" : "text-gray-500 hover:text-gray-300"}`}
                        >
                          Optimistic (Maker)
                        </button>
                      </div>
                      <p className="text-[10px] text-gray-400 mt-2 italic text-center">
                        {state.useTakerFees
                          ? "Uses market order fees (Safer)"
                          : "Uses limit order fees (Riskier)"}
                      </p>
                    </div>
                  </div>
                </section>

                {/* Pair Specific Thresholds */}
                <section>
                  <div className="flex justify-between items-center mb-4">
                    <h3 className="text-sm font-black text-gray-500 uppercase tracking-widest">
                      Asset Thresholds
                    </h3>
                    <span className="text-[10px] text-gray-500 uppercase">
                      Override global {state.minProfitThreshold}%
                    </span>
                  </div>

                  <div className="space-y-2">
                    {Object.entries(state.pairThresholds).map(
                      ([pair, threshold]) => (
                        <div
                          key={pair}
                          className="glass p-3 rounded-xl border border-white/5 flex items-center justify-between group"
                        >
                          <div className="flex items-center gap-3">
                            <div className="w-8 h-8 rounded-lg bg-white/5 flex items-center justify-center text-xs font-bold text-white uppercase">
                              {pair.substring(0, 3)}
                            </div>
                            <span className="text-sm font-bold text-gray-300">
                              {pair}
                            </span>
                          </div>
                          <div className="flex items-center gap-4">
                            <div className="flex items-center gap-2 bg-black/20 px-2 py-1 rounded-lg border border-white/5">
                              <input
                                type="number"
                                step="0.01"
                                value={threshold}
                                onChange={(e) =>
                                  handleUpdateThreshold(pair, e.target.value)
                                }
                                className="bg-transparent text-sm font-bold text-blue-400 w-16 outline-none text-right"
                              />
                              <span className="text-xs text-gray-500 font-bold">
                                %
                              </span>
                            </div>
                            <button
                              onClick={() => handleRemovePair(pair)}
                              className="p-1.5 text-gray-500 hover:text-red-400 transition-colors opacity-0 group-hover:opacity-100"
                            >
                              <svg
                                className="w-4 h-4"
                                fill="none"
                                stroke="currentColor"
                                viewBox="0 0 24 24"
                              >
                                <path
                                  strokeLinecap="round"
                                  strokeLinejoin="round"
                                  strokeWidth={2}
                                  d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"
                                />
                              </svg>
                            </button>
                          </div>
                        </div>
                      ),
                    )}

                    {/* Add New Pair */}
                    <div className="pt-4 mt-4 border-t border-white/5">
                      <div className="flex gap-2">
                        <input
                          type="text"
                          placeholder="BTCUSDT"
                          value={newPair}
                          onChange={(e) =>
                            setNewPair(e.target.value.toUpperCase())
                          }
                          className="flex-1 bg-white/5 border border-white/10 rounded-xl px-4 py-2 text-sm text-white placeholder:text-gray-600 outline-none focus:border-blue-500/50 transition-all font-bold"
                        />
                        <div className="w-24 bg-white/5 border border-white/10 rounded-xl px-2 py-2 flex items-center gap-1">
                          <input
                            type="number"
                            step="0.01"
                            value={newThreshold}
                            onChange={(e) => setNewThreshold(e.target.value)}
                            className="w-full bg-transparent text-sm font-bold text-blue-400 outline-none text-right"
                          />
                          <span className="text-[10px] text-gray-500 font-bold">
                            %
                          </span>
                        </div>
                        <button
                          onClick={handleAddPair}
                          disabled={!newPair}
                          className="bg-blue-500 hover:bg-blue-600 disabled:opacity-50 disabled:hover:bg-blue-500 text-white px-4 py-2 rounded-xl text-xs font-black transition-all active:scale-95 shadow-lg shadow-blue-500/20 uppercase"
                        >
                          Add
                        </button>
                      </div>
                    </div>
                  </div>
                </section>
              </>
            )
          )}
        </div>

        {/* Footer */}
        <div className="p-6 border-t border-white/5 flex gap-3">
          <button
            onClick={onClose}
            className="flex-1 py-3 bg-white/5 hover:bg-white/10 rounded-xl text-xs font-black uppercase transition-all tracking-widest border border-white/5"
          >
            Cancel
          </button>
          <button
            onClick={handleSave}
            disabled={saving || !state}
            className="flex-1 py-3 bg-gradient-to-r from-blue-500 to-purple-600 hover:scale-[1.02] active:scale-[0.98] rounded-xl text-xs font-black uppercase transition-all tracking-widest shadow-xl shadow-blue-500/20 disabled:opacity-50"
          >
            {saving ? "Saving..." : "Save Settings"}
          </button>
        </div>
      </div>
    </div>
  );
};

export default SettingsView;
