using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using KCTools.LicenseServer.Data;
using Microsoft.EntityFrameworkCore;

namespace KCTools.LicenseServer.Services
{
    public sealed record ActivationOutcome(
        bool Success, int StatusCode, string? Error, License? License, Device? Device);

    public sealed record DeviceActivationOutcome(
        bool Success, int StatusCode, string? Error, License? License, Device? Device);

    public sealed record ConsumeOutcome(
        bool Success, int StatusCode, string? Error, int Credits);

    /// <summary>
    /// Lisans/cihaz/kullanıcı iş kurallarının tek noktası. Tüm kurallar burada
    /// uygulanır: iptal, tarih aralığı, tek cihaz, kredi, audit.
    /// </summary>
    public sealed class LicenseRepository
    {
        private static readonly char[] KeyAlphabet =
            "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

        private readonly LicenseDbContext _db;

        public LicenseRepository(LicenseDbContext db)
        {
            _db = db;
        }

        // ------------------------------------------------------------ lisans

        public Task<License?> FindLicenseAsync(string key)
        {
            var normalized = NormalizeKey(key);

            return _db.Licenses
                .Include(item => item.Devices)
                .FirstOrDefaultAsync(item => item.Key == normalized);
        }

        public async Task<ActivationOutcome> ActivateAsync(
            string key, string hwid, string? machineName, string? ip)
        {
            var normalized = NormalizeKey(key);
            hwid = (hwid ?? string.Empty).Trim();

            if (hwid.Length < 8)
            {
                return new ActivationOutcome(false, 400, "Geçersiz HWID.", null, null);
            }

            var license = await _db.Licenses
                .Include(item => item.Devices)
                .FirstOrDefaultAsync(item => item.Key == normalized);

            if (license == null)
            {
                return new ActivationOutcome(false, 404, "Lisans bulunamadı.", null, null);
            }

            var now = DateTimeOffset.UtcNow;

            if (license.IsRevoked)
            {
                return new ActivationOutcome(false, 409, "Lisans iptal edilmiş.", license, null);
            }

            if (license.IsFrozen)
            {
                return new ActivationOutcome(false, 409, "Lisans dondurulmuş.", license, null);
            }

            if (now < license.StartsAt)
            {
                return new ActivationOutcome(false, 409, "Lisans henüz başlamadı.", license, null);
            }

            if (now > license.EndsAt)
            {
                return new ActivationOutcome(false, 409, "Lisans süresi doldu.", license, null);
            }

            var device = license.Devices
                .FirstOrDefault(item => item.IsActive &&
                                        string.Equals(item.Hwid, hwid, StringComparison.OrdinalIgnoreCase));

            if (device == null)
            {
                var activeCount = license.Devices.Count(item => item.IsActive);

                if (activeCount >= license.MaxDevices)
                {
                    return new ActivationOutcome(
                        false, 409, "Lisans başka bir bilgisayara bağlı.", license, null);
                }

                device = new Device
                {
                    LicenseId = license.Id,
                    Hwid = hwid,
                    MachineName = machineName,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    IsActive = true
                };

                _db.Devices.Add(device);
            }
            else
            {
                device.LastSeenAt = now;
                device.MachineName = machineName ?? device.MachineName;
            }

            await _db.SaveChangesAsync();
            await LogAuditAsync(
                "device:" + hwid, "license.activate", "License", license.Key,
                "Plan " + license.Plan + " · " + license.Credits + " kredi", ip);

            return new ActivationOutcome(true, 200, null, license, device);
        }

