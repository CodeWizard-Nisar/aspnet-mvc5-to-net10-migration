using System;
using System.Collections.Generic;
using ConfReg.Domain.Entities;

namespace ConfReg.Domain.Wizard
{
    /// <summary>
    /// Everything the 4-step wizard remembers between requests, as a FLAT, SERIALISABLE value: ids and primitives only.
    ///
    /// BASELINE-PROBLEM #2, fixed in Phase 5. Phase 1's WizardState held whole EF entities (Attendee, Talk, Registration):
    /// dynamic proxies with navigation cycles that cannot be serialised. That was invisible while session lived in the
    /// worker process's memory, and fatal the moment session is shared with another process, because every value in it
    /// must then cross an HTTP boundary as JSON. Running it for real showed the two failure modes:
    ///   - leave the key UNREGISTERED with SystemWebAdapters and it is silently skipped: the other app just never sees it;
    ///   - register it and the serializer has to walk an EF object graph.
    /// This type is what makes the wizard hold shareable.
    ///
    /// Rules for anything stored in a shared session:
    ///   - public get/set properties of simple types, no behaviour, no computed properties (both apps must emit and
    ///     read exactly the same JSON, and System.Text.Json serialises every public getter);
    ///   - ids, not entities: re-load from the database when you need the row, so you see the truth, not a stale copy;
    ///   - its name and type are registered on BOTH sides (SessionKeys), or it does not cross.
    /// </summary>
    public class WizardSnapshot
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Company { get; set; }
        public string JobTitle { get; set; }
        public string DietaryRequirements { get; set; }

        public TicketType? TicketType { get; set; }
        public List<int> SelectedTalkIds { get; set; } = new List<int>();

        /// <summary>The Pending registration holding the seats. The DATABASE row is the truth; this is only the pointer to it.</summary>
        public int? RegistrationId { get; set; }
        public string ReferenceCode { get; set; }
        public decimal? TotalAmount { get; set; }

        /// <summary>When the attendee's hold lapses (their countdown). The expiry job decides from the row, not from this.</summary>
        public DateTime? HoldExpiresUtc { get; set; }
    }
}
