using CatCar.AuthFunction;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();
builder.AddServiceDefaults();

builder.Services.Configure<CustomerJwtOptions>(builder.Configuration.GetSection(CustomerJwtOptions.SectionName));
builder.Services.AddSingleton<CustomerTokenIssuer>();

var connectionString = builder.Configuration.GetConnectionString("catcar-auth")
    ?? builder.Configuration.GetConnectionString("catcar")
    ?? throw new InvalidOperationException("Connection string 'catcar-auth' or 'catcar' is required.");

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddScoped<ICustomerLookupService, CustomerLookupService>();

var host = builder.Build();

host.Run();
