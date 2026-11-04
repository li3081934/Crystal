using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Client
{
    /// <summary>
    /// 客户端资源按需下载器（支持 HTTP / SOCKS5 代理 + 单文件多 Range 分段并行）
    ///
    /// 工作方式：
    ///   1. 绘制时 MLibrary 发现本地缺文件 → MirDownload.Request(localPath)
    ///   2. 后台线程拉 PList.gz 清单（167KB，一次），据此判断文件是否存在、原始大小
    ///   3. 优先级队列 + N 个并发文件 worker；大文件再按 SegmentSize 分段，每段一个 Range 请求并行下载
    ///   4. 写入 .part 的同时维护“连续可用前缀”，MLibrary 据此边下边读
    ///   5. 完成 → Finished 队列 → 渲染线程 Libraries.ProcessDownloads() 触发 MLibrary.Reload()
    ///
    /// 所有配置见 Mir2Config.ini 的 [Download] 段。
    /// </summary>
    internal static class MirDownload
    {
        private const string ManifestName = "PList.gz";
        private const char BS_CHAR = (char)92;   // 反斜杠
        private static readonly char sep = Path.DirectorySeparatorChar;
        private const int SegmentSize = 4 * 1024 * 1024;     // 4 MB 一段

        // ---------- 对外状态 ----------
        public static volatile bool ManifestReady;
        public static int PendingCount;
        public static int DoneCount;
        public static int FailCount;
        public static int SkipCount;
        public static long BytesDone;
        public static long SpeedBytes;          // 字节/秒（进度循环算）
        public static string LastError = string.Empty;

        public static readonly ConcurrentQueue<string> Finished = new ConcurrentQueue<string>();

        /// <summary>是否还有活干（用于界面显示/隐藏进度条）</summary>
        public static bool IsActive => Enabled && (_started && (PendingCount > 0 || Volatile.Read(ref _active) > 0));

        // ---------- 内部 ----------
        private struct QueueItem
        {
            public int Priority;
            public long Seq;
            public string Rel;
        }

        private static readonly object QueueLock = new object();
        // 队列状态：既避免重复入队，又支持“失败后冷却重试 / 半成品文件自动续传”
        private static readonly HashSet<string> Queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // 排队中或正在下载
        private static readonly HashSet<string> Done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);     // 已完成（永不重复请求）
        private static readonly Dictionary<string, DateTime> NextRetry = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> Pending = new List<string>();
        private static readonly List<QueueItem> Queue = new List<QueueItem>();
        private static readonly Dictionary<string, int> Manifest = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // 清单里的"规范大小写"。HTTP 服务器是大小写敏感的，
        // 客户端代码里写的是 MMap / StateItem / DNItems / wood\ ...
        // 而清单里是 mmap / Stateitem / dnitems / Wood\ ...
        // 直接用代码里的拼写去请求会 404，所以一律用这里查出来的规范名。
        private static readonly Dictionary<string, string> CanonicalRel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 每个正在下载的文件“连续可安全读取的字节前缀” —— MLibrary 靠它判断某张图能不能读
        private static readonly ConcurrentDictionary<string, long> SafePrefix =
            new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        private static HttpClient _client;

        /// <summary>每个文件实际可用几段并行（由 MaxConnections / MaxConcurrent 反推，构造上封顶）</summary>
        private static int _segWorkersPerFile = 1;
        private static int _active;             // 正在处理的文件任务数
        private static long _seq;
        private static DateTime _nextLog = DateTime.UtcNow;
        private static long _lastSpeedBytes;
        private static DateTime _lastSpeedTime = DateTime.UtcNow;
        private static volatile bool _started;

        public static bool Enabled => Settings.DownloadEnabled;

        public static string Host
        {
            get
            {
                string h = Settings.DownloadHost ?? string.Empty;
                if (!h.EndsWith("/")) h += "/";
                return h;
            }
        }

        // ================= 供 MLibrary 查询的可用长度 =================

        /// <summary>
        /// 某本地文件的“可安全读取长度”。没在下这个文件时返回 -1（调用方退回用 FileStream.Length）。
        /// 分段并行下载时文件长度可能已经是满的（有空洞），所以不能只看 Length。
        /// </summary>
        public static long GetSafeLength(string localPath)
        {
            if (SafePrefix.Count == 0) return -1;

            string key;
            try { key = Path.GetFullPath(localPath); }
            catch { return -1; }

            long v;
            return SafePrefix.TryGetValue(key, out v) ? v : -1;
        }

        // ================= 入口 =================

        /// <summary>请求下载一个本地缺失的资源文件（传入 MLibrary 的本地路径，如 ".\Data\Prguse.Lib"）</summary>
        public static void Request(string localPath)
        {
            if (!Enabled || string.IsNullOrEmpty(localPath)) return;

            string rel = ToRelative(localPath);
            if (rel == null) return;

            lock (QueueLock)
            {
                if (Done.Contains(rel) || Queued.Contains(rel)) return;

                DateTime retryAt;
                if (NextRetry.TryGetValue(rel, out retryAt) && DateTime.UtcNow < retryAt) return;

                Queued.Add(rel);
                Pending.Add(rel);
                PendingCount = Pending.Count + Queue.Count;
            }

            Start();
        }

        /// <summary>
        /// 本地文件被判为损坏（已删除）：清掉"已完成"标记和重试冷却，允许重新下载。
        /// </summary>
        public static void InvalidateFile(string localPath)
        {
            string rel = ToRelative(localPath);
            if (rel == null) return;

            string full = null;
            try { full = Path.GetFullPath(localPath); } catch { }

            lock (QueueLock)
            {
                Done.Remove(rel);
                Queued.Remove(rel);
                NextRetry.Remove(rel);
            }

            if (full != null)
            {
                long dummy;
                SafePrefix.TryRemove(full, out dummy);
            }

            // 立刻重新排队
            Request(localPath);
        }

        private static string ToRelative(string localPath)
        {
            string p = localPath.Replace('\\', '/').Trim();
            if (p.StartsWith("./")) p = p.Substring(2);
            if (p.Length == 0) return null;
            if (p.Contains("://")) return null;
            return p;
        }

        private static void Start()
        {
            if (_started) return;
            lock (QueueLock)
            {
                if (_started) return;
                _started = true;
            }

            try
            {
                _client = BuildHttpClient();
            }
            catch (Exception ex)
            {
                LastError = "代理/HttpClient 初始化失败: " + ex.Message;
                CMain.SaveError(LastError);
                _started = false;
                return;
            }

            Thread t = new Thread(Dispatcher) { IsBackground = true, Name = "MirDownload" };
            t.Start();
        }

        private static HttpClient BuildHttpClient()
        {
            HttpClientHandler handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxConnectionsPerServer = Math.Max(2, Settings.DownloadMaxConnections),
                AutomaticDecompression = DecompressionMethods.None,   // .Lib 内部自带 gzip，别让 HTTP 层再解一次
            };

            string proxy = (Settings.DownloadProxy ?? string.Empty).Trim();
            if (proxy.Length > 0)
            {
                Uri uri;
                if (!proxy.Contains("://")) proxy = "http://" + proxy;
                if (!Uri.TryCreate(proxy, UriKind.Absolute, out uri))
                    throw new UriFormatException("Proxy 格式不对: " + Settings.DownloadProxy);

                WebProxy wp = new WebProxy(uri);

                string user = Settings.DownloadProxyUser ?? string.Empty;
                string pass = Settings.DownloadProxyPass ?? string.Empty;

                if (!string.IsNullOrEmpty(uri.UserInfo))
                {
                    string[] ui = uri.UserInfo.Split(':');
                    user = Uri.UnescapeDataString(ui[0]);
                    if (ui.Length > 1) pass = Uri.UnescapeDataString(ui[1]);
                }

                if (user.Length > 0)
                    wp.Credentials = new NetworkCredential(user, pass);

                wp.BypassProxyOnLocal = true;
                handler.Proxy = wp;
                handler.UseProxy = true;
            }
            else
            {
                handler.UseProxy = false;
            }

            HttpClient client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(180) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CrystalClient/1.0");
            return client;
        }

        // ================= 调度 =================

        private static void Dispatcher()
        {
            DateTime nextManifestTry = DateTime.MinValue;

            while (true)
            {
                try
                {
                    if (!ManifestReady)
                    {
                        if (DateTime.UtcNow >= nextManifestTry)
                        {
                            nextManifestTry = DateTime.UtcNow.AddSeconds(15);
                            try { LoadManifest(); }
                            catch (Exception ex) { LastError = "下载清单获取失败(稍后重试): " + ex.Message; }
                        }
                    }
                    else
                    {
                        DrainPending();

                        int maxFiles = Math.Max(1, Settings.DownloadConcurrent);
                        int maxConn = Math.Max(1, Settings.DownloadMaxConnections);

                        // 连接总数封顶：文件数 × 每文件段数 ≤ MaxConnections
                        if (maxFiles > maxConn) maxFiles = maxConn;
                        _segWorkersPerFile = Math.Max(1, Math.Min(Settings.DownloadSegmentWorkers, maxConn / maxFiles));

                        while (Volatile.Read(ref _active) < maxFiles)
                        {
                            string rel = TakeNext();
                            if (rel == null) break;

                            Interlocked.Increment(ref _active);
                            string captured = rel;
                            Task.Run(() =>
                            {
                                try { Fetch(captured); }
                                catch (Exception ex) { RecordFail(captured, ex.Message); }
                                finally { Interlocked.Decrement(ref _active); }
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }

                if (DateTime.UtcNow >= _nextLog)
                {
                    _nextLog = DateTime.UtcNow.AddSeconds(5);
                    UpdateSpeed();
                    WriteProgressLog();
                }

                Thread.Sleep(120);
            }
        }

        private static void UpdateSpeed()
        {
            long now = BytesDone;
            DateTime t = DateTime.UtcNow;
            double secs = (t - _lastSpeedTime).TotalSeconds;
            if (secs <= 0) return;

            SpeedBytes = (long)((now - _lastSpeedBytes) / secs);
            _lastSpeedBytes = now;
            _lastSpeedTime = t;
        }

        /// <summary>给界面用的进度文字</summary>
        public static string ProgressText()
        {
            if (!Enabled) return string.Empty;

            if (!ManifestReady)
                return "获取资源清单…";

            int need = DoneCount + FailCount + PendingCount + Math.Max(0, Volatile.Read(ref _active));
            if (need <= 0) return string.Empty;

            return string.Format("资源下载 {0}/{1}   {2:F1} MB/s",
                                 DoneCount,
                                 need,
                                 SpeedBytes / 1048576.0);
        }

        private static void LoadManifest()
        {
            byte[] raw = _client.GetByteArrayAsync(Host + ManifestName).GetAwaiter().GetResult();

            using (MemoryStream ms = new MemoryStream(raw))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    string name = reader.ReadString().Replace('\\', '/');
                    int length = reader.ReadInt32();
                    reader.ReadInt32();      // Compressed
                    reader.ReadInt64();      // Creation

                    if (!Manifest.ContainsKey(name))
                        Manifest[name] = length;

                    CanonicalRel[name] = name;
                }
            }

            ManifestReady = true;
        }

        /// <summary>把客户端代码里的路径映射成清单里的规范大小写（HTTP 请求必须完全一致）</summary>
        private static string Canonicalize(string rel)
        {
            string canon;
            if (CanonicalRel.TryGetValue(rel, out canon))
                return canon;

            return rel;
        }

        private static void DrainPending()
        {
            if (!ManifestReady) return;      // 没清单就不排队，否则会把大量不存在的编号塞进去

            List<string> batch;
            lock (QueueLock)
            {
                if (Pending.Count == 0) return;
                batch = new List<string>(Pending);
                Pending.Clear();
            }

            List<QueueItem> add = new List<QueueItem>();

            foreach (string rel in batch)
            {
                int size;
                if (!Manifest.TryGetValue(rel, out size))
                {
                    SkipCount++;             // 服务器上就没有这个文件，放弃
                    continue;
                }

                // 本地已经有完整文件了，跳过
                try
                {
                    string local = Path.GetFullPath(Path.Combine(".", rel.Replace(BS_CHAR, sep)));
                    if (File.Exists(local) && new FileInfo(local).Length == size)
                    {
                        SkipCount++;
                        continue;
                    }
                }
                catch
                {
                    // 忽略
                }

                add.Add(new QueueItem { Priority = PriorityOf(rel), Seq = ++_seq, Rel = Canonicalize(rel) });
            }

            if (add.Count == 0)
            {
                lock (QueueLock) PendingCount = 0;
                return;
            }

            lock (QueueLock)
            {
                Queue.AddRange(add);
                Queue.Sort((a, b) => a.Priority != b.Priority
                    ? a.Priority.CompareTo(b.Priority)
                    : a.Seq.CompareTo(b.Seq));
                PendingCount = Queue.Count;
            }
        }

        private static string TakeNext()
        {
            lock (QueueLock)
            {
                if (Queue.Count == 0)
                {
                    PendingCount = 0;
                    return null;
                }
                string rel = Queue[0].Rel;
                Queue.RemoveAt(0);
                PendingCount = Queue.Count;
                return rel;
            }
        }

        /// <summary>下载优先级（数字越小越先）</summary>
        private static int PriorityOf(string rel)
        {
            if (rel.StartsWith("Data/", StringComparison.OrdinalIgnoreCase) && rel.IndexOf('/', 5) < 0)
                return 0;                                        // Data/ 根目录单体库

            if (rel.StartsWith("Data/Map/", StringComparison.OrdinalIgnoreCase)) return 5;
            if (rel.StartsWith("Data/Cursors/", StringComparison.OrdinalIgnoreCase)) return 5;
            if (rel.StartsWith("DirectX/", StringComparison.OrdinalIgnoreCase)) return 15;
            if (rel.StartsWith("Data/Monster/", StringComparison.OrdinalIgnoreCase)) return 60;
            if (rel.StartsWith("Sound/", StringComparison.OrdinalIgnoreCase)) return 95;

            return 40;
        }

        // ================= 单文件下载 =================

        private static void Fetch(string rel)
        {
            int size = 0;
            if (ManifestReady && Manifest.TryGetValue(rel, out int s)) size = s;

            string local = Path.GetFullPath(Path.Combine(".", rel.Replace(BS_CHAR, sep)));
            string part = local + ".part";

            try
            {
                string dir = Path.GetDirectoryName(local);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { }

            if (File.Exists(local))
            {
                long existing = new FileInfo(local).Length;
                if (size <= 0 || existing == size)
                {
                    Interlocked.Increment(ref DoneCount);
                    SafePrefix[local] = existing;
                    Finished.Enqueue(local);
                    return;
                }
            }

            // 大文件走分段并行；小文件单连接就够
            if (size >= SegmentSize * 2 && _segWorkersPerFile > 1)
            {
                if (FetchSegmented(rel, size, local, part))
                    return;
                // 分段失败（比如服务器不支持 Range）→ 掉回单连接
            }

            FetchWhole(rel, size, local, part);
        }

        // ---------- 单连接（顺序写，前缀 = 文件长度） ----------

        private static void FetchWhole(string rel, int size, string local, string part)
        {
            string url = Host + UrlPath(rel);
            int retry = Math.Max(1, Settings.DownloadRetry);
            string lastErr = string.Empty;

            for (int attempt = 1; attempt <= retry; attempt++)
            {
                try
                {
                    long have = File.Exists(part) ? new FileInfo(part).Length : 0;
                    if (size > 0 && have > size) { File.Delete(part); have = 0; }
                    if (size > 0 && have == size)
                    {
                        Complete(part, local, size);
                        return;
                    }

                    SafePrefix[local] = have;

                    using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        if (have > 0)
                            req.Headers.Range = new RangeHeaderValue(have, null);

                        using (HttpResponseMessage resp = _client.Send(req, HttpCompletionOption.ResponseHeadersRead))
                        {
                            if (resp.StatusCode == HttpStatusCode.NotFound || resp.StatusCode == HttpStatusCode.Forbidden)
                            {
                                SkipCount++;
                                if (File.Exists(part)) File.Delete(part);

                                // 清单里有、服务器却 404 → 多半是大小写/路径问题，留个痕迹
                                LastError = "HTTP " + (int)resp.StatusCode + " " + rel;
                                WriteNotFoundLog(rel, (int)resp.StatusCode);
                                return;
                            }

                            resp.EnsureSuccessStatusCode();

                            bool append = have > 0 && resp.StatusCode == HttpStatusCode.PartialContent;

                            using (Stream src = resp.Content.ReadAsStream())
                            using (FileStream fs = new FileStream(part, append ? FileMode.Append : FileMode.Create,
                                                                  FileAccess.Write, FileShare.ReadWrite | FileShare.Delete,
                                                                  64 * 1024, false))
                            {
                                byte[] buf = new byte[64 * 1024];
                                int n;
                                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                                {
                                    fs.Write(buf, 0, n);
                                    Interlocked.Add(ref BytesDone, n);
                                    SafePrefix[local] = fs.Position;
                                }
                            }
                        }
                    }

                    long got = new FileInfo(part).Length;
                    if (size > 0 && got != size)
                    {
                        lastErr = string.Format("大小不符 got={0} expect={1}", got, size);
                        if (got > size) File.Delete(part);
                        Thread.Sleep(Math.Min(2000, 300 * attempt));
                        continue;
                    }

                    Complete(part, local, size > 0 ? size : (int)got);
                    return;
                }
                catch (Exception ex)
                {
                    lastErr = ex.Message;
                    Thread.Sleep(Math.Min(3000, 400 * attempt));
                }
            }

            RecordFail(rel, lastErr);
        }

        // ---------- 分段并行（每段一个 Range 请求，维护“连续可用前缀”） ----------

        private static bool FetchSegmented(string rel, int size, string local, string part)
        {
            string meta = part + ".meta";
            int segCount = (size + SegmentSize - 1) / SegmentSize;
            bool[] done = new bool[segCount];
            object sync = new object();

            // ---- 恢复进度 ----
            long existingLen = File.Exists(part) ? new FileInfo(part).Length : 0;

            if (File.Exists(meta))
            {
                try
                {
                    string[] lines = File.ReadAllLines(meta);
                    long recordedLen = 0;
                    if (lines.Length >= 4) long.TryParse(lines[3].Trim(), out recordedLen);

                    // 一致性：.part 比上次记录时还短 → 被截断过，元数据不可信，重下
                    if (recordedLen > existingLen)
                    {
                        if (File.Exists(meta)) File.Delete(meta);
                        done = new bool[segCount];
                    }
                    else if (lines.Length >= 3 && lines[0].Trim() == size.ToString())
                    {
                        foreach (string idx in lines[2].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            int v;
                            if (int.TryParse(idx, out v) && v >= 0 && v < segCount)
                                done[v] = true;
                        }
                    }
                }
                catch { }
            }
            else if (existingLen > 0)
            {
                // 没有 meta 但有 .part：老版本是顺序写的，前 floor(len/SEG) 段视为已完成
                int covered = (int)Math.Min(segCount, existingLen / SegmentSize);
                for (int i = 0; i < covered; i++) done[i] = true;
                SaveMeta(meta, size, done);
            }

            if (existingLen > size) { File.Delete(part); }
            if (!File.Exists(part))
            {
                using (FileStream fs = File.Create(part)) { }
            }

            long prefix = 0;
            {
                int p = 0;
                while (p < segCount && done[p]) p++;
                prefix = (long)p * SegmentSize;
                if (p >= segCount) prefix = size;
            }
            SafePrefix[local] = Math.Min(prefix, existingLen);

            string url = Host + UrlPath(rel);
            int fileWorkers = Math.Min(Math.Max(1, _segWorkersPerFile), segCount);
            int[] noRange = new int[1];
            int[] incomplete = new int[1];

            Parallel.For(0, segCount, new ParallelOptions { MaxDegreeOfParallelism = fileWorkers }, s =>
            {
                if (done[s]) return;

                long start = (long)s * SegmentSize;
                long end = Math.Min((long)size, start + SegmentSize) - 1;

                int r = DownloadSegment(url, part, start, end);

                if (r == -1)
                {
                    Interlocked.Increment(ref noRange[0]);
                    return;
                }

                if (r == 0)
                {
                    // 只是这次没下完：保留已完成的段，交给 30 秒后的冷却重试，
                    // 不要退回单连接整份重下（那会把下好的几十 MB 全扔掉）
                    Interlocked.Increment(ref incomplete[0]);
                    return;
                }

                lock (sync)
                {
                    done[s] = true;

                    long newPrefix = 0;
                    int p = 0;
                    while (p < segCount && done[p]) p++;
                    newPrefix = p >= segCount ? size : (long)p * SegmentSize;

                    if (newPrefix > SafePrefix[local])
                        SafePrefix[local] = newPrefix;

                    SaveMeta(meta, size, done);
                }
            });

            if (noRange[0] > 0)
                return false;           // 服务器不支持 Range → 让调用方退回单连接

            if (incomplete[0] > 0)
                return true;            // 分段机制可用，只是这次没下完，等着续传

            long final = new FileInfo(part).Length;
            if (final != size)
                return false;

            try { File.Delete(meta); } catch { }
            Complete(part, local, size);
            return true;
        }

        /// <returns>1 = 成功；-1 = 服务器不支持 Range（需要整体重下）；0 = 重试耗尽（保留已完成的段，稍后再续）</returns>
        private static int DownloadSegment(string url, string part, long start, long end)
        {
            int retry = Math.Max(1, Settings.DownloadRetry);
            long want = end - start + 1;

            for (int attempt = 1; attempt <= retry; attempt++)
            {
                try
                {
                    using (HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        req.Headers.Range = new RangeHeaderValue(start, end);

                        using (HttpResponseMessage resp = _client.Send(req, HttpCompletionOption.ResponseHeadersRead))
                        {
                            if (resp.StatusCode != HttpStatusCode.PartialContent)
                                return -1;          // 服务器不支持 Range，交给单连接

                            using (Stream src = resp.Content.ReadAsStream())
                            using (FileStream fs = new FileStream(part, FileMode.OpenOrCreate, FileAccess.Write,
                                                                  FileShare.ReadWrite | FileShare.Delete, 64 * 1024, false))
                            {
                                fs.Seek(start, SeekOrigin.Begin);

                                byte[] buf = new byte[64 * 1024];
                                long written = 0;
                                while (written < want)
                                {
                                    int n = src.Read(buf, 0, (int)Math.Min(buf.Length, want - written));
                                    if (n <= 0) break;
                                    fs.Write(buf, 0, n);
                                    written += n;
                                    Interlocked.Add(ref BytesDone, n);
                                }

                                if (written != want) return 0;
                            }
                        }
                    }

                    return 1;
                }
                catch
                {
                    Thread.Sleep(Math.Min(3000, 300 * attempt));
                }
            }

            return 0;
        }

        private static void SaveMeta(string meta, int size, bool[] done)
        {
            try
            {
                List<string> ids = new List<string>();
                for (int i = 0; i < done.Length; i++)
                    if (done[i]) ids.Add(i.ToString());

                long len = 0;
                try
                {
                    string part = meta.Substring(0, meta.Length - ".meta".Length);
                    if (File.Exists(part)) len = new FileInfo(part).Length;
                }
                catch { }

                // 第 4 行记录当时的 .part 长度，用来识别"文件被截断但元数据还在"
                File.WriteAllText(meta, size + Environment.NewLine +
                                        SegmentSize + Environment.NewLine +
                                        string.Join(",", ids) + Environment.NewLine +
                                        len);
            }
            catch { }
        }

        private static void Complete(string part, string local, int size)
        {
            try
            {
                File.Move(part, local, true);
            }
            catch
            {
                // 句柄被占用时退一步：直接拷贝内容
                try { File.Copy(part, local, true); File.Delete(part); }
                catch { }
            }

            SafePrefix[local] = size;
            Interlocked.Increment(ref DoneCount);
            Finished.Enqueue(local);
        }

        /// <summary>
        /// 一个文件处理结束后更新队列状态：
        ///  - 磁盘上已经是完整文件 → 记入 Done，永不再请求
        ///  - 否则（失败/中断）→ 冷却 30 秒后才允许重新入队，
        ///    这样重启后遗留的 .part 会被自动续传，也不会因为每帧 Request 而疯狂重试
        /// </summary>
        private static void FinishBookkeeping(string rel)
        {
            bool complete = false;

            try
            {
                string local = Path.GetFullPath(Path.Combine(".", rel.Replace(BS_CHAR, sep)));
                if (File.Exists(local))
                {
                    int size;
                    complete = !Manifest.TryGetValue(rel, out size) || new FileInfo(local).Length == size;
                }
            }
            catch
            {
                // 忽略
            }

            lock (QueueLock)
            {
                Queued.Remove(rel);

                if (complete)
                {
                    Done.Add(rel);
                    NextRetry.Remove(rel);
                }
                else
                {
                    NextRetry[rel] = DateTime.UtcNow.AddSeconds(30);
                }
            }
        }

        private static void WriteNotFoundLog(string rel, int code)
        {
            try
            {
                string dir = Path.Combine(".", "Logs");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.AppendAllText(Path.Combine(dir, "Download.txt"),
                    string.Format("[{0:HH:mm:ss}] !! HTTP {1}  {2}{3}", DateTime.Now, code, rel, Environment.NewLine));
            }
            catch { }
        }

        private static void RecordFail(string rel, string err)
        {
            FailCount++;
            LastError = rel + " : " + err;
            CMain.SaveError("Download failed " + rel + " -> " + err);
        }

        private static void WriteProgressLog()
        {
            try
            {
                string dir = ".\\Logs";
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.AppendAllText(Path.Combine(dir, "Download.txt"),
                    string.Format("[{0:HH:mm:ss}] 队列 {1} | 完成 {2} | 失败 {3} | 跳过 {4} | {5:F1} MB/s | 累计 {6:F1} MB{7}{8}",
                        DateTime.Now, PendingCount, DoneCount, FailCount, SkipCount,
                        SpeedBytes / 1048576.0, BytesDone / 1048576.0,
                        LastError.Length > 0 ? " | " + LastError : string.Empty,
                        Environment.NewLine));
            }
            catch { }
        }

        /// <summary>逐段 URL 编码（文件名里可能有空格，如 "00 L.Lib"）</summary>
        private static string UrlPath(string rel)
        {
            string[] parts = rel.Split('/');
            for (int i = 0; i < parts.Length; i++)
                parts[i] = Uri.EscapeDataString(parts[i]);
            return string.Join("/", parts);
        }

        // ================= 手动全量下载 =================

        /// <summary>把清单里所有文件都排队（用于"一键下全量资源"）</summary>
        public static void RequestEverything()
        {
            Start();

            ThreadPool.QueueUserWorkItem(o =>
            {
                DateTime until = DateTime.UtcNow.AddSeconds(60);
                while (!ManifestReady && DateTime.UtcNow < until) Thread.Sleep(200);
                if (!ManifestReady) return;

                List<string> all;
                lock (QueueLock) { all = new List<string>(Manifest.Keys); }

                foreach (string rel in all)
                    Request("./" + rel);
            });
        }
    }
}
