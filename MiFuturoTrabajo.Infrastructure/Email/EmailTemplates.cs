using System.Globalization;
using System.Text.Encodings.Web;

namespace MiFuturoTrabajo.Infrastructure.Email
{
    /// <summary>Subject line plus HTML body, ready to hand to <see cref="IEmailService"/>.</summary>
    public record EmailMessageContent(string Subject, string HtmlBody);

    /// <summary>
    /// The transactional emails this application sends, in Spanish and English.
    ///
    /// Every interpolated value is HTML-encoded. Names and job titles are
    /// user-supplied and end up inside markup that lands in someone else's
    /// inbox, so an unencoded value is a stored XSS with an email client as
    /// the sink. The links are the one exception: they are built from
    /// configuration plus a generated token, and are encoded for attribute
    /// context rather than left raw.
    ///
    /// Kept as plain string composition rather than a templating engine - five
    /// messages do not justify a dependency, and this way the markup is right
    /// here where it can be read alongside what sends it. The copy lives in a
    /// per-language <see cref="Copy"/> record rather than in a .resx: these
    /// strings are sentences with markup and pluralisation around them, and
    /// keeping each language's wording in one readable block beats scattering
    /// it across three dozen resource keys.
    ///
    /// The language is a required first argument on every method, not an
    /// optional trailing one, so a new call site cannot quietly default to
    /// Spanish for an English-speaking recipient.
    /// </summary>
    public static class EmailTemplates
    {
        private const string BrandName = "Mi Futuro Trabajo";

        private static string E(string? value) => HtmlEncoder.Default.Encode(value ?? string.Empty);

