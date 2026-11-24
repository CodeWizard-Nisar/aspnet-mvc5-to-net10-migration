using System;
using System.Collections.Generic;
using ConfReg.Domain.Entities;

namespace ConfReg.Domain.Wizard
{
    /// <summary>
    /// Everything the 4-step wizard remembers between requests.
    ///
    /// BASELINE-PROBLEM #2: this holds WHOLE EF entities (Attendee, Talk, Registration),
    /// not ids or flat DTOs. The consequences, each of which bites during migration:
    ///  - The entities are EF dynamic proxies with navigation properties and cycles
    ///    (Registration -> Attendee -> Registrations -> ...). They cannot be serialised.
    ///    InProc session never serialises, so the app "works"; the moment session is shared
    ///    with another process (Phase 5) every Session[...] write has to be serialised.
    ///  - They are detached from the DbContext that loaded them, so RegistrationService.Pay
    ///    re-Attach()es them, which is how a stale entity can overwrite fresher DB state.
    ///  - The session is a second copy of truth about the hold (HoldExpiresUtc) next to the
    ///    DB row the expiry job looks at.
    /// STILL PRESENT after Phase 3 on purpose: Phase 3 moves WHERE session is reached from, not WHAT is kept in it.
    /// Fixed in Phase 5: replaced by a flat, serialisable WizardSnapshot of ids and primitives.
    /// </summary>
    public class WizardState
    {
        public Attendee Attendee { get; set; }
        public TicketType? TicketType { get; set; }
        public List<Talk> SelectedTalks { get; set; } = new List<Talk>();
        public Registration Registration { get; set; }

        /// <summary>When the seat hold lapses. Lives in Session, deliberately: see docs/01-baseline.md.</summary>
        public DateTime? HoldExpiresUtc { get; set; }
    }
}
