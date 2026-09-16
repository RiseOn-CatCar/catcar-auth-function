using CatCar.AuthFunction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.Configure<CustomerJwtOptions>(context.Configuration.GetSection(CustomerJwtOptions.SectionName));
        services.AddSingleton<CustomerTokenIssuer>();
    })
    .Build();

host.Run();
