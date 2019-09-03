using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace grepcmd
{
    /// <summary>
    /// SmallHintWindow.xaml 的交互逻辑
    /// </summary>
    public partial class SmallHintWindow : Window
    {
        public SmallHintWindow()
        {
            InitializeComponent();
        }

        public void BindText(List<(string data, bool toolong, TaggingHighlight highlight)> txtlines)
        {
            codecontainer.Inlines.Clear();
            int c = 0;
            foreach (var txtline in txtlines)
            {
                if (txtline.highlight == null)
                    codecontainer.Inlines.Add(new Run(txtline.data));
                else
                    MatchFile.AddInlineWithHL(0, codecontainer.Inlines, txtline.data, 0, txtline.data.Length, txtline.highlight, null);
                if (txtline.toolong)
                    codecontainer.Inlines.Add(new Run(" ..."));
                if (c != txtlines.Count - 1)
                    codecontainer.Inlines.Add(new LineBreak());
                c++;
            }

            codecontainer.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            codecontainer.Arrange(new Rect(codecontainer.DesiredSize));
            Height = Math.Ceiling(codecontainer.ActualHeight) + 12;
            Width = Math.Ceiling(codecontainer.ActualWidth) + 12;
        }

    }
}
