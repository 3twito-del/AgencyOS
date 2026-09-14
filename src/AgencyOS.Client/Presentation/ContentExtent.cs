namespace AgencyOS.Client.Presentation;

/// <summary>
/// How large the page inside the shell's scrolling host should be laid out.
/// </summary>
/// <remarks>
/// <para>
/// A <c>ScrollViewer</c> offers its content unlimited width. That is what makes
/// scrolling possible and it is also a trap: a page whose columns are star-sized
/// will happily take all of it, so the page stretches past the window at every
/// size and the right-hand column is scrolled out of a window that had room for
/// it. The first attempt at <c>AOS-R001-007</c> did exactly that — it rescued the
/// detail pane at 900x700 and pushed Command Center's actions off a 1600x1000
/// desktop, which is a worse defect than the one it fixed.
/// </para>
/// <para>
/// The rule is one line: fill the host, unless the host is narrower than the width
/// the page needs to be usable, in which case take that width and let the host
/// scroll. Below the floor a reader scrolls; at or above it nothing scrolls and
/// the layout is exactly what it was before the repair.
/// </para>
/// </remarks>
public static class ContentExtent
{
    /// <summary>
    /// The size a page should be given along one axis of a host of a known size.
    /// </summary>
    /// <param name="available">The host's size along one axis, in effective pixels.</param>
    /// <param name="minimum">The smallest the page stays usable at along that axis.</param>
    /// <returns>
    /// The size to lay the page out at, or <see cref="double.NaN"/> while the
    /// host has no size yet — which is what XAML reads as "decide for yourself",
    /// and is the right answer before the first layout pass.
    /// </returns>
    public static double For(double available, double minimum)
    {
        if (double.IsNaN(available) || double.IsInfinity(available) || available <= 0)
        {
            return double.NaN;
        }

        if (double.IsNaN(minimum) || double.IsInfinity(minimum) || minimum <= 0)
        {
            return available;
        }

        return Math.Max(available, minimum);
    }
}
