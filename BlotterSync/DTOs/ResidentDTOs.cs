using System.ComponentModel.DataAnnotations;

namespace BlotterSync.DTOs
{
    public class ResidentDTOs
    {
        public class ResidentDTO
        {
            public int ResidentId { get; set; }
            public string FirstName { get; set; } = null!;
            public string LastName { get; set; } = null!;
            public string? ContactNumber { get; set; }
            public string Address { get; set; } = null!;
        }

        public class CreateResidentDTO
        {
            [Required]
            [StringLength(100)]
            public string FirstName { get; set; } = null!;

            [Required]
            [StringLength(100)]
            public string LastName { get; set; } = null!;

            [StringLength(20)]
            public string? ContactNumber { get; set; }

            [Required]
            [StringLength(255)]
            public string Address { get; set; } = null!;
        }
    }
}