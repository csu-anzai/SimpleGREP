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
    /// SearchResult.xaml 的交互逻辑
    /// </summary>
    public partial class SearchResult : UserControl
    {
        public readonly int MAXFILECNT = 10000;
        public int filec = 0;
        public int matchc = 0;
        public SearchResult()
        {
            InitializeComponent();
        }

        private void FileCntAdd()
        {
            filec += 1;
            filecnt.Text = filec.ToString();
        }

        public void MatchCntAdd()
        {
            matchc += 1;
            totalmatches.Text = matchc.ToString();
        }

        MatchFile lastfile;
        string lastextra;
        int extracnt = 0;
        Run extrafiles;
        public MatchFile AddFile(string fileName, string highlighter)
        {
            if (fileName == lastextra) return null;
            if (filec> MAXFILECNT)
            {
                lastextra = fileName;
                extracnt++;
                if (extrafiles==null)
                {
                    var tb = new TextBlock()
                    {
                        Margin = new Thickness(16, 0, 0, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(0x48, 0x97, 0x48))
                    };
                    tb.Inlines.Add(new Run("And:    "));
                    tb.Inlines.Add(extrafiles = new Run(""));
                    tb.Inlines.Add(" more files");
                    LargePanel.Children.Add(LargePanel);
                }
                extrafiles.Text = extracnt.ToString();
                return null;
            }
            if (lastfile != null && lastfile.fileName.Text == fileName) return lastfile;

            MatchFile filem = new MatchFile();
            filem.SetFilename(fileName, highlighter);
            filem.HorizontalAlignment = HorizontalAlignment.Stretch;
            filem.SetPreviewLines(envapply);
            LargePanel.Children.Add(filem);
            FileCntAdd();
            lastfile = filem;
            return filem;
        }

        int envapply;
        internal void SetEnv(int envlines)
        {
            envapply = envlines;
        }
    }
}
