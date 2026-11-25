using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CleanRamBoosterApp.WpfUI
{
    public class MetricCardControl : UserControl
    {
        private readonly Border rootBorder;
        private readonly Border topStripe;
        private readonly TextBlock lblTitle;
        private readonly TextBlock lblValue;
        private Color accentColor = Color.FromRgb(59, 130, 246);

        public Color AccentColor
        {
            get => accentColor;
            set
            {
                accentColor = value;
                topStripe.Background = new SolidColorBrush(accentColor);
                lblValue.Foreground = new SolidColorBrush(accentColor);
            }
        }

        public string Title
        {
            get => lblTitle.Text;
            set => lblTitle.Text = value;
        }

        public string ValueText
        {
            get => lblValue.Text;
            set => lblValue.Text = value;
        }

        public string Value
        {
            get => ValueText;
            set => ValueText = value;
        }

        public MetricCardControl()
        {
            Height = 76;
            MinHeight = 76;

            rootBorder = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                ClipToBounds = true
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) }); // Row 0: Top accent stripe
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Row 1: Content

            // Top accent stripe with rounded top corners matching container radius
            topStripe = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(7, 7, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(accentColor)
            };
            Grid.SetRow(topStripe, 0);
            mainGrid.Children.Add(topStripe);

            // Padded text content - comfortably separated below the top stripe
            var contentStack = new StackPanel
            {
                Margin = new Thickness(14, 8, 14, 8),
                VerticalAlignment = VerticalAlignment.Center
            };

            lblTitle = new TextBlock
            {
                FontSize = 11.5,
                FontWeight = FontWeights.Normal,
                Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            contentStack.Children.Add(lblTitle);

            lblValue = new TextBlock
            {
                FontSize = 19.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(accentColor)
            };
            contentStack.Children.Add(lblValue);

            Grid.SetRow(contentStack, 1);
            mainGrid.Children.Add(contentStack);

            rootBorder.Child = mainGrid;
            Content = rootBorder;

            UpdateTheme();
        }

        public void UpdateTheme()
        {
            bool isDark = WpfTheme.IsDarkMode;
            rootBorder.Background = WpfTheme.CardBrush;
            rootBorder.BorderBrush = WpfTheme.BorderBrush;
            lblTitle.Foreground = WpfTheme.TextSecondaryBrush;
            lblValue.Foreground = new SolidColorBrush(accentColor);
            topStripe.Background = new SolidColorBrush(accentColor);
        }
    }
}
