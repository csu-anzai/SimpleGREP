using grepcmd.EditHelper;
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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace grepcmd
{
    /// <summary>
    /// MatchFile.xaml 的交互逻辑
    /// 仅能在呈现线程访问
    /// 带2种保护：行过多(>1000)，单行过长(>1000)
    /// 用Highlight简单处理高亮
    /// </summary>
    public partial class MatchFile : UserControl
    {
        public readonly int MAXLINES = 1000;
        public readonly int MAXSINGLELINE = 1000;

        public MatchFile()
        {
            InitializeComponent();
            plussign.Visibility = Visibility.Hidden;
            markdatas = new List<MarkDataItem>();
            markmap = new Dictionary<int, int>();
            lineBuffer = new Dictionary<int, (string data, bool toolong, TaggingHighlight highlight)>();
            extraLines = new HashSet<int>();
        }
        public void SetPreviewLines(int lines)
        {
            this.previewLines = lines;
        }
        public void SetFilename(string fileName, string highlighter)
        {
            this.fileName.Text = fileName;
            this.highlighter = highlighter;
        }

        int matchc = 0;
        int extralines = 0;
        HashSet<int> extraLines;
        Dictionary<int, (string data, bool toolong, TaggingHighlight highlight)> lineBuffer;
        private string highlighter;
        private int previewLines;

        public void AddLineBuffer(int line, string Data)
        {
            if (lineBuffer.ContainsKey(line)) return;
            var txt = Data.Substring(0, Math.Min(Data.Length, MAXSINGLELINE));
            lineBuffer[line] = (txt, Data.Length > MAXSINGLELINE, null); //Not highlighted line buffer
        }
        /// <summary>
        /// 添加一个对某行的标记
        /// 添加标记的顺序
        /// </summary>
        /// <param name="Line">添加到的行</param>
        /// <param name="data">行携带的数据</param>
        /// <param name="MarkStart">标记开始位置</param>
        /// <param name="MarkEnd">标记结束位置</param>
        /// <param name="hl">语法高亮配置</param>
        public void AddMark(int Line, string Data, int MarkStart, int MarkEnd, TaggingHighlight hl)
        {
            if (markdatas.Count> MAXLINES && !markmap.ContainsKey(Line))
            {
                if (!extraLines.Contains(Line))
                {
                    extralines++;
                    extraLines.Add(Line);
                }
                matchc++;
                matchcnt.Text = matchc.ToString();
                IncrementalKeepingExtra();
                return;
            }
            if (!lineBuffer.ContainsKey(Line) || lineBuffer[Line].highlight == null)
            {
                var txt = Data.Substring(0, Math.Min(Data.Length, MAXSINGLELINE));
                lineBuffer[Line] = (txt, Data.Length > MAXSINGLELINE, hl);
            }
            if (!markmap.ContainsKey(Line))
            {
                markdatas.Add(new MarkDataItem() { line = Line });
                markmap[Line] = markdatas.Count - 1;
            }
            //此处实现超过MAXSINGLELINE截断
            if (MarkStart < MAXSINGLELINE)
            {
                if (MarkEnd > MAXSINGLELINE)
                    MarkEnd = MAXSINGLELINE;
                markdatas[markmap[Line]].marks.Add((MarkStart, MarkEnd));
            }
            IncrementalKeeping(Line);
            matchc++;
            matchcnt.Text = matchc.ToString();
        }

        Run extracnt;
        private void IncrementalKeepingExtra()
        {
            if (extralines > 0)
            {
                if (extracnt == null)
                {
                    TextBlock linedata = new TextBlock()
                    {
                        Text = "",
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(85, 15 * (markdatas.Count + 1), 0, 0),
                        TextWrapping = TextWrapping.NoWrap,
                        VerticalAlignment = VerticalAlignment.Top,
                        Foreground = Brushes.LightGray
                    };
                    Grid.SetColumnSpan(linedata, 2);

                    expandtext.Children.Add(linedata);
                    linedata.Inlines.Add(new Run("... and "));
                    linedata.Inlines.Add(extracnt = new Run(""));
                    linedata.Inlines.Add(new Run(" more matched lines"));
                }

                extracnt.Text = extralines.ToString();
            }
        }

        /// <summary>
        /// 增量修改，若没有展开则忽略行更新
        /// </summary>
        /// <param name="line">要更新的行</param>
        private void IncrementalKeeping(int line)
        {
            if (plussign.Visibility != Visibility.Visible)
            {
                RebindHeight();
                var m = markdatas[markmap[line]];
                if (m.ctrl == null)
                {
                    TextBlock linehint = new TextBlock()
                    {
                        Text = line.ToString(),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 15 * markdatas.Count, 0, 0),
                        Height = 16,
                        Foreground = new SolidColorBrush(Color.FromArgb(0xff, 0x29, 0x3c, 0xff))
                    };
                    if (line == 0)
                    {
                        linehint.Text = "(file)";
                        linehint.Foreground = Brushes.LightGray;
                    }
                    else
                    {
                        linehint.Cursor = Cursors.Hand;
                        linehint.TextDecorations.Add(TextDecorations.Underline);
                        linehint.MouseDown += Linehint_MouseDown;
                        linehint.MouseMove += Linehint_MouseMove;
                        linehint.MouseLeave += Linehint_MouseLeave;
                    }

                    TextBlock linedata = new EditHelper.SelectableTextBlock()
                    {
                        Text = "",
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(85, 15 * markdatas.Count, 0, 0),
                        TextWrapping = TextWrapping.NoWrap,
                        VerticalAlignment = VerticalAlignment.Top
                    };
                    Grid.SetColumnSpan(linedata, 2);

                    expandtext.Children.Add(linehint);
                    expandtext.Children.Add(linedata);

                    m.ctrl = linedata;
                }

                m.ctrl.Inlines.Clear();

                //读取行数据，并高亮其中的标记
                string txt = lineBuffer[line].data;
                var hlcfg = lineBuffer[line].highlight;

                int stoff = 0;
                int nextcfg = 0;
                foreach (var i in m.marks)
                {
                    if (stoff < i.st)
                    {
                        nextcfg = AddInlineWithHL(nextcfg, m.ctrl.Inlines, txt.Substring(stoff, i.st - stoff), stoff, i.st, hlcfg, null);
                        stoff = i.st;
                    }
                    nextcfg = AddInlineWithHL(nextcfg, m.ctrl.Inlines, txt.Substring(stoff, i.ed - stoff), stoff, i.ed, hlcfg, new Action<Inline>((hli)=> {
                        hli.Background = new SolidColorBrush(Color.FromArgb(255, 255, 240, 0));
                        hli.TextDecorations.Add(TextDecorations.Underline);
                        hli.Tag = Tuple.Create(line, i.st, i.ed);
                        hli.MouseDown += Hli_MouseDown;
                    }));
                    stoff = i.ed;
                }
                if (stoff < txt.Length)
                    AddInlineWithHL(nextcfg, m.ctrl.Inlines, txt.Substring(stoff, txt.Length - stoff), stoff, txt.Length, hlcfg, null);
                if (lineBuffer[line].toolong)
                    m.ctrl.Inlines.Add(new Run(" ..."));
            }
        }

        internal static int AddInlineWithHL(int lastcfg, InlineCollection inlines, string data, int stp, int edp, TaggingHighlight cfg, Action<Inline> chook)
        {
            if (cfg == null || lastcfg >= cfg.spans.Count)
            {
                var r = new Run(data);
                chook?.Invoke(r);
                inlines.Add(r);
                return lastcfg;
            }
            else
            {
                int shift = 0;
                while (stp < edp)
                {
                    while (lastcfg < cfg.spans.Count && stp >= cfg.spans[lastcfg].ed)
                        lastcfg++;
                    if (lastcfg >= cfg.spans.Count)
                    {
                        var r = new Run(data.Substring(shift));
                        chook?.Invoke(r);
                        inlines.Add(r);
                        return lastcfg;
                    }

                    //加入间隔
                    var cfginst = cfg.spans[lastcfg];
                    int applyto = cfginst.st;
                    if (applyto > edp) applyto = edp;
                    if (stp < applyto)
                    {
                        var r = new Run(data.Substring(shift, applyto - stp));
                        chook?.Invoke(r);
                        inlines.Add(r);
                        shift += applyto - stp;
                        stp = applyto;
                        continue;
                    }

                    //应用颜色
                    applyto = cfginst.ed;
                    if (applyto > edp) applyto = edp;
                    if (stp < applyto)
                    {
                        var r = new Run(data.Substring(shift, applyto - stp));
                        r.Foreground = new SolidColorBrush(cfg.coloridx[cfginst.style]);
                        chook?.Invoke(r);
                        inlines.Add(r);
                        shift += applyto - stp;
                        stp = applyto;
                        continue;
                    }
                }
                return lastcfg;
            }
        }

        class MarkDataItem
        {
            public int line;
            public TextBlock ctrl;
            public List<(int st, int ed)> marks = new List<(int st, int ed)>();
        }
        List<MarkDataItem> markdatas;
        Dictionary<int, int> markmap;

        /// <summary>
        /// 生成展开的信息
        /// </summary>
        private void BuildExpandText()
        {
            plussign.Visibility = Visibility.Hidden;
            RebindHeight();
            for (int j = 0; j < markdatas.Count; j++)
            {
                TextBlock linehint = new TextBlock()
                {
                    Text = markdatas[j].line.ToString(),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 15 * (j + 1), 0, 0),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xff, 0x29, 0x3c, 0xff))
                };
                if (markdatas[j].line == 0)
                {
                    linehint.Text = "(file)";
                    linehint.Foreground = Brushes.LightGray;
                }
                else
                {
                    linehint.Cursor = Cursors.Hand;
                    linehint.TextDecorations.Add(TextDecorations.Underline);
                    linehint.MouseDown += Linehint_MouseDown;
                    linehint.MouseMove += Linehint_MouseMove;
                    linehint.MouseLeave += Linehint_MouseLeave;
                }

                TextBlock linedata = new SelectableTextBlock()
                {
                    Text = "",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(85, 15 * (j + 1), 0, 0),
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalAlignment = VerticalAlignment.Top
                };
                Grid.SetColumnSpan(linedata, 2);

                expandtext.Children.Add(linehint);
                expandtext.Children.Add(linedata);

                markdatas[j].ctrl = linedata;
            }
            for (int j = 0; j < markdatas.Count; j++) IncrementalKeeping(markdatas[j].line);
            IncrementalKeepingExtra();
        }

        SmallHintWindow hintwin;
        int leaveid = 0;
        /// <summary>
        /// 鼠标移出Linehint范围，去除代码框
        /// </summary>
        private void Linehint_MouseLeave(object sender, MouseEventArgs e)
        {
            var myleaveid = ++leaveid;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (myleaveid == leaveid && hintwin != null && hintwin.Tag == sender)
                {
                    hintwin.Hide();
                    hintwin.Tag = null;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }


        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (hintwin != null)
            {
                hintwin.Close();
                hintwin = null;
            }
        }

        /// <summary>
        /// 鼠标进入Linehint范围，生成代码框
        /// </summary>
        private void Linehint_MouseMove(object sender, MouseEventArgs e)
        {
            if (hintwin == null)
                hintwin = new SmallHintWindow();
            if (hintwin.Tag != sender)
            {
                List<(string data, bool toolong, TaggingHighlight highlight)> buildEnv = new List<(string data, bool toolong, TaggingHighlight highlight)>();

                var cline = int.Parse((sender as TextBlock).Text);
                for (var i = -previewLines; i <= previewLines; i++)
                    if (lineBuffer.ContainsKey(i + cline))
                    {
                        if (lineBuffer[i + cline].highlight == null)
                        {
                            if (highlighter != null)
                            {
                                TaggingHighlight hl = new TaggingHighlight();
                                new Highlight.Highlighter(hl).Highlight(highlighter, lineBuffer[i + cline].data);
                                lineBuffer[i + cline] = (lineBuffer[i + cline].data, lineBuffer[i + cline].toolong, hl);
                            }
                        }
                        buildEnv.Add(lineBuffer[i + cline]);
                    }
                    else
                        buildEnv.Add(("(line too long)", true, null));

                hintwin.BindText(buildEnv);
                hintwin.Show();
                hintwin.Tag = sender;
            }
            //Move window to proper position
            var ss = sender as TextBlock;
            var screenpos = ss.PointToScreen(new Point(ss.ActualWidth, ss.ActualHeight));

            hintwin.Left = screenpos.X + 5;
            hintwin.Top = screenpos.Y - hintwin.Height / 2 - ss.ActualHeight / 2 + 4;
        }

        /// <summary>
        /// 点击高亮文字
        /// </summary>
        private void Hli_MouseDown(object sender, MouseButtonEventArgs e)
        {
        }

        /// <summary>
        /// 点击行数字
        /// </summary>
        private void Linehint_MouseDown(object sender, MouseButtonEventArgs e)
        {
        }

        /// <summary>
        /// 点击文件名
        /// </summary>
        private void FileName_MouseDown(object sender, MouseButtonEventArgs e)
        {
            string filePath = (sender as Run).Text;
            if (filePath.Contains('!'))
                filePath = filePath.Split('!')[0];

            if (!System.IO.File.Exists(filePath))
            {
                return;
            }

            // combine the arguments together
            // it doesn't matter if there is a space after ','
            string argument = "/select, \"" + filePath + "\"";

            System.Diagnostics.Process.Start("explorer.exe", argument);
        }

        private void RebindHeight()
        {
            if (markdatas.Count >= 1)
            {
                lines.Y1 = linev.Y2 = (markdatas.Count - 1 + (extralines > 0 ? 1 : 0)) * 15 + 15 + 7;
                lines.Y2 = (markdatas.Count - 1 + (extralines > 0 ? 1 : 0)) * 15 + 15 + 14;
                lines.Visibility = Visibility.Visible;
                linev.Visibility = Visibility.Visible;
            }
            else
            {
                lines.Visibility = Visibility.Collapsed;
                linev.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 收缩展开的信息
        /// </summary>
        private void CollapseExpandText()
        {
            expandtext.Children.Clear();
            linev.Visibility = Visibility.Collapsed;
            lines.Visibility = Visibility.Collapsed;
            plussign.Visibility = Visibility.Visible;
            foreach (var i in markdatas) i.ctrl = null;
            extracnt = null;
        }

        /// <summary>
        /// 修改展开状态
        /// </summary>
        private void Rectangle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (plussign.Visibility == Visibility.Visible)
                BuildExpandText();
            else
                CollapseExpandText();
        }
    }
}
