using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SCI_PH.Application.Abstractions.Biometrics;
using SCI_PH.Application.Features.Clinical;

namespace SCI_PH.Infrastructure.Biometrics.Face.OpenCv
{
    // Background cleanup of revoked face templates (ADR-012, PR 2), only in the Web BFF and only with Provider=OpenCv. Every
    // IntervalSeconds it runs one FaceTemplateCleanupService pass in its own scope; each ReconcileIntervalMinutes it also
    // compares the store with the clinical references (report only). Several instances may run: the job's concurrency
    // tokens let one result apply per attempt. A failed pass is logged and the next one tries again; the host never stops
    // because of it. Its options and the reconciler (which needs the template store) are resolved when it runs, after the host
    // validated the options.
    internal sealed class FaceTemplateCleanupWorker : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly TimeProvider _time;
        private readonly ILogger<FaceTemplateCleanupWorker> _logger;

        public FaceTemplateCleanupWorker(IServiceProvider services, TimeProvider time, ILogger<FaceTemplateCleanupWorker> logger)
        {
            _services = services;
            _time = time;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var options = _services.GetRequiredService<OpenCvFaceOptions>().Cleanup;
            var reconciler = _services.GetRequiredService<FaceTemplateReconciler>();
            var reconcileEvery = TimeSpan.FromMinutes(Math.Max(1, options.ReconcileIntervalMinutes));
            DateTimeOffset? lastReconciled = null;
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.IntervalSeconds), _time);
            do
            {
                await RunPassAsync(options.BatchSize, stoppingToken);
                var now = _time.GetUtcNow();
                if (lastReconciled is not { } last || now - last >= reconcileEvery)
                {
                    lastReconciled = now;
                    await ReconcileAsync(reconciler, stoppingToken);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task RunPassAsync(int batchSize, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _services.CreateScope();
                var completed = await scope.ServiceProvider.GetRequiredService<FaceTemplateCleanupService>()
                    .RunOnceAsync(batchSize, cancellationToken);
                if (completed > 0)
                {
                    _logger.LogInformation("Face template cleanup completed {Count} job(s).", completed);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Face template cleanup pass failed ({ExceptionType}); the jobs stay Pending.", exception.GetType().Name);
            }
        }

        private async Task ReconcileAsync(FaceTemplateReconciler reconciler, CancellationToken cancellationToken)
        {
            try
            {
                await reconciler.ReportAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Face template reconciliation failed ({ExceptionType}).", exception.GetType().Name);
            }
        }
    }

    // Report-only reconciliation (ADR-012, PR 2). Never deletes anything.
    //   A: an Active reference whose template is missing (it answers 503 until re-enrolled): category FaceTemplateMissing.
    //   B: a template that no retained reference points to (a failed compensation, a cleanup completed elsewhere, or an
    //      enrollment still committing): category FaceTemplateOrphan, with the opaque template ids (no patient data).
    //   Cleanup jobs that need attention (FaceTemplateCleanupService.AttentionAttempts failed attempts or more; they keep
    //   being retried): category FaceTemplateCleanupAttention, with the job ids.
    internal sealed class FaceTemplateReconciler
    {
        private const int MaxReportedIds = 20;
        private const int MaxInspectedJobs = 500;

        private readonly IServiceScopeFactory _scopes;
        private readonly IFaceTemplateStore _templates;
        private readonly ILogger<FaceTemplateReconciler> _logger;

        public FaceTemplateReconciler(IServiceScopeFactory scopes, IFaceTemplateStore templates, ILogger<FaceTemplateReconciler> logger)
        {
            _scopes = scopes;
            _templates = templates;
            _logger = logger;
        }

        public async Task<FaceTemplateReconciliation> ReportAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<RetainedFaceTemplate> retained;
            List<Guid> attention;
            using (var scope = _scopes.CreateScope())
            {
                var deletions = scope.ServiceProvider.GetRequiredService<IFaceTemplateDeletionStore>();
                retained = await deletions.ListRetainedAsync(LocalOpenCvFaceVerificationService.ProviderName, cancellationToken);
                attention = (await deletions.ListPendingAsync(MaxInspectedJobs, cancellationToken))
                    .Where(t => t.Deletion.Attempts >= FaceTemplateCleanupService.AttentionAttempts)
                    .Select(t => t.Deletion.Id)
                    .ToList();
            }

            var stored = new HashSet<Guid>();
            await foreach (var id in _templates.ListAsync(cancellationToken))
            {
                stored.Add(id);
            }

            var expected = new HashSet<Guid>();
            var missing = new List<Guid>();
            foreach (var reference in retained)
            {
                if (!FaceSubjectReference.TryParse(reference.SubjectReference, out var id))
                {
                    continue;
                }

                expected.Add(id);
                if (reference.Active && !stored.Contains(id))
                {
                    missing.Add(id);
                }
            }

            var orphans = stored.Where(id => !expected.Contains(id)).ToList();
            var result = new FaceTemplateReconciliation(stored.Count, missing, orphans, attention);
            if (missing.Count > 0)
            {
                _logger.LogWarning("FaceTemplateMissing: {Count} active face reference(s) without a template: {TemplateIds}",
                    missing.Count, string.Join(",", missing.Take(MaxReportedIds)));
            }

            if (orphans.Count > 0)
            {
                _logger.LogWarning("FaceTemplateOrphan: {Count} template(s) without a retained reference (not deleted): {TemplateIds}",
                    orphans.Count, string.Join(",", orphans.Take(MaxReportedIds)));
            }

            if (attention.Count > 0)
            {
                _logger.LogWarning("FaceTemplateCleanupAttention: {Count} cleanup job(s) failed {Attempts} or more times and keep being retried: {JobIds}",
                    attention.Count, FaceTemplateCleanupService.AttentionAttempts, string.Join(",", attention.Take(MaxReportedIds)));
            }

            _logger.LogInformation("Face template reconciliation: {Stored} stored, {Missing} missing, {Orphans} orphan(s), {Attention} cleanup job(s) needing attention.",
                stored.Count, missing.Count, orphans.Count, attention.Count);
            return result;
        }
    }

    internal sealed record FaceTemplateReconciliation(int Stored, IReadOnlyList<Guid> Missing, IReadOnlyList<Guid> Orphans, IReadOnlyList<Guid> Attention);
}