        public async Task<ActivationOutcome> ValidateAsync(string key, string hwid)
        {
            var normalized = NormalizeKey(key);
            hwid = (hwid ?? string.Empty).Trim();

            var license = await _db.Licenses
                .Include(item => item.Devices)
                .FirstOrDefaultAsync(item => item.Key == normalized);

            if (license == null)
            {
                return new ActivationOutcome(false, 404, "Lisans bulunamadı.", null, null);
            }

            var now = DateTimeOffset.UtcNow;

            if (license.IsRevoked)
            {
                return new ActivationOutcome(false, 409, "Lisans iptal edilmiş.", license, null);
            }

            if (license.IsFrozen)
            {
                return new ActivationOutcome(false, 409, "Lisans dondurulmuş.", license, null);
            }

            if (!license.IsUsable(now))
            {
                return new ActivationOutcome(false, 409, "Lisans geçerlilik aralığı dışında.", license, null);
            }

            var device = license.Devices.FirstOrDefault(
                item => item.IsActive &&
                        string.Equals(item.Hwid, hwid, StringComparison.OrdinalIgnoreCase));

            if (device == null)
            {
                return new ActivationOutcome(false, 409, "Bu bilgisayar lisansa bağlı değil.", license, null);
            }

            device.LastSeenAt = now;
            await _db.SaveChangesAsync();

            return new ActivationOutcome(true, 200, null, license, device);
        }

        public async Task<ConsumeOutcome> ConsumeAsync(
            string key, string hwid, int amount, string? operation, string? ip)
        {
            if (amount <= 0)
            {
                return new ConsumeOutcome(false, 400, "Miktar 0'dan büyük olmalı.", 0);
            }

            var validation = await ValidateAsync(key, hwid);

            if (!validation.Success || validation.License == null)
            {
                return new ConsumeOutcome(
                    false, validation.StatusCode, validation.Error, validation.License?.Credits ?? 0);
            }

            var license = validation.License;

            if (license.Credits < amount)
            {
                return new ConsumeOutcome(false, 409, "Yetersiz kredi.", license.Credits);
            }

            license.Credits -= amount;
            await _db.SaveChangesAsync();

            await LogAuditAsync(
                "device:" + hwid, "license.consume", "License", license.Key,
                amount + " kredi · " + operation + " · kalan " + license.Credits, ip);

            return new ConsumeOutcome(true, 200, null, license.Credits);
        }

        public async Task<(License? License, string? Error)> CreateLicenseAsync(
            string? key, string plan, int credits, int days, Guid? userId,
            string actor, string? ip)
        {
            plan = string.IsNullOrWhiteSpace(plan) ? "PRO" : plan.Trim().ToUpperInvariant();
            credits = Math.Max(0, credits);
            days = Math.Clamp(days, 1, 3650);

            var normalized = string.IsNullOrWhiteSpace(key)
                ? await GenerateUniqueKeyAsync()
                : NormalizeKey(key);

            if (await _db.Licenses.AnyAsync(item => item.Key == normalized))
            {
                return (null, "Bu anahtar zaten var.");
            }

            if (userId.HasValue &&
                !await _db.Users.AnyAsync(item => item.Id == userId.Value))
            {
                return (null, "Kullanıcı bulunamadı.");
            }

            var now = DateTimeOffset.UtcNow;

            var license = new License
            {
                Key = normalized,
                Plan = plan,
                Credits = credits,
                MaxDevices = 1,
                StartsAt = now,
                EndsAt = now.AddDays(days),
                UserId = userId,
                CreatedAt = now
            };

            _db.Licenses.Add(license);
            await _db.SaveChangesAsync();

            await LogAuditAsync(
                actor, "admin.license.create", "License", license.Key,
                plan + " · " + credits + " kredi · " + days + " gün", ip);

            return (license, null);
        }

        public async Task<(bool Ok, string? Error)> RevokeLicenseAsync(
            string key, string? reason, string actor, string? ip)
        {
            var normalized = NormalizeKey(key);

            var license = await _db.Licenses
                .Include(item => item.Devices)
                .FirstOrDefaultAsync(item => item.Key == normalized);

            if (license == null)
            {
                return (false, "Lisans bulunamadı.");
            }

            if (license.IsRevoked)
            {
                return (true, null);
            }

            license.IsRevoked = true;
            license.RevokedAt = DateTimeOffset.UtcNow;
            license.RevokedReason = reason;

            foreach (var device in license.Devices)
            {
                device.IsActive = false;
            }

            await _db.SaveChangesAsync();

            await LogAuditAsync(
                actor, "admin.license.revoke", "License", license.Key,
                reason ?? "sebep belirtilmedi", ip);

            return (true, null);
        }

