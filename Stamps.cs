using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IpInfo
{
    /// <summary>국가코드 도장 (색 계산과 큰 도장 만들기)</summary>
    public static class Stamps
    {
        // 자주 보는 국가는 서로 잘 구분되는 색을 고정, 나머지는 팔레트에서
        private static readonly Dictionary<string, int> FixedHue = new Dictionary<string, int>
        {
            ["KR"] = 212, ["CN"] = 2, ["US"] = 248, ["JP"] = 330, ["RU"] = 24, ["KP"] = 352,
            ["HK"] = 300, ["TW"] = 175, ["VN"] = 140, ["IN"] = 28, ["DE"] = 48, ["NL"] = 195,
            ["GB"] = 228, ["FR"] = 285, ["SG"] = 160, ["AU"] = 100, ["BR"] = 125, ["IR"] = 75, ["UA"] = 205,
        };
        private static readonly int[] Palette = { 160, 285, 45, 190, 120, 310, 70, 235, 15, 265 };
        private static readonly Dictionary<string, SolidColorBrush> Cache = new Dictionary<string, SolidColorBrush>();
        private static readonly SolidColorBrush Neutral = Freeze(new SolidColorBrush(Color.FromRgb(0x5A, 0x6B, 0x7D)));

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        public static bool IsNeutral(string cc) => cc == "--" || cc == "ZZ" || string.IsNullOrEmpty(cc);

        public static SolidColorBrush BrushFor(string cc)
        {
            if (IsNeutral(cc)) return Neutral;
            lock (Cache)
            {
                if (Cache.TryGetValue(cc, out var b)) return b;
                int hue;
                if (!FixedHue.TryGetValue(cc, out hue))
                {
                    uint h = 0;
                    foreach (char ch in cc) h = unchecked(h * 31 + ch);
                    hue = Palette[h % (uint)Palette.Length];
                }
                b = Freeze(new SolidColorBrush(FromHsl(hue, 0.52, 0.36)));
                Cache[cc] = b;
                return b;
            }
        }

        private static Color FromHsl(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double hp = h / 60.0;
            double x = c * (1 - Math.Abs(hp % 2 - 1));
            double r = 0, g = 0, b = 0;
            if (hp < 1) { r = c; g = x; }
            else if (hp < 2) { r = x; g = c; }
            else if (hp < 3) { g = c; b = x; }
            else if (hp < 4) { g = x; b = c; }
            else if (hp < 5) { r = x; b = c; }
            else { r = c; b = x; }
            double m = l - c / 2;
            byte To(double v) => (byte)Math.Round(Math.Clamp((v + m) * 255, 0, 255));
            return Color.FromRgb(To(r), To(g), To(b));
        }

        private static FontFamily StampFont => (FontFamily)Application.Current.FindResource("StampFont");
        private static FontFamily UiFont => (FontFamily)Application.Current.FindResource("UiFont");

        /// <summary>단건 조회 결과에 쓰는 큰 도장. animate 이면 찍히는 동작을 보여 줌.</summary>
        public static FrameworkElement Big(string cc, string name, bool animate)
        {
            var brush = BrushFor(cc);
            var root = new Grid { Width = 172, Height = 140, RenderTransformOrigin = new Point(0.5, 0.5) };
            root.Children.Add(new Border { BorderBrush = brush, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(12) });
            root.Children.Add(new Border
            {
                BorderBrush = brush, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8),
                Margin = new Thickness(6),
            });
            var text = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = cc, FontFamily = StampFont, FontWeight = FontWeights.Bold, FontSize = 72,
                Foreground = brush, HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 72,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            });
            if (!string.IsNullOrEmpty(name))
                text.Children.Add(new TextBlock
                {
                    Text = name, FontFamily = UiFont, FontWeight = FontWeights.Bold, FontSize = 14,
                    Foreground = brush, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap, MaxWidth = 140, Margin = new Thickness(0, 6, 0, 0),
                });
            root.Children.Add(text);
            root.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "국가코드 " + cc);

            var rotate = new RotateTransform(-4);
            var scale = new ScaleTransform(1, 1);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(rotate);
            root.RenderTransform = group;

            if (animate && SystemParameters.ClientAreaAnimation)
            {
                var dur = TimeSpan.FromMilliseconds(320);
                var ease = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.7, 1, dur) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.7, 1, dur) { EasingFunction = ease });
                rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-14, -4, dur) { EasingFunction = ease });
                root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            }
            return root;
        }

        /// <summary>걸러보기 칩에 쓰는 작은 도장</summary>
        public static FrameworkElement Mini(string cc)
        {
            var brush = BrushFor(cc);
            return new Border
            {
                BorderBrush = brush, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 1, 6, 0), MinWidth = 40, VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = cc, Foreground = brush, FontFamily = StampFont, FontWeight = FontWeights.Bold,
                    FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center,
                },
            };
        }
    }
}
