using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace grepcmd
{
    /// <summary>
    /// GREP配置
    /// </summary>
    public class GrepConfig
    {
        public string command;
        public string entry;
        public string filter;

        public bool inczip;
        public bool docase;
        public bool dofilename;
        public bool doregex;

        public string encoding;
        public int envlines;
    }

    /// <summary>
    /// GREP模块，用异步的方式扫描文件夹
    /// </summary>
    class GrepMain
    {
        public readonly int TotalMatchLimit = 10000;
        public void DoGrep(GrepConfig config, Action stopped, Action finished, Action<(string title, long total, long done)> ProgressReport, Action<(string file, int line, int st, int ed, string linedata)> AddLine, Action<(string file, int line, string linedata)> AddEnv)
        {
            //启动新的线程并开启Grep
            this.config = config;
            this.stopped = stopped;
            this.finished = finished;
            this.report = ProgressReport;
            this.addLine = AddLine;
            this.addEnv = AddEnv;

            grepThread = new Thread(Grep);
            grepThread.Name = "grepworker";
            grepThread.IsBackground = true;
            grepThread.Start();
        }

        Thread grepThread;
        GrepConfig config;
        Action stopped;
        Action finished;
        Action<(string title, long total, long done)> report;
        Action<(string file, int line, int st, int ed, string linedata)> addLine;
        Action<(string file, int line, string linedata)> addEnv;

        private IEnumerable<FileInfo> EnumerateFiles(string directory)
        {
            foreach (var file in new DirectoryInfo(directory).GetFiles())
                yield return file;
            foreach(var dir in Directory.EnumerateDirectories(directory))
                foreach (var j in EnumerateFiles(dir))
                    yield return j;
        }
        private Regex CompilePattern(string pattern)
        {
            if (pattern == "")
                return new Regex("");
            List<string> qstr = new List<string>();
            foreach(var i in pattern)
            {
                if (i == '?')
                    qstr.Add(".");
                else if (i == '*')
                    qstr.Add(".*");
                else if (i == ';')
                    qstr.Add("|");
                else
                    qstr.Add(Regex.Escape(new string(i, 1)));
            }
            return new Regex("^(?:" + String.Join("", qstr) + ")$");
        }
        private void Grep()
        {
            List<(string path, string container, long size)> tasks = new List<(string path, string container, long size)>();
            //先读取文件夹，遍历所有信息，计算大致时间
            report(("Scan task files", 1, 0));
            var preg = CompilePattern(config.filter);
            long totlen = 0;
            int allfiles = 0;
            foreach (var f in EnumerateFiles(config.entry))
            {
                if (preg.IsMatch(f.Name))
                {
                    tasks.Add((f.FullName, null, f.Length));
                    totlen += f.Length;
                }
                if (config.inczip && CachedArchiveIterator.ContentIterable(f.FullName))
                {
                    foreach(var ff in CachedArchiveIterator.EnumerateNames(f.FullName))
                    {
                        if (preg.IsMatch(ff.name))
                        {
                            tasks.Add((ff.name, f.FullName, ff.length));
                            totlen += ff.length;
                        }
                        allfiles++;
                        report(("Scan task files, scanned " + allfiles.ToString() + ", got " + tasks.Count.ToString(), 1, 0));
                        if (StopGrep)
                        {
                            stopped();
                            return;
                        }
                    }
                }
                allfiles++;
                report(("Scan task files, scanned " + allfiles.ToString() + ", got " + tasks.Count.ToString(), 1, 0));
                if (StopGrep)
                {
                    stopped();
                    return;
                }
            }

            //逐文件搜索
            Regex searchregex = null;
            if (config.doregex) try { searchregex = new Regex(config.command); } catch { System.Windows.MessageBox.Show("Invalid regular expression " + config.doregex); return; }
            else
            {
                pat = config.command;
                M = pat.Length;
                lps = new int[M];
                computeLPSArray(config.docase ? config.command : config.command.ToLower(), M, lps);
            }
            long scanlen = 0;
            int matchcnt = 0;
            using (var ziphelper = new CachedArchiveIterator())
                foreach (var f in tasks)
                {
                    int line = 0;
                    Stream fs;
                    if (f.container == null)
                        fs = new FileStream(f.path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1048576);
                    else
                        fs = ziphelper.GetArchiveFileStream(f.container, f.path);
                    try
                    {
                        if (config.dofilename)
                        {
                            var fts = Path.GetFileName(f.path);
                            int shift = f.path.Length - fts.Length;
                            if (config.doregex)
                                foreach(var m in searchregex.Matches(fts))
                                {
                                    var mm = m as Match;
                                    addLine(((f.container == null ? "" : (f.container + "!")) + f.path, 0, mm.Index + shift, mm.Index + mm.Length + shift, f.path));
                                    matchcnt++;
                                    if (matchcnt > TotalMatchLimit)
                                    {
                                        stopped();
                                        return;
                                    }
                                }
                            else
                                foreach (var posi in KMPSearch(config.docase ? fts : fts.ToLower()))
                                {
                                    addLine(((f.container == null ? "" : (f.container + "!")) + f.path, line, posi + shift, posi + M + shift, f.path));
                                    matchcnt++;
                                    if (matchcnt > TotalMatchLimit)
                                    {
                                        stopped();
                                        return;
                                    }
                                }
                        }
                        StreamReader sr = new StreamReader(fs, Encoding.GetEncoding(config.encoding));
                        var fp = "Scan " + Path.GetFileName(f.path);
                        int tocache = 0;
                        Queue<(int line, string s)> prevlines = new Queue<(int line, string s)>();
                        while (true)
                        {
                            line++;
                            tocache--;
                            var lr = sr.ReadLine();
                            if (lr == null) break;
                            if (lr.Length > 65536) continue;
                            //处理上下文缓存部分
                            if (tocache > 0)
                                addEnv(((f.container == null ? "" : (f.container + "!")) + f.path, line, lr));
                            else
                            {
                                prevlines.Enqueue((line, lr));
                                if (prevlines.Count > config.envlines + 1) //若匹配则本行也会在queue中，所以取上文需要多加一行
                                    prevlines.Dequeue();
                                tocache = 0;
                            }

                            bool matched = false;
                            if (config.doregex)
                                foreach (var m in searchregex.Matches(lr))
                                {
                                    var mm = m as Match;
                                    addLine(((f.container == null ? "" : (f.container + "!")) + f.path, line, mm.Index, mm.Index + mm.Length, lr));
                                    matchcnt++;
                                    if (matchcnt > TotalMatchLimit)
                                    {
                                        stopped();
                                        return;
                                    }
                                    matched = true;
                                }
                            else
                                foreach (var posi in KMPSearch(config.docase ? lr : lr.ToLower()))
                                {
                                    addLine(((f.container == null ? "" : (f.container + "!")) + f.path, line, posi, posi + M, lr));
                                    matchcnt++;
                                    if (matchcnt > TotalMatchLimit)
                                    {
                                        stopped();
                                        return;
                                    }
                                    matched = true;
                                }
                            //在匹配的行周围加上上下文信息
                            if (matched)
                            {
                                while (prevlines.Count > 0)
                                {
                                    var t = prevlines.Dequeue();
                                    addEnv(((f.container == null ? "" : (f.container + "!")) + f.path, t.line, t.s));
                                }
                                tocache = config.envlines + 1;
                            }

                            report((fp, totlen, scanlen + fs.Position));
                            if (StopGrep)
                            {
                                stopped();
                                return;
                            }
                        }
                        scanlen += f.size;
                        report((fp, totlen, scanlen));
                    }
                    finally { fs.Close(); }
                }

            finished();
        }

        public bool StopGrep = false;

        int M;
        int[] lps;
        string pat;
        private IEnumerable<int> KMPSearch(string txt)
        {
            int N = txt.Length;
            int j = 0;
            int i = 0; // index for txt[] 
            while (i < N)
            {
                if (pat[j] == txt[i])
                {
                    j++;
                    i++;
                }
                if (j == M)
                {
                    yield return i - j;
                    j = lps[j - 1];
                }

                // mismatch after j matches 
                else if (i < N && pat[j] != txt[i])
                {
                    // Do not match lps[0..lps[j-1]] characters, 
                    // they will match anyway 
                    if (j != 0)
                        j = lps[j - 1];
                    else
                        i = i + 1;
                }
            }
        }

        private void computeLPSArray(string pat, int M, int[] lps)
        {
            // length of the previous longest prefix suffix 
            int len = 0;
            int i = 1;
            lps[0] = 0; // lps[0] is always 0 

            // the loop calculates lps[i] for i = 1 to M-1 
            while (i < M)
            {
                if (pat[i] == pat[len])
                {
                    len++;
                    lps[i] = len;
                    i++;
                }
                else // (pat[i] != pat[len]) 
                {
                    // This is tricky. Consider the example. 
                    // AAACAAAA and i = 7. The idea is similar 
                    // to search step. 
                    if (len != 0)
                    {
                        len = lps[len - 1];

                        // Also, note that we do not increment 
                        // i here 
                    }
                    else // if (len == 0) 
                    {
                        lps[i] = len;
                        i++;
                    }
                }
            }
        }
    }
}
