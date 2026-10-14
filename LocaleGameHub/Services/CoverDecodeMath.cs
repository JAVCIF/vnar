namespace LocaleGameHub.Services;

/// <summary>
/// Decides how large a cover must be decoded to fill a card with no visible loss.
/// Pure math, no WPF types, so it can be tested anywhere.
/// </summary>
public static class CoverDecodeMath
{
    /// <summary>
    /// Returns the decode constraint for an image shown with Stretch=UniformToFill
    /// in a box of <paramref name="boxWidthDip"/> × <paramref name="boxHeightDip"/>
    /// device-independent pixels at <paramref name="dpiScale"/> (1.0 = 96 DPI).
    /// Exactly one of the returned values is non-zero, or both are zero when the image
    /// must be decoded at full size (it is not larger than what the screen shows).
    /// Only one dimension is constrained so the decoder keeps the aspect ratio.
    /// </summary>
    public static (int DecodeWidth, int DecodeHeight) ForFill(
        int imageWidth, int imageHeight, double boxWidthDip, double boxHeightDip, double dpiScale)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || boxWidthDip <= 0 || boxHeightDip <= 0)
            return (0, 0);

        dpiScale = dpiScale is > 0 and < 16 ? dpiScale : 1.0;

        // Physical pixels the card occupies on screen. Rounded up: never one pixel short.
        var targetWidth = (int)Math.Ceiling(boxWidthDip * dpiScale);
        var targetHeight = (int)Math.Ceiling(boxHeightDip * dpiScale);

        var scaleX = targetWidth / (double)imageWidth;
        var scaleY = targetHeight / (double)imageHeight;

        // UniformToFill scales by the larger factor so the box is covered; the other
        // dimension overflows and is cropped. Matching that keeps a 1:1 pixel mapping.
        var fillScale = Math.Max(scaleX, scaleY);
        if (fillScale >= 1.0)
            return (0, 0); // never upscale at decode time

        return scaleX >= scaleY
            ? (targetWidth, 0)
            : (0, targetHeight);
    }
}
