a 
    using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using WebGhiHinh.Components;
using WebGhiHinh.Data;
using WebGhiHinh.Services;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 1. ĐĂNG KÝ SERVICES
// ==========================================

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Cấu hình Swagger có nút Authorize
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhập chuỗi Token của bạn vào đây."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

// Kết nối SQL
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton<FfmpegService>();

// HttpClient
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri("http://192.168.1.48")
});

//builder.Services.AddHttpContextAccessor();
//builder.Services.AddScoped(sp =>
//{
//    var accessor = sp.GetRequiredService<IHttpContextAccessor>();
//    var request = accessor.HttpContext?.Request;

//    string baseUrl = "http://localhost";

//    if (request != null)
//    {
//        baseUrl = $"{request.Scheme}://{request.Host}";
//    }

//    return new HttpClient
//    {
//        BaseAddress = new Uri(baseUrl)
//    };
//});



builder.Services.AddScoped<ProtectedSessionStorage>();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
builder.Services.AddCascadingAuthenticationState();

// Authentication JWT
// Dòng này vẫn giữ để xóa mapping mặc định cũ
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var key = builder.Configuration["Jwt:Key"];
var issuer = builder.Configuration["Jwt:Issuer"];
var audience = builder.Configuration["Jwt:Audience"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // 👇 QUAN TRỌNG: Tắt hoàn toàn việc tự đổi tên Claim của .NET
        // Giúp server đọc đúng "role" thay vì "http://schemas..."
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),

            // Khớp với token đã tạo
            RoleClaimType = "role",
            NameClaimType = "name"
        };
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", b => b.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// ==========================================
// 2. MIDDLEWARE
// ==========================================

// 👇 ĐÃ SỬA: Cho phép Swagger chạy ở mọi môi trường (kể cả khi Publish)
// if (app.Environment.IsDevelopment()) // <--- Bỏ check này
// {
app.UseSwagger();
app.UseSwaggerUI();
// }

app.UseHttpsRedirection();

// Static Files
app.UseStaticFiles();
var videoPath = @"C:\GhiHinhVideos";
if (!Directory.Exists(videoPath)) Directory.CreateDirectory(videoPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(videoPath),
    RequestPath = "/videos"
});

app.UseAntiforgery();
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();
