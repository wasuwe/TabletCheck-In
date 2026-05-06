using System;
using System.Net.Mail;

namespace TabletCheckIn.Utility
{
    public static class EmailService
    {
        private const string SmtpHost = "nonauth-smtp.global.canon.co.jp";
        private const int SmtpPort = 25;
        private const string SenderAddress = "Tablet-Check-In@mail.canon";

        public static void Send(string subject, string to, string body, bool isHtml = true, string cc = null)
        {
            using (var mail = new MailMessage())
            {
                mail.From = new MailAddress(SenderAddress);
                mail.Subject = subject;
                mail.Body = body;
                mail.IsBodyHtml = isHtml;

                foreach (var addr in SplitAddresses(to))
                    mail.To.Add(new MailAddress(addr));

                if (!string.IsNullOrWhiteSpace(cc))
                    foreach (var addr in SplitAddresses(cc))
                        mail.CC.Add(new MailAddress(addr));

                using (var smtp = new SmtpClient(SmtpHost, SmtpPort))
                {
                    smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                    smtp.UseDefaultCredentials = false;
                    if (mail.To.Count > 0 || mail.CC.Count > 0)
                        smtp.Send(mail);
                }
            }
        }

        private static string[] SplitAddresses(string input)
        {
            return input.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
