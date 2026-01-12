using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Security.Claims;
using WebGhiHinh.Data;

namespace WebGhiHinh.Services
{
    /// <summary>
    /// Auto release station when user disconnects (close tab, crash browser, lost network).
    /// Supports multi-tab by counting active circuits per user.
    /// </summary>
    public class AutoReleaseCircuitHandler : CircuitHandler
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly AuthenticationStateProvider _authProvider;

        // circuitId -> userId
        private static readonly ConcurrentDictionary<string, string> _circuitToUser = new();

        // userId -> active circuits count
        private static readonly ConcurrentDictionary<string, int> _userCircuitCounts = new();

        public AutoReleaseCircuitHandler(
            IServiceScopeFactory scopeFactory,
            AuthenticationStateProvider authProvider)
        {
            _scopeFactory = scopeFactory;
            _authProvider = authProvider;
        }

        public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            var userId = await GetUserIdAsync();
            if (string.IsNullOrWhiteSpace(userId))
                return;

            _circuitToUser[circuit.Id] = userId;
            _userCircuitCounts.AddOrUpdate(userId, 1, (_, old) => old + 1);
        }

        public override async Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            if (!_circuitToUser.TryRemove(circuit.Id, out var userId) || string.IsNullOrWhiteSpace(userId))
                return;

            int newCount = _userCircuitCounts.AddOrUpdate(userId, 0, (_, old) =>
            {
                var next = old - 1;
                return next < 0 ? 0 : next;
            });

            if (newCount <= 0)
            {
                _userCircuitCounts.TryRemove(userId, out _);
                await ReleaseStationsOfUserAsync(userId, cancellationToken);
            }
        }

        private async Task<string?> GetUserIdAsync()
        {
            try
            {
                var state = await _authProvider.GetAuthenticationStateAsync();
                var user = state.User;

                if (user?.Identity?.IsAuthenticated != true)
                    return null;

                return user.FindFirst("Id")?.Value
                    ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            }
            catch
            {
                return null;
            }
        }

        private async Task ReleaseStationsOfUserAsync(string userIdStr, CancellationToken ct)
        {
            if (!int.TryParse(userIdStr, out var userId))
                return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var stations = await db.Stations
                .Where(s => s.CurrentUserId == userId)
                .ToListAsync(ct);

            if (stations.Count == 0) return;

            foreach (var st in stations)
                st.CurrentUserId = null;

            await db.SaveChangesAsync(ct);
        }
    }
}
