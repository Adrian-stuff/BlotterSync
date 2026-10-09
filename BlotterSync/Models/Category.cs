using System;
using System.Collections.Generic;

namespace BlotterSync.Models;

public partial class Category
{
    public int CategoryId { get; set; }

    public string Name { get; set; } = null!;

    public int SeverityLevel { get; set; }

    public virtual ICollection<BlotterRecord> BlotterRecords { get; set; } = new List<BlotterRecord>();
}
