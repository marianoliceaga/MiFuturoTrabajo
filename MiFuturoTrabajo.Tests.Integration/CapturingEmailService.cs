using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Web;
using MiFuturoTrabajo.Api.Services;
using MiFuturoTrabajo.Infrastructure.Email;

namespace MiFuturoTrabajo.Tests.Integration
{
    /// <param name="ToEmail">Recipient.</param>
    /// <param name="Subject">Subject line.</param>
    /// <param name="HtmlBody">Rendered body.</param>
    public record CapturedEmail(string ToEmail, string Subject, string HtmlBody)
    {
        private static readonly Regex Href = new("href=\"(?<url>[^\"]+)\"", RegexOptions.Compiled);

        /// <summary>The first link in the message, as a browser would follow it.</summary>
        public string? Link
        {
            get
            {
                var match = Href.Match(HtmlBody);
                return match.Success ? HttpUtility.HtmlDecode(match.Groups["url"].Value) : null;
            }
        }

        /// <summary>Reads a query-string value out of that link.</summary>
        public string? LinkParameter(string name)
        {
            var link = Link;
            if (link == null) return null;

            var query = HttpUtility.ParseQueryString(new Uri(link).Query);
            return query[name];
        }
    }

    /// <summary>
    /// Captures outbound mail instead of sending it.
    ///
    /// This exists so tests can obtain a reset or verification token the way a
    /// real user does - by reading the email - rather than by reaching into the
    /// database. That distinction is the whole point of this phase: the
    /// database now stores only a hash, so a test that could still read a
    /// usable token out of it would be proving the opposite of what is wanted.
    /// </summary>
    public class CapturingEmailService : IEmailService
    {
        private readonly ConcurrentQueue<CapturedEmail> _sent = new();

        public IReadOnlyList<CapturedEmail> Sent => _sent.ToList();

        public int Count => _sent.Count;

        public void Clear() => _sent.Clear();

        /// <summary>The most recent message to this address, or null.</summary>
        public CapturedEmail? LastTo(string email) =>
            _sent.LastOrDefault(e => string.Equals(e.ToEmail, email, StringComparison.OrdinalIgnoreCase));

        public Task SendEmailAsync(string toEmail, string subject, string htmlContent)
        {
            _sent.Enqueue(new CapturedEmail(toEmail, subject, htmlContent));
            return Task.CompletedTask;
        }
    }
}
