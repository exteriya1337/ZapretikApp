using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ZapretikApp
{
    /// <summary>
    /// Soft fade + slide when swapping TextBlock content.
    /// </summary>
    internal static class UiAnimation
    {
        private static readonly Duration Fast = new Duration(TimeSpan.FromMilliseconds(130));
        private static readonly Duration Soft = new Duration(TimeSpan.FromMilliseconds(200));
        private static readonly Dictionary<TextBlock, int> Versions = new Dictionary<TextBlock, int>();

        public static void SetText(TextBlock target, string text, bool slide = true, bool force = false)
        {
            if (target == null)
                return;

            text = text ?? string.Empty;

            if (!force && string.Equals(target.Text, text, StringComparison.Ordinal))
                return;

            // First paint / invisible: set immediately.
            if (!target.IsLoaded || target.ActualWidth <= 0 && target.ActualHeight <= 0 && string.IsNullOrEmpty(target.Text))
            {
                target.BeginAnimation(UIElement.OpacityProperty, null);
                target.Text = text;
                target.Opacity = 1;
                ResetTransform(target);
                return;
            }

            var version = NextVersion(target);
            EnsureTransform(target);

            var transform = (TranslateTransform)target.RenderTransform;
            var fromOpacity = target.Opacity > 0.01 ? target.Opacity : 1;

            target.BeginAnimation(UIElement.OpacityProperty, null);
            transform.BeginAnimation(TranslateTransform.YProperty, null);

            var fadeOut = new DoubleAnimation
            {
                From = fromOpacity,
                To = 0,
                Duration = Fast,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            DoubleAnimation slideOut = null;
            if (slide)
            {
                slideOut = new DoubleAnimation
                {
                    From = 0,
                    To = -7,
                    Duration = Fast,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
            }

            fadeOut.Completed += (s, e) =>
            {
                int current;
                if (!Versions.TryGetValue(target, out current) || current != version)
                    return;

                target.Text = text;
                target.Opacity = 0;
                if (slide)
                    transform.Y = 7;

                var fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = Soft,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                if (slide)
                {
                    var slideIn = new DoubleAnimation
                    {
                        From = 7,
                        To = 0,
                        Duration = Soft,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    transform.BeginAnimation(TranslateTransform.YProperty, slideIn);
                }

                target.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            };

            target.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            if (slideOut != null)
                transform.BeginAnimation(TranslateTransform.YProperty, slideOut);
        }

        /// <summary>
        /// Lighter tick animation for frequently updating values (uptime).
        /// </summary>
        public static void SetTextTick(TextBlock target, string text)
        {
            if (target == null)
                return;

            text = text ?? string.Empty;
            if (string.Equals(target.Text, text, StringComparison.Ordinal))
                return;

            // First value or placeholder swap uses full animation.
            if (string.IsNullOrEmpty(target.Text) || target.Text == "—" || text == "—")
            {
                SetText(target, text, slide: true);
                return;
            }

            var version = NextVersion(target);
            target.BeginAnimation(UIElement.OpacityProperty, null);

            var dip = new DoubleAnimation
            {
                From = 1,
                To = 0.35,
                Duration = new Duration(TimeSpan.FromMilliseconds(90)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            dip.Completed += (s, e) =>
            {
                int current;
                if (!Versions.TryGetValue(target, out current) || current != version)
                    return;

                target.Text = text;
                var rise = new DoubleAnimation
                {
                    From = 0.35,
                    To = 1,
                    Duration = new Duration(TimeSpan.FromMilliseconds(140)),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                target.BeginAnimation(UIElement.OpacityProperty, rise);
            };

            target.BeginAnimation(UIElement.OpacityProperty, dip);
        }

        public static void PulseElement(UIElement element, double peakScale = 1.035)
        {
            if (element == null)
                return;

            // Resource transforms are often frozen — clone to a mutable local tree first.
            var scale = EnsureMutableScaleTransform(element);
            if (scale == null)
                return;

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            var upX = new DoubleAnimation(1, peakScale, new Duration(TimeSpan.FromMilliseconds(140)))
            {
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var upY = new DoubleAnimation(1, peakScale, new Duration(TimeSpan.FromMilliseconds(140)))
            {
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, upX);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, upY);
        }

        private static ScaleTransform EnsureMutableScaleTransform(UIElement element)
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5);

            var current = element.RenderTransform;
            if (current == null || current == Transform.Identity)
            {
                var scale = new ScaleTransform(1, 1);
                element.RenderTransform = scale;
                return scale;
            }

            // Frozen freezables cannot be animated — clone the whole tree.
            if (current.IsFrozen)
            {
                current = current.Clone();
                element.RenderTransform = current;
            }

            var group = current as TransformGroup;
            if (group != null)
            {
                if (group.IsFrozen)
                {
                    group = group.Clone();
                    element.RenderTransform = group;
                }

                for (var i = 0; i < group.Children.Count; i++)
                {
                    var child = group.Children[i];
                    if (child.IsFrozen)
                    {
                        child = child.Clone();
                        group.Children[i] = child;
                    }

                    var existingScale = child as ScaleTransform;
                    if (existingScale != null)
                        return existingScale;
                }

                var added = new ScaleTransform(1, 1);
                group.Children.Add(added);
                return added;
            }

            var singleScale = current as ScaleTransform;
            if (singleScale != null)
            {
                if (singleScale.IsFrozen)
                {
                    singleScale = singleScale.Clone();
                    element.RenderTransform = singleScale;
                }
                return singleScale;
            }

            // Unknown transform type: wrap with a group so we keep original + scale.
            var wrap = new TransformGroup();
            wrap.Children.Add(current.IsFrozen ? current.Clone() : current);
            var wrapScale = new ScaleTransform(1, 1);
            wrap.Children.Add(wrapScale);
            element.RenderTransform = wrap;
            return wrapScale;
        }

        private static int NextVersion(TextBlock target)
        {
            int version;
            Versions.TryGetValue(target, out version);
            version++;
            Versions[target] = version;
            return version;
        }

        private static void EnsureTransform(TextBlock target)
        {
            var transform = target.RenderTransform as TranslateTransform;
            if (transform == null)
            {
                transform = new TranslateTransform();
                target.RenderTransform = transform;
            }
        }

        private static void ResetTransform(TextBlock target)
        {
            var transform = target.RenderTransform as TranslateTransform;
            if (transform != null)
            {
                transform.BeginAnimation(TranslateTransform.YProperty, null);
                transform.Y = 0;
            }
        }
    }
}
