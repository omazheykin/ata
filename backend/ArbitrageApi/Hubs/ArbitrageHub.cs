using Microsoft.AspNetCore.SignalR;

namespace ArbitrageApi.Hubs;

public class ArbitrageHub : Hub
{
    private static readonly HashSet<string> _connectedClients = new();

    public override async Task OnConnectedAsync()
    {
        _connectedClients.Add(Context.ConnectionId);
        Console.WriteLine($"Client connected: {Context.ConnectionId}. Total clients: {_connectedClients.Count}");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connectedClients.Remove(Context.ConnectionId);
        Console.WriteLine($"Client disconnected: {Context.ConnectionId}. Total clients: {_connectedClients.Count}");
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SubscribeToOpportunities()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "OpportunitySubscribers");
        Console.WriteLine($"Client {Context.ConnectionId} subscribed to opportunities");
    }

    public async Task UnsubscribeFromOpportunities()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "OpportunitySubscribers");
        Console.WriteLine($"Client {Context.ConnectionId} unsubscribed from opportunities");
    }

    public static int GetConnectedClientsCount() => _connectedClients.Count;
}
