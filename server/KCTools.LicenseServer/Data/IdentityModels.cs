using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;

namespace KCTools.LicenseServer.Data
{
    /// <summary>Identity kullanıcısı. Parola hash'i Identity tarafından yönetilir.</summary>
    public sealed class AppUser : IdentityUser<Guid>
    {
        public string? DisplayName { get; set; }

        public bool IsActive { get; set; } = true;

        public bool MustChangePassword { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? LastLoginAt { get; set; }

        public ICollection<License> Licenses { get; set; } = new List<License>();

        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    }

    public sealed class AppRole : IdentityRole<Guid>
    {
        public AppRole()
        {
        }

        public AppRole(string name)
            : base(name)
        {
        }
    }

    public static class AppRoles
    {
        public const string Admin = "Admin";

        public const string Customer = "Customer";
    }
}
