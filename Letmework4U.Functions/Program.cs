using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Letmework4U.Infrastructure;
using Letmework4U.Functions.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        // Configure Entity Framework Core with Cosmos DB
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var cosmosConnectionString = context.Configuration["CosmosDb:ConnectionString"];
            var cosmosDatabaseName = context.Configuration["CosmosDb:DatabaseName"];

            if (!string.IsNullOrEmpty(cosmosConnectionString) && !string.IsNullOrEmpty(cosmosDatabaseName))
            {
                options.UseCosmos(cosmosConnectionString, cosmosDatabaseName);
            }
        });

        // Register Email Service
        services.AddSingleton<IEmailService>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var logger = provider.GetRequiredService<ILogger<EmailService>>();
            var connectionString = config["AzureCommunicationServices:ConnectionString"] ?? "dummy";
            var senderAddress = config["AzureCommunicationServices:SenderAddress"] ?? "test@example.com";

            return new EmailService(connectionString, senderAddress, logger);
        });
    })
    .Build();

host.Run();
