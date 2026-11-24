using System;
using System.ComponentModel.DataAnnotations;

namespace ConfReg.Domain.Entities
{
    public class Payment
    {
        public int Id { get; set; }
        public int RegistrationId { get; set; }
        public virtual Registration Registration { get; set; }

        public decimal Amount { get; set; }

        [Required, StringLength(50)]
        public string Provider { get; set; }

        [StringLength(100)]
        public string ProviderRef { get; set; }

        public PaymentStatus Status { get; set; }
        public DateTime ProcessedUtc { get; set; }
    }
}
