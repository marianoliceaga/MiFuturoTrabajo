using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using MiFuturoTrabajo.Shared.Models;
using Xunit;

namespace MiFuturoTrabajo.Tests.Integration.Tests
{
    /// <summary>
    /// A host with tiny quotas, so the caps can be proven rather than assumed.
    /// The rest of the suite runs with them raised out of the way.
    /// </summary>
    public class TightlyQuotaedFactory : TestWebApplicationFactory
    {
        public const int CompanyLimit = 3;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.UseSetting("Quotas:Companies", CompanyLimit.ToString());
        }
    }

    public class QuotaEnforcementTests : IClassFixture<TightlyQuotaedFactory>
    {
        private readonly TightlyQuotaedFactory _factory;

        public QuotaEnforcementTests(TightlyQuotaedFactory factory)
        {
            _factory = factory;
        }

        private async Task<HttpClient> RegisteredClientAsync()
        {
            var client = _factory.CreateClient();

            var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
            {
                Email = $"quota{Guid.NewGuid()}@example.com",
                Password = "TestPassword123!",
                ConfirmPassword = "TestPassword123!",
                FirstName = "Quota",
                LastName = "Tester"
            });

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

            return client;
        }

        /// <summary>
        /// Acceptance criterion 11 from the roadmap: exceed a quota, get a clean
        /// 429 with a useful message - not a 500 when the store eventually
        /// complains, and not silent success.
        /// </summary>
        [Fact]
        public async Task ExceedingTheCompanyQuota_Returns429WithAUsefulMessage()
        {
            var client = await RegisteredClientAsync();

            for (var i = 0; i < TightlyQuotaedFactory.CompanyLimit; i++)
            {
                var ok = await client.PostAsJsonAsync("/api/companies/CompanyAdd", new CompanyDto
                {
                    Name = $"Company {i}"
                });

                Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
            }

            var rejected = await client.PostAsJsonAsync("/api/companies/CompanyAdd", new CompanyDto
            {
                Name = "One too many"
            });

            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

            var body = await rejected.Content.ReadAsStringAsync();
            Assert.Contains("limit", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(TightlyQuotaedFactory.CompanyLimit.ToString(), body);
        }

        /// <summary>Quotas are per account, not global.</summary>
        [Fact]
        public async Task OneAccountHittingItsQuotaDoesNotAffectAnother()
        {
            var first = await RegisteredClientAsync();

            for (var i = 0; i < TightlyQuotaedFactory.CompanyLimit; i++)
            {
                await first.PostAsJsonAsync("/api/companies/CompanyAdd", new CompanyDto { Name = $"C{i}" });
            }

            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await first.PostAsJsonAsync("/api/companies/CompanyAdd", new CompanyDto { Name = "over" })).StatusCode);

            var second = await RegisteredClientAsync();

            Assert.Equal(
                HttpStatusCode.Created,
                (await second.PostAsJsonAsync("/api/companies/CompanyAdd", new CompanyDto { Name = "fine" })).StatusCode);
        }
    }
}
