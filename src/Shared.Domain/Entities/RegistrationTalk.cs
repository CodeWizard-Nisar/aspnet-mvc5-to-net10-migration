namespace ConfReg.Domain.Entities
{
    /// <summary>
    /// Join row. An existing row for a Pending or Paid registration IS a held seat;
    /// there is no separate "seats taken" counter to drift out of sync.
    /// </summary>
    public class RegistrationTalk
    {
        public int RegistrationId { get; set; }
        public virtual Registration Registration { get; set; }

        public int TalkId { get; set; }
        public virtual Talk Talk { get; set; }
    }
}
