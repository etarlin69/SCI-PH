using Microsoft.Extensions.Logging;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Abstractions.Clinical;
using SCI_PH.Application.Common.Biometrics;
using SCI_PH.Application.DTOs.Biometrics;

namespace SCI_PH.Application.Features.Devices
{
    // The reader's slot inventory (Device API, ADR-010 §10): the slots of its own sensor that SCI-PH considers occupied, so a
    // physical reader can find orphan templates (a lost enrollment acknowledgement, a reset, lost local storage, a replaced
    // controller) and delete them. Occupied means:
    // - the slot of an ACTIVE fingerprint reference of this reader;
    // - the slot of a reference whose deletion job of this reader is still Pending: the template is physically there and its
    //   cleanup belongs to the server, so the reader must never treat it as an orphan.
    // The reader is the authenticated DeviceCredential; it only sees its own slots and only their numbers. Slots are read
    // with the one parser of the format (FingerprintReferences.TryParse): a reference that does not follow it, or belongs to
    // another reader, is left out and never reinterpreted. A snapshot: it reserves nothing, enrollment keeps its own rules.
    // A read: no clinical audit entry (as the other polls of the reader); one log line with the count, never a slot.
    public sealed class FingerprintSlotInventoryReaderService
    {
        private const string Operation = "fingerprint-slot-inventory";

        private readonly IClinicalReadStore _reads;
        private readonly IFingerprintSlotDeletionStore _deletions;
        private readonly TimeProvider _time;
        private readonly ILogger<FingerprintSlotInventoryReaderService> _logger;

        public FingerprintSlotInventoryReaderService(
            IClinicalReadStore reads,
            IFingerprintSlotDeletionStore deletions,
            TimeProvider time,
            ILogger<FingerprintSlotInventoryReaderService> logger)
        {
            _reads = reads;
            _deletions = deletions;
            _time = time;
            _logger = logger;
        }

        public async Task<FingerprintSlotInventory> GetAsync(Guid readerId, string readerCode, CancellationToken cancellationToken)
        {
            var started = _time.GetTimestamp();
            var active = await _reads.ListActiveFingerprintSubjectReferencesAsync(
                FingerprintReferences.Provider, FingerprintReferences.ReaderPrefix(readerId), cancellationToken);
            var pendingDeletion = await _deletions.ListPendingSubjectReferencesForReaderAsync(readerId, cancellationToken);

            var slots = new SortedSet<int>();
            var ignored = 0;
            foreach (var subjectReference in active.Concat(pendingDeletion))
            {
                if (FingerprintReferences.TryParse(subjectReference, out var owner, out var slot) && owner == readerId)
                {
                    slots.Add(slot);
                }
                else
                {
                    ignored++;
                }
            }

            _logger.Log(
                ignored == 0 ? LogLevel.Information : LogLevel.Warning,
                "Device {Operation} for {DeviceCode}: Success, {Count} slots, {Ignored} unreadable references ignored, latency {Latency}.",
                Operation, readerCode, slots.Count, ignored, Latency(_time.GetElapsedTime(started)));
            return new FingerprintSlotInventory([.. slots]);
        }

        private static string Latency(TimeSpan elapsed) => elapsed.TotalMilliseconds switch
        {
            < 100 => "Under100ms",
            < 500 => "Under500ms",
            < 2000 => "Under2s",
            _ => "Over2s"
        };
    }
}