        public async Task<(License? License, string? Error)> ExtendLicenseAsync(
            string key, int days, string actor, string? ip)
        {
            var license = await _db.Licenses
                .FirstOrDefaultAsync(item => item.Key == NormalizeKey(key));

            if (license == null)
            {
                return (null, "Lisans bulunamadı.");
            }

            if (license.IsRevoked)
            {
                return (null, "İptal edilen lisans uzatılamaz.");
            }

            license.EndsAt = license.EndsAt.AddDays(Math.Clamp(days, 1, 3650));
            license.ExtendedCount += 1;

            await _db.SaveChangesAsync();

            await LogAuditAsync(
                actor, "admin.license.extend", "License", license.Key,
                "+" + days + " gün · yeni bitiş " + license.EndsAt.ToString("yyyy-MM-dd"), ip);

            return (license, null);
        }

        public async Task<(License? License, string? Error)> FreezeLicenseAsync(
            string key, string? reason, string actor, string? ip)
        {
            var license = await _db.Licenses
                .FirstOrDefaultAsync(item => item.Key == NormalizeKey(key));

            if (license == null)
            {
                return (null, "Lisans bulunamadı.");
            }

            if (license.IsRevoked)
            {
                return (null, "İptal edilen lisans dondurulamaz.");
            }

            license.IsFrozen = true;
            license.FrozenAt = DateTimeOffset.UtcNow;
            license.FreezeReason = reason;

            await _db.SaveChangesAsync();

            await LogAuditAsync(
                actor, "admin.license.freeze", "License", license.Key,
                reason ?? "sebep belirtilmedi", ip);

            return (license, null);
        }

        public async Task<(License? License, string? Error)> UnfreezeLicenseAsync(
            string key, string actor, string? ip)
        {
            var license = await _db.Licenses
                .FirstOrDefaultAsync(item => item.Key == NormalizeKey(key));

            if (license == null)
            {
                return (null, "Lisans bulunamadı.");
            }

            license.IsFrozen = false;
            license.FrozenAt = null;
            license.FreezeReason = null;

            await _db.SaveChangesAsync();

            await LogAuditAsync(actor, "admin.license.unfreeze", "License", license.Key, null, ip);

            return (license, null);
        }

        public async Task<List<License>> GetLicensesAsync(int take = 200)
        {
            // SQLite DateTimeOffset ORDER BY desteklemez: sıralama bellekte yapılır.
            var items = await _db.Licenses
                .Include(item => item.User)
                .Include(item => item.Devices)
                .ToListAsync();

            return items
                .OrderByDescending(item => item.CreatedAt)
                .Take(Math.Clamp(take, 1, 1000))
                .ToList();
        }

        public async Task<List<Device>> GetDevicesAsync(int take = 200)
        {
            var devices = await _db.Devices
                .Include(item => item.License)
                .ToListAsync();

            return devices
                .OrderByDescending(item => item.LastSeenAt)
                .Take(Math.Clamp(take, 1, 1000))
                .ToList();
        }
        // ------------------------------------------------------------ refresh token

        public async Task<(string? RawToken, DateTimeOffset? ExpiresAt, string? Error)>
            CreateRefreshTokenAsync(AppUser user, string? ip)
        {
            var raw = PasswordHasher.NewToken();
            var now = DateTimeOffset.UtcNow;

            var token = new RefreshToken
            {
                UserId = user.Id,
                TokenHash = PasswordHasher.HashToken(raw),
                CreatedAt = now,
                ExpiresAt = now.AddDays(30),
                CreatedByIp = ip
            };

            _db.RefreshTokens.Add(token);
            await _db.SaveChangesAsync();

            return (raw, token.ExpiresAt, null);
        }

