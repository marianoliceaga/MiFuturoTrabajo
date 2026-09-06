using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using MiFuturoTrabajo.Api.Helpers;

namespace MiFuturoTrabajo.Api.Services
{
    /// <summary>
    /// Configurable under <c>RateLimits:</c>. Environments differ: a shared
    /// public demo wants a tight ceiling, and a test run issuing hundreds of
    /// requests from one address in seconds is not abuse.
    /// </summary>
    public class RateLimitOptions
    {
        public int AuthPermitPerMinute { get; set; } = 10;
        public int PerUserPermitPerMinute { get; set; } = 120;

        /// <summary>
        /// Deliberately tiny. An AI request costs real money and takes seconds,
        /// so the honest usage pattern is a handful an hour, not two a second.
        /// </summary>
        public int AiPermitPerMinute { get; set; } = 4;
    }

    /// <summary>
    /// Rate limits, sized for a public demo that anyone can register on.
    ///
    /// Two policies, because the two risks are different. The auth endpoints
    /// are anonymous and are where credential stuffing, mass registration and
    /// mail-bombing happen, so they are limited per client address and limited
    /// hard. Everything else runs behind a token, so it is limited per user:
    /// one noisy account cannot degrade the service for anybody else, and a
    /// stolen token cannot be used to hammer the store.
    /// </summary>
    public static class RateLimiting
    {
        public const string AuthPolicy = "auth";
        public const string PerUserPolicy = "per-user";
        public const string AiPolicy = "ai";

        public static IServiceCollection AddAppRateLimiting(
            this IServiceCollection services, RateLimitOptions limits)
        {
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.OnRejected = async (context, cancellationToken) =>
                {
                    // Tell the caller when to come back. Without Retry-After a
                    // client has no way to behave well, and a badly behaved
                    // retry loop is what turns a limit into an outage.
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        context.HttpContext.Response.Headers.RetryAfter =
                            ((int)retryAfter.TotalSeconds).ToString();
                    }

                    context.HttpContext.Response.ContentType = "application/json";

                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new { error = "Too many requests. Please slow down and try again shortly." },
                        cancellationToken);
                };

                // Anonymous, credential-adjacent. Per client address.
                options.AddPolicy(AuthPolicy, context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: ClientKey(context),
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limits.AuthPermitPerMinute,
                            Window = TimeSpan.FromMinutes(1),

                            // No queue: a rejected login should be told so now,
                            // not held open. Queuing here would let an attacker
                            // consume server connections for free.
                            QueueLimit = 0
                        }));

                // Authenticated application traffic. Per user, so one account
                // cannot spoil the demo for another.
                options.AddPolicy(PerUserPolicy, context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: UserKey(context),
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limits.PerUserPermitPerMinute,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));

                // The AI endpoints. There is a per-account allowance behind
                // these as well, but an allowance is not a burst control: it
                // stops the tenth call this month, not the ten fired at once
                // that each read the counter before any of them wrote it back.
                options.AddPolicy(AiPolicy, context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: UserKey(context),
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limits.AiPermitPerMinute,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));
            });

            return services;
        }

        /// <summary>
        /// Prefers the forwarded address, because in App Service every request
        /// arrives from the front end and <c>RemoteIpAddress</c> would put the
        /// entire internet in one partition.
        /// </summary>
        private static string ClientKey(HttpContext context)
        {
            var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                // "client, proxy1, proxy2" - the first entry is the caller, and
                // App Service appends a port to it.
                var first = forwarded.Split(',')[0].Trim();
                var colon = first.LastIndexOf(':');

                return colon > 0 && first.Count(c => c == ':') == 1 ? first[..colon] : first;
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        private static string UserKey(HttpContext context)
        {
            var userId = AuthHelper.GetUserIdFromClaims(context.User);

            return string.IsNullOrEmpty(userId) ? $"anon:{ClientKey(context)}" : $"user:{userId}";
        }
    }
}
