namespace PropTracker.Models
{
    public class Parlay
    {
        public int ParlayId { get; set; }
        public double Multi { get; set; }
        public List<int> PropId { get; set; }
        public ParlayResult Result { get; set; } = ParlayResult.Pending;
        public DateTime? HitAt { get; set; }

        public enum ParlayResult
        {
            Pending,
            Hit,
            Miss
        }
    }
}