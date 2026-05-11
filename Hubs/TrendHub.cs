using Microsoft.AspNetCore.SignalR;

namespace WebAPIDateTrendSelector.Hubs
{
    public class TrendHub : Hub
    {
        // Permet au JS de récupérer le connectionId via invoke
        public string GetConnectionId()
        {
            return Context.ConnectionId;
        }

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
        }
    }
}