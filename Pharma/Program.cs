using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Supabase; // Ensure you have this for the client
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// --- 1. SERVICE REGISTRATIONS ---

// Add Controllers (This was missing!)
builder.Services.AddControllers();

// Authorization (This was the cause of your Render crash!)
builder.Services.AddAuthorization();

// Supabase Client Setup (Ensure this matches your setup)
var supabaseUrl = builder.Configuration["Supabase:Url"];
var supabaseKey = builder.Configuration["Supabase:AnonKey"];
builder.Services.AddScoped(_ => new Supabase.Client(supabaseUrl, supabaseKey, new SupabaseOptions
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
        ValidateAudience = true,
        ValidAudience = "authenticated",
        ClockSkew = TimeSpan.Zero
    };
});

// CORS
builder.Services.AddCors(options => {
    options.AddPolicy("AllowAll", b => b.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// --- 2. MIDDLEWARE PIPELINE ---
// Order is extremely important here!

app.UseCors("AllowAll");

app.UseAuthentication(); // Checks WHO you are
app.UseAuthorization();  // Checks what you are ALLOWED to do

app.MapControllers();

app.Run();