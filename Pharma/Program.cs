using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Supabase;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// --- 1. SERVICE REGISTRATIONS ---
builder.Services.AddControllers();
builder.Services.AddAuthorization();

// Supabase Client Setup
var supabaseUrl = builder.Configuration["Supabase:Url"];
var supabaseServiceKey = builder.Configuration["Supabase:ServiceRoleKey"]
                         ?? builder.Configuration["Supabase:Key"];

builder.Services.AddScoped(_ => new Supabase.Client(supabaseUrl, supabaseServiceKey, new SupabaseOptions
{
    AutoRefreshToken = true,
    AutoConnectRealtime = true
}));

// JWT Authentication Setup
var jwtSecret = builder.Configuration["Supabase:JwtSecret"]
                ?? throw new InvalidOperationException("Supabase JWT Secret is not configured.");
var key = Encoding.ASCII.GetBytes(jwtSecret);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false,
        ClockSkew = TimeSpan.Zero
    };
});

// ✅ IMPROVED CORS Configuration
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.WithOrigins(
                "https://kinlight.netlify.app",
                "http://localhost:3000",
                "http://localhost:5173"
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

var app = builder.Build();

// --- 2. MIDDLEWARE PIPELINE ---
// ✅ CORS must be BEFORE Authentication/Authorization
app.UseCors("AllowAll");

// ✅ Add global exception handler
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";

        // Add CORS headers even on errors
        context.Response.Headers.Add("Access-Control-Allow-Origin", "https://kinlight.netlify.app");

        await context.Response.WriteAsJsonAsync(new
        {
            Message = "An internal server error occurred",
            Detail = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error.Message
        });
    });
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();