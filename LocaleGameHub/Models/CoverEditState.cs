namespace LocaleGameHub.Models;

public sealed class CoverEditState
{
    public string SourcePath { get; set; } = string.Empty;
    public double Zoom { get; set; } = 1.0;
    public double FocusX { get; set; } = 0.5;
    public double FocusY { get; set; } = 0.5;
    public string BackgroundMode { get; set; } = "black";
    public bool ImproveQuality { get; set; }

    public CoverEditState Clone() => new()
    {
        SourcePath = SourcePath,
        Zoom = Zoom,
        FocusX = FocusX,
        FocusY = FocusY,
        BackgroundMode = BackgroundMode,
        ImproveQuality = ImproveQuality
    };
}
