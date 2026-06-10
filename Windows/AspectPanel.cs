using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace HBT.Windows
{

/// <summary>
/// 根据子 Image 的实际像素宽高比自动设置面板高度。
/// 图片源变化后自动触发重新布局。
/// </summary>
public class AspectPanel : Panel
{
    private const double DefaultRatio = 256.0 / 59.0;

    protected override Size MeasureOverride(Size availableSize)
    {
        var ratio = GetImageRatio();
        var width = double.IsInfinity(availableSize.Width) ? 256 : availableSize.Width;
        var height = width / ratio;

        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(width, height));

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
            child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }

    protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);

        if (visualAdded is Image img)
            img.SourceUpdated += OnSourceUpdated;

        if (visualRemoved is Image oldImg)
            oldImg.SourceUpdated -= OnSourceUpdated;
    }

    private void OnSourceUpdated(object sender, System.Windows.Data.DataTransferEventArgs e)
    {
        InvalidateMeasure();
        InvalidateArrange();
    }

    private double GetImageRatio()
    {
        foreach (UIElement child in InternalChildren)
        {
            if (child is Image img && img.Source is BitmapSource bmp && bmp.PixelHeight > 0)
                return (double)bmp.PixelWidth / bmp.PixelHeight;
        }
        return DefaultRatio;
    }
}
}
