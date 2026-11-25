using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CleanRamBoosterApp.UI
{
    /// <summary>
    /// Crisp GDI+ antialiased vector icon renderer for WinForms headers, sidebars, and buttons.
    /// Designed cleanly on a normalized 24x24 grid so icons scale sharply across any DPI.
    /// </summary>
    public static class VectorIcons
    {
        public static void DrawIcon(Graphics g, string key, RectangleF bounds, Color color, float strokeWidth = 1.75f)
        {
            var prevMode = g.SmoothingMode;
            var prevOffset = g.PixelOffsetMode;
            var prevCompositing = g.CompositingQuality;
            var state = g.Save();

            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                float size = Math.Min(bounds.Width, bounds.Height);
                float ox = bounds.X + (bounds.Width - size) / 2f;
                float oy = bounds.Y + (bounds.Height - size) / 2f;

                g.TranslateTransform(ox, oy);
                g.ScaleTransform(size / 24f, size / 24f);

                using var pen = new Pen(color, strokeWidth)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                };
                using var brush = new SolidBrush(color);

                switch (key)
                {
                    case "Wrench":
                        g.DrawLine(pen, 5.5f, 18.5f, 14.5f, 9.5f);
                        g.DrawArc(pen, 13f, 3f, 8f, 8f, 20f, 260f);
                        g.DrawEllipse(pen, 3.5f, 16.5f, 4f, 4f);
                        break;

                    case "Clock":
                        g.DrawEllipse(pen, 3f, 3f, 18f, 18f);
                        g.DrawLines(pen, new[] { new PointF(12f, 7f), new PointF(12f, 12.5f), new PointF(16f, 14.5f) });
                        break;

                    case "SearchFiles":
                        g.DrawEllipse(pen, 3.5f, 3.5f, 13.5f, 13.5f);
                        g.DrawLine(pen, 15.2f, 15.2f, 20.5f, 20.5f);
                        break;

                    case "Box":
                        g.DrawPolygon(pen, new[] {
                            new PointF(12f, 2.5f), new PointF(20.5f, 7f),
                            new PointF(20.5f, 17f), new PointF(12f, 21.5f),
                            new PointF(3.5f, 17f), new PointF(3.5f, 7f)
                        });
                        g.DrawLine(pen, 12f, 12f, 20.5f, 7f);
                        g.DrawLine(pen, 12f, 12f, 3.5f, 7f);
                        g.DrawLine(pen, 12f, 12f, 12f, 21.5f);
                        break;

                    case "Folder":
                        g.DrawPolygon(pen, new[] {
                            new PointF(3f, 5.5f), new PointF(9.5f, 5.5f), new PointF(11.5f, 8f),
                            new PointF(21f, 8f), new PointF(21f, 19.5f), new PointF(3f, 19.5f)
                        });
                        break;

                    case "Globe":
                        g.DrawEllipse(pen, 3f, 3f, 18f, 18f);
                        g.DrawLine(pen, 3f, 12f, 21f, 12f);
                        g.DrawEllipse(pen, 8.2f, 3f, 7.6f, 18f);
                        break;

                    case "OsWindows":
                        g.DrawRectangle(pen, 3.5f, 3.5f, 7.5f, 7.5f);
                        g.DrawRectangle(pen, 13f, 3.5f, 7.5f, 7.5f);
                        g.DrawRectangle(pen, 3.5f, 13f, 7.5f, 7.5f);
                        g.DrawRectangle(pen, 13f, 13f, 7.5f, 7.5f);
                        break;

                    case "Cpu":
                        g.DrawRectangle(pen, 6f, 6f, 12f, 12f);
                        g.DrawRectangle(pen, 9.5f, 9.5f, 5f, 5f);
                        g.DrawLine(pen, 9f, 2.5f, 9f, 6f);
                        g.DrawLine(pen, 15f, 2.5f, 15f, 6f);
                        g.DrawLine(pen, 9f, 18f, 9f, 21.5f);
                        g.DrawLine(pen, 15f, 18f, 15f, 21.5f);
                        g.DrawLine(pen, 2.5f, 9f, 6f, 9f);
                        g.DrawLine(pen, 2.5f, 15f, 6f, 15f);
                        g.DrawLine(pen, 18f, 9f, 21.5f, 9f);
                        g.DrawLine(pen, 18f, 15f, 21.5f, 15f);
                        break;

                    case "Memory":
                        g.DrawRectangle(pen, 2.5f, 6.5f, 19f, 10f);
                        g.DrawRectangle(pen, 5.5f, 9.5f, 3f, 4f);
                        g.DrawRectangle(pen, 10.5f, 9.5f, 3f, 4f);
                        g.DrawRectangle(pen, 15.5f, 9.5f, 3f, 4f);
                        g.DrawLine(pen, 6f, 16.5f, 6f, 19.5f);
                        g.DrawLine(pen, 12f, 16.5f, 12f, 19.5f);
                        g.DrawLine(pen, 18f, 16.5f, 18f, 19.5f);
                        break;

                    case "Drive":
                        g.DrawRectangle(pen, 3f, 5f, 18f, 14f);
                        g.DrawLine(pen, 3f, 14f, 21f, 14f);
                        g.FillEllipse(brush, 16f, 15.5f, 2.5f, 2.5f);
                        break;

                    case "Display":
                        g.DrawRectangle(pen, 3f, 4f, 18f, 12.5f);
                        g.DrawLine(pen, 12f, 16.5f, 12f, 20f);
                        g.DrawLine(pen, 8f, 20f, 16f, 20f);
                        break;

                    case "Network":
                        g.DrawArc(pen, 3f, 6f, 18f, 18f, 225f, 90f);
                        g.DrawArc(pen, 6.5f, 9.5f, 11f, 11f, 225f, 90f);
                        g.FillEllipse(brush, 10.6f, 16.5f, 2.8f, 2.8f);
                        break;

                    case "Keyboard":
                        g.DrawRectangle(pen, 2.5f, 6f, 19f, 12f);
                        g.DrawLine(pen, 6f, 10f, 7.5f, 10f);
                        g.DrawLine(pen, 11f, 10f, 12.5f, 10f);
                        g.DrawLine(pen, 16f, 10f, 18f, 10f);
                        g.DrawLine(pen, 7f, 14.5f, 17f, 14.5f);
                        break;

                    case "Bolt":
                        g.DrawPolygon(pen, new[] {
                            new PointF(13f, 2f), new PointF(4.5f, 13.5f),
                            new PointF(11.5f, 13.5f), new PointF(10.5f, 22f),
                            new PointF(19.5f, 10.5f), new PointF(12.5f, 10.5f)
                        });
                        break;

                    case "Trash":
                        g.DrawLine(pen, 4f, 6.5f, 20f, 6.5f);
                        g.DrawLine(pen, 9.5f, 6.5f, 9.5f, 4f);
                        g.DrawLine(pen, 9.5f, 4f, 14.5f, 4f);
                        g.DrawLine(pen, 14.5f, 4f, 14.5f, 6.5f);
                        g.DrawPolygon(pen, new[] {
                            new PointF(6.5f, 6.5f), new PointF(7.5f, 20.5f),
                            new PointF(16.5f, 20.5f), new PointF(17.5f, 6.5f)
                        });
                        g.DrawLine(pen, 10.2f, 10.5f, 10.2f, 17f);
                        g.DrawLine(pen, 13.8f, 10.5f, 13.8f, 17f);
                        break;

                    case "Copy":
                        g.DrawRectangle(pen, 8f, 8f, 12f, 13f);
                        g.DrawLines(pen, new[] {
                            new PointF(4.5f, 15.5f), new PointF(4.5f, 4f), new PointF(15f, 4f)
                        });
                        break;

                    case "Export":
                        g.DrawPolygon(pen, new[] {
                            new PointF(5f, 3.5f), new PointF(14.5f, 3.5f),
                            new PointF(19f, 8f), new PointF(19f, 20.5f), new PointF(5f, 20.5f)
                        });
                        g.DrawLines(pen, new[] {
                            new PointF(14.5f, 3.5f), new PointF(14.5f, 8f), new PointF(19f, 8f)
                        });
                        g.DrawLine(pen, 8.5f, 12.5f, 15.5f, 12.5f);
                        g.DrawLine(pen, 8.5f, 16f, 15.5f, 16f);
                        break;

                    case "Stop":
                        g.DrawRectangle(pen, 6f, 6f, 12f, 12f);
                        break;

                    case "Undo":
                        g.DrawArc(pen, 5f, 7f, 14f, 12f, 190f, 160f);
                        g.DrawLines(pen, new[] { new PointF(7f, 5.5f), new PointF(4.5f, 9f), new PointF(8.5f, 10f) });
                        break;
                }
            }
            finally
            {
                g.Restore(state);
                g.SmoothingMode = prevMode;
                g.PixelOffsetMode = prevOffset;
                g.CompositingQuality = prevCompositing;
            }
        }

        public static Bitmap RenderBitmap(string key, int pixelSize, Color color, int dpi = 96, float strokeWidth = 1.85f)
        {
            var bmp = new Bitmap(pixelSize, pixelSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            bmp.SetResolution(dpi, dpi);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            DrawIcon(g, key, new RectangleF(1f, 1f, pixelSize - 2f, pixelSize - 2f), color, strokeWidth);
            return bmp;
        }

        public static void PaintHeaderBadge(Graphics g, string key, Rectangle rect, Color accent, bool isDark)
        {
            var prevMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int radius = Math.Max(6, rect.Width / 4);
            using var path = UITheme.GetRoundedPath(rect, radius);
            using var bg = new SolidBrush(Color.FromArgb(isDark ? 36 : 24, accent.R, accent.G, accent.B));
            using var border = new Pen(Color.FromArgb(isDark ? 85 : 65, accent.R, accent.G, accent.B), 1f);
            g.FillPath(bg, path);
            g.DrawPath(border, path);

            int pad = Math.Max(5, (int)Math.Round(rect.Width * 0.22));
            var iconRect = new RectangleF(rect.X + pad, rect.Y + pad, rect.Width - pad * 2, rect.Height - pad * 2);
            DrawIcon(g, key, iconRect, accent, 1.9f);
            g.SmoothingMode = prevMode;
        }
    }
}
