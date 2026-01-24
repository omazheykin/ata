# Arbitrage Opportunity Software

A real-time arbitrage opportunity detection and monitoring system with .NET 10 ASP.NET Core backend and React + TypeScript frontend.

## Features

- 🚀 **Real-time Updates**: SignalR-powered live arbitrage opportunity streaming
- 📊 **Comprehensive Dashboard**: Modern, responsive UI with glassmorphism design
- 📈 **Data Visualization**: Interactive charts showing profit trends
- 💰 **Statistics Panel**: Key metrics at a glance
- ⚡ **Live Monitoring**: Continuous monitoring of multiple cryptocurrency exchanges
- 🎨 **Premium Design**: Dark theme with vibrant colors and smooth animations

## Technology Stack

### Backend
- .NET 10 ASP.NET Core Web API
- SignalR for real-time communication
- Background services for arbitrage detection
- RESTful API endpoints

### Frontend
- React 18 with TypeScript
- Vite for fast development
- TailwindCSS for styling
- Recharts for data visualization
- SignalR client for real-time updates

## Project Structure

```
ata/
├── backend/
│   └── ArbitrageApi/
│       ├── Controllers/      # REST API controllers
│       ├── Hubs/            # SignalR hubs
│       ├── Models/          # Data models
│       ├── Services/        # Background services
│       └── Program.cs       # Application entry point
└── frontend/
    └── src/
        ├── components/      # React components
        ├── services/        # API and SignalR services
        └── types/          # TypeScript type definitions
```

## Getting Started

### Prerequisites

- .NET 10 SDK
- Node.js 18+ and npm
- Modern web browser

### Backend Setup

1. Navigate to the backend directory:
   ```bash
   cd backend/ArbitrageApi
   ```

2. Build the project:
   ```bash
   dotnet build
   ```

3. Run the backend server:
   ```bash
   dotnet run
   ```

   The API will be available at:
   - HTTPS: `https://localhost:5001`
   - HTTP: `http://localhost:5000`
   - SignalR Hub: `https://localhost:5001/arbitrageHub`
   - Swagger UI: `https://localhost:5001/swagger` (development only)

### Frontend Setup

1. Navigate to the frontend directory:
   ```bash
   cd frontend
   ```

2. Install dependencies (if not already installed):
   ```bash
   npm install
   ```

3. Start the development server:
   ```bash
   npm run dev
   ```

   The application will be available at `http://localhost:5173`

## API Endpoints

### REST API

- `GET /api/arbitrage/recent` - Get recent arbitrage opportunities
- `GET /api/arbitrage/statistics` - Get statistics summary
- `GET /api/arbitrage/exchanges` - Get list of supported exchanges

### SignalR Hub

- **Hub URL**: `/arbitrageHub`
- **Event**: `ReceiveOpportunity` - Broadcasts new arbitrage opportunities in real-time

## Features in Detail

### Real-time Arbitrage Detection

The backend continuously generates arbitrage opportunities (simulated data) and broadcasts them to all connected clients via SignalR. Each opportunity includes:

- Asset (cryptocurrency)
- Buy exchange and price
- Sell exchange and price
- Profit percentage (after fees)
- Volume
- Timestamp

### Dashboard Components

1. **Connection Status**: Shows SignalR connection state with visual indicators
2. **Statistics Panel**: Displays total opportunities, average profit, best profit, and total volume
3. **Profit Chart**: Line chart showing profit trends over time
4. **Opportunity Cards**: Grid of recent opportunities with detailed information

## Development Notes

### Simulated Data

The current implementation uses simulated arbitrage data. To integrate with real exchanges:

1. Implement exchange API clients in the backend
2. Replace `ArbitrageDetectionService` logic with real price fetching
3. Add authentication for exchange APIs
4. Implement rate limiting and error handling

### CORS Configuration

The backend is configured to allow requests from:
- `http://localhost:5173` (Vite dev server)
- `http://localhost:3000` (alternative React dev server)

Update `Program.cs` to add additional origins as needed.

## Building for Production

### Backend

```bash
cd backend/ArbitrageApi
dotnet publish -c Release -o ./publish
```

### Frontend

```bash
cd frontend
npm run build
```

The production build will be in the `dist/` directory.

## License

This project is for demonstration purposes.

## Support

For issues or questions, please open an issue in the repository.