        public static EmailMessageContent Verification(
            string language, string firstName, string verificationLink, int expiryHours)
        {
            var c = Copy.For(language);

            return new($"{c.VerificationSubject} - {BrandName}",
                Wrap(c,
                    c.VerificationHeading,
                    $@"
                    <p>{c.Greeting(E(firstName))}</p>
                    <p>{c.VerificationBody(BrandName)}</p>
                    {Button(c, verificationLink, c.VerificationButton)}
                    <p class=""muted"">{c.VerificationExpiry(expiryHours)}</p>"));
        }

        public static EmailMessageContent PasswordReset(
            string language, string firstName, string resetLink, int expiryHours)
        {
            var c = Copy.For(language);

            return new($"{c.ResetSubject} - {BrandName}",
                Wrap(c,
                    c.ResetHeading,
                    $@"
                    <p>{c.Greeting(E(firstName))}</p>
                    <p>{c.ResetBody}</p>
                    {Button(c, resetLink, c.ResetButton)}
                    <p class=""muted"">{c.ResetExpiry(expiryHours)}</p>"));
        }

        /// <param name="items">Title and deadline of each task falling due.</param>
        public static EmailMessageContent TaskReminder(
            string language, string firstName, IReadOnlyList<(string Title, DateTime DeadlineUtc)> items)
        {
            var c = Copy.For(language);

            var subject = items.Count == 1
                ? c.TaskSubjectOne(items[0].Title)
                : c.TaskSubjectMany(items.Count);

            var rows = string.Concat(items.Select(i =>
                $@"<li><strong>{E(i.Title)}</strong> &mdash; {E(When(c, i.DeadlineUtc))}</li>"));

            return new(subject, Wrap(c,
                items.Count == 1 ? c.TaskHeadingOne : c.TaskHeadingMany,
                $@"
                <p>{c.Greeting(E(firstName))}</p>
                <p>{(items.Count == 1 ? c.TaskLeadOne : c.TaskLeadMany)}</p>
                <ul style=""padding-left:20px"">{rows}</ul>
                <p class=""muted"">{c.TaskFooter}</p>"));
        }

        /// <param name="items">Title, start time and location of each meeting.</param>
        public static EmailMessageContent InterviewReminder(
            string language, string firstName,
            IReadOnlyList<(string Title, DateTime StartsAtUtc, string? Location)> items)
        {
            var c = Copy.For(language);

            var subject = items.Count == 1
                ? c.InterviewSubjectOne(items[0].Title)
                : c.InterviewSubjectMany(items.Count);

            var rows = string.Concat(items.Select(i =>
                $@"<li><strong>{E(i.Title)}</strong> &mdash; {E(When(c, i.StartsAtUtc))}{
                    (string.IsNullOrWhiteSpace(i.Location) ? "" : $"<br /><span style=\"color:#6b7280\">{E(i.Location)}</span>")
                }</li>"));

            return new(subject, Wrap(c,
                items.Count == 1 ? c.InterviewHeadingOne : c.InterviewHeadingMany,
                $@"
                <p>{c.Greeting(E(firstName))}</p>
                <ul style=""padding-left:20px"">{rows}</ul>
                <p class=""muted"">{c.InterviewFooter}</p>"));
        }

        /// <summary>
        /// Sent when a social identity is attached to an existing account. If
        /// the person did not do it, this is the only signal they get - so it
        /// is worth sending even though the link required a verified address.
        /// </summary>
        public static EmailMessageContent ProviderLinked(string language, string firstName, string provider)
        {
            var c = Copy.For(language);

            return new(c.ProviderLinkedSubject(provider),
                Wrap(c,
                    c.ProviderLinkedHeading,
                    $@"
                    <p>{c.Greeting(E(firstName))}</p>
                    <p>{c.ProviderLinkedBody(E(BrandName), E(provider))}</p>
                    <p class=""muted"">{c.ProviderLinkedWarning}</p>"));
        }

        /// <summary>
        /// Every stored time is UTC and the recipient time zone is unknown, so
        /// the offset is stated rather than silently implied. The date itself is
        /// formatted in the recipient's own culture, which is why the pattern
        /// comes from <see cref="Copy"/> rather than being fixed here.
        /// </summary>
        private static string When(Copy c, DateTime utc) =>
            utc.ToString(c.WhenPattern, c.Culture) + " UTC";

        /// <summary>
        /// Inline styles only, and a table-free layout: email clients strip
        /// stylesheets and Outlook in particular ignores most of what a browser
        /// would honour.
        ///
        /// Deliberately light-only. An email is rendered by someone else's
        /// client, which may or may not respect a dark scheme, and a message
        /// that half-inverts is worse than one that stays legible.
        /// </summary>
        private static string Wrap(Copy c, string heading, string body) => $@"
<div style=""font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;font-size:16px;line-height:1.55;color:#1c1e21;max-width:560px;margin:0 auto;padding:24px"">
  <p style=""font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:#6b7280;margin:0 0 24px"">{E(BrandName)}</p>
  <h1 style=""font-size:22px;margin:0 0 16px;color:#111827"">{E(heading)}</h1>
  {body.Replace(@"class=""muted""", @"style=""font-size:14px;color:#6b7280""")}
  <hr style=""border:none;border-top:1px solid #e5e7eb;margin:28px 0"" />
  <p style=""font-size:13px;color:#9ca3af;margin:0"">{E(BrandName)} - {E(c.Tagline)}</p>
</div>";

        private static string Button(Copy c, string href, string label) => $@"
  <p style=""margin:24px 0"">
    <a href=""{HtmlEncoder.Default.Encode(href)}""
       style=""display:inline-block;padding:12px 22px;background:#2563eb;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:600"">{E(label)}</a>
  </p>
  <p style=""font-size:13px;color:#6b7280;margin:0 0 8px"">{E(c.ButtonFallback)}</p>
  <p style=""font-size:13px;word-break:break-all;margin:0""><a href=""{HtmlEncoder.Default.Encode(href)}"" style=""color:#2563eb"">{E(href)}</a></p>";

        /// <summary>
        /// One language's worth of wording. Everything a reader sees is here, so
        /// adding a language means adding one instance and nothing else.
        /// </summary>
        private sealed record Copy(
            CultureInfo Culture,
            string WhenPattern,
            string Tagline,
            string ButtonFallback,
            Func<string, string> Greeting,
            string VerificationSubject,
            string VerificationHeading,
            Func<string, string> VerificationBody,
            string VerificationButton,
            Func<int, string> VerificationExpiry,
            string ResetSubject,
            string ResetHeading,
            string ResetBody,
            string ResetButton,
            Func<int, string> ResetExpiry,
            Func<string, string> TaskSubjectOne,
            Func<int, string> TaskSubjectMany,
            string TaskHeadingOne,
            string TaskHeadingMany,
            string TaskLeadOne,
            string TaskLeadMany,
            string TaskFooter,
            Func<string, string> InterviewSubjectOne,
            Func<int, string> InterviewSubjectMany,
            string InterviewHeadingOne,
            string InterviewHeadingMany,
            string InterviewFooter,
            Func<string, string> ProviderLinkedSubject,
            string ProviderLinkedHeading,
            Func<string, string, string> ProviderLinkedBody,
            string ProviderLinkedWarning)
        {
            public static Copy For(string? language) =>
                EmailLanguage.Resolve(language) == EmailLanguage.English ? English : Spanish;

            private static readonly Copy Spanish = new(
                Culture: CultureInfo.GetCultureInfo("es"),
                // "martes 10 de marzo a las 09:00"
                WhenPattern: @"dddd d 'de' MMMM 'a las' HH:mm",
                Tagline: "seguimiento de postulaciones",
                ButtonFallback: "Si el botón no funciona, pegá este enlace en tu navegador:",
                Greeting: name => $"Hola {name}:",
                VerificationSubject: "Verificá tu email",
                VerificationHeading: "Verificá tu email",
                VerificationBody: brand => $"Confirmá esta dirección para terminar de configurar tu cuenta de {brand}.",
                VerificationButton: "Verificar el email",
                VerificationExpiry: hours =>
                    $"Este enlace vence en {hours} horas. Si no creaste ninguna cuenta, podés ignorar este mensaje.",
                ResetSubject: "Restablecé tu contraseña",
                ResetHeading: "Restablecé tu contraseña",
                ResetBody: "Usá el botón de abajo para elegir una contraseña nueva.",
                ResetButton: "Restablecer la contraseña",
                ResetExpiry: hours =>
                    $"Este enlace vence en {hours} {(hours == 1 ? "hora" : "horas")} y se puede usar una sola vez. " +
                    "Si no pediste esto, no cambió nada y podés ignorar el mensaje.",
                TaskSubjectOne: title => $"Vence pronto: {title}",
                TaskSubjectMany: count => $"{count} tareas vencen en las próximas 24 horas",
                TaskHeadingOne: "Una tarea vence pronto",
                TaskHeadingMany: "Tareas que vencen pronto",
                TaskLeadOne: "Esto vence en las próximas 24 horas:",
                TaskLeadMany: "Esto vence en las próximas 24 horas:",
                TaskFooter: "Recibís este aviso porque la fecha límite entra en las próximas 24 horas.",
                InterviewSubjectOne: title => $"Recordatorio: {title}",
                InterviewSubjectMany: count => $"{count} entrevistas próximas",
                InterviewHeadingOne: "Tenés una entrevista próxima",
                InterviewHeadingMany: "Entrevistas próximas",
                InterviewFooter: "Mucha suerte.",
                ProviderLinkedSubject: provider => $"Se agregó el ingreso con {provider} a tu cuenta",
                ProviderLinkedHeading: "Se agregó una forma de ingreso",
                ProviderLinkedBody: (brand, provider) =>
                    $"Ya podés entrar a {brand} con <strong>{provider}</strong>. Tu contraseña sigue funcionando.",
                ProviderLinkedWarning: "Si no fuiste vos, restablecé tu contraseña ahora mismo.");

            private static readonly Copy English = new(
                Culture: CultureInfo.GetCultureInfo("en"),
                WhenPattern: @"dddd d MMMM 'at' HH:mm",
                Tagline: "job application tracker",
                ButtonFallback: "If the button does not work, paste this into your browser:",
                Greeting: name => $"Hi {name},",
                VerificationSubject: "Verify your email",
                VerificationHeading: "Verify your email",
                VerificationBody: brand => $"Confirm this address to finish setting up your {brand} account.",
                VerificationButton: "Verify email",
                VerificationExpiry: hours =>
                    $"This link expires in {hours} hours. If you did not create an account, you can ignore this message.",
                ResetSubject: "Reset your password",
                ResetHeading: "Reset your password",
                ResetBody: "Use the button below to choose a new password.",
                ResetButton: "Reset password",
                ResetExpiry: hours =>
                    $"This link expires in {hours} hour{(hours == 1 ? "" : "s")} and can be used once. " +
                    "If you did not ask for this, nothing has changed and you can ignore this message.",
                TaskSubjectOne: title => $"Due soon: {title}",
                TaskSubjectMany: count => $"{count} tasks due in the next 24 hours",
                TaskHeadingOne: "A task is due soon",
                TaskHeadingMany: "Tasks due soon",
                TaskLeadOne: "This is due in the next 24 hours:",
                TaskLeadMany: "These are due in the next 24 hours:",
                TaskFooter: "You are getting this because the deadline is inside the next 24 hours.",
                InterviewSubjectOne: title => $"Reminder: {title}",
                InterviewSubjectMany: count => $"{count} interviews coming up",
                InterviewHeadingOne: "You have an interview coming up",
                InterviewHeadingMany: "Interviews coming up",
                InterviewFooter: "Good luck.",
                ProviderLinkedSubject: provider => $"{provider} sign-in was added to your account",
                ProviderLinkedHeading: "A new sign-in method was added",
                ProviderLinkedBody: (brand, provider) =>
                    $"You can now sign in to {brand} with <strong>{provider}</strong>. Your password still works.",
                ProviderLinkedWarning: "If you did not do this, reset your password straight away.");
        }
    }
}
