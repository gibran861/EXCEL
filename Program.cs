using AfbGenerator.Api.Data;
using AfbGenerator.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddDbContext<XrtDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("XRTConnection")));

// Dependency injection registration placeholders
builder.Services.AddScoped<FluxService>();
builder.Services.AddScoped<LibelleService>();
builder.Services.AddScoped<XrtSyncService>();
builder.Services.AddScoped<CibService>();
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

   
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();
