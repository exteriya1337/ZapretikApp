using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace ZapretikApp
{
    /// <summary>
    /// ScrollViewer with smooth animated mouse-wheel scrolling.
    /// </summary>
    public class SmoothScrollViewer : ScrollViewer
    {
        public static readonly DependencyProperty AnimatableVerticalOffsetProperty =
            DependencyProperty.Register(
                "AnimatableVerticalOffset",
                typeof(double),
                typeof(SmoothScrollViewer),
                new PropertyMetadata(0.0, OnAnimatableVerticalOffsetChanged));

        private double _wheelTarget;
        private bool _isWheelAnimating;
        private int _animationVersion;

        public double AnimatableVerticalOffset
        {
            get { return (double)GetValue(AnimatableVerticalOffsetProperty); }
            set { SetValue(AnimatableVerticalOffsetProperty, value); }
        }

        /// <summary>Pixels to move per mouse-wheel notch (Delta ≈ 120).</summary>
        public double WheelStep { get; set; } = 100;

        /// <summary>Duration of one smooth scroll animation.</summary>
        public TimeSpan WheelDuration { get; set; } = TimeSpan.FromMilliseconds(280);

        private static void OnAnimatableVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SmoothScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue);
        }

        protected override void OnScrollChanged(ScrollChangedEventArgs e)
        {
            base.OnScrollChanged(e);

            // Keep wheel target aligned with thumb-drag / keyboard / ScrollIntoView.
            if (!_isWheelAnimating && e.VerticalChange != 0)
                _wheelTarget = VerticalOffset;
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            if (ScrollableHeight <= 0)
            {
                base.OnPreviewMouseWheel(e);
                return;
            }

            e.Handled = true;

            if (!_isWheelAnimating)
                _wheelTarget = VerticalOffset;

            // Scale step by notch count; precision touchpads send fractional notches.
            var notches = e.Delta / 120.0;
            if (Math.Abs(notches) < 0.05)
                notches = Math.Sign(e.Delta);

            _wheelTarget = Clamp(_wheelTarget - notches * WheelStep, 0, ScrollableHeight);
            AnimateTo(_wheelTarget);
        }

        private void AnimateTo(double target)
        {
            var from = VerticalOffset;
            if (Math.Abs(from - target) < 0.5)
            {
                _isWheelAnimating = false;
                return;
            }

            var version = ++_animationVersion;
            _isWheelAnimating = true;

            // Stop previous animation without snapping.
            BeginAnimation(AnimatableVerticalOffsetProperty, null);
            AnimatableVerticalOffset = from;

            var animation = new DoubleAnimation
            {
                From = from,
                To = target,
                Duration = new Duration(WheelDuration),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };

            animation.Completed += (s, e) =>
            {
                if (version != _animationVersion)
                    return;

                BeginAnimation(AnimatableVerticalOffsetProperty, null);
                AnimatableVerticalOffset = target;
                ScrollToVerticalOffset(target);
                _isWheelAnimating = false;
                _wheelTarget = target;
            };

            BeginAnimation(AnimatableVerticalOffsetProperty, animation);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
