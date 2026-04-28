using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SpaBookingSystem.Api.Services;
using SpaBookingSystem.Services;
using SpaBookingSystem.DataLayer;
using SpaBookingSystem.Api.Options;
using SpaBookingSystem.Api.Services.Email;
using SpaBookingSystem.Api.Services.Momo;
using System.Text;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<SpaDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IPasswordService, PasswordService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAdminSeedService, AdminSeedService>();
builder.Services.AddScoped<IBookingStaffingService, BookingStaffingService>();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.Configure<MomoOptions>(builder.Configuration.GetSection(MomoOptions.SectionName));
builder.Services.Configure<BankTransferOptions>(builder.Configuration.GetSection(BankTransferOptions.SectionName));
builder.Services.AddHttpClient<ResendEmailSender>();
builder.Services.AddHttpClient<SequenzyEmailSender>();
builder.Services.AddScoped<IEmailSender>(sp =>
{
    var options = sp.GetRequiredService<IOptions<EmailOptions>>().Value;
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger<IEmailSender>();

    if (string.Equals(options.Provider, "Resend", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(options.ResendApiKey))
    {
        return sp.GetRequiredService<ResendEmailSender>();
    }

    if (string.Equals(options.Provider, "Sequenzy", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(options.SequenzyApiKey))
    {
        return sp.GetRequiredService<SequenzyEmailSender>();
    }

    logger.LogInformation("Email sender fallback to FileEmailSender (provider={Provider})", options.Provider);
    return ActivatorUtilities.CreateInstance<FileEmailSender>(sp);
});
builder.Services.AddHttpClient<IMomoService, MomoService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"] ?? throw new InvalidOperationException("Jwt:Key is missing from configuration.");
var issuer = jwtSection["Issuer"];
var audience = jwtSection["Audience"];
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Spa Booking System API", Version = "v1" });

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };

    options.AddSecurityDefinition("Bearer", securityScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            securityScheme,
            Array.Empty<string>()
        }
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("SpaFrontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors("SpaFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var adminSeedService = scope.ServiceProvider.GetRequiredService<IAdminSeedService>();
    await adminSeedService.SeedAsync();
}

app.Run();
