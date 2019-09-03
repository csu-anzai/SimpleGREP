using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace grepcmd
{
    /// <summary>
    /// 本类的意义为并行读取和处理压缩文件的内容
    /// </summary>
    class CachedArchiveIterator : IDisposable
    {
        public static readonly HashSet<string> ArchiveExts = new HashSet<string>() {
            ".7z",
            ".zip",
            ".rar",
            ".cab",
            ".iso",
            ".tar",
            ".lzh",
            ".lha",
            ".rpm",
            ".deb",
            ".arj",
            ".vhd",
            ".dmg",
            ".xar",
            ".squashfs",
            ".jar",
            ".cpio"
        };

        public static bool ContentIterable(string archivename) => ArchiveExts.Contains(Path.GetExtension(archivename).ToLower());
        public static List<(string name, long length)> EnumerateNames(string archivename) {
            List<(string name, long length)> rets = new List<(string name, long length)>();
            try
            {
                using (SevenZipExtractor.ArchiveFile arcfile = new SevenZipExtractor.ArchiveFile(archivename))
                    foreach (var i in arcfile.Entries)
                        rets.Add((i.FileName, (long)i.Size));
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            } //Not a valid compressed file
            return rets;
        }

        BufferBackStream ioStream = null;
        bool cancel = false;
        string cmdFile;
        AutoResetEvent commitCommand = new AutoResetEvent(false);
        AutoResetEvent waitResult = new AutoResetEvent(false);

        Thread sevenzipworker;
        string workerfile;
        void SevenzipWorkThread()
        {
            try
            {
                SevenZipExtractor.ArchiveFile archive = new SevenZipExtractor.ArchiveFile(workerfile);
                try
                {
                    Dictionary<string, SevenZipExtractor.Entry> entmap = archive.Entries.ToDictionary(i => i.FileName);
                    while (true)
                    {
                        commitCommand.WaitOne();
                        if (cancel) break;
                        var fname = cmdFile;
                        waitResult.Set();
                        entmap[fname].Extract(ioStream);
                        ioStream.FinishWrite();
                    }
                }
                catch { }
                archive.Dispose();
            }
            catch { }
            workerfile = null;
            sevenzipworker = null;
            waitResult.Set();
        }

        public Stream FileGetCommand(string file)
        {
            ioStream = new BufferBackStream();
            cmdFile = file;
            commitCommand.Set();
            waitResult.WaitOne();
            if (workerfile == null) return null; //Worker died
            return ioStream;
        }

        public void FreeCommand()
        {
            if (sevenzipworker != null)
            {
                cancel = true;
                commitCommand.Set();
                waitResult.WaitOne();
                cancel = false;
            }
        }
        
        public void CreateWorker(string archive)
        {
            if (workerfile != archive)
            {
                FreeCommand();
                workerfile = archive;
                sevenzipworker = new Thread(SevenzipWorkThread);
                sevenzipworker.Name = "7zworker";
                sevenzipworker.IsBackground = true;
                sevenzipworker.Start();
            }
        }

        public Stream GetArchiveFileStream(string archivename, string filename)
        {
            CreateWorker(archivename);
            return FileGetCommand(filename);
        }

        public void Dispose()
        {
            FreeCommand();
        }
    }

    /// <summary>
    /// One reader, one writer, in different threads
    /// </summary>
    class BufferStructure
    {
        int rdoffset = 0;
        public List<byte[]> readBufferChain = new List<byte[]>();
        public volatile List<byte[]> bufferChain = new List<byte[]>();
        bool freezed = false;
        bool bypassfeed = false;
        public AutoResetEvent dataArrived = new AutoResetEvent(false);

        public int RetrieveBuffer(byte[] buffer, int offset, int count)
        {
            int readed = 0;

        consumeLocalBuffers:
            while (readBufferChain.Count > 0)
            {
                byte[] lastr = readBufferChain[readBufferChain.Count - 1];
                int avail = lastr.Length - rdoffset;
                //本切片数据足够满足需要
                if (avail >= count)
                {
                    Array.Copy(lastr, rdoffset, buffer, offset, count);
                    rdoffset += count;
                    if (rdoffset == lastr.Length)
                    {
                        rdoffset = 0;
                        readBufferChain.RemoveAt(readBufferChain.Count - 1);
                    }
                    return readed + count;
                }
                //本切片数据不够，先消费调切片
                Array.Copy(lastr, rdoffset, buffer, offset, avail);
                readed += avail;
                rdoffset = 0;
                offset += avail;
                count -= avail;
                readBufferChain.RemoveAt(readBufferChain.Count - 1);
            }
            //所有切片均被消费完毕
            while (true)
            {
                lock (bufferChain)
                {
                    //远端有新切片喂入
                    if (bufferChain.Count > 0)
                    {
                        for (var i = bufferChain.Count - 1; i >= 0; i--)
                            readBufferChain.Add(bufferChain[i]);
                        bufferChain.Clear();
                        goto consumeLocalBuffers;
                    }
                    if (freezed)
                        return readed; //没有更多数据了
                }
                dataArrived.WaitOne(); //等待更多数据
            }
        }

        public void StopFeed()
        {
            bypassfeed = true;
            readBufferChain.Clear();
            lock (bufferChain)
                bufferChain.Clear();
        }

        public void AddBuffer(byte[] buffer, int start, int size)
        {
            if (bypassfeed) return;
            byte[] bufcpy = new byte[size];
            Array.Copy(buffer, start, bufcpy, 0, size);
            lock (bufferChain)
                bufferChain.Add(bufcpy);
            dataArrived.Set();
        }

        public void Freeze()
        {
            freezed = true;
            dataArrived.Set();
        }
    }

    class BufferBackStream : Stream
    {
        long lenrec = -1;
        long posrec = 0;

        public BufferStructure buffer = new BufferStructure();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => lenrec;

        public override long Position { get => posrec; set => throw new NotImplementedException(); }

        public override void Close()
        {
            base.Close();
            buffer.StopFeed();
        }

        public override void Flush()
        {}

        public override int Read(byte[] buffer, int offset, int count)
        {
            var rdlen = this.buffer.RetrieveBuffer(buffer, offset, count);
            posrec += rdlen;
            return rdlen;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            lenrec = value;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.buffer.AddBuffer(buffer, offset, count);
        }

        public void FinishWrite()
        {
            this.buffer.Freeze();
        }
    }
}
