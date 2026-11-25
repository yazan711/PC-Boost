using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CleanRamBoosterApp.Services;

namespace CleanRamBoosterApp.WpfUI
{
    public class CircularGaugeControl : UserControl
    {
        private readonly Path trackPath;
        private readonly Path progressPath;
        private readonly TextBlock lblPercent;
        private readonly TextBlock lblTitle;
        private readonly Border badgeBorder;
        private readonly TextBlock lblBadge;

        private int percent = 0;

        public int Percent
        {
            get => percent;
            set
            {
                percent = Math.Clamp(value, 0, 100);
                UpdateGauge();
            }
        }

        public int Percentage
        {
            get => Percent;
            set => Percent = value;
        }

        public CircularGaugeControl()
        {
            Width = 180;
            Height = 175;

            var canvas = new Canvas
            {
                Width = 180,
                Height = 175
            };

            trackPath = new Path
            {
                Stroke = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                StrokeThickness = 12,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };

            progressPath = new Path
            {
                Stroke = new LinearGradientBrush(
                    Color.FromRgb(56, 189, 248),
                    Color.FromRgb(37, 99, 235),
                    new Point(0, 0),
                    new Point(1, 1)),
                StrokeThickness = 12,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };

            canvas.Children.Add(trackPath);
            canvas.Children.Add(progressPath);

            var centerStack = new StackPanel
            {
                Width = 180,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Canvas.SetTop(centerStack, 36);

            lblPercent = new TextBlock
            {
                Text = "0%",
                FontSize = 31,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(240, 246, 252)),
                TextAlignment = TextAlignment.Center
            };

            lblTitle = new TextBlock
            {
                Text = LocalizationService.IsArabic ? "الذاكرة الفعلية" : "PHYSICAL RAM",
                FontSize = 11,
                FontWeight = FontWeights.Normal,
                Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };

            centerStack.Children.Add(lblPercent);
            centerStack.Children.Add(lblTitle);
            canvas.Children.Add(centerStack);

            badgeBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Canvas.SetLeft(badgeBorder, 8);
            Canvas.SetTop(badgeBorder, 140);
            badgeBorder.Width = 164;

            lblBadge = new TextBlock
            {
                Text = LocalizationService.IsArabic ? "حالة ممتازة" : "OPTIMAL STATE",
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                TextAlignment = TextAlignment.Center
            };
            badgeBorder.Child = lblBadge;
            canvas.Children.Add(badgeBorder);

            Content = canvas;
            UpdateTrack();
            UpdateGauge();
        }

        private void UpdateTrack()
        {
            trackPath.Data = CreateArcGeometry(135, 270);
        }

        private void UpdateGauge()
        {
            lblPercent.Text = $"{percent}%";
            lblTitle.Text = LocalizationService.IsArabic ? "الذاكرة الفعلية" : "PHYSICAL RAM";
            double sweep = (percent / 100.0) * 270;
            progressPath.Data = CreateArcGeometry(135, sweep);

            bool isDark = WpfTheme.IsDarkMode;
            trackPath.Stroke = new SolidColorBrush(isDark ? Color.FromRgb(30, 41, 59) : Color.FromRgb(226, 232, 240));
            lblPercent.Foreground = new SolidColorBrush(isDark ? Color.FromRgb(240, 246, 252) : Color.FromRgb(15, 23, 42));

            Color arcStart, arcEnd, badgeBg, badgeFg;
            string badgeText;

            if (percent < 70)
            {
                arcStart = Color.FromRgb(56, 189, 248);
                arcEnd = Color.FromRgb(37, 99, 235);
                badgeBg = isDark ? Color.FromArgb(40, 16, 185, 129) : Color.FromArgb(28, 16, 185, 129);
                badgeFg = isDark ? Color.FromRgb(52, 211, 153) : Color.FromRgb(5, 150, 105);
                badgeText = LocalizationService.T("StatusOptimal", "OPTIMAL STATE");
            }
            else if (percent < 85)
            {
                arcStart = Color.FromRgb(251, 191, 36);
                arcEnd = Color.FromRgb(245, 158, 11);
                badgeBg = isDark ? Color.FromArgb(45, 245, 158, 11) : Color.FromArgb(30, 245, 158, 11);
                badgeFg = isDark ? Color.FromRgb(251, 191, 36) : Color.FromRgb(217, 119, 6);
                badgeText = LocalizationService.T("StatusWarning", "ELEVATED LOAD");
            }
            else
            {
                arcStart = Color.FromRgb(248, 113, 113);
                arcEnd = Color.FromRgb(239, 68, 68);
                badgeBg = isDark ? Color.FromArgb(45, 239, 68, 68) : Color.FromArgb(30, 239, 68, 68);
                badgeFg = isDark ? Color.FromRgb(248, 113, 113) : Color.FromRgb(220, 38, 38);
                badgeText = LocalizationService.T("StatusCritical", "CRITICAL LOAD");
            }

            progressPath.Stroke = new LinearGradientBrush(arcStart, arcEnd, new Point(0, 0), new Point(1, 1));
            badgeBorder.Background = new SolidColorBrush(badgeBg);
            badgeBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(80, badgeFg.R, badgeFg.G, badgeFg.B));
            badgeBorder.BorderThickness = new Thickness(1);
            lblBadge.Foreground = new SolidColorBrush(badgeFg);
            lblBadge.Text = badgeText;
            lblTitle.Text = LocalizationService.IsArabic ? "الذاكرة الفعلية" : "PHYSICAL RAM";
        }

        private static PathGeometry CreateArcGeometry(double startAngleDegrees, double sweepDegrees)
        {
            double cx = 90;
            double cy = 72;
            double r = 58;

            if (sweepDegrees <= 0.5) return new PathGeometry();

            double startRad = (startAngleDegrees) * Math.PI / 180.0;
            double endRad = (startAngleDegrees + sweepDegrees) * Math.PI / 180.0;

            Point startPoint = new Point(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
            Point endPoint = new Point(cx + r * Math.Cos(endRad), cy + r * Math.Sin(endRad));

            bool isLargeArc = sweepDegrees > 180.0;

            var segment = new ArcSegment(endPoint, new Size(r, r), 0, isLargeArc, SweepDirection.Clockwise, true);
            var figure = new PathFigure(startPoint, new PathSegment[] { segment }, false);
            return new PathGeometry(new PathFigure[] { figure });
        }
    }
}
