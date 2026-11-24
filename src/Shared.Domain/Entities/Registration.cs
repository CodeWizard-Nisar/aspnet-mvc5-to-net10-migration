using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ConfReg.Domain.Entities
{
    public class Registration
    {
        public int Id { get; set; }

        [Required, StringLength(20)]
        [Index(IsUnique = true)]
        public string ReferenceCode { get; set; }

        public int AttendeeId { get; set; }
        public virtual Attendee Attendee { get; set; }

        public TicketType TicketType { get; set; }
        public RegistrationStatus Status { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? ConfirmedUtc { get; set; }

        // Not in the original entity list. Added because the "confirmation email" job
        // (Phase 4) must be idempotent: it needs to know which paid registrations it
        // has already mailed. See docs/01-baseline.md, "Additions to the spec".
        public DateTime? ConfirmationSentUtc { get; set; }

        public virtual ICollection<RegistrationTalk> RegistrationTalks { get; set; }
        public virtual ICollection<Payment> Payments { get; set; }
    }
}
