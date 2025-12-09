using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;
using System.Security.Claims;
using System.Text.Json;

namespace WebGhiHinh.Services
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private readonly IJSRuntime _js;
        private readonly ProtectedSessionStorage _sessionStorage;
        private readonly HttpClient _http;

        public CustomAuthStateProvider(IJSRuntime js, ProtectedSessionStorage sessionStorage, HttpClient http)
        {
            _js = js;
            _sessionStorage = sessionStorage;
            _http = http;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            // 1. Cố gắng lấy token từ LocalStorage (Client)
            string token = await GetTokenAsync();

            var identity = new ClaimsIdentity();
            _http.DefaultRequestHeaders.Authorization = null;

            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    // 2. Parse Token lấy Claims
                    var claims = ParseClaimsFromJwt(token);

                    // 3. QUAN TRỌNG: Tạo Identity với cấu hình Name/Role type chuẩn
                    // Tham số thứ 3: Key dùng làm Name (User.Identity.Name)
                    // Tham số thứ 4: Key dùng làm Role (User.IsInRole) -> Fix lỗi [Authorize]
                    identity = new ClaimsIdentity(claims, "JwtAuth", "name", "role");

                    _http.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }
                catch
                {
                    // Token lỗi hoặc hết hạn -> Xóa
                    await _js.InvokeVoidAsync("localStorage.removeItem", "token");
                    identity = new ClaimsIdentity();
                }
            }

            var user = new ClaimsPrincipal(identity);
            return new AuthenticationState(user);
        }

        public async Task MarkUserAsAuthenticated(string token)
        {
            var claims = ParseClaimsFromJwt(token);

            // Fix lỗi tương tự khi Login nóng
            var identity = new ClaimsIdentity(claims, "JwtAuth", "name", "role");
            var user = new ClaimsPrincipal(identity);

            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
        }

        public async Task MarkUserAsLoggedOut()
        {
            await _js.InvokeVoidAsync("localStorage.removeItem", "token");
            await _js.InvokeVoidAsync("localStorage.removeItem", "username");
            await _js.InvokeVoidAsync("localStorage.removeItem", "role");

            var identity = new ClaimsIdentity();
            var user = new ClaimsPrincipal(identity);

            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
        }

        private async Task<string> GetTokenAsync()
        {
            try
            {
                // Vì Blazor Server Prerender không gọi được JS ngay lập tức, cần try-catch
                return await _js.InvokeAsync<string>("localStorage.getItem", "token");
            }
            catch
            {
                return null;
            }
        }

        public async Task LoadUserFromLocalStorage()
        {
            // Hàm này gọi từ OnAfterRenderAsync để refresh state khi F5
            var token = await GetTokenAsync();
            if (!string.IsNullOrEmpty(token))
            {
                await MarkUserAsAuthenticated(token);
            }
        }

        private IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
        {
            var claims = new List<Claim>();
            var payload = jwt.Split('.')[1];
            var jsonBytes = ParseBase64WithoutPadding(payload);
            var keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonBytes);

            if (keyValuePairs != null)
            {
                foreach (var kvp in keyValuePairs)
                {
                    // Xử lý trường hợp Role là mảng (nhiều quyền) hoặc đơn (1 quyền)
                    if (kvp.Value is JsonElement element && element.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in element.EnumerateArray())
                        {
                            claims.Add(new Claim(kvp.Key, item.ToString()));
                        }
                    }
                    else
                    {
                        claims.Add(new Claim(kvp.Key, kvp.Value.ToString()));
                    }
                }
            }

            return claims;
        }

        private byte[] ParseBase64WithoutPadding(string base64)
        {
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }
    }
}