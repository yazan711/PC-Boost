using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CleanRamBoosterApp.WpfUI
{
    /// <summary>
    /// Crisp DPI-independent vector icons replacing blurry/pixelated monochrome Unicode emojis.
    /// All paths live inside a uniform 24x24 viewbox.
    /// </summary>
    public static class WpfIcons
    {
        private sealed class IconDef
        {
            public string[] Strokes { get; }
            public string[] Fills { get; }
            public IconDef(string[]? strokes = null, string[]? fills = null)
            {
                Strokes = strokes ?? Array.Empty<string>();
                Fills = fills ?? Array.Empty<string>();
            }
        }

        private static readonly Dictionary<string, IconDef> Icons = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Lightning"] = new IconDef(
                strokes: new[] { "M13 2 L4.5 13.5 H11.5 L10.5 22 L19.5 10.5 H12.5 Z" }),
            ["Rocket"] = new IconDef(
                strokes: new[] {
                    "M4.5 16.5 C3 18 3 21 3 21 C3 21 6 21 7.5 19.5 C8.3 18.7 8.3 17.3 7.5 16.5 C6.7 15.7 5.3 15.7 4.5 16.5 Z",
                    "M12 15 L9 12 A22 22 0 0 1 21 3 A22 22 0 0 1 12 15 Z",
                    "M9 12 H4 L6 8 L9.5 7",
                    "M12 15 V20 L16 18 L17 14.5"
                }),
            ["Shield"] = new IconDef(
                strokes: new[] {
                    "M12 2.5 L4.5 5.8 V11.5 C4.5 16.4 7.7 20.4 12 21.8 C16.3 20.4 19.5 16.4 19.5 11.5 V5.8 Z",
                    "M9.2 12.2 L11.2 14.2 L15.2 9.8"
                }),
            ["Startup"] = new IconDef(
                strokes: new[] {
                    "M12 3 A9 9 0 1 0 21 12",
                    "M12 12 L17.5 6.5",
                    "M16 3 H21 V8"
                }),
            ["Toolbox"] = new IconDef(
                strokes: new[] {
                    "M3 9 H21 V19.5 A1.5 1.5 0 0 1 19.5 21 H4.5 A1.5 1.5 0 0 1 3 19.5 Z",
                    "M8.5 9 V5.5 A1.5 1.5 0 0 1 10 4 H14 A1.5 1.5 0 0 1 15.5 5.5 V9",
                    "M3 13.5 H21",
                    "M10 13.5 V15.5 H14 V13.5"
                }),
            ["Settings"] = new IconDef(
                strokes: new[] {
                    "M12 15 A3 3 0 1 0 12 9 A3 3 0 1 0 12 15 Z",
                    "M19.4 15 A1.65 1.65 0 0 0 19.7 16.8 L19.8 16.9 A2 2 0 1 1 16.9 19.8 L16.8 19.7 A1.65 1.65 0 0 0 15 19.4 A1.65 1.65 0 0 0 14 20.9 V21 A2 2 0 1 1 10 21 V20.9 A1.65 1.65 0 0 0 9 19.4 A1.65 1.65 0 0 0 7.2 19.7 L7.1 19.8 A2 2 0 1 1 4.2 16.9 L4.3 16.8 A1.65 1.65 0 0 0 4.6 15 A1.65 1.65 0 0 0 3.1 14 H3 A2 2 0 1 1 3 10 H3.1 A1.65 1.65 0 0 0 4.6 9 A1.65 1.65 0 0 0 4.3 7.2 L4.2 7.1 A2 2 0 1 1 7.1 4.2 L7.2 4.3 A1.65 1.65 0 0 0 9 4.6 H9 A1.65 1.65 0 0 0 10 3.1 V3 A2 2 0 1 1 14 3 V3.1 A1.65 1.65 0 0 0 15 4.6 A1.65 1.65 0 0 0 16.8 4.3 L16.9 4.2 A2 2 0 1 1 19.8 7.1 L19.7 7.2 A1.65 1.65 0 0 0 19.4 9 V9 A1.65 1.65 0 0 0 20.9 10 H21 A2 2 0 1 1 21 14 H20.9 A1.65 1.65 0 0 0 19.4 15 Z"
                }),
            ["Sun"] = new IconDef(
                strokes: new[] {
                    "M12 16.5 A4.5 4.5 0 1 0 12 7.5 A4.5 4.5 0 1 0 12 16.5 Z",
                    "M12 2.5 V4.5 M12 19.5 V21.5 M4.9 4.9 L6.3 6.3 M17.7 17.7 L19.1 19.1 M2.5 12 H4.5 M19.5 12 H21.5 M4.9 19.1 L6.3 17.7 M17.7 6.3 L19.1 4.9"
                }),
            ["Moon"] = new IconDef(
                strokes: new[] {
                    "M20.2 14.5 A8.5 8.5 0 0 1 9.5 3.8 A8.5 8.5 0 1 0 20.2 14.5 Z"
                }),
            ["Globe"] = new IconDef(
                strokes: new[] {
                    "M12 21 A9 9 0 1 0 12 3 A9 9 0 1 0 12 21 Z",
                    "M3 12 H21",
                    "M12 3 C14.6 5.8 15.8 8.8 15.8 12 C15.8 15.2 14.6 18.2 12 21 C9.4 18.2 8.2 15.2 8.2 12 C8.2 8.8 9.4 5.8 12 3 Z"
                }),
            ["Alert"] = new IconDef(
                strokes: new[] {
                    "M10.3 4.2 L2.5 18 A1.8 1.8 0 0 0 4.1 20.7 H19.9 A1.8 1.8 0 0 0 21.5 18 L13.7 4.2 A1.9 1.9 0 0 0 10.3 4.2 Z",
                    "M12 9.5 V13.8",
                    "M12 17.2 H12.01"
                }),
            ["Wrench"] = new IconDef(
                strokes: new[] {
                    "M14.7 6.3 A1 1 0 0 0 14.7 7.7 L16.3 9.3 A1 1 0 0 0 17.7 9.3 L21.5 5.5 A6 6 0 0 1 13.6 13.4 L6.7 20.3 A2.1 2.1 0 0 1 3.7 17.3 L10.6 10.4 A6 6 0 0 1 18.5 2.5 Z"
                }),
            ["Monitor"] = new IconDef(
                strokes: new[] {
                    "M3 5 A1.5 1.5 0 0 1 4.5 3.5 H19.5 A1.5 1.5 0 0 1 21 5 V15 A1.5 1.5 0 0 1 19.5 16.5 H4.5 A1.5 1.5 0 0 1 3 15 Z",
                    "M8 20.5 H16",
                    "M12 16.5 V20.5"
                }),
            ["Clock"] = new IconDef(
                strokes: new[] {
                    "M12 21 A9 9 0 1 0 12 3 A9 9 0 1 0 12 21 Z",
                    "M12 7 V12.5 L15.5 14.5"
                }),
            ["SearchFiles"] = new IconDef(
                strokes: new[] {
                    "M10.5 17.5 A7 7 0 1 0 10.5 3.5 A7 7 0 1 0 10.5 17.5 Z",
                    "M15.8 15.8 L20.5 20.5"
                }),
            ["Box"] = new IconDef(
                strokes: new[] {
                    "M12 2.5 L20 7 V17 L12 21.5 L4 17 V7 Z",
                    "M12 12 L20 7",
                    "M12 12 V21.5",
                    "M12 12 L4 7"
                }),
            ["Folder"] = new IconDef(
                strokes: new[] {
                    "M3 6.5 A1.5 1.5 0 0 1 4.5 5 H9.5 L11.5 7.5 H19.5 A1.5 1.5 0 0 1 21 9 V18.5 A1.5 1.5 0 0 1 19.5 20 H4.5 A1.5 1.5 0 0 1 3 18.5 Z"
                }),
            ["Sliders"] = new IconDef(
                strokes: new[] {
                    "M4 7 H20 M4 12 H20 M4 17 H20",
                    "M8.5 5 V9 M15.5 10 V14 M10.5 15 V19"
                }),
            ["Copy"] = new IconDef(
                strokes: new[] {
                    "M8 8 H18.5 A1.5 1.5 0 0 1 20 9.5 V19.5 A1.5 1.5 0 0 1 18.5 21 H8 A1.5 1.5 0 0 1 6.5 19.5 V9.5 A1.5 1.5 0 0 1 8 8 Z",
                    "M4 15.5 V5 A1.5 1.5 0 0 1 5.5 3.5 H15"
                }),
            ["Trash"] = new IconDef(
                strokes: new[] {
                    "M4 6.5 H20",
                    "M9.5 6.5 V4.5 A1 1 0 0 1 10.5 3.5 H13.5 A1 1 0 0 1 14.5 4.5 V6.5",
                    "M6.5 6.5 L7.5 19.5 A1.5 1.5 0 0 0 9 21 H15 A1.5 1.5 0 0 0 16.5 19.5 L17.5 6.5",
                    "M10 10.5 V17 M14 10.5 V17"
                }),
            ["Refresh"] = new IconDef(
                strokes: new[] {
                    "M20 11 A8 8 0 0 0 6.3 5.7 L4 8",
                    "M4 4 V8 H8",
                    "M4 13 A8 8 0 0 0 17.7 18.3 L20 16",
                    "M20 20 V16 H16"
                }),
            ["ExportDoc"] = new IconDef(
                strokes: new[] {
                    "M6 3.5 H14 L18.5 8 V19.5 A1.5 1.5 0 0 1 17 21 H6 A1.5 1.5 0 0 1 4.5 19.5 V5 A1.5 1.5 0 0 1 6 3.5 Z",
                    "M14 3.5 V8 H18.5",
                    "M8.5 12.5 H14.5 M8.5 16 H14.5"
                }),
            ["Gamepad"] = new IconDef(
                strokes: new[] {
                    "M6.5 7.5 H17.5 A4.5 4.5 0 0 1 21.8 13 L20.8 17.5 A2 2 0 0 1 17.2 18 L15 15.5 H9 L6.8 18 A2 2 0 0 1 3.2 17.5 L2.2 13 A4.5 4.5 0 0 1 6.5 7.5 Z",
                    "M6.5 11.5 H9.5 M8 10 V13",
                    "M15.5 11 H15.51 M17.5 12.5 H17.51"
                }),
            ["Briefcase"] = new IconDef(
                strokes: new[] {
                    "M3.5 8 H20.5 A1.5 1.5 0 0 1 22 9.5 V19 A1.5 1.5 0 0 1 20.5 20.5 H3.5 A1.5 1.5 0 0 1 2 19 V9.5 A1.5 1.5 0 0 1 3.5 8 Z",
                    "M8.5 8 V5.5 A1.5 1.5 0 0 1 10 4 H14 A1.5 1.5 0 0 1 15.5 5.5 V8",
                    "M2 13 H22"
                }),
            ["Store"] = new IconDef(
                strokes: new[] {
                    "M4 8.5 H20 L19 19.5 A1.5 1.5 0 0 1 17.5 21 H6.5 A1.5 1.5 0 0 1 5 19.5 Z",
                    "M8.5 8.5 V6.5 A3.5 3.5 0 0 1 15.5 6.5 V8.5"
                }),
            ["Sparkle"] = new IconDef(
                strokes: new[] {
                    "M12 3 L14.2 9.8 L21 12 L14.2 14.2 L12 21 L9.8 14.2 L3 12 L9.8 9.8 Z"
                }),
            ["InfoCircle"] = new IconDef(
                strokes: new[] {
                    "M12 21 A9 9 0 1 0 12 3 A9 9 0 1 0 12 21 Z",
                    "M12 11.2 V16.5",
                    "M12 7.8 H12.01"
                }),
            ["HelpCircle"] = new IconDef(
                strokes: new[] {
                    "M12 21 A9 9 0 1 0 12 3 A9 9 0 1 0 12 21 Z",
                    "M9.4 9.5 A2.8 2.8 0 0 1 14.8 10.4 C14.8 12.2 12 12.6 12 14.3",
                    "M12 17.2 H12.01"
                }),
            ["CloseX"] = new IconDef(
                strokes: new[] {
                    "M6.5 6.5 L17.5 17.5",
                    "M17.5 6.5 L6.5 17.5"
                }),
            ["SpeedGauge"] = new IconDef(
                strokes: new[] {
                    "M4.5 17.5 A9.5 9.5 0 1 1 19.5 17.5",
                    "M12 14 L16.2 9.2",
                    "M12 16 A2 2 0 1 0 12 12 A2 2 0 1 0 12 16 Z",
                    "M8 19 H16"
                }),
            ["CheckCircle"] = new IconDef(
                strokes: new[] {
                    "M12 21 A9 9 0 1 0 12 3 A9 9 0 1 0 12 21 Z",
                    "M8.5 12.2 L10.8 14.5 L15.8 9.5"
                }),
            ["ChevronDown"] = new IconDef(
                strokes: new[] {
                    "M6.5 9.5 L12 15 L17.5 9.5"
                }),
            ["Undo"] = new IconDef(
                strokes: new[] {
                    "M9 14 L4 9 L9 4",
                    "M4 9 H14.5 A5.5 5.5 0 1 1 14.5 20 H11"
                }),
            ["Activity"] = new IconDef(
                strokes: new[] {
                    "M3 12 H7 L10 5 L14 19 L17 12 H21"
                }),
            ["Stop"] = new IconDef(
                strokes: new[] {
                    "M6.5 6.5 H17.5 V17.5 H6.5 Z"
                }),
            ["OsWindows"] = new IconDef(
                strokes: new[] {
                    "M3.5 4.5 H11 V11 H3.5 Z",
                    "M13 4.5 H20.5 V11 H13 Z",
                    "M3.5 13 H11 V19.5 H3.5 Z",
                    "M13 13 H20.5 V19.5 H13 Z"
                }),
            ["Cpu"] = new IconDef(
                strokes: new[] {
                    "M7 7 H17 V17 H7 Z",
                    "M10 10 H14 V14 H10 Z",
                    "M9 3.5 V7 M15 3.5 V7 M9 17 V20.5 M15 17 V20.5",
                    "M3.5 9 H7 M3.5 15 H7 M17 9 H20.5 M17 15 H20.5"
                }),
            ["Memory"] = new IconDef(
                strokes: new[] {
                    "M3.5 8 H20.5 V16 H3.5 Z",
                    "M7 11 V13 M10.5 11 V13 M14 11 V13 M17.5 11 V13",
                    "M6 16 V19 M10 16 V19 M14 16 V19 M18 16 V19"
                }),
            ["Drive"] = new IconDef(
                strokes: new[] {
                    "M4 6 A1.5 1.5 0 0 1 5.5 4.5 H18.5 A1.5 1.5 0 0 1 20 6 V18 A1.5 1.5 0 0 1 18.5 19.5 H5.5 A1.5 1.5 0 0 1 4 18 Z",
                    "M4 14.5 H20",
                    "M16.5 17 H16.51"
                }),
            ["Network"] = new IconDef(
                strokes: new[] {
                    "M5 12.5 A10 10 0 0 1 19 12.5",
                    "M8 15.5 A6 6 0 0 1 16 15.5",
                    "M12 18.5 H12.01",
                    "M2.5 9.5 A13.5 13.5 0 0 1 21.5 9.5"
                }),
            ["Keyboard"] = new IconDef(
                strokes: new[] {
                    "M3 7 A1.5 1.5 0 0 1 4.5 5.5 H19.5 A1.5 1.5 0 0 1 21 7 V17 A1.5 1.5 0 0 1 19.5 18.5 H4.5 A1.5 1.5 0 0 1 3 17 Z",
                    "M6.5 9.5 H6.51 M10 9.5 H10.01 M13.5 9.5 H13.51 M17 9.5 H17.01",
                    "M7.5 14.5 H16.5"
                })
        };

        public static Viewbox CreateIcon(string key, Brush brush, double size = 16, double strokeThickness = 1.75)
        {
            var canvas = new Canvas
            {
                Width = 24,
                Height = 24,
                SnapsToDevicePixels = false
            };

            if (Icons.TryGetValue(key, out var def))
            {
                foreach (string d in def.Strokes)
                {
                    canvas.Children.Add(new Path
                    {
                        Data = Geometry.Parse(d),
                        Stroke = brush,
                        StrokeThickness = strokeThickness,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round,
                        SnapsToDevicePixels = false
                    });
                }

                foreach (string d in def.Fills)
                {
                    canvas.Children.Add(new Path
                    {
                        Data = Geometry.Parse(d),
                        Fill = brush,
                        SnapsToDevicePixels = false
                    });
                }
            }

            return new Viewbox
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                FlowDirection = FlowDirection.LeftToRight,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = canvas
            };
        }

        public static Border CreateHeaderBadge(string key, Color accentColor, double boxSize = 40, double iconSize = 21)
        {
            var bg = new SolidColorBrush(Color.FromArgb(32, accentColor.R, accentColor.G, accentColor.B));
            var borderBrush = new SolidColorBrush(Color.FromArgb(72, accentColor.R, accentColor.G, accentColor.B));
            var fg = new SolidColorBrush(accentColor);

            return new Border
            {
                Width = boxSize,
                Height = boxSize,
                CornerRadius = new CornerRadius(Math.Round(boxSize * 0.25)),
                Background = bg,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = CreateIcon(key, fg, iconSize, 1.85)
            };
        }

        public static StackPanel CreateIconTextContent(string iconKey, string text, Brush foreground, double iconSize = 15, double gap = 8)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var icon = CreateIcon(iconKey, foreground, iconSize, 1.75);
            icon.Margin = new Thickness(0, 0, gap, 0);
            panel.Children.Add(icon);

            var tb = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(tb);

            return panel;
        }
    }
}
