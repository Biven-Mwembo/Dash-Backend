using Supabase;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------
// Load Supabase settings
// ---------------------------------------------------------
var supabaseUrl = builder.Configuration["Supabase:Url"];
var supabaseAnonKey = builder.Configuration["Supabase:AnonKey"];
var supabaseServiceRoleKey = builder.Configuration["Supabase:ServiceRoleKey"];
var supabaseJwtSecret = builder.Configuration["Supabase:JwtSecret"]; // Must match GoTrue JWT Secret

// ---------------------------------------------------------
// Controllers
// ---------------------------------------------------------
builder.Services.AddControllers();

// ---------------------------------------------------------
// Supabase Client (public anon key only)
// ---------------------------------------------------------
builder.Services.AddSingleton<Supabase.Client>(sp =>
{
    var options = new Supabase.SupabaseOptions
    {
        AutoRefreshToken = true,
        AutoConnectRealtime = false
    };

    return new Supabase.Client(supabaseUrl, supabaseAnonKey, options);
});

// ---------------------------------------------------------
// JWT Authentication (Supabase Auth)
// ---------------------------------------------------------
var keyBytes = Encoding.UTF8.GetBytes(supabaseJwtSecret);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"{supabaseUrl}/auth/v1",

            ValidateAudience = true,
            ValidAudience = "authenticated",

            ValidateLifetime = true,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes)
        };
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------
// CORS (Netlify frontend)
// ---------------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy
            .WithOrigins("https://kinlight.netlify.app")
            .AllowAnyHeader()
            .AllowAnyMethod();
        // No AllowCredentials — using Bearer tokens
    });
});

// ---------------------------------------------------------
// Swagger (Dev only)
// ---------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ---------------------------------------------------------
// Development tools
// ---------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseHttpsRedirection(); // Safe locally only
}

// ---------------------------------------------------------
// Pipeline order (CRITICAL)
// ---------------------------------------------------------
app.UseCors("AllowReactApp");     // MUST be first

app.UseAuthentication();
app.UseAuthorization();

// ---------------------------------------------------------
// Allow preflight OPTIONS requests (CORS hardening)
// ---------------------------------------------------------
app.MapMethods("{*path}", new[] { "OPTIONS" }, () => Results.Ok());

// ---------------------------------------------------------
// Controllers
// ---------------------------------------------------------
app.MapControllers();

app.Run();
