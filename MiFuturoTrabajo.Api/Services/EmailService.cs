using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Logging;

namespace MiFuturoTrabajo.Api.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string htmlContent);
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

                _logger.LogInformation($"Email sent successfully to {toEmail}. MessageId: {emailSendOperation.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send email to {toEmail}");
                throw;
            }
        }
    }

    /// <summary>
    /// No-op email service used when Azure Communication Services is not configured.
    /// Logs a warning instead of throwing, allowing the app to run locally without ACS.
    /// </summary>
    public class NoOpEmailService : IEmailService
    {
        private readonly ILogger _logger;

        public NoOpEmailService(ILogger logger)
        {
            _logger = logger;
        }

        public Task SendEmailAsync(string toEmail, string subject, string htmlContent)
        {
            _logger.LogWarning(
                "[NoOpEmailService] Email NOT sent to {Email} — ACS not configured. Subject: {Subject}",
                toEmail, subject);
            return Task.CompletedTask;
        }
    }
}
