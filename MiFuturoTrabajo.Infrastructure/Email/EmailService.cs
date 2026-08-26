using System.Text.RegularExpressions;
using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Logging;

namespace MiFuturoTrabajo.Infrastructure.Email
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string htmlContent);

        Task SendAsync(string toEmail, EmailMessageContent content) =>
            SendEmailAsync(toEmail, content.Subject, content.HtmlBody);
    }

    public class EmailService : IEmailService
    {
        private readonly EmailClient _emailClient;
        private readonly string _senderAddress;
        private readonly ILogger<EmailService> _logger;

        public EmailService(string connectionString, string senderAddress, ILogger<EmailService> logger)
        {
            _emailClient = new EmailClient(connectionString);
            _senderAddress = senderAddress;
            _logger = logger;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlContent)
        {
            try
            {
                var emailMessage = new EmailMessage(
                    senderAddress: _senderAddress,
                    content: new EmailContent(subject)
                    {
                        Html = htmlContent
                    },
                    recipients: new EmailRecipients(new List<EmailAddress>
                    {
                        new EmailAddress(toEmail)
                    }));

                EmailSendOperation emailSendOperation = await _emailClient.SendAsync(
                    WaitUntil.Started,
                    emailMessage);

                _logger.LogInformation("Email sent to {Email}. MessageId: {MessageId}", toEmail, emailSendOperation.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
                throw;
            }
        }
    }

    /// <summary>
    /// Local and test stand-in for when Azure Communication Services is not
    /// configured.
    ///
    /// It logs the message and, crucially, pulls the action link out of the
    /// body and logs that separately. That link is the whole reason the API
    /// used to return raw reset and verification tokens in its HTTP responses
    /// "for development" - a habit that only had to survive one environment
    /// misconfiguration to become anonymous account takeover. The developer
    /// still gets the link; it just arrives in the server log, where whoever
    /// sent the request cannot read it.
    /// </summary>
    public class NoOpEmailService : IEmailService
    {
        private static readonly Regex FirstHref = new("href=\"(?<url>[^\"]+)\"", RegexOptions.Compiled);

        private readonly ILogger _logger;

        public NoOpEmailService(ILogger logger)
        {
            _logger = logger;
        }

        public Task SendEmailAsync(string toEmail, string subject, string htmlContent)
        {
            var match = FirstHref.Match(htmlContent ?? string.Empty);

            if (match.Success)
            {
                _logger.LogWarning(
                    "[no email configured] Would have sent \"{Subject}\" to {Email}. Link: {Link}",
                    subject, toEmail, System.Net.WebUtility.HtmlDecode(match.Groups["url"].Value));
            }
            else
            {
                _logger.LogWarning(
                    "[no email configured] Would have sent \"{Subject}\" to {Email}.", subject, toEmail);
            }

            return Task.CompletedTask;
        }
    }
}
