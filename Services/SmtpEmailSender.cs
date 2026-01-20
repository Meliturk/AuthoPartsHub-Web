using System.Net;
using System.Net.Mail;
using System.Text;

namespace AutoPartsWeb.Services
{
    public class SmtpEmailSender : IEmailSender
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendAsync(string to, string subject, string body)
        {
            var host = _config["Email:Smtp:Host"];
            if (string.IsNullOrWhiteSpace(host))
            {
                _logger.LogWarning("Email not configured. To={To} Subject={Subject} Body={Body}", to, subject, body);
                return;
            }

            var port = int.TryParse(_config["Email:Smtp:Port"], out var p) ? p : 587;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) && port == 2525)
            {
                _logger.LogInformation("Dev email preview. To={To} Subject={Subject} Body={Body}", to, subject, body);
                return;
            }
            var user = _config["Email:Smtp:User"];
            var pass = _config["Email:Smtp:Pass"];
            var enableSsl = !string.Equals(_config["Email:Smtp:EnableSsl"], "false", StringComparison.OrdinalIgnoreCase);
            var fromAddress = _config["Email:FromAddress"] ?? user ?? "no-reply@localhost";
            var fromName = _config["Email:FromName"] ?? "AutoParts Hub";

            try
            {
                using var message = new MailMessage
                {
                    From = new MailAddress(fromAddress, fromName),
                    Subject = subject,
                    Body = body,
                    SubjectEncoding = Encoding.UTF8,
                    BodyEncoding = Encoding.UTF8
                };
                message.To.Add(to);

                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = enableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };

                if (!string.IsNullOrWhiteSpace(user))
                {
                    client.Credentials = new NetworkCredential(user, pass);
                }

                await client.SendMailAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email send failed for {To}", to);
            }
        }
    }
}