        public async Task<(AppUser? User, string? RawToken, DateTimeOffset? ExpiresAt, string? Error)>
            RotateRefreshTokenAsync(string rawToken, string? ip)
        {
            var hash = PasswordHasher.HashToken(rawToken ?? string.Empty);

            var current = await _db.RefreshTokens
                .Include(item => item.User)
                .FirstOrDefaultAsync(item => item.TokenHash == hash);

            if (current == null)
            {
                return (null, null, null, "Yenileme token'ı geçersiz.");
            }

            if (!current.IsActive(DateTimeOffset.UtcNow))
            {
                return (null, null, null, "Yenileme token'ı süresi dolmuş veya iptal edilmiş.");
            }

            var raw = PasswordHasher.NewToken();
            var now = DateTimeOffset.UtcNow;

            current.RevokedAt = now;
            current.ReplacedByTokenHash = PasswordHasher.HashToken(raw);

            var replacement = new RefreshToken
            {
                UserId = current.UserId,
                TokenHash = current.ReplacedByTokenHash,
                CreatedAt = now,
                ExpiresAt = now.AddDays(30),
                CreatedByIp = ip
            };

            _db.RefreshTokens.Add(replacement);
            await _db.SaveChangesAsync();

            return (current.User, raw, replacement.ExpiresAt, null);
        }

        // ------------------------------------------------------------ cihaz anahtarı

        public async Task<(string? Nonce, DateTimeOffset? ExpiresAt, string? Error, int StatusCode)>
            CreateChallengeAsync(string key, Guid deviceId, string publicKeyPem)
        {
            var license = await FindLicenseAsync(key);

            if (license == null)
            {
                return (null, null, "Lisans bulunamadı.", 404);
            }

            if (!license.IsUsable(DateTimeOffset.UtcNow))
            {
                return (null, null, "Lisans geçerli değil (iptal/donmuş/süre).", 409);
            }

            if (deviceId == Guid.Empty || string.IsNullOrWhiteSpace(publicKeyPem))
            {
                return (null, null, "Cihaz kimliği ve açık anahtar gerekli.", 400);
            }

            string thumbprint;

            try
            {
                thumbprint = DeviceProof.Thumbprint(publicKeyPem);
            }
            catch (Exception)
            {
                return (null, null, "Geçersiz açık anahtar (SPKI PEM bekleniyor).", 400);
            }

            var otherLicense = await _db.Devices.FirstOrDefaultAsync(item =>
                item.IsActive && item.KeyThumbprint == thumbprint && item.LicenseId != license.Id);

            if (otherLicense != null)
            {
                return (null, null, "Bu anahtar başka bir lisansa bağlı.", 409);
            }

            var nonce = DeviceProof.NewNonce();
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

            _db.DeviceChallenges.Add(new DeviceChallenge
            {
                LicenseId = license.Id,
                DeviceId = deviceId,
                NonceHash = DeviceProof.HashNonce(nonce),
                ExpiresAt = expiresAt
            });

            await _db.SaveChangesAsync();

            return (nonce, expiresAt, null, 200);
        }

