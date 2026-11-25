using ConfReg.Domain.Abstractions;

namespace ConfReg.Domain.Wizard
{
    /// <summary>
    /// The wizard's view of the visitor's session, over an ISessionStore (BASELINE-PROBLEM #1, fixed in Phase 3).
    ///
    /// Phase 5: the stored value is a flat WizardSnapshot, and writing back is EXPLICIT (Save). With in-process session
    /// the object you read is the stored object, so mutating it "just works"; with a remote or serialised session the
    /// object you read is a copy and mutations are lost unless you store it again. Code that is going to be shared across
    /// processes must not rely on the former, so Save() is part of the contract.
    /// </summary>
    public class WizardSession
    {
        private readonly ISessionStore _session;
        public WizardSession(ISessionStore session) { _session = session; }

        /// <summary>The current snapshot, never null (a fresh one if the visitor has none). Call Save after changing it.</summary>
        public WizardSnapshot Load()
        {
            return _session.Get<WizardSnapshot>(SessionKeys.Wizard) ?? new WizardSnapshot();
        }

        public void Save(WizardSnapshot snapshot) { _session.Set(SessionKeys.Wizard, snapshot); }

        public void Clear() { _session.Remove(SessionKeys.Wizard); }
    }
}
