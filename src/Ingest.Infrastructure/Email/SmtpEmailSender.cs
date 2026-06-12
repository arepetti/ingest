using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;

namespace Ingest.Infrastructure.Email;

/// <summary>
/// <see cref="IEmailSender"/> backed by the framework <see cref="SmtpClient"/>. Deliberately the
/// only place that touches SMTP, so swapping in a different transport (a SaaS provider, MailKit, …)
/// later is a one-class change with no ripple into the queue or notification logic.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly IEmailSecretProtector _protector;

    /// <summary>Create a new <see cref="SmtpEmailSender"/>.</summary>
    public SmtpEmailSender(IEmailSecretProtector protector) => _protector = protector;

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, EmailSettings settings, CancellationToken ct = default)
    {
        using var mail = new MailMessage
        {
            From = string.IsNullOrWhiteSpace(settings.FromName)
                ? new MailAddress(settings.FromAddress)
                : new MailAddress(settings.FromAddress, settings.FromName),
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            Body = message.TextBody,
            IsBodyHtml = false,
        };
        mail.To.Add(string.IsNullOrWhiteSpace(message.ToName)
            ? new MailAddress(message.ToAddress)
            : new MailAddress(message.ToAddress, message.ToName));

        // When the template produced HTML, send a proper multipart/alternative so clients that
        // prefer plain text still get a clean fallback.
        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.TextBody, Encoding.UTF8, MediaTypeNames.Text.Plain));
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.HtmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));
        }

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.UseStartTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };

        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            var password = _protector.Unprotect(settings.PasswordCipher) ?? "";
            client.Credentials = new NetworkCredential(settings.Username, password);
        }

        await client.SendMailAsync(mail, ct);
    }
}
