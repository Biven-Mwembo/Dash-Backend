using Supabase;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Load Supabase settings from environment / appsettings.json
var supabaseUrl = builder.Configuration["Supabase:Url"];
var supabaseAnonKey = builder.Configuration["Supabase:AnonKey"];
var supabaseServiceRoleKey = builder.Configuration["Supabase:ServiceRoleKey"];
var supabaseJwtSecret = builder.Configuration["Supabase:JwtSecret"]; // MUST match GoTrue JWT Secret

// Register Supabase Client (use anon key for normal client operations)
builder.Services.AddSingleton<Supabase.Client>(sp =>
{
    var options = new Supabase.SupabaseOptions
    {
        AutoRefreshToken = true,
        AutoConnectRealtime = false
    };

    return new Supabase.Client(supabaseUrl, supabaseAnonKey, options);
});

// JWT Authentication — VALIDATES Supabase-issued JWTs
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

            // Supabase JWT Secret (from Settings ? Auth ? JWT ? JWT Secret)
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(supabaseJwtSecret))
        };
    });

builder.Services.AddAuthorization();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("https://kinlight.netlify.app")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowReactApp");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
