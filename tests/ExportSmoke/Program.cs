// Synthetic local export only: no cTrader instance, broker, signing or anchor calls.
using VCP.CTrader;

if (args.Length != 1)
    throw new ArgumentException("Provide a temporary output directory.");

var root = Path.GetFullPath(args[0]);
using var pack = new VCPEvidencePack(new VCPEvidencePackConfig
{
    BasePath = Path.Combine(root, "working"),
    AutoAnchorEnabled = false,
    EventLoggingEnabled = false,
    DailyReportEnabled = false,
});
await pack.InitializeAsync();
await pack.ExportVerificationPackageAsync(Path.Combine(root, "without-batch"));
await pack.RecordSignalAsync("SYNTHETIC", "BUY", 1.0, 0.5, "offline fixture 1");
await pack.RecordSignalAsync("SYNTHETIC", "BUY", 1.5, 0.6, "offline fixture 2");
await pack.CreateBatchAsync();
await pack.ExportVerificationPackageAsync(Path.Combine(root, "with-batch"));
