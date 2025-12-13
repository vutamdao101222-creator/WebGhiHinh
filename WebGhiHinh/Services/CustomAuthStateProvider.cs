using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic; // Thêm dòng này

namespace WebGhiHinh.Services
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly AuthenticationState _anonymous;

        public CustomAuthStateProvider(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
            _anonymous = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                var token = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "token");

                if (string.IsNullOrEmpty(token))
                    return _anonymous;

                var username = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "username") ?? "User";
                var role = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "role") ?? "";

                // Tạo danh sách claims
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, username)
                };

                if (!string.IsNullOrEmpty(role))
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }

                var identity = new ClaimsIdentity(claims, "jwt");
                return new AuthenticationState(new ClaimsPrincipal(identity));
            }
            catch
            {
                return _anonymous;
            }
        }

        // ✅ ĐÃ SỬA: Thêm tham số tùy chọn (string? ... = null) để tránh lỗi thiếu tham số
        public Task MarkUserAsAuthenticated(string username, string? token = null, string? role = null)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, username)
            };

            if (!string.IsNullOrEmpty(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var identity = new ClaimsIdentity(claims, "jwt");
            var user = new ClaimsPrincipal(identity);

            // Báo cho app biết trạng thái đã thay đổi
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));

            return Task.CompletedTask;
        }

        public Task MarkUserAsLoggedOut()
        {
            NotifyAuthenticationStateChanged(Task.FromResult(_anonymous));
            return Task.CompletedTask;
        }

        public async Task LoadUserFromLocalStorage()
        {
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
            await Task.CompletedTask;
        }
    }
}