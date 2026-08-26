using System.Text.Encodings.Web;

namespace MiFuturoTrabajo.Api.Services
{
    /// <summary>Subject line plus HTML body, ready to hand to <see cref="IEmailService"/>.</summary>
    public record EmailMessageContent(string Subject, string HtmlBody);

    /// <summary>
    /// The transactional emails this application sends.
    ///
    /// Every interpolated value is HTML-encoded. Names and job titles are
    /// user-supplied and end up inside markup that lands in someone else's
    /// inbox, so an unencoded value is a stored XSS with an email client as
    /// the sink. The links are the one exception: they are built from
    /// configuration plus a generated token, and are encoded for attribute
    /// context rather than left raw.
    ///
    /// Kept as plain string composition rather than a templating engine - four
    /// messages do not justify a dependency, and this way the markup is right
    /// here where it can be read alongside what sends it.
    /// </summary>
    public static class EmailTemplates
    {
        private const string BrandName = "Mi Futuro Trabajo";

        private static string E(string? value) => HtmlEncoder.Default.Encode(value ?? string.Empty);

        public static EmailMessageContent Verification(string firstName, string verificationLink, int expiryHours) =>
            new($"Verify your email - {BrandName}",
                Wrap(
                    "Verify your email",
                    $@"
                    <p>Hi {E(firstName)},</p>
                    <p>Confirm this address to finish setting up your {BrandName} account.</p>
                    {Button(verificationLink, "Verify email")}
                    <p class=""muted"">This link expires in {expiryHours} hours. If you did not create an account, you can ignore this message.</p>"));

        public static EmailMessageContent PasswordReset(string firstName, string resetLink, int expiryHours) =>
            new($"Reset your password - {BrandName}",
                Wrap(
                    "Reset your password",
                    $@"
                    <p>Hi {E(firstName)},</p>
                    <p>Use the button below to choose a new password.</p>
                    {Button(resetLink, "Reset password")}
                    <p class=""muted"">This link expires in {expiryHours} hour{(expiryHours == 1 ? "" : "s")} and can be used once. If you did not ask for this, nothing has changed and you can ignore this message.</p>"));

        public static EmailMessageContent TaskReminder(string firstName, string taskTitle, DateTime deadlineUtc) =>
            new($"Due tomorrow: {taskTitle}",
                Wrap(
                    "A task is due soon",
                    $@"
                    <p>Hi {E(firstName)},</p>
                    <p><strong>{E(taskTitle)}</strong> is due on {E(deadlineUtc.ToString("dddd d MMMM, HH:mm"))} UTC.</p>
                    <p class=""muted"">You are getting this because the task has a deadline within the next 24 hours.</p>"));

        public static EmailMessageContent InterviewReminder(
            string firstName, string jobTitle, string companyName, DateTime startsAtUtc, string? location) =>
            new($"Interview reminder: {jobTitle}",
                Wrap(
                    "You have an interview coming up",
                    $@"
                    <p>Hi {E(firstName)},</p>
                    <p><strong>{E(jobTitle)}</strong>{(string.IsNullOrWhiteSpace(companyName) ? "" : $" at {E(companyName)}")}
                       starts at {E(startsAtUtc.ToString("dddd d MMMM, HH:mm"))} UTC.</p>
                    {(string.IsNullOrWhiteSpace(location) ? "" : $@"<p>Where: {E(location)}</p>")}
                    <p class=""muted"">Good luck.</p>"));

        /// <summary>
        /// Inline styles only, and a table-free layout: email clients strip
        /// stylesheets and Outlook in particular ignores most of what a browser
        /// would honour.
        /// </summary>
        private static string Wrap(string heading, string body) => $@"
<div style=""font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;font-size:16px;line-height:1.55;color:#1c1e21;max-width:560px;margin:0 auto;padding:24px"">
  <p style=""font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:#6b7280;margin:0 0 24px"">{E(BrandName)}</p>
  <h1 style=""font-size:22px;margin:0 0 16px;color:#111827"">{E(heading)}</h1>
  {body.Replace(@"class=""muted""", @"style=""font-size:14px;color:#6b7280""")}
  <hr style=""border:none;border-top:1px solid #e5e7eb;margin:28px 0"" />
  <p style=""font-size:13px;color:#9ca3af;margin:0"">{E(BrandName)} - job application tracker</p>
</div>";

        private static string Button(string href, string label) => $@"
  <p style=""margin:24px 0"">
    <a href=""{HtmlEncoder.Default.Encode(href)}""
       style=""display:inline-block;padding:12px 22px;background:#2563eb;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:600"">{E(label)}</a>
  </p>
  <p style=""font-size:13px;color:#6b7280;margin:0 0 8px"">If the button does not work, paste this into your browser:</p>
  <p style=""font-size:13px;word-break:break-all;margin:0""><a href=""{HtmlEncoder.Default.Encode(href)}"" style=""color:#2563eb"">{E(href)}</a></p>";
    }
}
