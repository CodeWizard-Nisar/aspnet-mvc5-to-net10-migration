namespace ConfReg.Domain.Abstractions
{
    public class EmailMessage
    {
        public string To { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
    }

    /// <summary>
    /// How the application sends mail. No real provider exists in this repo: the shipped implementation
    /// (FileEmailSender) writes each message to an outbox folder. A real SMTP or API sender is one class away.
    /// Implementations MUST throw if the message was not accepted: the confirmation job relies on that to retry.
    /// </summary>
    public interface IEmailSender
    {
        void Send(EmailMessage message);
    }
}
