using System.Globalization;

namespace AgencyOS.Reviewer.Runtime;

/// <summary>
/// A screen rectangle, read back from the automation tree.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="UiaNode"/> stores bounds as the string <c>left,top,width,height</c>,
/// which is the right shape for evidence a person reads and the wrong shape for a
/// question like "did the footer cover the selected destination". This is the
/// arithmetic, kept in one place so the layout pass and any later finding agree on
/// what overlapping means.
/// </para>
/// <para>
/// Values are physical pixels, because that is what UI Automation reports. On a
/// display scaled to 150% a window the harness asked for at 1600x1000 is 1067x667
/// effective units, and a comparison against a WinUI breakpoint has to be made in
/// those units rather than these.
/// </para>
/// </remarks>
/// <param name="Left">Left edge.</param>
/// <param name="Top">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
internal readonly record struct Rectangle(double Left, double Top, double Width, double Height)
{
    /// <summary>Right edge.</summary>
    internal double Right => Left + Width;

    /// <summary>Bottom edge.</summary>
    internal double Bottom => Top + Height;

    /// <summary>Reads a rectangle a tree snapshot wrote.</summary>
    /// <param name="bounds">The stored string, or null for a control with no rectangle.</param>
    /// <returns>The rectangle, or null when there was none to read.</returns>
    internal static Rectangle? Parse(string? bounds)
    {
        if (string.IsNullOrWhiteSpace(bounds))
        {
            return null;
        }

        string[] parts = bounds.Split(',');

        if (parts.Length != 4)
        {
            return null;
        }

        double[] values = new double[4];

        for (int i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i], CultureInfo.InvariantCulture, out values[i]))
            {
                return null;
            }
        }

        return new Rectangle(values[0], values[1], values[2], values[3]);
    }

    /// <summary>The smallest rectangle holding all of them.</summary>
    /// <param name="rectangles">The rectangles, any of which may be null.</param>
    /// <returns>The union, or null when none of them had a rectangle.</returns>
    internal static Rectangle? Union(IEnumerable<Rectangle?> rectangles)
    {
        ArgumentNullException.ThrowIfNull(rectangles);

        Rectangle? union = null;

        foreach (Rectangle? rectangle in rectangles)
        {
            if (rectangle is not { } next)
            {
                continue;
            }

            if (union is not { } current)
            {
                union = next;

                continue;
            }

            double left = Math.Min(current.Left, next.Left);
            double top = Math.Min(current.Top, next.Top);

            union = new Rectangle(
                left,
                top,
                Math.Max(current.Right, next.Right) - left,
                Math.Max(current.Bottom, next.Bottom) - top);
        }

        return union;
    }

    /// <summary>How many pixels of one rectangle the other covers vertically.</summary>
    /// <remarks>
    /// Horizontal overlap as well, because two things in different columns are not
    /// covering each other however much their rows agree.
    /// </remarks>
    /// <param name="first">One rectangle.</param>
    /// <param name="second">The other.</param>
    /// <returns>The covered height, or zero when they do not intersect.</returns>
    internal static double VerticalOverlap(Rectangle first, Rectangle second)
    {
        double horizontal = Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left);

        if (horizontal <= 0)
        {
            return 0;
        }

        return Math.Max(
            0, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top));
    }

    /// <summary>Whether this rectangle holds the whole of another.</summary>
    /// <param name="other">The rectangle that should be inside.</param>
    /// <returns>True when every edge of <paramref name="other"/> is within this one.</returns>
    internal bool Contains(Rectangle other) =>
        other.Left >= Left - 1 && other.Top >= Top - 1
        && other.Right <= Right + 1 && other.Bottom <= Bottom + 1;

    /// <summary>Whether the two rectangles share any area at all.</summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>True when they intersect.</returns>
    internal bool Intersects(Rectangle other) =>
        Math.Min(Right, other.Right) > Math.Max(Left, other.Left)
        && Math.Min(Bottom, other.Bottom) > Math.Max(Top, other.Top);

    /// <summary>Writes the rectangle the way a tree snapshot stores it.</summary>
    /// <returns>The string <c>left,top,width,height</c>.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture, $"{Left:F0},{Top:F0},{Width:F0},{Height:F0}");
}
