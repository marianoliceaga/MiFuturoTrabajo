using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using MiFuturoTrabajo.Shared.Models;
using Xunit;

namespace MiFuturoTrabajo.Tests.Integration.Tests
{
    /// <summary>
    /// A host with a deliberately tiny auth limit, so the limiter can be proven
    /// rather than assumed.
    ///
    /// The rest of the suite runs with the ceiling raised out of the way: every
    /// test shares one partition (a TestServer has no client address), so
    /// production limits would just make tests fail each other.
    /// </summary>
    public class TightlyLimitedFactory : TestWebApplicationFactory
    {
        public const int AuthLimit = 5;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // UseSetting rather than ConfigureAppConfiguration: the latter is
            // layered underneath appsettings.Testing.json, which sets the
            // ceiling out of the way for every other test, so it would lose.
            builder.UseSetting("RateLimits:AuthPermitPerMinute", AuthLimit.ToString());
            builder.UseSetting("RateLimits:PerUserPermitPerMinute", "100000");
        }
    }

    public class RateLimiterTests : IClassFixture<TightlyLimitedFactory>
    {
        private readonly TightlyLimitedFactory _factory;

        public RateLimiterTests(TightlyLimitedFactory factory)
        {
            _factory = factory;
        }

        /// <summary>
        /// Acceptance criterion 10 from the roadmap: hammer login, get a 429.
        /// This is the defence against credential stuffing on an endpoint that
        /// is necessarily anonymous.
        /// </summary>
        [Fact]
        public async Task HammeringLogin_EventuallyReturns429()
        {
            var client = _factory.CreateClient();

            var statuses = new List<HttpStatusCode>();

            for (var i = 0; i < TightlyLimitedFactory.AuthLimit + 5; i++)
            {
                var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
                {
                    Email = "nobody@example.com",
                    Password = "WrongPassword123!"
                });

                statuses.Add(response.StatusCode);
            }

            Assert.Contains(HttpStatusCode.TooManyRequests, statuses);

            // The first few must have been served - a limiter that rejects
            // everything is not a limiter, it is an outage.
            Assert.Contains(HttpStatusCode.Unauthorized, statuses);
        }

        /// <summary>
        /// A client with no Retry-After has no way to back off correctly, and a
        /// bad retry loop turns a limit into an outage.
        /// </summary>
        [Fact]
        public async Task ARejectedRequestSaysWhenToComeBack()
        {
            var client = _factory.CreateClient();

            HttpResponseMessage? rejected = null;

            for (var i = 0; i < TightlyLimitedFactory.AuthLimit + 5 && rejected == null; i++)
            {
                var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
                {
                    Email = "nobody2@example.com",
                    Password = "WrongPassword123!"
                });

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    rejected = response;
                }
            }

            Assert.NotNull(rejected);
            Assert.True(rejected!.Headers.RetryAfter != null || rejected.Headers.Contains("Retry-After"),
                "a 429 should carry Retry-After");

            var body = await rejected.Content.ReadAsStringAsync();
            Assert.Contains("Too many requests", body);
        }
    }
}
