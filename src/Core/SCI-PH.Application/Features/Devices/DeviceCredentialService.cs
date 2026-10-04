using SCI_PH.Application.Abstractions.Security;
using SCI_PH.Application.Common.Security;
using SCI_PH.Application.DTOs.Security;

namespace SCI_PH.Application.Features.Devices
{
    public sealed record DeviceSecretResult(DeviceOperationOutcome Outcome, DeviceSecret? Secret);

    // Device provisioning (Devices.Manage). The raw key is returned once; only its hash is stored.
    // Rotation replaces the key immediately: the previous key stops working at once.
    public sealed class DeviceCredentialService
    {
        private const int SecretBytes = 32;

        private readonly IDeviceCredentialStore _devices;
        private readonly ISecureTokenGenerator _secrets;
        private readonly ISecurityAuditLog _audit;

        public DeviceCredentialService(IDeviceCredentialStore devices, ISecureTokenGenerator secrets, ISecurityAuditLog audit)
        {
            _devices = devices;
            _secrets = secrets;
            _audit = audit;
        }

        public static string NormalizeCode(string deviceCode) => deviceCode.Trim().ToUpperInvariant();

        public Task<IReadOnlyList<DeviceSummary>> ListAsync(CancellationToken cancellationToken) => _devices.ListAsync(cancellationToken);

        public async Task<DeviceSecretResult> ProvisionAsync(ProvisionDeviceRequest request, Guid actorUserId, ClientContext client, CancellationToken cancellationToken)
        {
            var scopes = request.Scopes.Distinct(StringComparer.Ordinal).ToArray();
            if (scopes.Length == 0 || scopes.Any(scope => !DeviceScopes.All.Contains(scope)))
            {
                return new DeviceSecretResult(DeviceOperationOutcome.InvalidScopes, null);
            }

            var code = NormalizeCode(request.DeviceCode);
            var secret = _secrets.Generate(SecretBytes);
            var id = await _devices.AddAsync(
                new NewDeviceCredential(code, request.Name.Trim(), scopes, _secrets.Hash(secret), actorUserId), cancellationToken);
            if (id is null)
            {
                return new DeviceSecretResult(DeviceOperationOutcome.DuplicateCode, null);
            }

            await RecordAsync(SecurityAuditEventType.DeviceProvisioned, id.Value, actorUserId, client, cancellationToken);
            return new DeviceSecretResult(DeviceOperationOutcome.Succeeded, new DeviceSecret(id.Value, code, secret));
        }

        public async Task<DeviceSecretResult> RotateSecretAsync(Guid deviceId, Guid actorUserId, ClientContext client, CancellationToken cancellationToken)
        {
            var secret = _secrets.Generate(SecretBytes);
            var code = await _devices.ReplaceSecretAsync(deviceId, _secrets.Hash(secret), cancellationToken);
            if (code is null)
            {
                return new DeviceSecretResult(DeviceOperationOutcome.NotFound, null);
            }

            await RecordAsync(SecurityAuditEventType.DeviceSecretRotated, deviceId, actorUserId, client, cancellationToken);
            return new DeviceSecretResult(DeviceOperationOutcome.Succeeded, new DeviceSecret(deviceId, code, secret));
        }

        public async Task<DeviceOperationOutcome> RevokeAsync(Guid deviceId, Guid actorUserId, ClientContext client, CancellationToken cancellationToken)
        {
            if (!await _devices.RevokeAsync(deviceId, cancellationToken))
            {
                return DeviceOperationOutcome.NotFound;
            }

            await RecordAsync(SecurityAuditEventType.DeviceRevoked, deviceId, actorUserId, client, cancellationToken);
            return DeviceOperationOutcome.Succeeded;
        }

        private Task RecordAsync(SecurityAuditEventType eventType, Guid deviceId, Guid actorUserId, ClientContext client, CancellationToken cancellationToken) =>
            _audit.RecordAsync(SecurityAuditRecord.Success(eventType, null, client) with
            {
                ActorUserId = actorUserId,
                DeviceCredentialId = deviceId
            }, cancellationToken);
    }
}
