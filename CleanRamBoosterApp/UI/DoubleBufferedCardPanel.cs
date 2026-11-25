using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CleanRamBoosterApp.UI
{
    public class DoubleBufferedCardPanel : Panel
    {
        public Color BorderColor { get; set; } = Color.Empty;
        public Color TopStripeColor { get; set; } = Color.Empty;
        public int CornerRadius { get; set; } = 8;

        public DoubleBufferedCardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            UpdateStyles();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Suppress default background erase to eliminate flicker
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var borderCol = BorderColor.IsEmpty ? UITheme.BorderSubtle : BorderColor;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);

            if (CornerRadius > 0)
            {
                using var path = UITheme.CreateRoundedPath(rect, CornerRadius);
                using var bg = new SolidBrush(BackColor);
                g.FillPath(bg, path);

                if (!TopStripeColor.IsEmpty)
                {
                    using var stripeBrush = new SolidBrush(TopStripeColor);
                    g.FillRectangle(stripeBrush, 3, 1, Width - 6, 3);
                }

                using var p = new Pen(borderCol, 1);
                g.DrawPath(p, path);
            }
            else
            {
                using var bg = new SolidBrush(BackColor);
                g.FillRectangle(bg, rect);

                if (!TopStripeColor.IsEmpty)
                {
                    using var stripeBrush = new SolidBrush(TopStripeColor);
                    g.FillRectangle(stripeBrush, 1, 1, Width - 2, 3);
                }

                using var p = new Pen(borderCol, 1);
                g.DrawRectangle(p, rect);
            }
        }
    }
}
