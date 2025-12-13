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
using WebGhiHinh.Hubs;
using WebGhiHinh.Services;

var builder = WebApplication.CreateBuilder(args);

// ===============================
// 1. Controllers + Swagger
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
        Description = "Nhập Bearer Token"
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
// 2. Database
// ===============================
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ===============================
// 3. Blazor Server
// ===============================
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<CircuitOptions>(o =>
{
    o.DetailedErrors = true;
});

// ===============================
// 4. Services
// ===============================
builder.Services.AddSingleton<FfmpegService>();

// HttpClient nội bộ (CHUẨN IIS)
builder.Services.AddHttpClient();

// Worker client (nếu cần base address)
builder.Services.AddHttpClient("QrScan", client =>
{
    client.BaseAddress = new Uri("http://localhost/");
});

builder.Services.AddScoped<ProtectedSessionStorage>();
builder.Services.AddSignalR();
builder.Services.AddHostedService<QrScanWorker>();

// ===============================
// 5. Authentication State (Blazor)
// ===============================
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
builder.Services.AddCascadingAuthenticationState();

// ===============================
// 6. JWT Authentication
// ===============================
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var key = builder.Configuration["Jwt:Key"];
var issuer = builder.Configuration["Jwt:Issuer"] ?? "WebGhiHinh";
var audience = builder.Configuration["Jwt:Audience"] ?? "WebGhiHinhUser";

// Fallback key (tránh crash IIS)
if (string.IsNullOrEmpty(key) || key.Length < 32)
{
    key = "Key_Du_Phong_Cuc_Manh_Chong_Sap_IIS_123456789_ABC";
}

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
// 7. CORS
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
// 8. Pipeline (CHUẨN IIS HTTP)
// ===============================
app.UseSwagger();
app.UseSwaggerUI();

app.UseStaticFiles();

// ===== Video storage an toàn =====
var videoPath = @"C:\GhiHinhVideos";
try
{
    if (!Directory.Exists(videoPath))
        Directory.CreateDirectory(videoPath);
}
catch
{
    videoPath = Path.Combine(app.Environment.ContentRootPath, "Videos_Store");
    if (!Directory.Exists(videoPath))
        Directory.CreateDirectory(videoPath);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(videoPath),
    RequestPath = "/videos"
});

// ❌ KHÔNG redirect HTTPS (IIS đang HTTP)
// app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

// ❌ KHÔNG bật Antiforgery khi dùng JWT
// app.UseAntiforgery();

app.MapControllers();
app.MapHub<ScanHub>("/scanHub");

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();
