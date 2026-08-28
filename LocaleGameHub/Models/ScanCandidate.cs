namespace LocaleGameHub.Models;

public sealed class ScanCandidate
{
    public bool Selected { get; set; } = true;
    public string Name { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string SizeText => SizeBytes >= 1024 * 1024
        ? $"{SizeBytes / 1024d / 1024d:0.0} MB"
        : $"{SizeBytes / 1024d:0} KB";
}
