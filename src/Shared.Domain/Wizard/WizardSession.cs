using System.Web;

namespace ConfReg.Domain.Wizard
{
    /// <summary>
    /// BASELINE-PROBLEM #1: business logic reaching straight into HttpContext.Current.Session.
    ///
    /// Why it hurts:
    ///  - Anything calling this needs an ASP.NET request in flight. The Windows Service, a
    ///    background job or a unit test has no HttpContext.Current, so it is null and this throws.
    ///  - System.Web does not exist on .NET 10, so this assembly cannot even load there.
    ///  - It is hidden coupling: nothing in a method signature says "I need a web request".
    /// Fixed in Phase 3: ISessionStore (legacy = HttpSessionState, modern = ISession).
    /// </summary>
    public static class WizardSession
    {
        private const string Key = "ConfReg.Wizard";

        public static WizardState Current
        {
            get
            {
                var session = HttpContext.Current.Session;
                var state = session[Key] as WizardState;
                if (state == null)
                {
                    state = new WizardState();
                    session[Key] = state;
                }
                return state;
            }
        }

        public static void Clear()
        {
            HttpContext.Current.Session.Remove(Key);
        }
    }
}
