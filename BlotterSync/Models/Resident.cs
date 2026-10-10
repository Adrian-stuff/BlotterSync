namespace BlotterSync.Models
{
    public class Resident
    {
        public int ResidentId { get; set; }
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string? ContactNumber { get; set; }
        public string Address { get; set; } = null!;
        public string Zone { get; set; } = null!;

        public ICollection<BlotterRecord> BlotterRecordComplainants { get; set; } = new List<BlotterRecord>();
        public ICollection<BlotterRecord> BlotterRecordRespondents { get; set; } = new List<BlotterRecord>();
    }
}