using ConfReg.Domain.Abstractions;

namespace ConfReg.Domain.Wizard
{
    /// <summary>
    /// The wizard's view of the visitor's session.
    ///
    /// Phase 1 (BASELINE-PROBLEM #1) this was a static class calling HttpContext.Current.Session. It is now an
    /// ordinary object that is handed an ISessionStore. Behaviour is identical; what changed is that it no longer
    /// needs a web request to exist, so it loads on .NET 10, runs in a unit test, and can be given a different
    /// session technology (ASP.NET Core's ISession in Phase 5) without touching this file.
    /// </summary>
    public class WizardSession
    {
        private const string Key = "ConfReg.Wizard";
        private readonly ISessionStore _session;

        public WizardSession(ISessionStore session) { _session = session; }

        public WizardState Current
        {
            get
            {
                var state = _session.Get<WizardState>(Key);
                if (state == null)
                {
                    state = new WizardState();
                    _session.Set(Key, state);
                }
                return state;
            }
        }

        public void Clear() { _session.Remove(Key); }
    }
}
