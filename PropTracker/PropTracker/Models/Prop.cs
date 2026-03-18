namespace PropTracker.Models
{
    public class Prop
    {
        public int PropId { get; set; }
        public string PlayerFirstName { get; set; }
        public string PlayerLastName { get; set; }
        public BetType PropType { get; set; }
        public double PropValue { get; set; }
        public OverUnderType OverUnder { get; set; } = OverUnderType.Over;
        public int ParlayId { get; set; }
        public PropResult Result { get; set; } = PropResult.Pending;

        // ESPN athlete ID — populated via the player search on the Create/Edit form.
        // Used by NbaStatsService to fetch game logs and auto-check pending props.
        // Find a player's ESPN ID at: espn.com/nba/player/_/id/{EspnPlayerId}
        public int EspnPlayerId { get; set; }

        // The date of the game this prop is for.
        // When set, CheckPropResultAsync targets the exact game instead of the most recent one.
        public DateTime? GameDate { get; set; }

        public enum BetType
        {
            PTS,
            PA,
            PR,
            AR,
            AST,
            REB,
            PRA,
            THREEMADE,
            DD,
            TD
        }

        public enum OverUnderType
        {
            Over,
            Under
        }

        public enum PropResult
        {
            Pending,
            Hit,
            Miss
        }
    }
}