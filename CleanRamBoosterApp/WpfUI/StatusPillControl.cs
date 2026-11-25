using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CleanRamBoosterApp.WpfUI
{
    public class StatusPillControl : UserControl
    {
        private readonly Border rootBorder;
        private readonly Border colorSquare;
        private readonly TextBlock lblTitlePart;
        private readonly TextBlock lblValuePart;
        private Color accentColor = Color.FromRgb(59, 130, 246);
        private string title = "";
        private string valueText = "";

        public Color AccentColor
        {
            get => accentColor;
            set
            {
                accentColor = value;
                UpdateAppearance();
            }
        }

        public string Title
        {
            get => title;
            set
            {
                title = value;
                UpdateText();
            }
        }

        public string ValueText
        {
            get => valueText;
            set
            {
                valueText = value;
                UpdateText();
            }
        }

        public string Value
        {
            get => ValueText;
            set => ValueText = value;
        }

        public StatusPillControl()
        {
            Height = 30;
            Cursor = System.Windows.Input.Cursors.Arrow;

            rootBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 0, 10, 0)
            };

            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // 10x10 Colored square matching the exact color of the bar segment below
            colorSquare = new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            lblTitlePart = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };

            lblValuePart = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0)
            };

            stack.Children.Add(colorSquare);
            stack.Children.Add(lblTitlePart);
            stack.Children.Add(lblValuePart);
            rootBorder.Child = stack;
            Content = rootBorder;

            UpdateAppearance();
        }

        private void UpdateAppearance()
        {
            bool isDark = WpfTheme.IsDarkMode;
            byte bgAlpha = (byte)(isDark ? 35 : 22);
            byte borderAlpha = (byte)(isDark ? 75 : 90);

            rootBorder.Background = new SolidColorBrush(Color.FromArgb(bgAlpha, accentColor.R, accentColor.G, accentColor.B));
            rootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(borderAlpha, accentColor.R, accentColor.G, accentColor.B));

            // Square is filled with the exact vibrant accent color matching the bar segment below
            colorSquare.Background = new SolidColorBrush(accentColor);

            lblTitlePart.Foreground = isDark ? WpfTheme.TextSecondaryBrush : new SolidColorBrush(Color.FromRgb(100, 116, 139));
            lblValuePart.Foreground = new SolidColorBrush(accentColor);
        }

        private void UpdateText()
        {
            lblTitlePart.Text = !string.IsNullOrEmpty(title) ? $"{title}:" : "";
            lblValuePart.Text = valueText;
        }

        public void SetContent(string titleText, string val, Color color)
        {
            title = titleText;
            valueText = val;
            accentColor = color;
            UpdateAppearance();
            UpdateText();
        }

        public void RefreshTheme()
        {
            UpdateAppearance();
        }
    }
}
