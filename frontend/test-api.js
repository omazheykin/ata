// Test the API endpoint directly
import { apiService } from './src/services/apiService';

async function testAPI() {
    try {
        console.log("Testing API endpoint...");
        const events = await apiService.getEventsByPair("POLUSDT");
        console.log("Events received:", events);
        console.log("Events count:", events.length);
        console.log("First event:", events[0]);
    } catch (error) {
        console.error("Error:", error);
    }
}

testAPI();