        public async Task<DeviceActivationOutcome> ActivateDeviceAsync(
            string key, Guid deviceId, string hwid, string? machineName,
            string publicKeyPem, string nonce, string signature, string? ip)
        {
            var license = await _db.Licenses
                .Include(item => item.Devices)
                .FirstOrDefaultAsync(item => item.Key == NormalizeKey(key));

            if (license == null)
            {
                return new DeviceActivationOutcome(false, 404, "Lisans bulunamadı.", null, null);
            }

            var now = DateTimeOffset.UtcNow;

            if (!license.IsUsable(now))
            {
                return new DeviceActivationOutcome(
                    false, 409, "Lisans geçerli değil (iptal/donmuş/süre).", license, null);
            }

            hwid = (hwid ?? string.Empty).Trim();

            if (deviceId == Guid.Empty || hwid.Length < 8 || string.IsNullOrWhiteSpace(publicKeyPem))
            {
                return new DeviceActivationOutcome(false, 400, "Eksik cihaz bilgisi.", license, null);
            }

            var challenge = await _db.DeviceChallenges.FirstOrDefaultAsync(item =>
                item.LicenseId == license.Id &&
                item.DeviceId == deviceId &&
                item.NonceHash == DeviceProof.HashNonce(nonce));

            if (challenge == null || challenge.UsedAt != null || challenge.ExpiresAt <= now)
            {
                return new DeviceActivationOutcome(
                    false, 401, "Doğrulama challenge'ı geçersiz veya süresi doldu.", license, null);
            }

            if (!DeviceProof.Verify(publicKeyPem, nonce, signature))
            {
                return new DeviceActivationOutcome(
                    false, 401, "Cihaz imzası doğrulanamadı.", license, null);
            }

            string thumbprint;

            try
            {
                thumbprint = DeviceProof.Thumbprint(publicKeyPem);
            }
            catch (Exception)
            {
                return new DeviceActivationOutcome(false, 400, "Geçersiz açık anahtar.", license, null);
            }

            challenge.UsedAt = now;

            var existing = license.Devices.FirstOrDefault(item =>
                item.IsActive && item.DeviceId == deviceId);

            if (existing != null)
            {
                if (!string.Equals(existing.KeyThumbprint, thumbprint, StringComparison.Ordinal))
                {
                    return new DeviceActivationOutcome(
                        false, 409, "Cihaz anahtarı değiştirilemez; yönetici sıfırlaması gerekir.",
                        license, existing);
                }

                existing.LastSeenAt = now;
                existing.Hwid = hwid;
                existing.MachineName = machineName ?? existing.MachineName;

                await _db.SaveChangesAsync();
                await RecordActivationAsync(license, existing, "reactivate", "device", null);

                return new DeviceActivationOutcome(true, 200, null, license, existing);
            }

            var byHwid = license.Devices.FirstOrDefault(item =>
                string.Equals(item.Hwid, hwid, StringComparison.OrdinalIgnoreCase));

            if (byHwid != null && byHwid.IsActive)
            {
                return new DeviceActivationOutcome(
                    false, 409, "Bu bilgisayar zaten aktif bir cihaza bağlı.", license, byHwid);
            }

            var sharedKey = await _db.Devices.AnyAsync(item =>
                item.IsActive && item.KeyThumbprint == thumbprint);

            if (sharedKey)
            {
                return new DeviceActivationOutcome(
                    false, 409, "Bu anahtar başka bir lisansa bağlı.", license, null);
            }

            if (byHwid != null)
            {
                byHwid.DeviceId = deviceId;
                byHwid.PublicKeyPem = publicKeyPem;
                byHwid.KeyThumbprint = thumbprint;
                byHwid.IsActive = true;
                byHwid.RevokedAt = null;
                byHwid.RevokedBy = null;
                byHwid.ActivatedAt = now;
                byHwid.LastSeenAt = now;
                byHwid.MachineName = machineName;

                await _db.SaveChangesAsync();
                await RecordActivationAsync(license, byHwid, "reactivate", "device", "HWID yeniden kullanıldı");

                return new DeviceActivationOutcome(true, 200, null, license, byHwid);
            }

            var activeCount = license.Devices.Count(item => item.IsActive);

            if (activeCount >= license.MaxDevices)
            {
                return new DeviceActivationOutcome(
                    false, 409, "Lisans başka bir bilgisayara bağlı.", license, null);
            }

            var device = new Device
            {
                LicenseId = license.Id,
                DeviceId = deviceId,
                Hwid = hwid,
                MachineName = machineName,
                PublicKeyPem = publicKeyPem,
                KeyThumbprint = thumbprint,
                FirstSeenAt = now,
                LastSeenAt = now,
                ActivatedAt = now,
                IsActive = true
            };

            _db.Devices.Add(device);
            await _db.SaveChangesAsync();

            await RecordActivationAsync(license, device, "activate", "device", null);

            await LogAuditAsync(
                "device:" + deviceId, "device.activate", "License", license.Key,
                "thumbprint " + thumbprint, ip);

            return new DeviceActivationOutcome(true, 200, null, license, device);
        }

