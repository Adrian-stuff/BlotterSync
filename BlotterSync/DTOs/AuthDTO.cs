using System.ComponentModel.DataAnnotations;

namespace BlotterSync.DTOs
{
    public class LoginDTO
    {
        [Required]
        public string Username { get; set; } = null!;

        [Required]
        public string Password { get; set; } = null!;
    }

    public class RegisterDTO
    {
        [Required]
        [StringLength(50)]
        public string BadgeNumber { get; set; } = null!;

        [Required]
        [StringLength(100)]
        public string FirstName { get; set; } = null!;

        [Required]
        [StringLength(100)]
        public string LastName { get; set; } = null!;

        [Required]
        [StringLength(50, MinimumLength = 3)]
        public string Username { get; set; } = null!;

        [Required]
        [StringLength(100, MinimumLength = 8)]
        public string Password { get; set; } = null!;
    }
}