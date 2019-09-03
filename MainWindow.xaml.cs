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
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Dosearch_Click(object sender, RoutedEventArgs e)
        {
            if (history.Items.Contains(command.Text))
            {
                history.Items.Remove(command.Text);
                history.Items.Insert(0, command.Text);
            }
            else
                history.Items.Insert(0, command.Text);

            if (history.Items.Count > 100)
                history.Items.RemoveAt(history.Items.Count - 1);

            //MakeCommand
            GrepConfig cfg = new GrepConfig() {
                command = command.Text,
                entry = searchpath.Text,
                filter = filter.Text,
                inczip = inczip.IsChecked.Value,
                docase = docase.IsChecked.Value,
                dofilename = dofilename.IsChecked.Value,
                doregex = doregex.IsChecked.Value,
                encoding = encoding.Text,
                envlines = int.Parse(preview.Text)
            };
            ResultMake maker = new ResultMake();
            maker.MakeEnv(this, cfg, new Action(() => { }));
        }

        private void History_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (history.SelectedItem != null)
                command.Text = history.SelectedItem.ToString();
        }

        private void Selectpath_Click(object sender, RoutedEventArgs e)
        {
            // Create a "Save As" dialog for selecting a directory (HACK)
            var dialog = new Microsoft.Win32.SaveFileDialog();
            dialog.InitialDirectory = searchpath.Text; // Use current value for initial dir
            dialog.Title = "Select a Directory"; // instead of default "Save As"
            dialog.Filter = "Directory|*.this.directory"; // Prevents displaying files
            dialog.FileName = "select"; // Filename will then be "select.this.directory"
            if (dialog.ShowDialog() == true)
            {
                string path = dialog.FileName;
                // Remove fake filename from resulting path
                path = path.Replace("\\select.this.directory", "");
                path = path.Replace(".this.directory", "");
                // If user has changed the filename, create the new directory
                if (!System.IO.Directory.Exists(path))
                {
                    System.IO.Directory.CreateDirectory(path);
                }
                // Our final value is in path
                searchpath.Text = path;
            }
        }

        private void Searchpath_Drop(object sender, DragEventArgs e)
        {

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // Note that you can have more than one file.
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                // Assuming you have one file that you care about, pass it off to whatever
                // handling code you have defined.
                if (System.IO.Directory.Exists(files[0]))
                    searchpath.Text = files[0];
                else
                    searchpath.Text = System.IO.Path.GetDirectoryName(files[0]);
            }
        }

        private void Searchpath_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Handled = true;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var sizes = Properties.Settings.Default.size.Split(',');
            Height = int.Parse(sizes[0]);
            Width = int.Parse(sizes[1]);

            sp0.Width = new GridLength(int.Parse(Properties.Settings.Default.split));

            searchpath.Text = Properties.Settings.Default.searchpath;

            filter.Text = Properties.Settings.Default.filters;

            encoding.Text = Properties.Settings.Default.encoding;

            var histories = Properties.Settings.Default.history.Split('\n');
            for (var i = 0; i < histories.Length - 1; i++)
                history.Items.Add(histories[i]);

            var sw = Properties.Settings.Default.switches.Split(',');
            inczip.IsChecked = sw[0] == "1";
            docase.IsChecked = sw[1] == "1";
            dofilename.IsChecked = sw[2] == "1";
            doregex.IsChecked = sw[3] == "1";

            preview.Text = Properties.Settings.Default.previews;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Properties.Settings.Default["size"] = string.Format("{0},{1}", Height, Width);
            Properties.Settings.Default["split"] = sp0.ActualWidth.ToString();
            Properties.Settings.Default["searchpath"] = searchpath.Text;
            Properties.Settings.Default["filters"] = filter.Text;
            Properties.Settings.Default["encoding"] = encoding.Text;
            List<string> items = new List<string>();
            for (var i = 0; i < history.Items.Count; i++) items.Add(history.Items[i].ToString());
            items.Add("");
            Properties.Settings.Default["history"] = string.Join("\n", items);
            Properties.Settings.Default["switches"] =
                (inczip.IsChecked.Value ? "1" : "0") + "," +
                (docase.IsChecked.Value ? "1" : "0") + "," +
                (dofilename.IsChecked.Value ? "1" : "0") + "," +
                (doregex.IsChecked.Value ? "1" : "0");
            Properties.Settings.Default["previews"] = preview.Text;
            Properties.Settings.Default.Save();
        }

        private void History_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (history.SelectedIndex != -1)
                Dosearch_Click(sender, e);
        }
    }
}
