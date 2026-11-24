using System;
using System.IO;
using System.Text;
using ConfReg.Domain.Abstractions;

namespace ConfReg.Data.Infrastructure
{
    /// <summary>Stub mail provider: writes an .eml file per message to ~/App_Data/outbox. Safe to run anywhere.</summary>
    public class FileEmailSender : IEmailSender
    {
        private readonly IAppPaths _paths;
        public FileEmailSender(IAppPaths paths) { _paths = paths; }

        public void Send(EmailMessage message)
        {
            string dir = _paths.Resolve("~/App_Data/outbox");
            Directory.CreateDirectory(dir);

            // Unique name per message: two workers (or a retry) can never overwrite each other's file.
            string file = Path.Combine(dir, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".eml");
            var sb = new StringBuilder();
            sb.Append("To: ").AppendLine(Clean(message.To));
            sb.Append("Subject: ").AppendLine(Clean(message.Subject));
            sb.AppendLine("Content-Type: text/plain; charset=utf-8");
            sb.AppendLine();
            sb.AppendLine(message.Body);
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
        }

        // Header injection guard: a CR or LF in a header value would let a registrant add headers of their own.
        private static string Clean(string value) { return (value ?? "").Replace("\r", " ").Replace("\n", " "); }
    }
}
