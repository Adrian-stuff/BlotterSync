using System;
using System.Collections.Generic;

namespace BlotterSync.Models;

public partial class Resident
{
    public int ResidentId { get; set; }

    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public string Address { get; set; } = null!;

    public string? ContactNumber { get; set; }

    public virtual ICollection<BlotterRecord> BlotterRecordComplainants { get; set; } = new List<BlotterRecord>();

    public virtual ICollection<BlotterRecord> BlotterRecordRespondents { get; set; } = new List<BlotterRecord>();
}
