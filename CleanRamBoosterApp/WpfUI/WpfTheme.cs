using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CleanRamBoosterApp.Services;
using CleanRamBoosterApp.UI;

namespace CleanRamBoosterApp.WpfUI
{
    public static class WpfTheme
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public static void ApplyDwmDarkMode(Window window, bool isDark)
        {
            var helper = new WindowInteropHelper(window);
            IntPtr hWnd = helper.Handle;
            if (hWnd == IntPtr.Zero) return;
            try
            {
                int useDarkMode = isDark ? 1 : 0;
                if (DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
                }
            }
            catch { }
        }

        public static bool IsDarkMode
        {
            get => SettingsService.Instance.Current.IsDarkMode;
            set
            {
                SettingsService.Instance.Update(s => s.IsDarkMode = value);
                ThemeChanged?.Invoke();
            }
        }

        public static event Action? ThemeChanged;

        // Dark Palette
        public static Color ColorBgDark => Color.FromRgb(13, 17, 23);             // #0D1117
        public static Color ColorSidebarDark => Color.FromRgb(15, 23, 42);         // #0F172A
        public static Color ColorCardDark => Color.FromRgb(22, 27, 34);            // #161B22
        public static Color ColorCardSubDark => Color.FromRgb(13, 17, 23);         // #0D1117
        public static Color ColorBorderDark => Color.FromRgb(48, 54, 61);          // #30363D
        public static Color ColorTextPrimaryDark => Color.FromRgb(240, 246, 252);  // #F0F6FC
        public static Color ColorTextSecondaryDark => Color.FromRgb(139, 148, 158);// #8B949E

        // Light Palette
        public static Color ColorBgLight => Color.FromRgb(248, 250, 252);          // #F8FAFC
        public static Color ColorSidebarLight => Color.FromRgb(241, 245, 249);      // #F1F5F9
        public static Color ColorCardLight => Colors.White;
        public static Color ColorCardSubLight => Color.FromRgb(241, 245, 249);      // #F1F5F9
        public static Color ColorBorderLight => Color.FromRgb(226, 232, 240);       // #E2E8F0
        public static Color ColorTextPrimaryLight => Color.FromRgb(15, 23, 42);    // #0F172A
        public static Color ColorTextSecondaryLight => Color.FromRgb(100, 116, 139);// #64748B

        // Accents
        public static Color ColorPrimary => Color.FromRgb(59, 130, 246);           // Blue 500 (#3B82F6)
        public static Color ColorPrimaryHover => Color.FromRgb(37, 99, 235);       // Blue 600
        public static Color ColorPurple => Color.FromRgb(168, 85, 247);            // Purple 500 (#A855F7)
        public static Color ColorSuccess => Color.FromRgb(16, 185, 129);           // Emerald 500 (#10B981)
        public static Color ColorDanger => Color.FromRgb(239, 68, 68);             // Red 500 (#EF4444)
        public static Color ColorCyan => Color.FromRgb(56, 189, 248);              // Sky 400 (#38BDF8)
        public static Color ColorWarning => Color.FromRgb(245, 158, 11);           // Amber 500 (#F59E0B)

        // Dynamic Brushes
        public static SolidColorBrush BgBrush => new(IsDarkMode ? ColorBgDark : ColorBgLight);
        public static SolidColorBrush SidebarBrush => new(IsDarkMode ? ColorSidebarDark : ColorSidebarLight);
        public static SolidColorBrush CardBrush => new(IsDarkMode ? ColorCardDark : ColorCardLight);
        public static SolidColorBrush CardSubBrush => new(IsDarkMode ? ColorCardSubDark : ColorCardSubLight);
        public static SolidColorBrush BorderBrush => new(IsDarkMode ? ColorBorderDark : ColorBorderLight);
        public static SolidColorBrush TextPrimaryBrush => new(IsDarkMode ? ColorTextPrimaryDark : ColorTextPrimaryLight);
        public static SolidColorBrush TextSecondaryBrush => new(IsDarkMode ? ColorTextSecondaryDark : ColorTextSecondaryLight);

