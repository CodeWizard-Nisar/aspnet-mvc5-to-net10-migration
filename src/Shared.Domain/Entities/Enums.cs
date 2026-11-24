namespace ConfReg.Domain.Entities
{
    public enum TicketType
    {
        Standard = 1,
        Workshop = 2,
        Student = 3
    }

    public enum RegistrationStatus
    {
        Pending = 0,
        Paid = 1,
        Cancelled = 2,
        Expired = 3
    }

    public enum PaymentStatus
    {
        Succeeded = 0,
        Refunded = 1,
        Failed = 2
    }
}
