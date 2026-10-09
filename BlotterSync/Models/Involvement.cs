using System;
using System.Collections.Generic;

namespace BlotterSync.Models;

public partial class Involvement
{
    public int InvolvementId { get; set; }

    public int RecordId { get; set; }

    public int CitizenId { get; set; }

    public string Role { get; set; } = null!;

    public string? Statement { get; set; }

    public virtual Citizen Citizen { get; set; } = null!;

    public virtual BlotterRecord Record { get; set; } = null!;
}
