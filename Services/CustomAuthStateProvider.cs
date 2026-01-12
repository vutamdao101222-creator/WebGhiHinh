using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using System.Security.Claims;

namespace WebGhiHinh.Services
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly AuthenticationState _anonymous = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

        public CustomAuthStateProvider(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
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

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, username),
                    new Claim("sub", username)
                };

                if (!string.IsNullOrEmpty(role))
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }

                var identity = new ClaimsIdentity(claims, "jwt");
                var user = new ClaimsPrincipal(identity);

                return new AuthenticationState(user);
            }
            catch
            {
                return _anonymous;
            }
        }

        public void MarkUserAsAuthenticated(string username, string token, string role)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, role)
            };

            var identity = new ClaimsIdentity(claims, "jwt");
            var user = new ClaimsPrincipal(identity);

            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
        }

        public async Task MarkUserAsLoggedOut()
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.clear");
            NotifyAuthenticationStateChanged(Task.FromResult(_anonymous));
        }

        // 👇👇👇 ĐÂY LÀ HÀM BẠN ĐANG THIẾU 👇👇👇
        public async Task LoadUserFromLocalStorage()
        {
            // Hàm này buộc Provider chạy lại GetAuthenticationStateAsync
            // Giúp UI cập nhật ngay lập tức sau khi F5 hoặc Login
            var state = await GetAuthenticationStateAsync();
            NotifyAuthenticationStateChanged(Task.FromResult(state));
        }
    }
}