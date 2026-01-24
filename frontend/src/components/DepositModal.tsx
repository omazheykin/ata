import React, { useState, useEffect } from "react";

interface DepositModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (amount: number, exchange: string, asset: string) => void;
  initialExchange?: string;
}

type Step = "amount" | "method" | "blik" | "confirming" | "success";

const DepositModal: React.FC<DepositModalProps> = ({
  isOpen,
  onClose,
  onSuccess,
  initialExchange,
}) => {
  const [step, setStep] = useState<Step>("amount");
  const [amount, setAmount] = useState<string>("100");
  const [exchange, setExchange] = useState<string>(
    initialExchange || "Binance",
  );
  const [currency, setCurrency] = useState<string>("USD");
  const [blikCode, setBlikCode] = useState<string[]>(["", "", "", "", "", ""]);

  useEffect(() => {
    if (isOpen) {
      setStep("amount");
      setCurrency("USD");
      setBlikCode(["", "", "", "", "", ""]);
      if (initialExchange) {
        setExchange(initialExchange);
      }
    }
  }, [isOpen, initialExchange]);

  const handleBlikChange = (index: number, value: string) => {
    if (value.length > 1) value = value[0];
    if (!/^\d*$/.test(value)) return;

    const newCode = [...blikCode];
    newCode[index] = value;
    setBlikCode(newCode);

    // Auto-focus next input
    if (value && index < 5) {
      const nextInput = document.getElementById(`blik-${index + 1}`);
      nextInput?.focus();
    }

    // If all filled, move to confirming
    if (newCode.every((digit) => digit !== "") && index === 5) {
      setTimeout(() => setStep("confirming"), 500);
    }
  };

  const handleKeyDown = (index: number, e: React.KeyboardEvent) => {
    if (e.key === "Backspace" && !blikCode[index] && index > 0) {
      const prevInput = document.getElementById(`blik-${index - 1}`);
      prevInput?.focus();
    }
  };

  useEffect(() => {
    if (step === "confirming") {
      const timer = setTimeout(() => {
        setStep("success");
      }, 3000);
      return () => clearTimeout(timer);
    }
  }, [step]);

  const handleFinish = () => {
    onSuccess(Number(amount), exchange, currency);
    onClose();
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center p-4">
      {/* Backdrop */}
      <div
        className="absolute inset-0 bg-black/80 backdrop-blur-md transition-opacity"
        onClick={onClose}
      />

      {/* Modal Content */}
      <div className="relative bg-[#0f172a] border border-white/10 rounded-3xl w-full max-w-md overflow-hidden shadow-2xl">
        {/* Header */}
        <div className="p-6 border-b border-white/5 flex justify-between items-center">
          <h3 className="text-xl font-bold text-white">Deposit Funds</h3>
          <button
            onClick={onClose}
            className="text-gray-400 hover:text-white transition-colors"
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

        <div className="p-8">
          {step === "amount" && (
            <div className="space-y-6 animate-in fade-in slide-in-from-bottom-4 duration-300">
              <div>
                <label className="block text-sm font-medium text-gray-400 mb-3">
                  Select Exchange
                </label>
                <div className="grid grid-cols-2 gap-3">
                  {["Binance", "Coinbase"].map((ex) => (
                    <button
                      key={ex}
                      onClick={() => setExchange(ex)}
                      className={`py-3 rounded-xl border transition-all ${
                        exchange === ex
                          ? "bg-primary-500/10 border-primary-500 text-white shadow-lg shadow-primary-500/10"
                          : "bg-white/5 border-white/10 text-gray-400 hover:bg-white/10"
                      }`}
                    >
                      {ex}
                    </button>
                  ))}
                </div>
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-400 mb-3">
                  Select Currency
                </label>
                <div className="grid grid-cols-3 gap-3">
                  {["USD", "BTC", "ETH"].map((curr) => (
                    <button
                      key={curr}
                      onClick={() => setCurrency(curr)}
                      className={`py-3 rounded-xl border transition-all ${
                        currency === curr
                          ? "bg-primary-500/10 border-primary-500 text-white shadow-lg shadow-primary-500/10"
                          : "bg-white/5 border-white/10 text-gray-400 hover:bg-white/10"
                      }`}
                    >
                      {curr}
                    </button>
                  ))}
                </div>
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-400 mb-3">
                  Amount ({currency})
                </label>
                <div className="relative">
                  <span className="absolute left-4 top-1/2 -translate-y-1/2 text-gray-400">
                    {currency === "USD" ? "$" : currency === "BTC" ? "₿" : "Ξ"}
                  </span>
                  <input
                    type="number"
                    value={amount}
                    onChange={(e) => setAmount(e.target.value)}
                    className="w-full bg-white/5 border border-white/10 rounded-xl py-4 pl-10 pr-4 text-2xl font-bold text-white focus:ring-2 focus:ring-primary-500 outline-none transition-all"
                    autoFocus
                  />
                </div>
              </div>

              <button
                onClick={() => setStep("method")}
                className="w-full py-4 bg-primary-500 hover:bg-primary-400 text-white font-bold rounded-xl shadow-lg shadow-primary-500/25 transition-all"
              >
                Continue
              </button>
            </div>
          )}

          {step === "method" && (
            <div className="space-y-4 animate-in fade-in slide-in-from-right-4 duration-300">
              <p className="text-sm text-gray-400 mb-2">
                Choose payment method
              </p>

              <button
                onClick={() => setStep("blik")}
                className="w-full p-4 bg-white/5 border border-white/10 rounded-2xl flex items-center justify-between group hover:bg-white/10 hover:border-white/20 transition-all"
              >
                <div className="flex items-center gap-4">
                  <div className="w-12 h-12 rounded-xl bg-gradient-to-br from-[#eb008b] to-[#ff009d] flex items-center justify-center text-white font-black italic text-xs">
                    BLIK
                  </div>
                  <div className="text-left">
                    <p className="font-bold text-white">BLIK</p>
                    <p className="text-xs text-gray-500">
                      Instant deposit via mobile app
                    </p>
                  </div>
                </div>
                <svg
                  className="w-5 h-5 text-gray-600 group-hover:text-gray-400 transition-colors"
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth={2}
                    d="M9 5l7 7-7 7"
                  />
                </svg>
              </button>

              <button className="w-full p-4 bg-white/5 border border-white/10 rounded-2xl flex items-center justify-between opacity-50 cursor-not-allowed">
                <div className="flex items-center gap-4">
                  <div className="w-12 h-12 rounded-xl bg-blue-500/20 flex items-center justify-center text-blue-400">
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
                        d="M3 10h18M7 15h1m4 0h1m-7 4h12a3 3 0 003-3V8a3 3 0 00-3-3H6a3 3 0 00-3 3v8a3 3 0 003 3z"
                      />
                    </svg>
                  </div>
                  <div className="text-left">
                    <p className="font-bold text-white">Credit Card</p>
                    <p className="text-xs text-gray-500">Visa, Mastercard</p>
                  </div>
                </div>
                <span className="text-[10px] bg-white/5 px-2 py-1 rounded text-gray-500 uppercase">
                  Soon
                </span>
              </button>

              <button
                onClick={() => setStep("amount")}
                className="w-full py-3 text-gray-500 hover:text-white transition-colors text-sm"
              >
                Back to amount
              </button>
            </div>
          )}

          {step === "blik" && (
            <div className="space-y-8 animate-in fade-in slide-in-from-right-4 duration-300 text-center">
              <div>
                <div className="w-16 h-16 rounded-2xl bg-gradient-to-br from-[#eb008b] to-[#ff009d] flex items-center justify-center text-white font-black italic text-xl mx-auto mb-4 shadow-lg shadow-[#eb008b]/20">
                  BLIK
                </div>
                <h4 className="text-xl font-bold text-white">
                  Enter BLIK Code
                </h4>
                <p className="text-sm text-gray-400 mt-2">
                  Generate a 6-digit code in your banking app
                </p>
              </div>

              <div className="flex justify-center gap-2">
                {blikCode.map((digit, idx) => (
                  <input
                    key={idx}
                    id={`blik-${idx}`}
                    type="text"
                    inputMode="numeric"
                    value={digit}
                    onChange={(e) => handleBlikChange(idx, e.target.value)}
                    onKeyDown={(e) => handleKeyDown(idx, e)}
                    className="w-12 h-16 bg-white/5 border border-white/10 rounded-xl text-center text-2xl font-bold text-white focus:ring-2 focus:ring-[#eb008b] focus:border-transparent outline-none transition-all"
                  />
                ))}
              </div>

              <div className="space-y-3">
                <button
                  disabled={!blikCode.every((d) => d !== "")}
                  onClick={() => setStep("confirming")}
                  className={`w-full py-4 font-bold rounded-xl transition-all ${
                    blikCode.every((d) => d !== "")
                      ? "bg-[#eb008b] hover:bg-[#d1007c] text-white shadow-lg shadow-[#eb008b]/25"
                      : "bg-white/5 text-gray-600 cursor-not-allowed"
                  }`}
                >
                  Confirm Payment
                </button>
                <button
                  onClick={() => setStep("method")}
                  className="text-gray-500 hover:text-white transition-colors text-sm"
                >
                  Change payment method
                </button>
              </div>
            </div>
          )}

          {step === "confirming" && (
            <div className="py-12 text-center space-y-6 animate-in fade-in duration-500">
              <div className="relative w-24 h-24 mx-auto">
                <div className="absolute inset-0 border-4 border-[#eb008b]/20 rounded-full" />
                <div className="absolute inset-0 border-4 border-[#eb008b] rounded-full border-t-transparent animate-spin" />
                <div className="absolute inset-0 flex items-center justify-center">
                  <svg
                    className="w-10 h-10 text-[#eb008b]"
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                  >
                    <path
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      strokeWidth={2}
                      d="M12 18h.01M8 21h8a2 2 0 002-2V5a2 2 0 00-2-2H8a2 2 0 00-2 2v14a2 2 0 002 2z"
                    />
                  </svg>
                </div>
              </div>
              <div>
                <h4 className="text-xl font-bold text-white">Confirm in App</h4>
                <p className="text-sm text-gray-400 mt-2">
                  Please open your banking app and confirm the payment of{" "}
                  <span className="text-white font-bold">
                    {currency === "USD" ? "$" : ""}
                    {amount}
                    {currency !== "USD" ? ` ${currency}` : ""}
                  </span>
                </p>
              </div>
              <div className="pt-4">
                <p className="text-[10px] text-gray-600 uppercase tracking-widest animate-pulse">
                  Waiting for authorization...
                </p>
              </div>
            </div>
          )}

          {step === "success" && (
            <div className="py-8 text-center space-y-6 animate-in zoom-in duration-500">
              <div className="w-24 h-24 bg-green-500/20 rounded-full flex items-center justify-center mx-auto text-green-500">
                <svg
                  className="w-12 h-12"
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth={3}
                    d="M5 13l4 4L19 7"
                  />
                </svg>
              </div>
              <div>
                <h4 className="text-2xl font-bold text-white">
                  Deposit Successful!
                </h4>
                <p className="text-sm text-gray-400 mt-2">
                  <span className="text-white font-bold">
                    {currency === "USD" ? "$" : ""}
                    {amount}
                    {currency !== "USD" ? ` ${currency}` : ""}
                  </span>{" "}
                  has been added to your{" "}
                  <span className="text-white font-bold">{exchange}</span>{" "}
                  account.
                </p>
              </div>
              <button
                onClick={handleFinish}
                className="w-full py-4 bg-green-500 hover:bg-green-400 text-white font-bold rounded-xl shadow-lg shadow-green-500/25 transition-all"
              >
                Done
              </button>
            </div>
          )}

          {/* Persistent Allocation Info (except on success) */}
          {step !== "success" && (
            <div className="mt-8 p-3 rounded-xl bg-primary-500/5 border border-primary-500/10 flex gap-3 items-start animate-in fade-in duration-500">
              <div className="mt-0.5">
                <svg
                  className="w-4 h-4 text-primary-400"
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth={2}
                    d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
                  />
                </svg>
              </div>
              <p className="text-[12px] leading-relaxed text-gray-400">
                Funds will be automatically allocated to your{" "}
                <span className="text-primary-400 font-bold">{exchange}</span>{" "}
                account once the transaction is confirmed.
              </p>
            </div>
          )}
        </div>

        {/* Footer Info */}
        {step !== "success" && (
          <div className="p-4 bg-black/20 text-center">
            <p className="text-[10px] text-gray-500 flex items-center justify-center gap-1">
              <svg
                className="w-3 h-3"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={2}
                  d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z"
                />
              </svg>
              Secure payment processed by ArbitragePay
            </p>
          </div>
        )}
      </div>
    </div>
  );
};

export default DepositModal;
