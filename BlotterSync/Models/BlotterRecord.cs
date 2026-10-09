using System;
using System.Collections.Generic;

namespace BlotterSync.Models;

public partial class BlotterRecord
{
    public int RecordId { get; set; }

    public string TrackingNumber { get; set; } = null!;

    public int CategoryId { get; set; }

    public int DeskOfficerId { get; set; }

    public DateTime IncidentDate { get; set; }

    public DateTime? ReportedDate { get; set; }

    public string Location { get; set; } = null!;

    public string Narrative { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime? ResolutionDate { get; set; }

    public int? ComplainantId { get; set; }

    public int? RespondentId { get; set; }

    public virtual Category Category { get; set; } = null!;

    public virtual Resident? Complainant { get; set; }

    public virtual Officer DeskOfficer { get; set; } = null!;

    public virtual ICollection<Involvement> Involvements { get; set; } = new List<Involvement>();

    public virtual Resident? Respondent { get; set; }
}
