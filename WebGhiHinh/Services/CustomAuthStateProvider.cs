using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using System.Security.Claims;
using System.Text.Json;

namespace WebGhiHinh.Services
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private readonly IJSRuntime _js;

        // 👇 BIẾN NÀY QUAN TRỌNG: Lưu giữ người dùng hiện tại trong RAM
        private ClaimsPrincipal _currentUser = new ClaimsPrincipal(new ClaimsIdentity());

        public CustomAuthStateProvider(IJSRuntime js)
        {
            _js = js;
        }

        // 1. Hàm này được gọi mỗi khi chuyển trang để kiểm tra quyền
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            // Trả về người dùng đang lưu trong biến _currentUser thay vì luôn trả về Anonymous
            return Task.FromResult(new AuthenticationState(_currentUser));
        }

        // 2. Load token từ LocalStorage (Dùng khi F5 trang)
        public async Task LoadUserFromLocalStorage()
        {
            try
            {
                var token = await _js.InvokeAsync<string>("auth.get");

                if (!string.IsNullOrEmpty(token))
                {
                    // Giải mã token và cập nhật biến _currentUser
                    _currentUser = BuildUserFromToken(token);
                }
                else
                {
                    // Nếu không có token -> Về ẩn danh
                    _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
                }
            }
            catch
            {
                // Lỗi JS (do prerender) -> Bỏ qua
            }

            // Thông báo cho toàn bộ App biết trạng thái đã thay đổi
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
        }

        // 3. Đăng nhập (Gọi từ LoginPage)
        public async Task MarkUserAsAuthenticated(string token)
        {
            // Cập nhật biến _currentUser ngay lập tức
            _currentUser = BuildUserFromToken(token);

            // Thông báo cập nhật giao diện
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));

            // Lưu xuống LocalStorage (phòng hờ code JS bên ngoài chưa chạy)
            try { await _js.InvokeVoidAsync("auth.set", token); } catch { }
        }

        // 4. Đăng xuất
        public async Task MarkUserAsLoggedOut()
        {
            _currentUser = new ClaimsPrincipal(new ClaimsIdentity());

            try { await _js.InvokeVoidAsync("auth.clear"); } catch { }

            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
        }

        // ==========================
        // ===== Helper Methods =====
        // ==========================

        private ClaimsPrincipal BuildUserFromToken(string token)
        {
            var claims = ParseClaimsFromJwt(token);
            var identity = new ClaimsIdentity(claims, "jwt"); // "jwt" là authentication type, bắt buộc phải có
            return new ClaimsPrincipal(identity);
        }

        private IEnumerable<Claim> ParseClaimsFromJwt(string token)
        {
            var claims = new List<Claim>();
            var payload = token.Split('.')[1];
            var jsonBytes = ParseBase64WithoutPadding(payload);
            var keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonBytes);

            foreach (var kvp in keyValuePairs)
            {
                string key = kvp.Key.ToLower();
                object value = kvp.Value;

                // Fix Role
                if (key == "role") key = ClaimTypes.Role;
                else if (key == "nameid") key = ClaimTypes.NameIdentifier;
                else if (key == "unique_name" || key == "name") key = ClaimTypes.Name;

                // Xử lý trường hợp Role là mảng JSON []
                if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in element.EnumerateArray())
                    {
                        claims.Add(new Claim(key, item.ToString()));
                    }
                }
                else
                {
                    claims.Add(new Claim(key, value?.ToString() ?? ""));
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