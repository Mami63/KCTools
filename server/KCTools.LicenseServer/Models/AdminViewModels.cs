using System.ComponentModel.DataAnnotations;

namespace KCTools.LicenseServer.Models
{
    public sealed class LoginViewModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }

        public string? ReturnUrl { get; set; }
    }

    public sealed class CreateUserModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(10)]
        public string Password { get; set; } = string.Empty;

        public string Role { get; set; } = "Customer";
    }

    public sealed class CreateLicenseModel
    {
        public string? Key { get; set; }

        public string Plan { get; set; } = "PRO";

        [Range(0, 100000)]
        public int Credits { get; set; } = 100;

        [Range(1, 3650)]
        public int Days { get; set; } = 365;

        public string? Email { get; set; }
    }

    public sealed class ExtendLicenseModel
    {
        [Required]
        public string Key { get; set; } = string.Empty;

        [Range(1, 3650)]
        public int Days { get; set; } = 30;
    }

    public sealed class RevokeLicenseModel
    {
        [Required]
        public string Key { get; set; } = string.Empty;

        public string? Reason { get; set; }
    }
}
