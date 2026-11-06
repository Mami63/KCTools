using System;
using System.Collections.Generic;

namespace KCTools.LicenseServer.Data
{
    /// <summary>Lisans anahtarı: plan, kredi, geçerlilik aralığı ve cihaz limiti.</summary>
    public sealed class License
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Key { get; set; } = string.Empty;

        public string Plan { get; set; } = "PRO";

        public int Credits { get; set; }

        /// <summary>Varsayılan olarak tek bilgisayar.</summary>
        public int MaxDevices { get; set; } = 1;

        public DateTimeOffset StartsAt { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset EndsAt { get; set; } = DateTimeOffset.UtcNow.AddDays(365);

        public bool IsRevoked { get; set; }

        public bool IsFrozen { get; set; }

        public DateTimeOffset? FrozenAt { get; set; }

        public string? FreezeReason { get; set; }

        public int ExtendedCount { get; set; }

        public DateTimeOffset? RevokedAt { get; set; }

        public string? RevokedReason { get; set; }

        public Guid? UserId { get; set; }

        public AppUser? User { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public ICollection<Device> Devices { get; set; } = new List<Device>();

        public bool IsUsable(DateTimeOffset now)
        {
            return !IsRevoked && !IsFrozen && now >= StartsAt && now <= EndsAt;
        }
    }

    /// <summary>Lisansın bağlandığı bilgisayar. Anahtar sahipliği HWID'den önce gelir.</summary>
    public sealed class Device
    {
        public Guid Id { get; set; }

        public Guid LicenseId { get; set; }

        public License License { get; set; } = null!;

        /// <summary>İstemcinin ürettiği kalıcı cihaz kimliği.</summary>
        public Guid DeviceId { get; set; }

        public string Hwid { get; set; } = string.Empty;

        public string? MachineName { get; set; }

        /// <summary>SPKI PEM açık anahtar; özel anahtar yalnızca istemcide kalır.</summary>
        public string? PublicKeyPem { get; set; }

        /// <summary>SPKI SHA-256 parmak izi (base64url).</summary>
        public string? KeyThumbprint { get; set; }

        public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? ActivatedAt { get; set; }

        public DateTimeOffset? LastProofAt { get; set; }

        public DateTimeOffset? RevokedAt { get; set; }

        public string? RevokedBy { get; set; }

        public bool IsActive { get; set; } = true;
    }

    /// <summary>Tek kullanımlık, süreli aktivasyon nonce'u.</summary>
    public sealed class DeviceChallenge
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid LicenseId { get; set; }

        public Guid DeviceId { get; set; }

        public string NonceHash { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset ExpiresAt { get; set; }

        public DateTimeOffset? UsedAt { get; set; }
    }

    /// <summary>Aktivasyon / sıfırlama / iptal geçmişi.</summary>
    public sealed class ActivationHistory
    {
        public long Id { get; set; }

        public Guid LicenseId { get; set; }

        public Guid DeviceId { get; set; }

        public string Hwid { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string Actor { get; set; } = "system";

        public string? Details { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>Yenileme token'ı yalnızca hash olarak saklanır; ham değer istemciye bir kez verilir.</summary>
    public sealed class RefreshToken
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }

        public AppUser User { get; set; } = null!;

        public string TokenHash { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(30);

        public DateTimeOffset? RevokedAt { get; set; }

        public string? ReplacedByTokenHash { get; set; }

        public string? CreatedByIp { get; set; }

        public bool IsActive(DateTimeOffset now) =>
            RevokedAt == null && now < ExpiresAt;
    }

    /// <summary>Yönetici ve güvenlik olayları için değiştirilemez kayıt.</summary>
    public sealed class AuditLog
    {
        public long Id { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public string Actor { get; set; } = "system";

        public string Action { get; set; } = string.Empty;

        public string? EntityType { get; set; }

        public string? EntityId { get; set; }

        public string? Details { get; set; }

        public string? IpAddress { get; set; }
    }
}
