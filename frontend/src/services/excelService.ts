import * as XLSX from 'xlsx';
import type { ArbitrageEvent } from '../types/types';

export const excelService = {
  exportEventsToExcel: (events: ArbitrageEvent[], filename: string) => {
    // 1. Prepare data for Excel
    const data = events.map(event => ({
      'Time': new Date(event.timestamp).toLocaleString(),
      'Pair': event.pair,
      'Direction': event.direction,
      'Spread %': (event.spreadPercent ?? (event.spread * 100)).toFixed(3),
      'Depth Buy': event.depthBuy.toFixed(2),
      'Depth Sell': event.depthSell.toFixed(2),
      'ID': event.id
    }));

    // 2. Create worksheet
    const worksheet = XLSX.utils.json_to_sheet(data);

    // 3. Create workbook
    const workbook = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(workbook, worksheet, 'Arbitrage Events');

    // 4. Generate file and trigger download
    XLSX.writeFile(workbook, filename);
  }
};
