using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Features.Devices
{
    // Authenticates ESP32 devices by code + key. Completely separate from human accounts.
    public sealed class DeviceAuthenticationService
    {
        // LastSeen is written at most once per interval to avoid a database write on every request.
        private static readonly TimeSpan SeenUpdateInterval = TimeSpan.FromMinutes(1);
        // Unknown devices still pay for a hash comparison so timing does not reveal registered codes.
        private static readonly string UnknownDeviceHash = new('0', 64);

        private readonly IDeviceCredentialStore _devices;
        private readonly ISecureTokenGenerator _secrets;
        private readonly ISecurityAuditLog _audit;
        private readonly TimeProvider _time;

        public DeviceAuthenticationService(
            IDeviceCredentialStore devices,
            ISecureTokenGenerator secrets,
            ISecurityAuditLog audit,
            TimeProvider time)
        {
            _devices = devices;
            _secrets = secrets;
            _audit = audit;
            _time = time;
        }

        public async Task<DeviceIdentity?> AuthenticateAsync(string deviceCode, string deviceKey, ClientContext client, CancellationToken cancellationToken)
        {
            var device = await _devices.FindByCodeAsync(DeviceCredentialService.NormalizeCode(deviceCode), cancellationToken);
            var keyMatches = _secrets.Matches(deviceKey, device?.SecretHash ?? UnknownDeviceHash);
            var failure = device switch
            {
                null => SecurityFailureReason.DeviceNotFound,
                { Status: DeviceCredentialStatus.Revoked } => SecurityFailureReason.DeviceRevoked,
                _ when !keyMatches => SecurityFailureReason.InvalidDeviceKey,
                _ => (SecurityFailureReason?)null
            };

            if (failure is not null)
            {
                await _audit.RecordAsync(SecurityAuditRecord.Failure(SecurityAuditEventType.DeviceAuthenticationFailed, null, failure.Value, client) with
                {
                    DeviceCredentialId = device?.Id
                }, cancellationToken);
                return null;
            }

            if (device!.LastSeenAtUtc is null || _time.GetUtcNow() - device.LastSeenAtUtc >= SeenUpdateInterval)
            {
                await _devices.RecordSeenAsync(device.Id, null, cancellationToken);
            }

            return new DeviceIdentity(device.Id, device.DeviceCode, device.Scopes);
        }

        public Task RecordHeartbeatAsync(Guid deviceId, DeviceHeartbeatRequest request, CancellationToken cancellationToken) =>
            _devices.RecordSeenAsync(deviceId, request.FirmwareVersion, cancellationToken);
    }
}
