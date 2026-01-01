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

// CORS Configuration
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("https://kinlight.netlify.app") // No trailing slash
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// JWT Authentication for Supabase
// NOTE: Supabase dashboard secrets are Base64. We convert them here.
var keyBytes = Convert.FromBase64String(supabaseJwtSecret);

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

// 1. First, redirect to HTTPS
app.UseHttpsRedirection();

// 2. Establish Routing
app.UseRouting();

// 3. CORS MUST be after UseRouting and BEFORE UseAuthentication
app.UseCors("AllowReactApp");

// 4. Security Middlewares
app.UseAuthentication();
app.UseAuthorization();

// 5. Final Step: Map Endpoints
app.MapControllers();

app.Run();