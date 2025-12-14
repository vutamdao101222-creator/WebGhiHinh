using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using WebGhiHinh.Components;
using WebGhiHinh.Data;
using WebGhiHinh.Hubs;
using WebGhiHinh.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Config Services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhập Token."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Blazor Interactive Server
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.Configure<CircuitOptions>(o => o.DetailedErrors = true);

// Services
builder.Services.AddSingleton<FfmpegService>();
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri("http://192.168.1.48/") });
builder.Services.AddHttpClient("QrScan", client => { client.BaseAddress = new Uri("http://192.168.1.48/"); });

builder.Services.AddScoped<ProtectedSessionStorage>();
builder.Services.AddSignalR();
// 👇 QUAN TRỌNG: Tạm thời đóng Worker lại để test giao diện trước. 
// Nếu web lên hình thì mở lại dòng này sau.
// builder.Services.AddHostedService<QrScanWorker>(); 

// Auth & Data Protection
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
builder.Services.AddCascadingAuthenticationState();

var keysFolder = Path.Combine(builder.Environment.ContentRootPath, "keys");
Directory.CreateDirectory(keysFolder);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("WebGhiHinhApp");

// JWT
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
var key = builder.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(key) || key.Length < 32) key = "Key_Du_Phong_Dai_Hon_32_Ky_Tu_De_Chong_Crash_App_123456_!!!";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "WebGhiHinh",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "WebGhiHinhUser",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            RoleClaimType = "role",
            NameClaimType = "name"
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddCors(o => o.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// 2. Config Pipeline (Thứ tự rất quan trọng)
app.UseSwagger();
app.UseSwaggerUI();

app.UseStaticFiles();

// Config Video Path
var videoPath = @"C:\GhiHinhVideos";
try { Directory.CreateDirectory(videoPath); }
catch
{
    videoPath = Path.Combine(app.Environment.ContentRootPath, "Videos_Store");
    Directory.CreateDirectory(videoPath);
}
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(videoPath),
    RequestPath = "/videos"
});

app.UseRouting(); // 👈 Phải đặt trước Auth
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery(); // 👈 Phải đặt sau Auth

app.MapControllers();
app.MapHub<ScanHub>("/scanHub");
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();