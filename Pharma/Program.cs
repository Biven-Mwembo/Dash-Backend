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
var supabaseJwtSecret = builder.Configuration["Supabase:JwtSecret"]; // MUST match GoTrue JWT Secret

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
// JWT Authentication for Supabase Auth tokens
// ---------------------------------------------------------
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(supabaseJwtSecret))
        };
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------
// CORS (must match EXACT frontend URL)
// ---------------------------------------------------------
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

// ---------------------------------------------------------
// Swagger
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
}

// ---------------------------------------------------------
// FORCE CORS HEADERS (Render fix)
// ---------------------------------------------------------
app.Use(async (context, next) =>
{
    context.Response.Headers["Access-Control-Allow-Origin"] = "https://kinlight.netlify.app";
    context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
    context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";
    context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";

    // Handle preflight cleanly
    if (context.Request.Method == "OPTIONS")
    {
        context.Response.StatusCode = 200;
        return;
    }

    await next();
});

// ---------------------------------------------------------
// Pipeline ordering (VERY IMPORTANT)
// ---------------------------------------------------------
app.UseHttpsRedirection();
app.UseCors("AllowReactApp");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
