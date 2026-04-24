using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace ConstellaTTS.SDK.UI.Animation;

/// <summary>
/// Runs a translate-Y animation across a slice of controls in parallel.
///
/// Design notes — why this is a runner, not an <c>ITransition</c>:
///   <c>ITransition</c> interpolates a single AvaloniaProperty change
///   (old → new value of some property). What we need here is the
///   illusion of a layout position change for a GROUP of controls —
///   the layout itself is not changing while the animation is in
///   flight. So we bypass the transition model entirely and drive a
///   temporary <see cref="TranslateTransform"/> on each visual. Callers
///   decide when to commit the underlying model change (typically
///   after awaiting this method) and when to reset offsets.
///
/// The helper is intentionally transient and stateless: created at the
/// call site, disposed when the await completes, owns nothing.
/// </summary>
public static class MoveTransition
{
    /// <summary>
    /// Animates each visual's <c>RenderTransform</c> Y by <paramref name="deltaY"/>
    /// pixels over <paramref name="duration"/>. Installs a
    /// <see cref="TranslateTransform"/> if the visual does not already have one.
    /// Existing Y offsets are respected — the animation adds on top.
    /// </summary>
    public static async Task RunAsync(
        IReadOnlyList<Control> visuals,
        double                 deltaY,
        System.TimeSpan        duration,
        Easing?                easing = null)
    {
        if (visuals.Count == 0 || deltaY == 0) return;
        easing ??= new CubicEaseOut();

        var tasks = new List<Task>(visuals.Count);

        foreach (var v in visuals)
        {
            // Avalonia's TransformAnimator expects the animation target to
            // be the Visual (it walks the Visual's RenderTransform to find
            // the Transform subclass owning the animated property). Passing
            // the TranslateTransform directly throws InvalidCastException.
            if (v.RenderTransform is not TranslateTransform tr)
            {
                tr = new TranslateTransform();
                v.RenderTransform = tr;
            }

            var startY = tr.Y;
            var animation = new Avalonia.Animation.Animation
            {
                Duration = duration,
                Easing   = easing,
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue     = new Cue(0d),
                        Setters = { new Setter(TranslateTransform.YProperty, startY) }
                    },
                    new KeyFrame
                    {
                        Cue     = new Cue(1d),
                        Setters = { new Setter(TranslateTransform.YProperty, startY + deltaY) }
                    }
                }
            };

            tasks.Add(animation.RunAsync(v));
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Clears any translate offset on each visual. Idempotent — visuals
    /// without a <see cref="TranslateTransform"/> are skipped. Typical use:
    /// call immediately before or together with a collection mutation that
    /// changes the underlying layout, so that render-transform and layout
    /// changes land in the same frame (no jump).
    /// </summary>
    public static void ResetOffsets(IEnumerable<Control> visuals)
    {
        foreach (var v in visuals)
        {
            if (v.RenderTransform is TranslateTransform tr)
            {
                tr.X = 0;
                tr.Y = 0;
            }
        }
    }
}
