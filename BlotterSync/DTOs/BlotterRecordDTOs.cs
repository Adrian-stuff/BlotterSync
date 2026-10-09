namespace BlotterSync.DTOs
{
    public class BlotterRecordDTOs
    {
        public class BlotterRecordDTO
        {
            public int RecordId { get; set; }
            public string TrackingNumber { get; set; } = null!;
            public int? ComplainantId { get; set; }
            public int? RespondentId { get; set; }
            public DateTime IncidentDate { get; set; }
            public DateTime? ReportedDate { get; set; }
            public string Location { get; set; } = null!;
            public string Narrative { get; set; } = null!;
            public string Status { get; set; } = null!;
        }

        public class CreateBlotterRecordDTO
        {
            public int CategoryId { get; set; }
            public int DeskOfficerId { get; set; }
            public int? ComplainantId { get; set; }
            public int? RespondentId { get; set; }
            public DateTime IncidentDate { get; set; }
            public string Location { get; set; } = null!;
            public string Narrative { get; set; } = null!;
        }

        public class UpdateBlotterRecordDTO
        {
            public string Narrative { get; set; } = null!;
            public string Status { get; set; } = null!;
            public DateTime? ResolutionDate { get; set; }
        }
    }
}