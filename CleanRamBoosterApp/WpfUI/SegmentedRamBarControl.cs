using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CleanRamBoosterApp.WpfUI
{
    public class SegmentedRamBarControl : UserControl
    {
        private readonly Border rootBorder;
        private readonly Grid grid;
        private readonly ColumnDefinition colUsed;
        private readonly ColumnDefinition colAvailable;

        private float usedRatio = 0.5f;
        private float availableRatio = 0.5f;

        public SegmentedRamBarControl()
        {
            Height = 16;

            rootBorder = new Border
            {
                CornerRadius = new CornerRadius(5),
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromRgb(24, 32, 47)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))
            };

            grid = new Grid();
            colUsed = new ColumnDefinition { Width = new GridLength(50, GridUnitType.Star) };
            colAvailable = new ColumnDefinition { Width = new GridLength(50, GridUnitType.Star) };

            grid.ColumnDefinitions.Add(colUsed);
            grid.ColumnDefinitions.Add(colAvailable);

            // 1. In-Use RAM segment
            var rectUsed = new Rectangle
            {
                Fill = new LinearGradientBrush(
                    Color.FromRgb(59, 130, 246),
                    Color.FromRgb(37, 99, 235),
                    new Point(0, 0),
                    new Point(1, 0))
            };
            Grid.SetColumn(rectUsed, 0);

            // 2. Available RAM segment
            var rectAvailable = new Rectangle
            {
                Fill = new LinearGradientBrush(
                    Color.FromRgb(16, 185, 129),
                    Color.FromRgb(5, 150, 105),
                    new Point(0, 0),
                    new Point(1, 0))
            };
            Grid.SetColumn(rectAvailable, 1);

            grid.Children.Add(rectUsed);
            grid.Children.Add(rectAvailable);

            rootBorder.Child = grid;
            Content = rootBorder;
        }

        public void SetValues(float used, float available)
        {
            float total = used + available;
            if (total <= 0.001f) total = 1f;

            usedRatio = used / total;
            availableRatio = available / total;

            colUsed.Width = new GridLength(Math.Max(0.01, usedRatio * 100), GridUnitType.Star);
            colAvailable.Width = new GridLength(Math.Max(0.01, availableRatio * 100), GridUnitType.Star);
        }

        public void SetValues(ulong used, ulong available)
        {
            SetValues((float)used, (float)available);
        }
    }
}
