using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer; // Cần thiết cho JWT
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens; // Cần thiết cho SecurityKey
using System.Text; // Cần thiết cho Encoding
using WebGhiHinh.Components;
using WebGhiHinh.Data;
using WebGhiHinh.Hubs;
using WebGhiHinh.Services;

var builder = WebApplication.CreateBuilder(args);

// ===============================
// 1. LOGGING
// ===============================
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// ===============================
// 2. DATABASE CONFIGURATION
// ===============================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new Exception("Missing connection string");

// 👇 QUAN TRỌNG: Đăng ký Factory cho Blazor Server (Fix lỗi treo loading)
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(connectionString), ServiceLifetime.Scoped);

// Đăng ký Context thường (Hỗ trợ Controller cũ nếu cần) 
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// ===============================
// 3. AUTHENTICATION (Đã sửa lỗi cú pháp)
// ===============================
builder.Services.AddAuthentication(options =>
{
    // Mặc định dùng Cookie cho Web App (Login người dùng)
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/account/logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    }) // 👈 KHÔNG CÓ DẤU CHẤM PHẨY Ở ĐÂY, ĐỂ NỐI TIẾP LỆNH
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false; // Chạy nội bộ tắt HTTPS cho đỡ lỗi
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            // ⚠️ CẤU HÌNH KHỚP VỚI WORKER SERVICE
            ValidIssuer = "http://localhost:5000",
            ValidAudience = "http://localhost:5000",
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("Chuoi_Bi_Mat_Nay_Phai_Dai_Hon_32_Ky_Tu_De_Dam_Bao_An_Toan_Cho_Token_!!!"))
        };
    }); // 👈 Dấu chấm phẩy kết thúc chuỗi lệnh nằm ở đây

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState(); // Cần thiết cho Blazor Auth
builder.Services.AddSingleton<QrDispatchService>();
string hubUrl = builder.Configuration["SignalRUrl"] ?? "http://localhost:5000/scanHub";
builder.Services.AddSingleton(sp => new SignalRClient(hubUrl));
// ===============================
// 4. MVC + SIGNALR + SWAGGER
// ===============================
builder.Services.AddControllers()
    .AddJsonOptions(opt =>
    {
        opt.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ===============================
// 5. REGISTER SERVICES
// ===============================
builder.Services.AddSingleton<FfmpegService>();
builder.Services.AddSingleton<SystemSettingsService>();
builder.Services.AddSingleton<QrDispatchService>(); // 👈 Thêm dòng này
// 👇 QUAN TRỌNG: StationService phải là SCOPED
builder.Services.AddHttpClient<StationService>();


var app = builder.Build();

// ===============================
// 6. MIDDLEWARE PIPELINE
// ===============================
app.UseDeveloperExceptionPage(); // Hiện lỗi chi tiết khi dev

app.UseStaticFiles();

// Tạo và Map thư mục Video
var videoPath = builder.Configuration["Recording:Root"] ?? @"C:\GhiHinhVideos";
try { Directory.CreateDirectory(videoPath); } catch { }

if (Directory.Exists(videoPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(videoPath),
        RequestPath = "/videos",
        ServeUnknownFileTypes = true
    });
}

// Bật WebSocket cho SignalR
app.UseWebSockets();

app.UseRouting();

// Thứ tự quan trọng: Auth -> Authorization -> Antiforgery
app.UseAuthentication();
app.UseAuthorization();

// 👇 QUAN TRỌNG: Fix lỗi Login bị crash
app.UseAntiforgery();

app.UseSwagger();
app.UseSwaggerUI();

// Map Endpoints
app.MapControllers();
app.MapHub<ScanHub>("/scanHub");

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();