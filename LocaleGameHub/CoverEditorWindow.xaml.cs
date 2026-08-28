using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using LocaleGameHub.Models;
using LocaleGameHub.Services;

namespace LocaleGameHub;

public partial class CoverEditorWindow : Window
{
    private const double ViewWidth = 310;
    private const double ViewHeight = 430;
    private const double Nudge = 18;
    private const int BaseTargetWidth = 620;
    private const int BaseTargetHeight = 860;

    private readonly Guid _gameId;
    private string _sourcePath;
    private readonly CoverEditState? _initialState;
    private BitmapSource? _bitmap;
    private double _baseScale = 1;
    private double _imageX;
    private double _imageY;
    private bool _dragging;
    private Point _dragStart;
    private double _startX;
    private double _startY;

    public string? EditedCoverPath { get; private set; }
    public CoverEditState? EditState { get; private set; }
    public string OutputDescription { get; private set; } = string.Empty;

    public CoverEditorWindow(Guid gameId, string sourcePath, CoverEditState? initialState = null)
    {
        InitializeComponent();
        DarkTitleBarService.Apply(this);
        LocalizationService.Apply(this);
        _gameId = gameId;
        _sourcePath = sourcePath;
        _initialState = initialState?.Clone();
        Loaded += (_, _) => LoadImage();
    }

    private void LoadImage()
    {
        try
        {
            // Older VNAR builds may already have persisted a WebP source. Normalize it lazily
            // when the editor opens so existing libraries are repaired without user action.
            _sourcePath = ImageCompatibilityService.NormalizeFileIfNeeded(_sourcePath, _gameId, "editor_compat");

            // Load the complete image into memory and release the source file immediately.
            // BitmapImage + IgnoreImageCache with StreamSource can make WPF look up a null URI
            // cache key ("Value cannot be null. Parameter 'key'"). BitmapDecoder avoids that path.
            using var stream = new FileStream(
                _sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
                throw new InvalidOperationException(LocalizationService.Bi("La imagen no contiene ningún frame que WPF pueda leer.", "The image contains no frame that WPF can read."));

            // Clone to an independent in-memory bitmap so neither the decoder nor the file stays locked.
            var bmp = new WriteableBitmap(decoder.Frames[0]);
            bmp.Freeze();
            _bitmap = bmp;

            PreviewImage.Source = bmp;
            BlurBackgroundImage.Source = bmp;
            ImageInfoText.Text = LocalizationService.IsSpanish ? $"Imagen fuente: {bmp.PixelWidth}×{bmp.PixelHeight} px" : $"Source image: {bmp.PixelWidth}×{bmp.PixelHeight} px";
            RestoreOrResetLayout();
            ApplyBackgroundPreviewMode();
            UpdateOutputInfo();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, LocalizationService.Bi("No se pudo cargar la portada para editarla.\n\n", "The cover could not be loaded for editing.\n\n") + ex.Message,
                LocalizationService.Bi("Editar portada", "Edit cover"), MessageBoxButton.OK, MessageBoxImage.Warning);
            DialogResult = false;
        }
    }

    private double CurrentScale() => _baseScale * ZoomSlider.Value;

    private double CurrentWidth() => _bitmap is null ? 0 : _bitmap.PixelWidth * CurrentScale();
    private double CurrentHeight() => _bitmap is null ? 0 : _bitmap.PixelHeight * CurrentScale();

    private void RestoreOrResetLayout()
    {
        if (_bitmap is null) return;

        _baseScale = Math.Min(ViewWidth / _bitmap.PixelWidth, ViewHeight / _bitmap.PixelHeight);
        if (_baseScale <= 0) _baseScale = 1;

        if (_initialState is null)
        {
            ResetLayout();
            return;
        }

        ZoomSlider.Value = Math.Clamp(_initialState.Zoom, ZoomSlider.Minimum, ZoomSlider.Maximum);
        var width = CurrentWidth();
        var height = CurrentHeight();
        _imageX = ViewWidth / 2.0 - Math.Clamp(_initialState.FocusX, 0.0, 1.0) * width;
        _imageY = ViewHeight / 2.0 - Math.Clamp(_initialState.FocusY, 0.0, 1.0) * height;
        SelectBackgroundMode(_initialState.BackgroundMode);
        ImproveQualityCheck.IsChecked = _initialState.ImproveQuality;
        ApplyImageLayout();
    }

