using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using WebGhiHinh.Components;
using WebGhiHinh.Data;
using WebGhiHinh.Services;

var builder = WebApplication.CreateBuilder(args);

// ===============================
// Controllers + Swagger
// ===============================
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
            Array.Empty<string>()
        }
    });
});

// ===============================
// Database
// ===============================
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ===============================
// Blazor Razor Components
// ===============================
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ✅ Bật lỗi chi tiết circuit để tránh trắng trang khó debug
builder.Services.Configure<CircuitOptions>(o =>
{
    o.DetailedErrors = true;
});

// ❌ TẠM TẮT handler cũ vì .NET 8 không có circuit.Services
// builder.Services.AddSingleton<CircuitHandler, AutoReleaseCircuitHandler>();

// ===============================
// Services
// ===============================
builder.Services.AddSingleton<FfmpegService>();

// HttpClient cho UI gọi API nội bộ
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri("http://192.168.1.48/")
});

builder.Services.AddScoped<ProtectedSessionStorage>();

// ===============================
// Auth state for Blazor
// ===============================
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
builder.Services.AddCascadingAuthenticationState();

// ===============================
// JWT for API
// ===============================
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var key = builder.Configuration["Jwt:Key"] ?? "";
var issuer = builder.Configuration["Jwt:Issuer"];
var audience = builder.Configuration["Jwt:Audience"];

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
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            RoleClaimType = "role",
            NameClaimType = "name"
        };
    });

builder.Services.AddAuthorization();

// ===============================
// CORS
// ===============================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", b =>
        b.AllowAnyOrigin()
         .AllowAnyHeader()
         .AllowAnyMethod());
});

var app = builder.Build();

// ===============================
// Swagger
// ===============================
app.UseSwagger();
app.UseSwaggerUI();

// ===============================
// Static files
// ===============================
app.UseStaticFiles();

// Map video folder: /videos -> C:\GhiHinhVideos
var videoPath = @"C:\GhiHinhVideos";
if (!Directory.Exists(videoPath)) Directory.CreateDirectory(videoPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(videoPath),
    RequestPath = "/videos"
});

// ===============================
// Pipeline
// ===============================
app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapControllers();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();
