using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EveOPreview.Mac.Models;

namespace EveOPreview.Mac.Views;

public sealed class OverlayThumbnailWindow : Window
{
    private readonly Border _root;
    private readonly Border _header;
    private readonly TextBlock _title;
    private readonly Image _previewImage;
    private Bitmap? _previewBitmap;
    private Func<ClientWindowInfo, Task>? _activateClientAsync;

    // Pointer travel (in pixels) before a press counts as a drag instead of a click.
    private const int DragThreshold = 4;
    private PixelPoint? _dragStartPointer;
    private PixelPoint _dragStartPosition;
    private bool _isDragging;

    public event Action<ClientWindowInfo, PixelPoint>? PositionChangedByUser;

    public OverlayThumbnailWindow()
    {
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        CanResize = false;
        Topmost = true;
        Background = Brushes.Transparent;

        _title = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 14,
            Margin = new Thickness(8, 4),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        _header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = _title,
            Cursor = new Cursor(StandardCursorType.SizeAll),
        };

        _previewImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var previewContainer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(10, 10, 10)),
            Child = _previewImage,
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
        };
        layout.Children.Add(_header);
        Grid.SetRow(previewContainer, 1);
        layout.Children.Add(previewContainer);

        _root = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
            Child = layout,
        };

        Content = _root;

        _root.PointerPressed += RootOnPointerPressed;
        _root.PointerMoved += RootOnPointerMoved;
        _root.PointerReleased += RootOnPointerReleased;
        _root.PointerCaptureLost += RootOnPointerCaptureLost;
    }

    public ClientWindowInfo? Client { get; private set; }

    public void BindClient(ClientWindowInfo client, Func<ClientWindowInfo, Task> activateClientAsync)
    {
        Client = client;
        _activateClientAsync = activateClientAsync;
        _title.Text = client.WindowTitle;
    }

    public void SetOverlayHeaderVisible(bool isVisible)
    {
        _header.IsVisible = isVisible;
    }

    public void UpdateFrame(byte[]? pngBytes)
    {
        try
        {
            if (pngBytes is null || pngBytes.Length == 0)
            {
                return;
            }

            using var stream = new MemoryStream(pngBytes);
            var bitmap = new Bitmap(stream);
            _previewBitmap?.Dispose();
            _previewBitmap = bitmap;
            _previewImage.Source = _previewBitmap;
        }
        catch
        {
            // Ignore invalid frame payloads.
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        base.OnClosed(e);
    }

    // Dragging is handled manually rather than with BeginMoveDrag: on macOS
    // BeginMoveDrag returns before the drag finishes, so a moved thumbnail
    // looked like a click and its new position was never saved.
    private void RootOnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_root);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragStartPointer = this.PointToScreen(e.GetPosition(this));
        _dragStartPosition = Position;
        _isDragging = false;
        e.Pointer.Capture(_root);
        e.Handled = true;
    }

    private void RootOnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStartPointer is not { } start)
        {
            return;
        }

        var current = this.PointToScreen(e.GetPosition(this));
        var deltaX = current.X - start.X;
        var deltaY = current.Y - start.Y;
        if (!_isDragging && Math.Abs(deltaX) < DragThreshold && Math.Abs(deltaY) < DragThreshold)
        {
            return;
        }

        _isDragging = true;
        Position = new PixelPoint(_dragStartPosition.X + deltaX, _dragStartPosition.Y + deltaY);
        e.Handled = true;
    }

    private async void RootOnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragStartPointer is null)
        {
            return;
        }

        var wasDragging = _isDragging;
        _dragStartPointer = null;
        _isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;

        if (Client is null)
        {
            return;
        }

        if (wasDragging)
        {
            PositionChangedByUser?.Invoke(Client, Position);
            return;
        }

        if (_activateClientAsync is not null)
        {
            await _activateClientAsync(Client);
        }
    }

    private void RootOnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // Capture can be lost mid-drag (e.g. the app is deactivated); keep where the
        // thumbnail ended up.
        if (_isDragging && Client is not null)
        {
            PositionChangedByUser?.Invoke(Client, Position);
        }

        _dragStartPointer = null;
        _isDragging = false;
    }
}
