using MiFuturoTrabajo.Api.Services;
using MiFuturoTrabajo.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MiFuturoTrabajo.Tests.Integration
{
    public class TestWebApplicationFactory : WebApplicationFactory<Program>
    {
        /// <summary>
        /// The same instance the API writes to, so a test can look in storage
        /// directly instead of inferring from status codes.
        /// </summary>
        public FakeBlobStorageService Blobs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Blob storage is faked so the suite runs with no emulator and
                // no storage account.
                var blobDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IBlobStorageService));

                if (blobDescriptor != null)
                {
                    services.Remove(blobDescriptor);
                }

                services.AddSingleton<IBlobStorageService>(Blobs);

                // Remove the existing DbContext registration
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));

                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                // Add in-memory database for testing
                services.AddDbContext<ApplicationDbContext>(options =>
                {
                    options.UseInMemoryDatabase("IntegrationTestDb");
                });

                // Build the service provider
                var sp = services.BuildServiceProvider();

                // Create a scope to obtain a reference to the database context
                using var scope = sp.CreateScope();
                var scopedServices = scope.ServiceProvider;
                var db = scopedServices.GetRequiredService<ApplicationDbContext>();

                // Ensure the database is created
                db.Database.EnsureCreated();
            });

            builder.UseEnvironment("Testing");
        }
    }
}
