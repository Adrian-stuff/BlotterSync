using System;
using System.Collections.Generic;

namespace BlotterSync.Models;

public partial class Citizen
{
    public int CitizenId { get; set; }

    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public string Address { get; set; } = null!;

    public string? ContactNumber { get; set; }

    public virtual ICollection<Involvement> Involvements { get; set; } = new List<Involvement>();
}
