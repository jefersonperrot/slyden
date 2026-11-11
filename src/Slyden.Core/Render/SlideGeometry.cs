namespace Slyden.Core.Render;

/// <summary>Como a foto ocupa o quadro: preenchendo (com recorte) ou inteira sobre fundo desfocado.</summary>
public readonly record struct SlideLayout(bool Fill, int Width, int Height, int OffsetX, int OffsetY);

/// <summary>Retângulo da foto (em pixels da foto) que aparece em um instante.</summary>
public readonly record struct ViewRect(double X0, double Y0, double X1, double Y1)
{
    public double Width => X1 - X0;
    public double Height => Y1 - Y0;
}

public static class SlideGeometry
{
    // Proporção da foto / proporção do quadro. Dentro desta faixa a foto preenche o quadro.
    private const double FillMin = 0.79, FillMax = 1.27;

    public static SlideLayout Layout(int imageWidth, int imageHeight, int frameWidth, int frameHeight)
    {
        double a = (double)imageWidth / imageHeight;
        double frameAspect = (double)frameWidth / frameHeight;
        double r = a / frameAspect;
        if (r is >= FillMin and <= FillMax)
            return new SlideLayout(true, frameWidth, frameHeight, 0, 0);

        int w, h;
        if (a >= frameAspect) { w = frameWidth; h = (int)(frameWidth / a); }
        else { w = (int)(frameHeight * a); h = frameHeight; }
        return new SlideLayout(false, w, h, (frameWidth - w) / 2, (frameHeight - h) / 2);
    }

    /// <summary>Retângulo com a proporção do destino, zoom <paramref name="zoom"/>, centrado em (cx, cy) e preso dentro da foto.</summary>
    public static ViewRect View(int imageWidth, int imageHeight, double aspect, double zoom, double cx, double cy)
    {
        double bw, bh;
        if ((double)imageWidth / imageHeight > aspect) { bh = imageHeight / zoom; bw = bh * aspect; }
        else { bw = imageWidth / zoom; bh = bw / aspect; }
        double x0 = Math.Min(Math.Max(cx * imageWidth - bw / 2, 0), imageWidth - bw);
        double y0 = Math.Min(Math.Max(cy * imageHeight - bh / 2, 0), imageHeight - bh);
        return new ViewRect(x0, y0, x0 + bw, y0 + bh);
    }

    /// <summary>Suave no início e no fim.</summary>
    public static double Ease(double t) => t * t * (3 - 2 * t);

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
