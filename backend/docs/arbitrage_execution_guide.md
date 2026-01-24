# Arbitrage Execution Guide

This guide describes the step-by-step process for executing a successful arbitrage trade using this platform.

## 1. Preparation: Maintaining Balances

To execute an arbitrage trade instantly, you must maintain "means" on both exchanges.

*   **Quote Asset (e.g., USD/USDT)**: Required on the **Buy Exchange** to purchase the asset.
*   **Base Asset (e.g., BTC/ETH)**: Required on the **Sell Exchange** to sell it immediately.

> [!IMPORTANT]
> You are not moving the asset from Exchange A to Exchange B during the trade. That would take too long (network confirmations) and the opportunity would vanish. Instead, you perform two simultaneous trades.

## 2. Detection

The system monitors the order books of Binance and Coinbase. An opportunity is detected when:
`Sell Price (Exchange B) > Buy Price (Exchange A) + Fees (Both)`

## 3. Execution Steps

When you click "Execute Trade" (simulated for now), the following happens:

1.  **Balance Check**: The system verifies you have enough Quote Asset on Exchange A and enough Base Asset on Exchange B.
2.  **Simultaneous Orders**:
    *   **Exchange A**: A "Market Buy" order is placed for the asset.
    *   **Exchange B**: A "Market Sell" order is placed for the same amount of the asset.
3.  **Confirmation**: The system waits for both exchanges to confirm the fills.

## 4. Rebalancing (The "Reset")

After the trade, your balances are "unbalanced":
*   You have more Base Asset on Exchange A.
*   You have more Quote Asset on Exchange B.

To prepare for the next trade, you must eventually **Rebalance**:
1.  Transfer the Base Asset from Exchange A to Exchange B.
2.  Transfer the Quote Asset from Exchange B to Exchange A.

## 5. Risks to Consider

*   **Slippage**: Market prices can change between detection and execution.
*   **Partial Fills**: One side of the trade might fill while the other doesn't.
*   **Transfer Fees**: Moving funds between exchanges costs money and must be factored into long-term profitability.
*   **API Latency**: The faster the connection, the lower the risk of missing the spread.
