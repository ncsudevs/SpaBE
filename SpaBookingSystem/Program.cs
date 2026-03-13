using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.DataLayer;

var builder = WebApplication.CreateBuilder(args);

// DbContext is registered in the API project while migrations remain in the DataLayer assembly.
builder.Services.AddDbContext<SpaDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// The frontend runs on a separate Vite origin during development.
builder.Services.AddCors(options =>
{
    options.AddPolicy("SpaFrontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173")
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

// Static files are required so uploaded service images can be requested directly by the frontend.
app.UseStaticFiles();
app.UseCors("SpaFrontend");
app.UseAuthorization();
app.MapControllers();
app.Run();
