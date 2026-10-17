using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RealmPortal.App.Controls;

/// <summary>Status of a server; shared by the seal and the badge.</summary>
public enum ServerStatus
{
    Active,
    Ready,
    Incompatible,
    NoResponse,
    Inactive,
}

/// <summary>
/// Wax seal of a server's status. The glyph is part of the art, so the state never relies on colour alone.
/// Uses the 64 px PNG up to 64 device pixels and the 128 px PNG above that.
/// </summary>
public class Seal : Control
{
    public static readonly StyledProperty<ServerStatus> StatusProperty =
        AvaloniaProperty.Register<Seal, ServerStatus>(nameof(Status));

    static Seal()
    {
        AffectsRender<Seal>(StatusProperty);
        AffectsMeasure<Seal>(WidthProperty, HeightProperty);
    }

    public Seal()
    {
        Width = 48;
        Height = 48;
    }

    public ServerStatus Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }

    internal static string AssetName(ServerStatus status) => status switch
    {
        ServerStatus.Active => "seal-active",
        ServerStatus.Ready => "seal-ready",
        ServerStatus.Incompatible => "seal-incompatible",
        ServerStatus.NoResponse => "seal-no-response",
        _ => "seal-inactive",
    };

    public override void Render(DrawingContext context)
    {
        var devicePixels = Math.Max(Bounds.Width, Bounds.Height) * AssetBitmaps.ScaleOf(this);
        var bitmap = AssetBitmaps.Seal(AssetName(Status), devicePixels > 64);
        using var _ = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality });
        context.DrawImage(bitmap, new Rect(bitmap.Size), new Rect(Bounds.Size));
    }
}
