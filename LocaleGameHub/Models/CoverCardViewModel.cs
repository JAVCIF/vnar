using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace LocaleGameHub.Models;

/// <summary>
/// Base for library cards whose cover is decoded in the background. The card shows
/// its placeholder until <see cref="CoverImage"/> is set, so page changes and
/// searches never wait for image decoding on the UI thread.
/// </summary>
public abstract class CoverCardViewModel : INotifyPropertyChanged
{
    private ImageSource? _coverImage;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Path of the cover file on disk (empty if none).</summary>
    public abstract string CoverPath { get; }

    public ImageSource? CoverImage
    {
        get => _coverImage;
        set
        {
            if (ReferenceEquals(_coverImage, value)) return;
            _coverImage = value;
            OnPropertyChanged();
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
