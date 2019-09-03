using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;

namespace grepcmd
{
    public class ResultMake
    {
        SearchResult place;
        TabItem container;
        GrepMain main;
        MainWindow window;
        Action resumecmd;

        bool running = true;

        public void MakeEnv(MainWindow window, GrepConfig config, Action resumecmds)
        {
            this.window = window;

            container = new TabItem();
            var sp = new StackPanel() { Orientation = Orientation.Horizontal };
            container.Header = sp;
            sp.Children.Add(new Label() { Content = config.command + " " });
            var btn = new Button() { Content = "X", Background = null, BorderBrush = null };
            btn.Click += Btn_Click;
            sp.Children.Add(btn);
            place = new SearchResult();
            var gd = new Grid();
            var sv = new ScrollViewer()
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            place.SetEnv(config.envlines);
            sv.Content = place;
            gd.Children.Add(sv);
            container.Content = gd;

            window.resulttabs.Items.Add(container);
            window.resulttabs.SelectedIndex = window.resulttabs.Items.Count - 1;

            resumecmd = resumecmds;

            latestprogress = ("", 1, 0);
            main = new GrepMain();
            main.DoGrep(config, stopped, finished, progress, addline, addenv);
        }

        private void Btn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (running)
            {
                InterruptRun();
            }

            window.resulttabs.Items.Remove(container);
        }

        ManualResetEvent done = new ManualResetEvent(false);
        private void InterruptRun()
        {
            main.StopGrep = true;
            done.WaitOne();
        }

        /* 批量执行函数 */
        object batchSync = new object();
        (string title, long total, long done) latestprogress;
        volatile List<(string file, int line, int st, int ed, string linedata, TaggingHighlight hl)> linesToAdd = new List<(string file, int line, int st, int ed, string linedata, TaggingHighlight hl)>();
        DispatcherTimer timer;
        
        void QueueRequest() {
            if (timer == null)
            {
                lock(batchSync)
                {
                    if (timer == null)
                    {
                        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, new EventHandler(RequestHandler), window.Dispatcher);
                        timer.Start();
                    }
                }
            }
        }

        void FlushRequest() { timer.Stop(); window.Dispatcher.BeginInvoke(new Action(()=>RequestHandler(null, null))); }

        void RequestHandler(object sender, EventArgs e)
        {
            //获取本次数据
            (string title, long total, long done) progress = latestprogress;
            List<(string file, int line, int st, int ed, string linedata, TaggingHighlight hl)> linesCommit = linesToAdd;
            linesToAdd = new List<(string file, int line, int st, int ed, string linedata, TaggingHighlight hl)>();

            //修改状态，此时处于Dispatcher线程中
            if (progress.total == 0)
            {
                window.progressinfo.Visibility = System.Windows.Visibility.Hidden;
                window.progress.Visibility = System.Windows.Visibility.Hidden;
            }
            else
            {
                window.progressinfo.Visibility = System.Windows.Visibility.Visible;
                window.progress.Visibility = System.Windows.Visibility.Visible;
                if (progress.title != (string)window.progressinfo.Content) window.progressinfo.Content = progress.title;
                window.progress.Maximum = progress.total;
                window.progress.Value = progress.done;
            }

            //逐项添加查找结果
            string lastfile = null;
            MatchFile fobj = null;
            foreach (var i in linesCommit)
            {
                if (lastfile != i.file)
                {
                    string highlightEngine;
                    extmap.TryGetValue(System.IO.Path.GetExtension(i.file).ToLower(), out highlightEngine);
                    fobj = place.AddFile(i.file, highlightEngine);
                    lastfile = i.file;
                }
                if (i.st == -1)
                    fobj?.AddLineBuffer(i.line, i.linedata);
                else
                {
                    fobj?.AddMark(i.line, i.linedata, i.st, i.ed, i.hl);
                    place.MatchCntAdd();
                }
            }
        }

        /* 
         * 异线程回调函数，再调出时一定会通过dispatcher完成
         * progress和addline会打包完成
         */
        void stopped() { done.Set(); window.Dispatcher.BeginInvoke(resumecmd); latestprogress = ("", 0, 0); FlushRequest(); }
        void finished() { done.Set(); window.Dispatcher.BeginInvoke(resumecmd); latestprogress = ("", 0, 0); FlushRequest(); }
        void progress((string title, long total, long done) progress) { latestprogress = progress; QueueRequest(); }
        string lastfile;
        int lastline;
        TaggingHighlight lasthl;

        void addenv((string file, int line, string linedata) env)
        {
            linesToAdd.Add((env.file, env.line, -1, -1, env.linedata, null));
            QueueRequest();
        }

        void addline((string file, int line, int st, int ed, string linedata) line)
        {
            
            //Apply highlight config
            TaggingHighlight hl = null;
            if (lastfile == line.file && lastline == line.line)
                hl = lasthl;
            else
            {
                string highlightEngine;
                extmap.TryGetValue(System.IO.Path.GetExtension(line.file).ToLower(), out highlightEngine);
                if (highlightEngine != null)
                {
                    hl = new TaggingHighlight();
                    new Highlight.Highlighter(hl).Highlight(highlightEngine, line.linedata);
                }
                lastfile = line.file;
                lastline = line.line;
                lasthl = hl;
            }

            linesToAdd.Add((line.file, line.line, line.st, line.ed, line.linedata, hl));
            QueueRequest();
        }

        static Dictionary<string, string> extmap = new Dictionary<string, string>()
        {
            { ".aspx", "ASPX" },
            { ".incx", "ASPX" },
            { ".c", "C" },
            { ".cu", "C" },
            { ".h", "C" },
            { ".cpp", "C++" },
            { ".cxx", "C++" },
            { ".ino", "C++" },
            { ".hpp", "C++" },
            { ".cs", "C#" },
            { ".cobol", "COBOL" },
            { ".cbl", "COBOL" },
            { ".wex", "Eiffel" },
            { ".f", "Fortran" },
            { ".f77", "Fortran" },
            { ".f90", "Fortran" },
            { ".hs", "Haskell" },
            { ".html", "HTML" },
            { ".htm", "HTML" },
            { ".java", "Java" },
            { ".js", "JavaScript" },
            { ".mryx", "Mercury" },
            { ".il", "MSIL" },
            { ".msil", "MSIL" },
            { ".pas", "Pascal" },
            { ".lpr", "Pascal" },
            { ".pp", "Pascal" },
            { ".inc", "Pascal" },
            { ".pl", "Perl" },
            { ".cgi", "Perl" },
            { ".perl", "Perl" },
            { ".prl", "Perl" },
            { ".pm", "Perl" },
            { ".php", "PHP" },
            { ".py", "Python" },
            { ".pyx", "Python" },
            { ".rb", "Ruby" },
            { ".ruby", "Ruby" },
            { ".sql", "SQL" },
            { ".bas", "Visual Basic" },
            { ".vbs", "VBScript" },
            { ".vb", "VB.NET" },
            { ".xml", "XML" },
            { ".xhtml", "XML" },
            { ".xhtm", "XML" },
            { ".dtd", "XML" }
        };

    }
}