        public async Task<(bool Ok, string? Error)> ResetDeviceAsync(
            Guid deviceId, string actor, string? ip)
        {
            var device = await _db.Devices
                .Include(item => item.License)
                .FirstOrDefaultAsync(item => item.DeviceId == deviceId || item.Id == deviceId);

            if (device == null)
            {
                return (false, "Cihaz bulunamadı.");
            }

            if (!device.IsActive)
            {
                return (true, null);
            }

            device.IsActive = false;
            device.RevokedAt = DateTimeOffset.UtcNow;
            device.RevokedBy = actor;

            await _db.SaveChangesAsync();

            await RecordActivationAsync(device.License, device, "admin.reset", actor, "cihaz sıfırlandı");

            await LogAuditAsync(
                actor, "admin.device.reset", "Device", device.DeviceId.ToString(),
                device.License.Key + " · " + device.Hwid, ip);

            return (true, null);
        }

        public async Task<List<ActivationHistory>> GetActivationHistoryAsync(
            string? licenseKey, int take = 100)
        {
            var query = _db.ActivationHistory.AsQueryable();

            if (!string.IsNullOrWhiteSpace(licenseKey))
            {
                var normalized = NormalizeKey(licenseKey);
                var license = await _db.Licenses
                    .FirstOrDefaultAsync(item => item.Key == normalized);

                if (license == null)
                {
                    return new List<ActivationHistory>();
                }

                query = query.Where(item => item.LicenseId == license.Id);
            }

            return await query
                .OrderByDescending(item => item.Id)
                .Take(Math.Clamp(take, 1, 500))
                .ToListAsync();
        }

        private async Task RecordActivationAsync(
            License license, Device device, string action, string actor, string? details)
        {
            _db.ActivationHistory.Add(new ActivationHistory
            {
                LicenseId = license.Id,
                DeviceId = device.DeviceId,
                Hwid = device.Hwid,
                Action = action,
                Actor = actor,
                Details = details,
                CreatedAt = DateTimeOffset.UtcNow
            });

            await _db.SaveChangesAsync();
        }
        // ------------------------------------------------------------ audit

        public async Task LogAuditAsync(
            string actor, string action, string? entityType, string? entityId,
            string? details, string? ip)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                Actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Details = details,
                IpAddress = ip,
                CreatedAt = DateTimeOffset.UtcNow
            });

            await _db.SaveChangesAsync();
        }

        public async Task<List<AuditLog>> GetAuditAsync(int take = 100)
        {
            // Id sırası sağlayıcıdan bağımsızdır (SQLite DateTimeOffset ORDER BY desteklemez).
            return await _db.AuditLogs
                .OrderByDescending(item => item.Id)
                .Take(Math.Clamp(take, 1, 500))
                .ToListAsync();
        }

        // ------------------------------------------------------------ yardımcılar

        public static string NormalizeKey(string key)
        {
            return (key ?? string.Empty).Trim().ToUpperInvariant();
        }

        private async Task<string> GenerateUniqueKeyAsync()
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var key = "KC-" + Block() + "-" + Block() + "-" + Block();

                if (!await _db.Licenses.AnyAsync(item => item.Key == key))
                {
                    return key;
                }
            }

            throw new InvalidOperationException("Benzersiz anahtar üretilemedi.");
        }

        private static string Block()
        {
            var builder = new StringBuilder(4);

            for (var i = 0; i < 4; i++)
            {
                builder.Append(KeyAlphabet[RandomNumberGenerator.GetInt32(KeyAlphabet.Length)]);
            }

            return builder.ToString();
        }
    }
}