    private void SelectBackgroundMode(string? mode)
    {
        mode = string.IsNullOrWhiteSpace(mode) ? "black" : mode;
        foreach (var item in BackgroundModeCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), mode, StringComparison.OrdinalIgnoreCase))
            {
                BackgroundModeCombo.SelectedItem = item;
                return;
            }
        }
        BackgroundModeCombo.SelectedIndex = 0;
    }

    private void ResetLayout()
    {
        if (_bitmap is null) return;

        _baseScale = Math.Min(ViewWidth / _bitmap.PixelWidth, ViewHeight / _bitmap.PixelHeight);
        if (_baseScale <= 0) _baseScale = 1;
        ZoomSlider.Value = 1.0;
        CenterWithinFrame();
        ApplyImageLayout();
        UpdateOutputInfo();
    }

    private void CenterWithinFrame()
    {
        var width = CurrentWidth();
        var height = CurrentHeight();
        _imageX = (ViewWidth - width) / 2.0;
        _imageY = (ViewHeight - height) / 2.0;
    }

    private void ClampPosition()
    {
        var width = CurrentWidth();
        var height = CurrentHeight();

        if (width <= ViewWidth)
            _imageX = Math.Clamp(_imageX, 0, ViewWidth - width);
        else
            _imageX = Math.Clamp(_imageX, ViewWidth - width, 0);

        if (height <= ViewHeight)
            _imageY = Math.Clamp(_imageY, 0, ViewHeight - height);
        else
            _imageY = Math.Clamp(_imageY, ViewHeight - height, 0);
    }

    private void ApplyImageLayout()
    {
        if (_bitmap is null) return;

        ClampPosition();
        PreviewImage.Width = CurrentWidth();
        PreviewImage.Height = CurrentHeight();
        Canvas.SetLeft(PreviewImage, _imageX);
        Canvas.SetTop(PreviewImage, _imageY);
        ApplyBlurPreviewLayout();
        ZoomText.Text = $"{ZoomSlider.Value:0.00}×";
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_bitmap is null || !IsLoaded) return;

        var oldWidth = _bitmap.PixelWidth * _baseScale * (e.OldValue <= 0 ? 1.0 : e.OldValue);
        var oldHeight = _bitmap.PixelHeight * _baseScale * (e.OldValue <= 0 ? 1.0 : e.OldValue);

        var centerX = ViewWidth / 2.0;
        var centerY = ViewHeight / 2.0;

        var relX = oldWidth > 0 ? (centerX - _imageX) / oldWidth : 0.5;
        var relY = oldHeight > 0 ? (centerY - _imageY) / oldHeight : 0.5;

        var newWidth = CurrentWidth();
        var newHeight = CurrentHeight();
        _imageX = centerX - relX * newWidth;
        _imageY = centerY - relY * newHeight;
        ApplyImageLayout();
    }

    private void PreviewHost_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var delta = e.Delta > 0 ? 0.10 : -0.10;
        ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + delta, ZoomSlider.Minimum, ZoomSlider.Maximum);
        e.Handled = true;
    }

    private void PreviewImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_bitmap is null) return;
        _dragging = true;
        _dragStart = e.GetPosition(PreviewHost);
        _startX = _imageX;
        _startY = _imageY;
        PreviewImage.CaptureMouse();
        e.Handled = true;
    }

    private void PreviewImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var pos = e.GetPosition(PreviewHost);
        var delta = pos - _dragStart;
        _imageX = _startX + delta.X;
        _imageY = _startY + delta.Y;
        ApplyImageLayout();
    }

    private void PreviewImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        PreviewImage.ReleaseMouseCapture();
        e.Handled = true;
    }

    private Point GetFocusNormalized()
    {
        var width = CurrentWidth();
        var height = CurrentHeight();
        if (width <= 0 || height <= 0) return new Point(0.5, 0.5);

        var focusX = (ViewWidth / 2.0 - _imageX) / width;
        var focusY = (ViewHeight / 2.0 - _imageY) / height;
        return new Point(Math.Clamp(focusX, 0.0, 1.0), Math.Clamp(focusY, 0.0, 1.0));
    }

    private void ApplyBlurPreviewLayout()
    {
        if (_bitmap is null) return;

        var layout = GetBlurLayout(ViewWidth, ViewHeight);
        BlurBackgroundImage.Width = layout.width;
        BlurBackgroundImage.Height = layout.height;
        Canvas.SetLeft(BlurBackgroundImage, layout.x);
        Canvas.SetTop(BlurBackgroundImage, layout.y);
    }

    private (double x, double y, double width, double height) GetBlurLayout(double frameWidth, double frameHeight)
    {
        if (_bitmap is null)
            return (0, 0, frameWidth, frameHeight);

        var focus = GetFocusNormalized();

        // The blur should feel like the same framing as the foreground, just enlarged enough
        // to cover the whole portrait frame. Zooming the foreground in must therefore zoom
        // the blurred background in too. When zooming out, keep at least a full-frame fill
        // so the background never exposes empty bars.
        var minimumFillScale = Math.Max(frameWidth / _bitmap.PixelWidth, frameHeight / _bitmap.PixelHeight) * 1.12;
        var zoomFactor = Math.Max(1.0, ZoomSlider.Value);
        var fillScale = minimumFillScale * zoomFactor;
        var width = _bitmap.PixelWidth * fillScale;
        var height = _bitmap.PixelHeight * fillScale;

        var x = frameWidth / 2.0 - focus.X * width;
        var y = frameHeight / 2.0 - focus.Y * height;

        if (width <= frameWidth) x = (frameWidth - width) / 2.0;
        else x = Math.Clamp(x, frameWidth - width, 0);

        if (height <= frameHeight) y = (frameHeight - height) / 2.0;
        else y = Math.Clamp(y, frameHeight - height, 0);

        return (x, y, width, height);
    }

    private void BackgroundModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyBackgroundPreviewMode();
        UpdateOutputInfo();
    }

    private void ApplyBackgroundPreviewMode()
    {
        var mode = SelectedBackgroundMode();
        TransparentPattern.Visibility = mode == "transparent" ? Visibility.Visible : Visibility.Collapsed;
        BlurBackgroundCanvas.Visibility = mode == "blur" ? Visibility.Visible : Visibility.Collapsed;

        PreviewHost.Background = mode switch
        {
            "white" => Brushes.White,
            "transparent" => Brushes.Transparent,
            _ => Brushes.Black
        };

        if (mode == "blur") ApplyBlurPreviewLayout();
    }

    private string SelectedBackgroundMode()
        => BackgroundModeCombo.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() ?? "black" : "black";

    private void MoveLeft_Click(object sender, RoutedEventArgs e) { _imageX -= Nudge; ApplyImageLayout(); }
    private void MoveRight_Click(object sender, RoutedEventArgs e) { _imageX += Nudge; ApplyImageLayout(); }
    private void MoveUp_Click(object sender, RoutedEventArgs e) { _imageY -= Nudge; ApplyImageLayout(); }
    private void MoveDown_Click(object sender, RoutedEventArgs e) { _imageY += Nudge; ApplyImageLayout(); }
    private void Center_Click(object sender, RoutedEventArgs e) { CenterWithinFrame(); ApplyImageLayout(); }
    private void Reset_Click(object sender, RoutedEventArgs e) => ResetLayout();
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void QualityMode_Changed(object sender, RoutedEventArgs e) => UpdateOutputInfo();

    private void UpdateOutputInfo()
    {
        if (OutputInfoText is null) return;
        var hq = ImproveQualityCheck?.IsChecked == true;
        var width = BaseTargetWidth * (hq ? 2 : 1);
        var height = BaseTargetHeight * (hq ? 2 : 1);
        OutputInfoText.Text = hq
            ? (LocalizationService.IsSpanish
                ? $"Salida: {width}×{height} px · reescalado HQ con interpolación de alta calidad."
                : $"Output: {width}×{height} px · HQ upscaling with high-quality interpolation.")
            : (LocalizationService.IsSpanish
                ? $"Salida: {width}×{height} px · estándar."
                : $"Output: {width}×{height} px · standard.");
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_bitmap is null)
        {
            MessageBox.Show(this, LocalizationService.Bi("No hay imagen para editar.", "There is no image to edit."), LocalizationService.Bi("Editar portada", "Edit cover"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var multiplier = ImproveQualityCheck.IsChecked == true ? 2 : 1;
            var targetWidth = BaseTargetWidth * multiplier;
            var targetHeight = BaseTargetHeight * multiplier;
            var backgroundMode = SelectedBackgroundMode();
            var bitmap = RenderComposedBitmap(targetWidth, targetHeight, backgroundMode);
            EditedCoverPath = CoverService.SaveBitmapCover(bitmap, _gameId, multiplier > 1 ? "rendered_hq" : "rendered");

            var focus = GetFocusNormalized();
            EditState = new CoverEditState
            {
                SourcePath = _sourcePath,
                Zoom = ZoomSlider.Value,
                FocusX = focus.X,
                FocusY = focus.Y,
                BackgroundMode = backgroundMode,
                ImproveQuality = ImproveQualityCheck.IsChecked == true
            };
            OutputDescription = $"{targetWidth}×{targetHeight} px{(multiplier > 1 ? " · HQ" : string.Empty)}";
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, LocalizationService.Bi("No se pudo generar la portada editada.\n\n", "The edited cover could not be generated.\n\n") + ex.Message,
                LocalizationService.Bi("Editar portada", "Edit cover"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private BitmapSource RenderComposedBitmap(int targetWidth, int targetHeight, string backgroundMode)
    {
        if (_bitmap is null) throw new InvalidOperationException(LocalizationService.Bi("No hay imagen cargada.", "No image is loaded."));

        var root = new Grid
        {
            Width = targetWidth,
            Height = targetHeight,
            ClipToBounds = true,
            Background = backgroundMode switch
            {
                "white" => Brushes.White,
                "transparent" => Brushes.Transparent,
                _ => Brushes.Black
            }
        };

        if (backgroundMode == "blur")
        {
            var blurLayout = GetBlurLayout(targetWidth, targetHeight);
            var blurCanvas = new Canvas { Width = targetWidth, Height = targetHeight, Background = Brushes.Transparent, ClipToBounds = true };
            var blurred = new Image
            {
                Source = _bitmap,
                Width = blurLayout.width,
                Height = blurLayout.height,
                Stretch = Stretch.Fill,
                Opacity = 0.82,
                Effect = new BlurEffect { Radius = 26 }
            };
            RenderOptions.SetBitmapScalingMode(blurred, BitmapScalingMode.HighQuality);
            Canvas.SetLeft(blurred, blurLayout.x);
            Canvas.SetTop(blurred, blurLayout.y);
            blurCanvas.Children.Add(blurred);
            root.Children.Add(blurCanvas);
        }

        var ratioX = targetWidth / ViewWidth;
        var ratioY = targetHeight / ViewHeight;

        var canvas = new Canvas { Width = targetWidth, Height = targetHeight, Background = Brushes.Transparent, ClipToBounds = true };
        var image = new Image
        {
            Source = _bitmap,
            Width = CurrentWidth() * ratioX,
            Height = CurrentHeight() * ratioY,
            Stretch = Stretch.Fill
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        Canvas.SetLeft(image, _imageX * ratioX);
        Canvas.SetTop(image, _imageY * ratioY);
        canvas.Children.Add(image);
        root.Children.Add(canvas);

        root.Measure(new Size(targetWidth, targetHeight));
        root.Arrange(new Rect(0, 0, targetWidth, targetHeight));
        root.UpdateLayout();

        var rtb = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        rtb.Freeze();
        return rtb;
    }
}