        public static SolidColorBrush PrimaryBrush => new(ColorPrimary);
        public static SolidColorBrush PurpleBrush => new(ColorPurple);
        public static SolidColorBrush SuccessBrush => new(ColorSuccess);
        public static SolidColorBrush DangerBrush => new(ColorDanger);
        public static SolidColorBrush CyanBrush => new(ColorCyan);

        public static System.Windows.Style CreateDataGridHeaderStyle()
        {
            var style = new System.Windows.Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
            style.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.BackgroundProperty, CardSubBrush));
            style.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.ForegroundProperty, TextSecondaryBrush));
            style.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.FontWeightProperty, System.Windows.FontWeights.Medium));
            style.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.FontSizeProperty, 12.5));
            style.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.BorderBrushProperty, BorderBrush));
            style.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.BorderThicknessProperty, new System.Windows.Thickness(0, 0, 1, 1)));
            style.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.PaddingProperty, new System.Windows.Thickness(6, 8, 6, 8)));
            return style;
        }

        public static System.Windows.Style CreateDataGridCellStyle()
        {
            var style = new System.Windows.Style(typeof(DataGridCell));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 0, 6, 0)));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));

            var selTrigger = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            var selBg = new SolidColorBrush(IsDarkMode ? Color.FromRgb(30, 41, 59) : Color.FromRgb(219, 234, 254));
            selBg.Freeze();
            selTrigger.Setters.Add(new Setter(Control.BackgroundProperty, selBg));
            selTrigger.Setters.Add(new Setter(Control.ForegroundProperty, IsDarkMode ? new SolidColorBrush(Colors.White) : TextPrimaryBrush));
            selTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Triggers.Add(selTrigger);

            return style;
        }

        public static System.Windows.Style CreateDataGridRowStyle()
        {
            var style = new System.Windows.Style(typeof(DataGridRow));
            style.Setters.Add(new Setter(Control.HeightProperty, 35.0));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.5));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            var hoverBg = new SolidColorBrush(IsDarkMode ? Color.FromRgb(28, 36, 48) : Color.FromRgb(241, 245, 249));
            hoverBg.Freeze();
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, hoverBg));
            style.Triggers.Add(hoverTrigger);

            return style;
        }

        private static Style? _buttonStyle;
        private static Style? _checkBoxStyle;
        private static Style? _comboBoxStyle;
        private static Style? _comboBoxItemStyle;
        private static Style? _scrollBarStyle;

        public static void ApplyWindowResources(ResourceDictionary targetResources)
        {
            if (targetResources == null) return;

            bool isDark = IsDarkMode;

            void SetBrush(string key, Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                targetResources[key] = brush;
                if (Application.Current != null && !ReferenceEquals(Application.Current.Resources, targetResources))
                {
                    Application.Current.Resources[key] = brush;
                }
            }

            if (isDark)
            {
                SetBrush("ThemeFg", ColorTextPrimaryDark);                          // #F0F6FC
                SetBrush("ThemeFgMuted", Color.FromRgb(148, 163, 184));             // #94A3B8
                SetBrush("ThemeCheckBg", Color.FromRgb(22, 27, 34));                // #161B22
                SetBrush("ThemeCheckHoverBg", Color.FromRgb(30, 41, 59));           // #1E293B
                SetBrush("ThemeCheckBorder", Color.FromRgb(71, 85, 105));           // #475569
                SetBrush("ThemeCheckHoverBorder", ColorPrimary);                    // #3B82F6
                SetBrush("ThemeComboBg", Color.FromRgb(13, 17, 23));                // #0D1117
                SetBrush("ThemeComboHoverBg", Color.FromRgb(30, 41, 59));           // #1E293B
                SetBrush("ThemeComboBorder", Color.FromRgb(51, 65, 85));            // #334155
                SetBrush("ThemeComboPopupBg", Color.FromRgb(22, 27, 34));           // #161B22
                SetBrush("ThemeComboItemHoverBg", Color.FromRgb(30, 41, 59));       // #1E293B
                SetBrush("ThemeComboItemSelectedBg", ColorPrimaryHover);            // #2563EB
                SetBrush("ThemeScrollTrack", Color.FromRgb(13, 17, 23));            // #0D1117
                SetBrush("ThemeScrollThumb", Color.FromRgb(51, 65, 85));            // #334155
                SetBrush("ThemeScrollThumbHover", Color.FromRgb(71, 85, 105));      // #475569
                SetBrush("ThemeScrollThumbDrag", ColorPrimary);                     // #3B82F6
                SetBrush("ThemeBtnDisabledBg", Color.FromRgb(28, 35, 47));          // #1C232F
                SetBrush("ThemeBtnDisabledBorder", Color.FromRgb(51, 65, 85));      // #334155
                SetBrush("ThemeBtnDisabledFg", Color.FromRgb(148, 163, 184));       // #94A3B8
            }
            else
            {
                SetBrush("ThemeFg", ColorTextPrimaryLight);                         // #0F172A
                SetBrush("ThemeFgMuted", Color.FromRgb(71, 85, 105));               // #475569
                SetBrush("ThemeCheckBg", Colors.White);
                SetBrush("ThemeCheckHoverBg", Color.FromRgb(241, 245, 249));        // #F1F5F9
                SetBrush("ThemeCheckBorder", Color.FromRgb(148, 163, 184));         // #94A3B8
                SetBrush("ThemeCheckHoverBorder", ColorPrimaryHover);               // #2563EB
                SetBrush("ThemeComboBg", Colors.White);
                SetBrush("ThemeComboHoverBg", Color.FromRgb(248, 250, 252));        // #F8FAFC
                SetBrush("ThemeComboBorder", Color.FromRgb(203, 213, 225));         // #CBD5E1
                SetBrush("ThemeComboPopupBg", Colors.White);
                SetBrush("ThemeComboItemHoverBg", Color.FromRgb(239, 246, 255));    // #EFF6FF
                SetBrush("ThemeComboItemSelectedBg", ColorPrimaryHover);            // #2563EB
                SetBrush("ThemeScrollTrack", Color.FromRgb(241, 245, 249));         // #F1F5F9
                SetBrush("ThemeScrollThumb", Color.FromRgb(203, 213, 225));         // #CBD5E1
                SetBrush("ThemeScrollThumbHover", Color.FromRgb(148, 163, 184));    // #94A3B8
                SetBrush("ThemeScrollThumbDrag", ColorPrimaryHover);                // #2563EB
                SetBrush("ThemeBtnDisabledBg", Color.FromRgb(226, 232, 240));       // #E2E8F0
                SetBrush("ThemeBtnDisabledBorder", Color.FromRgb(203, 213, 225));   // #CBD5E1
                SetBrush("ThemeBtnDisabledFg", Color.FromRgb(100, 116, 139));       // #64748B
            }

            SetBrush("ThemeAccent", ColorPrimaryHover);                             // #2563EB
            SetBrush("ThemeAccentHover", Color.FromRgb(29, 78, 216));               // #1D4ED8

            EnsureImplicitStyles(targetResources);
            if (Application.Current != null && !ReferenceEquals(Application.Current.Resources, targetResources))
            {
                EnsureImplicitStyles(Application.Current.Resources);
            }
        }

        private static void EnsureImplicitStyles(ResourceDictionary res)
        {
            if (!res.Contains(typeof(Button))) res[typeof(Button)] = GetButtonStyle();
            if (!res.Contains(typeof(CheckBox))) res[typeof(CheckBox)] = GetCheckBoxStyle();
            if (!res.Contains(typeof(ComboBox))) res[typeof(ComboBox)] = GetComboBoxStyle();
            if (!res.Contains(typeof(ComboBoxItem))) res[typeof(ComboBoxItem)] = GetComboBoxItemStyle();
            if (!res.Contains(typeof(ScrollBar))) res[typeof(ScrollBar)] = GetScrollBarStyle();
        }

        public static Style GetButtonStyle()
        {
            if (_buttonStyle != null) return _buttonStyle;

            const string xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='{x:Type Button}'>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='HorizontalContentAlignment' Value='Center'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='Template'>
        <Setter.Value>
            <ControlTemplate TargetType='{x:Type Button}'>
                <Border x:Name='BtnBorder'
                        Background='{TemplateBinding Background}'
                        BorderBrush='{TemplateBinding BorderBrush}'
                        BorderThickness='{TemplateBinding BorderThickness}'
                        CornerRadius='6'
                        Padding='{TemplateBinding Padding}'
                        SnapsToDevicePixels='True'>
                    <ContentPresenter x:Name='BtnContent'
                                      HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'
                                      VerticalAlignment='{TemplateBinding VerticalContentAlignment}'
                                      RecognizesAccessKey='True'/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'>
                        <Setter TargetName='BtnBorder' Property='Opacity' Value='0.90'/>
                    </Trigger>
                    <Trigger Property='IsPressed' Value='True'>
                        <Setter TargetName='BtnBorder' Property='Opacity' Value='0.78'/>
                    </Trigger>
                    <Trigger Property='IsEnabled' Value='False'>
                        <Setter TargetName='BtnBorder' Property='Background' Value='{DynamicResource ThemeBtnDisabledBg}'/>
                        <Setter TargetName='BtnBorder' Property='BorderBrush' Value='{DynamicResource ThemeBtnDisabledBorder}'/>
                        <Setter TargetName='BtnBorder' Property='BorderThickness' Value='1'/>
                        <Setter TargetName='BtnContent' Property='TextElement.Foreground' Value='{DynamicResource ThemeBtnDisabledFg}'/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>";

            _buttonStyle = (Style)XamlReader.Parse(xaml);
            _buttonStyle.Seal();
            return _buttonStyle;
        }

        public static Style GetCheckBoxStyle()
        {
            if (_checkBoxStyle != null) return _checkBoxStyle;

            const string xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='{x:Type CheckBox}'>
    <Setter Property='Foreground' Value='{DynamicResource ThemeFg}'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='FontWeight' Value='Normal'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
        <Setter.Value>
            <ControlTemplate TargetType='{x:Type CheckBox}'>
                <Border Background='Transparent' SnapsToDevicePixels='True'>
                    <Grid VerticalAlignment='Center'>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width='Auto'/>
                            <ColumnDefinition Width='*'/>
                        </Grid.ColumnDefinitions>
                        <Border x:Name='CheckBoxRect'
                                Grid.Column='0'
                                Width='18' Height='18'
                                CornerRadius='5'
                                BorderThickness='1.5'
                                VerticalAlignment='Center'
                                Background='{DynamicResource ThemeCheckBg}'
                                BorderBrush='{DynamicResource ThemeCheckBorder}'>
                            <Grid FlowDirection='LeftToRight'>
                                <Path x:Name='CheckMark'
                                      Data='M 2.8,7.8 L 6.2,11.2 L 12.2,4.2'
                                      Stroke='#FFFFFF'
                                      StrokeThickness='2.1'
                                      StrokeStartLineCap='Round'
                                      StrokeEndLineCap='Round'
                                      StrokeLineJoin='Round'
                                      Stretch='None'
                                      Visibility='Collapsed'/>
                                <Border x:Name='IndeterminateMark'
                                        Width='8' Height='2'
                                        CornerRadius='1'
                                        Background='#FFFFFF'
                                        HorizontalAlignment='Center'
                                        VerticalAlignment='Center'
                                        Visibility='Collapsed'/>
                            </Grid>
                        </Border>
                        <ContentPresenter x:Name='ContentHost'
                                          Grid.Column='1'
                                          Margin='9,0,0,0'
                                          VerticalAlignment='Center'
                                          HorizontalAlignment='Left'
                                          RecognizesAccessKey='True'/>
                    </Grid>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property='HasContent' Value='False'>
                        <Setter TargetName='ContentHost' Property='Visibility' Value='Collapsed'/>
                    </Trigger>
                    <Trigger Property='IsChecked' Value='True'>
                        <Setter TargetName='CheckBoxRect' Property='Background' Value='{DynamicResource ThemeAccent}'/>
                        <Setter TargetName='CheckBoxRect' Property='BorderBrush' Value='{DynamicResource ThemeAccent}'/>
                        <Setter TargetName='CheckMark' Property='Visibility' Value='Visible'/>
                    </Trigger>
                    <Trigger Property='IsChecked' Value='{x:Null}'>
                        <Setter TargetName='CheckBoxRect' Property='Background' Value='{DynamicResource ThemeAccent}'/>
                        <Setter TargetName='CheckBoxRect' Property='BorderBrush' Value='{DynamicResource ThemeAccent}'/>
                        <Setter TargetName='IndeterminateMark' Property='Visibility' Value='Visible'/>
                    </Trigger>
                    <MultiTrigger>
                        <MultiTrigger.Conditions>
                            <Condition Property='IsMouseOver' Value='True'/>
                            <Condition Property='IsChecked' Value='False'/>
                        </MultiTrigger.Conditions>
                        <Setter TargetName='CheckBoxRect' Property='Background' Value='{DynamicResource ThemeCheckHoverBg}'/>
                        <Setter TargetName='CheckBoxRect' Property='BorderBrush' Value='{DynamicResource ThemeCheckHoverBorder}'/>
                    </MultiTrigger>
                    <MultiTrigger>
                        <MultiTrigger.Conditions>
                            <Condition Property='IsMouseOver' Value='True'/>
                            <Condition Property='IsChecked' Value='True'/>
                        </MultiTrigger.Conditions>
                        <Setter TargetName='CheckBoxRect' Property='Background' Value='{DynamicResource ThemeAccentHover}'/>
                        <Setter TargetName='CheckBoxRect' Property='BorderBrush' Value='{DynamicResource ThemeAccentHover}'/>
                    </MultiTrigger>
                    <Trigger Property='IsEnabled' Value='False'>
                        <Setter Property='Opacity' Value='0.5'/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>";
            _checkBoxStyle = (Style)XamlReader.Parse(xaml);
            return _checkBoxStyle;
        }

        public static Style GetComboBoxStyle()
        {
            if (_comboBoxStyle != null) return _comboBoxStyle;

            const string xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='{x:Type ComboBox}'>
    <Setter Property='Foreground' Value='{DynamicResource ThemeFg}'/>
    <Setter Property='Background' Value='{DynamicResource ThemeComboBg}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource ThemeComboBorder}'/>
    <Setter Property='FontSize' Value='12.5'/>
    <Setter Property='FontWeight' Value='Normal'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Auto'/>
    <Setter Property='ScrollViewer.VerticalScrollBarVisibility' Value='Auto'/>
    <Setter Property='ScrollViewer.CanContentScroll' Value='True'/>
    <Setter Property='Template'>
        <Setter.Value>
            <ControlTemplate TargetType='{x:Type ComboBox}'>
                <Grid SnapsToDevicePixels='True'>
                    <ToggleButton x:Name='ToggleButton'
                                  Grid.ColumnSpan='2'
                                  Focusable='False'
                                  ClickMode='Press'
                                  IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
                        <ToggleButton.Template>
                            <ControlTemplate TargetType='{x:Type ToggleButton}'>
                                <Border x:Name='BoxBorder'
                                        CornerRadius='6'
                                        BorderThickness='1'
                                        Background='{DynamicResource ThemeComboBg}'
                                        BorderBrush='{DynamicResource ThemeComboBorder}'
                                        SnapsToDevicePixels='True'>
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width='*'/>
                                            <ColumnDefinition Width='28'/>
                                        </Grid.ColumnDefinitions>
                                        <Path x:Name='Chevron'
                                              Grid.Column='1'
                                              Data='M 0,0 L 4,4 L 8,0'
                                              Stroke='{DynamicResource ThemeFgMuted}'
                                              StrokeThickness='1.75'
                                              StrokeStartLineCap='Round'
                                              StrokeEndLineCap='Round'
                                              StrokeLineJoin='Round'
                                              HorizontalAlignment='Center'
                                              VerticalAlignment='Center'/>
                                    </Grid>
                                </Border>
                                <ControlTemplate.Triggers>
                                    <Trigger Property='IsMouseOver' Value='True'>
                                        <Setter TargetName='BoxBorder' Property='Background' Value='{DynamicResource ThemeComboHoverBg}'/>
                                        <Setter TargetName='BoxBorder' Property='BorderBrush' Value='{DynamicResource ThemeCheckHoverBorder}'/>
                                        <Setter TargetName='Chevron' Property='Stroke' Value='{DynamicResource ThemeFg}'/>
                                    </Trigger>
                                    <Trigger Property='IsChecked' Value='True'>
                                        <Setter TargetName='BoxBorder' Property='BorderBrush' Value='{DynamicResource ThemeAccent}'/>
                                        <Setter TargetName='Chevron' Property='Stroke' Value='{DynamicResource ThemeAccent}'/>
                                    </Trigger>
                                </ControlTemplate.Triggers>
                            </ControlTemplate>
                        </ToggleButton.Template>
                    </ToggleButton>
                    <ContentPresenter x:Name='ContentSite'
                                      IsHitTestVisible='False'
                                      Content='{TemplateBinding SelectionBoxItem}'
                                      ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'
                                      ContentTemplateSelector='{TemplateBinding ItemTemplateSelector}'
                                      Margin='10,2,28,2'
                                      VerticalAlignment='Center'
                                      HorizontalAlignment='Stretch'/>
                    <Popup x:Name='PART_Popup'
                           AllowsTransparency='True'
                           Placement='Bottom'
                           IsOpen='{TemplateBinding IsDropDownOpen}'
                           PopupAnimation='Slide'
                           Focusable='False'
                           FlowDirection='{Binding FlowDirection, RelativeSource={RelativeSource TemplatedParent}}'>
                        <Border x:Name='DropDownBorder'
                                MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'
                                MaxHeight='{TemplateBinding MaxDropDownHeight}'
                                Margin='0,4,0,0'
                                CornerRadius='6'
                                BorderThickness='1'
                                Padding='4'
                                Background='{DynamicResource ThemeComboPopupBg}'
                                BorderBrush='{DynamicResource ThemeComboBorder}'
                                SnapsToDevicePixels='True'>
                            <ScrollViewer SnapsToDevicePixels='True'>
                                <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Contained'/>
                            </ScrollViewer>
                        </Border>
                    </Popup>
                </Grid>
                <ControlTemplate.Triggers>
                    <Trigger Property='IsEnabled' Value='False'>
                        <Setter Property='Opacity' Value='0.5'/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>";
            _comboBoxStyle = (Style)XamlReader.Parse(xaml);
            return _comboBoxStyle;
        }

        public static Style GetComboBoxItemStyle()
        {
            if (_comboBoxItemStyle != null) return _comboBoxItemStyle;

            const string xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='{x:Type ComboBoxItem}'>
    <Setter Property='Foreground' Value='{DynamicResource ThemeFg}'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='FontSize' Value='12.5'/>
    <Setter Property='FontWeight' Value='Normal'/>
    <Setter Property='Padding' Value='10,7'/>
    <Setter Property='Margin' Value='0,1'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
        <Setter.Value>
            <ControlTemplate TargetType='{x:Type ComboBoxItem}'>
                <Border x:Name='ItemBorder'
                        CornerRadius='5'
                        Padding='{TemplateBinding Padding}'
                        Background='{TemplateBinding Background}'
                        SnapsToDevicePixels='True'>
                    <ContentPresenter VerticalAlignment='Center' HorizontalAlignment='Stretch'/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property='IsHighlighted' Value='True'>
                        <Setter TargetName='ItemBorder' Property='Background' Value='{DynamicResource ThemeComboItemHoverBg}'/>
                    </Trigger>
                    <Trigger Property='IsSelected' Value='True'>
                        <Setter TargetName='ItemBorder' Property='Background' Value='{DynamicResource ThemeComboItemSelectedBg}'/>
                        <Setter Property='Foreground' Value='#FFFFFF'/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>";
            _comboBoxItemStyle = (Style)XamlReader.Parse(xaml);
            return _comboBoxItemStyle;
        }

        public static Style GetScrollBarStyle()
        {
            if (_scrollBarStyle != null) return _scrollBarStyle;

            const string xaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='{x:Type ScrollBar}'>
    <Setter Property='Stylus.IsFlicksEnabled' Value='False'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='10'/>
    <Setter Property='MinWidth' Value='10'/>
    <Setter Property='Template'>
        <Setter.Value>
            <ControlTemplate TargetType='{x:Type ScrollBar}'>
                <Border Background='{DynamicResource ThemeScrollTrack}' CornerRadius='5' Margin='1,2'>
                    <Track x:Name='PART_Track' IsDirectionReversed='True' Focusable='False'>
                        <Track.DecreaseRepeatButton>
                            <RepeatButton Command='ScrollBar.PageUpCommand' Focusable='False'>
                                <RepeatButton.Template>
                                    <ControlTemplate TargetType='{x:Type RepeatButton}'>
                                        <Border Background='Transparent'/>
                                    </ControlTemplate>
                                </RepeatButton.Template>
                            </RepeatButton>
                        </Track.DecreaseRepeatButton>
                        <Track.Thumb>
                            <Thumb MinHeight='28'>
                                <Thumb.Template>
                                    <ControlTemplate TargetType='{x:Type Thumb}'>
                                        <Border x:Name='ThumbRect'
                                                CornerRadius='3.5'
                                                Margin='1.5'
                                                Background='{DynamicResource ThemeScrollThumb}'/>
                                        <ControlTemplate.Triggers>
                                            <Trigger Property='IsMouseOver' Value='True'>
                                                <Setter TargetName='ThumbRect' Property='Background' Value='{DynamicResource ThemeScrollThumbHover}'/>
                                            </Trigger>
                                            <Trigger Property='IsDragging' Value='True'>
                                                <Setter TargetName='ThumbRect' Property='Background' Value='{DynamicResource ThemeScrollThumbDrag}'/>
                                            </Trigger>
                                        </ControlTemplate.Triggers>
                                    </ControlTemplate>
                                </Thumb.Template>
                            </Thumb>
                        </Track.Thumb>
                        <Track.IncreaseRepeatButton>
                            <RepeatButton Command='ScrollBar.PageDownCommand' Focusable='False'>
                                <RepeatButton.Template>
                                    <ControlTemplate TargetType='{x:Type RepeatButton}'>
                                        <Border Background='Transparent'/>
                                    </ControlTemplate>
                                </RepeatButton.Template>
                            </RepeatButton>
                        </Track.IncreaseRepeatButton>
                    </Track>
                </Border>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
    <Style.Triggers>
        <Trigger Property='Orientation' Value='Horizontal'>
            <Setter Property='Width' Value='Auto'/>
            <Setter Property='MinWidth' Value='0'/>
            <Setter Property='Height' Value='10'/>
            <Setter Property='MinHeight' Value='10'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='{x:Type ScrollBar}'>
                        <Border Background='{DynamicResource ThemeScrollTrack}' CornerRadius='5' Margin='2,1'>
                            <Track x:Name='PART_Track' IsDirectionReversed='False' Focusable='False'>
                                <Track.DecreaseRepeatButton>
                                    <RepeatButton Command='ScrollBar.PageLeftCommand' Focusable='False'>
                                        <RepeatButton.Template>
                                            <ControlTemplate TargetType='{x:Type RepeatButton}'>
                                                <Border Background='Transparent'/>
                                            </ControlTemplate>
                                        </RepeatButton.Template>
                                    </RepeatButton>
                                </Track.DecreaseRepeatButton>
                                <Track.Thumb>
                                    <Thumb MinWidth='28'>
                                        <Thumb.Template>
                                            <ControlTemplate TargetType='{x:Type Thumb}'>
                                                <Border x:Name='ThumbRect'
                                                        CornerRadius='3.5'
                                                        Margin='1.5'
                                                        Background='{DynamicResource ThemeScrollThumb}'/>
                                                <ControlTemplate.Triggers>
                                                    <Trigger Property='IsMouseOver' Value='True'>
                                                        <Setter TargetName='ThumbRect' Property='Background' Value='{DynamicResource ThemeScrollThumbHover}'/>
                                                    </Trigger>
                                                    <Trigger Property='IsDragging' Value='True'>
                                                        <Setter TargetName='ThumbRect' Property='Background' Value='{DynamicResource ThemeScrollThumbDrag}'/>
                                                    </Trigger>
                                                </ControlTemplate.Triggers>
                                            </ControlTemplate>
                                        </Thumb.Template>
                                    </Thumb>
                                </Track.Thumb>
                                <Track.IncreaseRepeatButton>
                                    <RepeatButton Command='ScrollBar.PageRightCommand' Focusable='False'>
                                        <RepeatButton.Template>
                                            <ControlTemplate TargetType='{x:Type RepeatButton}'>
                                                <Border Background='Transparent'/>
                                            </ControlTemplate>
                                        </RepeatButton.Template>
                                    </RepeatButton>
                                </Track.IncreaseRepeatButton>
                            </Track>
                        </Border>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Trigger>
    </Style.Triggers>
</Style>";
            _scrollBarStyle = (Style)XamlReader.Parse(xaml);
            return _scrollBarStyle;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "\u200E0 MB";
            double mb = bytes / (1024.0 * 1024.0);
            if (mb <= 1023.0) return $"\u200E{mb:N1} MB";
            double gb = mb / 1024.0;
            return $"\u200E{gb:N2} GB";
        }

        public static string FormatRam(long bytes) => FormatBytes(bytes);

        public static Image? CreateMiniBrandLogo(double size = 20)
        {
            try
            {
                if (UITheme.AppIconBitmap == null) return null;
                using var ms = new MemoryStream();
                UITheme.AppIconBitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Seek(0, SeekOrigin.Begin);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.DecodePixelWidth = 48;
                bmp.StreamSource = ms;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                var img = new Image { Source = bmp, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                return img;
            }
            catch
            {
                return null;
            }
        }

        public static ControlTemplate CreateFlatButtonTemplate(bool enableHoverBg = false, double cornerRadius = 5)
        {
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "FlatBorder";
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));

            var cpFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            cpFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            cpFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(cpFactory);

            template.VisualTree = borderFactory;

            if (enableHoverBg)
            {
                var hoverBg = IsDarkMode
                    ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))
                    : new SolidColorBrush(Color.FromArgb(24, 15, 23, 42));
                hoverBg.Freeze();
                var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
                hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hoverBg, "FlatBorder"));
                template.Triggers.Add(hoverTrigger);
            }

            return template;
        }

        public static ControlTemplate CreateRoundedCtaTemplate(Color normalStart, Color normalEnd, Color hoverStart, Color hoverEnd, double cornerRadius = 6)
        {
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "CtaBorder";
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            borderFactory.SetValue(Border.BackgroundProperty, new LinearGradientBrush(
                normalStart, normalEnd, new Point(0, 0), new Point(1, 0)));

            var cpFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            cpFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cpFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(cpFactory);

            template.VisualTree = borderFactory;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new LinearGradientBrush(
                hoverStart, hoverEnd, new Point(0, 0), new Point(1, 0)), "CtaBorder"));
            template.Triggers.Add(hoverTrigger);

            return template;
        }
    }
}
