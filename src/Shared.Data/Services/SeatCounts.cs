using System.Collections.Generic;
using System.Linq;
using ConfReg.Domain.Entities;

namespace ConfReg.Data.Services
{
    /// <summary>Seats currently held per talk. Pending and Paid hold seats; Cancelled and Expired do not.</summary>
    public static class SeatCounts
    {
        public static Dictionary<int, int> For(ConfRegContext ctx)
        {
            return ctx.RegistrationTalks
                .Where(rt => rt.Registration.Status == RegistrationStatus.Pending
                          || rt.Registration.Status == RegistrationStatus.Paid)
                .GroupBy(rt => rt.TalkId)
                .Select(g => new { TalkId = g.Key, Count = g.Count() })
                .ToDictionary(x => x.TalkId, x => x.Count);
        }
    }
}
