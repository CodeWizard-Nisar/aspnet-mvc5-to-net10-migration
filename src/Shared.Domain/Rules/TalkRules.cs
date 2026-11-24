using System.Collections.Generic;
using System.Linq;
using ConfReg.Domain.Entities;

namespace ConfReg.Domain.Rules
{
    /// <summary>
    /// The talk-selection rules. Deliberately pure (entities in, messages out): this is the
    /// part of the business logic that already migrates cleanly, and it is what the Phase 2
    /// tests lean on hardest.
    /// </summary>
    public static class TalkRules
    {
        /// <summary>Half-open interval overlap: a talk ending at 10:30 does not clash with one starting at 10:30.</summary>
        public static bool Overlaps(Talk a, Talk b)
        {
            return a.StartsUtc < b.EndsUtc && b.StartsUtc < a.EndsUtc;
        }

        public static int Remaining(Talk talk, IDictionary<int, int> seatsTaken)
        {
            int taken;
            seatsTaken.TryGetValue(talk.Id, out taken);
            return talk.Capacity - taken;
        }

        public static List<string> Validate(IList<Talk> selected, IDictionary<int, int> seatsTaken, int maxTalks)
        {
            var errors = new List<string>();

            if (selected.Count > maxTalks)
                errors.Add("You can select at most " + maxTalks + " talks.");

            foreach (var talk in selected)
            {
                if (Remaining(talk, seatsTaken) <= 0)
                    errors.Add("\"" + talk.Title + "\" is full.");
            }

            var ordered = selected.OrderBy(t => t.StartsUtc).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                for (int j = i + 1; j < ordered.Count; j++)
                {
                    if (Overlaps(ordered[i], ordered[j]))
                        errors.Add("\"" + ordered[i].Title + "\" and \"" + ordered[j].Title + "\" overlap in time.");
                }
            }

            return errors;
        }
    }
}
