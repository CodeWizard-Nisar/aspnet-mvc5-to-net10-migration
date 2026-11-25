using ConfReg.Domain.Wizard;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SystemWebAdapters;
using System.Web.SessionState;

namespace ConfReg.Modern.Controllers;

/// <summary>
/// "What does the modern app see in the shared session?" The page exists to make the cross-app session observable: it reads,
/// through WizardSession, the WizardSnapshot that LEGACY wrote when a visitor was part-way through registering. If that value
/// appears here, the whole chain works: both sides registered the key, the types agree, the API key matches, and the session
/// cookie crossed the gateway. Admin-only (the snapshot holds a visitor's name and email).
/// </summary>
// ReadOnly + PreLoad: fetch the legacy session BEFORE the action runs (a synchronous session read needs it loaded), without a write lock.
[Session(SessionBehavior = SessionStateBehavior.ReadOnly, IsPreLoad = true)]
public class DiagnosticsController(WizardSession wizard) : AppController
{
    [HttpGet("/Admin/Diagnostics/Session")]
    public IActionResult SessionProbe()
    {
        var snapshot = wizard.Load();
        return Respond(new
        {
            Source = "ConfReg.Modern.Web",
            HoldVisibleFromLegacySession = snapshot.RegistrationId != null,
            snapshot.ReferenceCode,
            snapshot.TicketType,
            snapshot.TotalAmount,
            snapshot.HoldExpiresUtc,
            SelectedTalkCount = snapshot.SelectedTalkIds.Count,
            snapshot.Email
        });
    }
}
