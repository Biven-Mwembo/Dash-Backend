using Supabase;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Add Supabase client
builder.Services.AddSingleton<Supabase.Client>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new Supabase.Client(config["Supabase:Url"], config["Supabase:Key"]);
});

// Add JWT authentication (updated for Supabase JWT validation)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"{builder.Configuration["Supabase:Url"]}/auth/v1", // Supabase issuer
            ValidateAudience = true,
            ValidAudience = "authenticated", // Supabase audience
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("vIrsfVAudYwpMy0hGf3e77J4IiOjluQUfrAAbCnCDH52BzkPoafq6Etjxfu8X8USeQkaKDQ8JBbb8849uF12MQ==")) // Replace with your actual JWT secret from Supabase
        };
    });

builder.Services.AddAuthorization();

// ----------------- CORS Setup -----------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("https://kinlight.netlify.app/") // React dev server URL
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // needed if sending cookies
    });
});
// -------------------------------------------------

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Apply CORS BEFORE authentication/authorization
app.UseCors("AllowReactApp");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
