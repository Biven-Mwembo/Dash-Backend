using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.FileProviders;
using Supabase;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ✅ PRODUCTION FIX: Disable file watching to prevent inotify limit errors
if (builder.Environment.IsProduction())
{
    builder.Configuration.Sources
        .OfType<FileConfigurationSource>()
        .ToList()
        .ForEach(s => s.ReloadOnChange = false);
}

// --- 1. SERVICE REGISTRATIONS ---

// ✅ Add logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// ✅ Configure JSON serialization options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✅ Add health checks
builder.Services.AddHealthChecks();

// ✅ Supabase Client Setup with validation
var supabaseUrl = builder.Configuration["Supabase:Url"]
    ?? throw new InvalidOperationException("Supabase URL is not configured.");
var supabaseServiceKey = builder.Configuration["Supabase:ServiceRoleKey"]
    ?? builder.Configuration["Supabase:Key"]
    ?? throw new InvalidOperationException("Supabase Key is not configured.");

builder.Services.AddScoped(_ => new Supabase.Client(
    supabaseUrl,
    supabaseServiceKey,
    new SupabaseOptions
    {
        AutoRefreshToken = true,
        AutoConnectRealtime = true
    }
));

// ✅ JWT Authentication Setup with enhanced validation
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
    options.RequireHttpsMetadata = builder.Environment.IsProduction();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
            {
                context.Response.Headers.Add("Token-Expired", "true");
            }
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsJsonAsync(new
            {
                Message = "Unauthorized",
                Detail = "Invalid or missing authentication token"
            });
        }
    };
});

builder.Services.AddAuthorization();

// ✅ IMPROVED CORS Configuration
var allowedOrigins = builder.Environment.IsDevelopment()
    ? new[] {
        "http://localhost:3000",
        "http://localhost:5173",
        "https://kinlight.netlify.app",
        "https://dash-frontend-1.onrender.com"
    }
    : new[] {
        "https://kinlight.netlify.app",
        "https://dash-frontend-1.onrender.com"
    };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()
            .SetIsOriginAllowedToAllowWildcardSubdomains();
    });
});

// ✅ Configure host options
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

// --- 2. MIDDLEWARE PIPELINE ---

// ✅ Global exception handler
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        var exceptionFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();

        if (exceptionFeature != null)
        {
            logger.LogError(exceptionFeature.Error, "Unhandled exception occurred");
        }

        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";

        var errorResponse = new ErrorResponse
        {
            Message = "An internal server error occurred",
            Detail = app.Environment.IsDevelopment()
                ? exceptionFeature?.Error.Message ?? "Unknown error"
                : "Please contact support if the problem persists",
            StackTrace = app.Environment.IsDevelopment()
                ? exceptionFeature?.Error.StackTrace
                : null
        };

        await context.Response.WriteAsJsonAsync(errorResponse);
    });
});

// ✅ Enable Swagger only in Development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Pharma API V1");
        c.RoutePrefix = string.Empty;
    });
}

// ✅ Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("X-Frame-Options", "DENY");
    context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");

    if (app.Environment.IsProduction())
    {
        context.Response.Headers.Add("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
    }

    await next();
});

app.UseCors("AllowFrontend");

if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.MapGet("/", () => new
{
    Service = "Pharma API",
    Version = "1.0.0",
    Status = "Running",
    Environment = app.Environment.EnvironmentName,
    Timestamp = DateTime.UtcNow
});

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("🚀 Pharma API started successfully");
logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);
logger.LogInformation("Allowed Origins: {Origins}", string.Join(", ", allowedOrigins));

app.Run();

// ✅ Error Response Model (MUST be at the bottom)
public class ErrorResponse
{
    public string Message { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
}