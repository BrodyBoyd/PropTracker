namespace PropTracker.Models
{
    public class Prop
    {
        public int PropId { get; set; }
        public string PlayerFirstName { get; set; }
        public string PlayerLastName { get; set; }
        public BetType PropType { get; set; }
        public double PropValue { get; set; }
        public int ParlayId { get; set; }
        public PropResult Result { get; set; } = PropResult.Pending;

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

        public enum PropResult
        {
            Pending,
            Hit,
            Miss
        }
    }
}