namespace BlotterSync.Models
{
    public class Announcement
    {
        public int Id { get; set; }
        public string Message { get; set; } = null!;
        public DateTime DatePosted { get; set; }
        public int PostedByOfficerId { get; set; }
        public bool IsActive { get; set; }
    }
}