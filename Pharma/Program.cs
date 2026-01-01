using Supabase;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------
// 1. Load Supabase settings
// ---------------------------------------------------------
var supabaseUrl = builder.Configuration["Supabase:Url"];
var supabaseAnonKey = builder.Configuration["Supabase:AnonKey"];
var supabaseServiceRoleKey = builder.Configuration["Supabase:ServiceRoleKey"];
var supabaseJwtSecret = builder.Configuration["Supabase:JwtSecret"];

// ---------------------------------------------------------
// 2. Services Configuration
// ---------------------------------------------------------
builder.Services.AddControllers();

// Supabase Client
builder.Services.AddSingleton<Supabase.Client>(sp =>
{
    var options = new Supabase.SupabaseOptions
    {
        AutoRefreshToken = true,
        AutoConnectRealtime = false
    };
    return new Supabase.Client(supabaseUrl, supabaseAnonKey, options);
});

// CORS Policy - Defined BEFORE Build
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("https://kinlight.netlify.app") // Ensure no trailing slash here
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// JWT Authentication
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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ---------------------------------------------------------
// 3. Middleware Pipeline (ORDER IS CRITICAL)
// ---------------------------------------------------------

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Ensure the app uses HTTPS redirection (standard practice)
app.UseHttpsRedirection();

// 1. Routing must come first so the app knows which endpoint is being hit
app.UseRouting();

// 2. CORS must come after UseRouting but before UseAuthentication
// This allows the OPTIONS preflight request to be handled even for protected routes
app.UseCors("AllowReactApp");

// 3. Security Middlewares
app.UseAuthentication();
app.UseAuthorization();

// 4. Map the actual controllers
app.MapControllers();

app.Run();