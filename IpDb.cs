using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace IpInfo
{
    // ==================================================================== 결과 모델

    public sealed class CustomHit
    {
        public string Network;
        public string Label;
        public string Country;
    }

    public sealed class RirHit
    {
        public string Registry;
        public string Country;
        public string Date;
        public string Range;
    }

    public sealed class AsnHit
    {
        public uint Asn;
        public string Country;
        public string Name;
        public string Range;
    }

    public sealed class LookupResult
    {
        public string Input;
        public string Ip;
        public int Version;
        public string Type;
        public string Country;
        public string CountrySource;
        public string GeoCountry;   // 실제 위치 국가 (DB-IP)
        public string RegCountry;   // 등록 국가 (RIR)
        public CustomHit Custom;
        public RirHit Rir;
        public AsnHit Asn;

        public string CountryName => Country == "--" ? "해당 없음" : Countries.NameOf(Country);

        /// <summary>위치 국가와 등록 국가가 다를 때 등록 국가 (클라우드·해외 리전 신호)</summary>
        public string RegDiff =>
            !string.IsNullOrEmpty(RegCountry) && !string.IsNullOrEmpty(GeoCountry) && RegCountry != Country ? RegCountry : "";

        public string OrgText
        {
            get
            {
                var parts = new List<string>();
                if (Custom != null && !string.IsNullOrEmpty(Custom.Label)) parts.Add(Custom.Label);
                if (Asn != null) parts.Add($"AS{Asn.Asn} {Asn.Name}");
                return string.Join(" / ", parts);
            }
        }
    }

    // ==================================================================== 범위 테이블

    internal sealed class RangeTable<T>
    {
        private readonly UInt128[] _s, _e;
        private readonly T[] _v;

        public int Count => _s.Length;

        public RangeTable(List<(UInt128 s, UInt128 e, T v)> rows)
        {
            rows.Sort((a, b) =>
            {
                int c = a.s.CompareTo(b.s);
                return c != 0 ? c : b.e.CompareTo(a.e);
            });
            _s = new UInt128[rows.Count];
            _e = new UInt128[rows.Count];
            _v = new T[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                _s[i] = rows[i].s;
                _e[i] = rows[i].e;
                _v[i] = rows[i].v;
            }
        }

        public bool Find(UInt128 n, out UInt128 start, out UInt128 end, out T value)
        {
            int lo = 0, hi = _s.Length; // 시작값이 n 보다 큰 첫 위치
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_s[mid] <= n) lo = mid + 1; else hi = mid;
            }
            int i = lo - 1;
            // 큰 범위 안에 작은 범위가 겹친 드문 경우를 위해 몇 칸 뒤로 확인
            for (int j = i; j >= 0 && j > i - 8; j--)
            {
                if (_s[j] <= n && n <= _e[j])
                {
                    start = _s[j]; end = _e[j]; value = _v[j];
                    return true;
                }
            }
            start = end = 0;
            value = default;
            return false;
        }
    }

    internal sealed class CustomNet
    {
        public int Version;
        public UInt128 Start, End;
        public int Prefix;
        public string Network, Label, Country;
    }

    // ==================================================================== 데이터베이스

    public sealed class DataFileInfo
    {
        public string Name { get; set; }
        public string Date { get; set; }
    }

    public sealed class IpDb
    {
        private readonly RangeTable<(string reg, string cc, string date)>[] _rir = new RangeTable<(string, string, string)>[2];
        private readonly RangeTable<(uint asn, string cc, string name)>[] _asn = new RangeTable<(uint, string, string)>[2];
        private readonly RangeTable<string>[] _geo = new RangeTable<string>[2];
        private List<CustomNet> _custom = new List<CustomNet>();

        public int[] GeoCount { get; } = new int[2];
        public bool HasGeo => GeoCount[0] + GeoCount[1] > 0;

        public int[] RirCount { get; } = new int[2];
        public int[] AsnCount { get; } = new int[2];
        public int CustomCount => _custom.Count;
        public List<string> Files { get; } = new List<string>();

        public const string GeoFileName = "dbip-country-lite.csv.gz";
        public const string GeoAttribution = "IP Geolocation by DB-IP (db-ip.com, CC BY 4.0)";

        public static readonly (string name, string url)[] Downloads =
        {
            (GeoFileName, "https://download.db-ip.com/free/dbip-country-lite-{0:yyyy-MM}.csv.gz"),
            ("ip2asn-combined.tsv.gz", "https://iptoasn.com/data/ip2asn-combined.tsv.gz"),
            ("delegated-apnic-extended-latest", "https://ftp.apnic.net/stats/apnic/delegated-apnic-extended-latest"),
            ("delegated-arin-extended-latest", "https://ftp.arin.net/pub/stats/arin/delegated-arin-extended-latest"),
            ("delegated-ripencc-extended-latest", "https://ftp.ripe.net/pub/stats/ripencc/delegated-ripencc-extended-latest"),
            ("delegated-lacnic-extended-latest", "https://ftp.lacnic.net/pub/stats/lacnic/delegated-lacnic-extended-latest"),
            ("delegated-afrinic-extended-latest", "https://ftp.afrinic.net/pub/stats/afrinic/delegated-afrinic-extended-latest"),
        };

        // ---------------------------------------------------------------- 데이터 폴더 찾기

        public static (List<string> rir, List<string> asn) FindSources(string dir)
        {
            var rir = new List<string>();
            var asn = new List<string>();
            if (!Directory.Exists(dir)) return (rir, asn);
            bool Skip(string p) => p.EndsWith(".md5") || p.EndsWith(".asc") || p.EndsWith(".tmp");
            rir.AddRange(Directory.GetFiles(dir, "delegated-*").Where(p => !Skip(p)).OrderBy(p => p));
            asn.AddRange(Directory.GetFiles(dir, "ip2asn*.tsv*").Where(p => !Skip(p)).OrderBy(p => p));
            return (rir, asn);
        }

        public static List<string> FindGeo(string dir)
        {
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir, "dbip-country*.csv*")
                .Where(p => !p.EndsWith(".tmp")).OrderBy(p => p).Take(1).ToList();
        }

        public static int SourceCount(string dir)
        {
            var (r, a) = FindSources(dir);
            return r.Count + a.Count + FindGeo(dir).Count;
        }

        /// <summary>exe 옆 data 폴더. 개발 중(dotnet run)에는 상위 폴더도 찾아봄.</summary>
        public static string FindDataDir()
        {
            string baseDir = AppContext.BaseDirectory;
            var d = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && d != null; i++, d = d.Parent)
            {
                string p = Path.Combine(d.FullName, "data");
                if (SourceCount(p) > 0) return p;
            }
            return Path.Combine(baseDir, "data");
        }

        // ---------------------------------------------------------------- 로딩

        private static StreamReader OpenText(string path)
        {
            Stream s = File.OpenRead(path);
            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                s = new GZipStream(s, CompressionMode.Decompress);
            return new StreamReader(s, new UTF8Encoding(false), false, 1 << 16);
        }

        private static bool TryParseAny(string s, out int version, out UInt128 n)
        {
            if (s.IndexOf(':') < 0)
            {
                version = 4;
                bool ok = IpUtil.TryParseV4(s, out uint v);
                n = (UInt128)v;
                return ok;
            }
            version = 6;
            n = 0;
            if (!IPAddress.TryParse(s, out var a)) return false;
            if (a.IsIPv4MappedToIPv6)
            {
                version = 4;
                a = a.MapToIPv4();
            }
            n = IpUtil.ToNumber(a);
            return true;
        }

        private void LoadRir(string path, List<(UInt128, UInt128, (string, string, string))>[] rows)
        {
            using var r = OpenText(path);
            string line;
            while ((line = r.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] p = line.Split('|');
                if (p.Length < 7) continue;
                string reg = p[0].ToUpperInvariant(), cc = p[1].ToUpperInvariant(), typ = p[2], start = p[3], value = p[4], date = p[5];
                if ((typ != "ipv4" && typ != "ipv6") || cc.Length == 0 || cc == "*" || cc == "ZZ" || start == "*") continue;
                if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong val) || val == 0) continue;

                if (typ == "ipv4")
                {
                    if (!IpUtil.TryParseV4(start, out uint s)) continue;
                    ulong end = s + val - 1;
                    if (end > uint.MaxValue) continue;
                    rows[0].Add(((UInt128)s, (UInt128)end, (reg, cc, date)));
                    RirCount[0]++;
                }
                else
                {
                    if (val > 128 || !IPAddress.TryParse(start, out var a)) continue;
                    UInt128 s = IpUtil.ToNumber(a);
                    rows[1].Add((s, s | IpUtil.HostMask((int)val, 128), (reg, cc, date)));
                    RirCount[1]++;
                }
            }
        }

        private void LoadAsn(string path, List<(UInt128, UInt128, (uint, string, string))>[] rows)
        {
            var intern = new Dictionary<string, string>();
            using var r = OpenText(path);
            string line;
            while ((line = r.ReadLine()) != null)
            {
                string[] p = line.Split('\t');
                if (p.Length < 5) continue;
                if (!uint.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint asn) || asn == 0) continue; // 0 = Not routed
                if (!TryParseAny(p[0], out int v1, out UInt128 s) || !TryParseAny(p[1], out int v2, out UInt128 e) || v1 != v2) continue;
                string cc = p[3].Trim().ToUpperInvariant();
                if (cc == "NONE" || cc == "ZZ") cc = "";
                string name = p[4].Trim();
                if (intern.TryGetValue(name, out var existing)) name = existing; else intern[name] = name;
                rows[v1 == 4 ? 0 : 1].Add((s, e, (asn, cc, name)));
                AsnCount[v1 == 4 ? 0 : 1]++;
            }
        }

        /// <summary>DB-IP 국가 CSV: 시작IP,끝IP,국가코드 (확장형이면 국가코드 열을 찾아 씀)</summary>
        private void LoadGeo(string path, List<(UInt128, UInt128, string)>[] rows)
        {
            var intern = new Dictionary<string, string>();
            using var r = OpenText(path);
            string line;
            while ((line = r.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                string[] p = line.Split(',');
                if (p.Length < 3) continue;
                for (int i = 0; i < p.Length; i++) p[i] = p[i].Trim().Trim('"');
                string cc = p.Length == 3 ? p[2] : (p.Length > 4 && IsCc(p[4]) ? p[4] : p[p.Length - 1]);
                cc = cc.ToUpperInvariant();
                if (!IsCc(cc) || cc == "ZZ") continue;
                if (!TryParseAny(p[0], out int v1, out UInt128 s) || !TryParseAny(p[1], out int v2, out UInt128 e) || v1 != v2) continue;
                if (intern.TryGetValue(cc, out var x)) cc = x; else intern[cc] = cc;
                rows[v1 == 4 ? 0 : 1].Add((s, e, cc));
                GeoCount[v1 == 4 ? 0 : 1]++;
            }
        }

        private static bool IsCc(string s) => s.Length == 2 && char.IsLetter(s[0]) && char.IsLetter(s[1]);

        public static IpDb Load(string dataDir)
        {
            var db = new IpDb();
            var (rirFiles, asnFiles) = FindSources(dataDir);

            var rr = new[] { new List<(UInt128, UInt128, (string, string, string))>(), new List<(UInt128, UInt128, (string, string, string))>() };
            var ar = new[] { new List<(UInt128, UInt128, (uint, string, string))>(), new List<(UInt128, UInt128, (uint, string, string))>() };
            foreach (var p in rirFiles)
            {
                try { db.LoadRir(p, rr); }
                catch (Exception ex) { throw new IOException($"{Path.GetFileName(p)}: {ex.Message}", ex); }
                db.Files.Add(Path.GetFileName(p));
            }
            foreach (var p in asnFiles)
            {
                try { db.LoadAsn(p, ar); }
                catch (Exception ex) { throw new IOException($"{Path.GetFileName(p)}: {ex.Message}", ex); }
                db.Files.Add(Path.GetFileName(p));
            }
            var gr = new[] { new List<(UInt128, UInt128, string)>(), new List<(UInt128, UInt128, string)>() };
            foreach (var p in FindGeo(dataDir))
            {
                try { db.LoadGeo(p, gr); }
                catch (Exception ex) { throw new IOException($"{Path.GetFileName(p)}: {ex.Message}", ex); }
                db.Files.Add(Path.GetFileName(p));
            }
            for (int i = 0; i < 2; i++)
            {
                db._geo[i] = new RangeTable<string>(gr[i]);
                db._rir[i] = new RangeTable<(string, string, string)>(rr[i]);
                db._asn[i] = new RangeTable<(uint, string, string)>(ar[i]);
            }
            db._custom = LoadCustom(Path.Combine(dataDir, "custom.csv"));
            return db;
        }

        // ---------------------------------------------------------------- custom.csv

        /// <summary>UTF-8 이 아니면 CP949(엑셀 기본 CSV)로 읽음</summary>
        public static string ReadTextAuto(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            try
            {
                return new UTF8Encoding(false, true).GetString(b).TrimStart('﻿');
            }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(949).GetString(b);
            }
        }

        private static List<CustomNet> LoadCustom(string path)
        {
            var list = new List<CustomNet>();
            if (!File.Exists(path)) return list;
            foreach (string raw in ReadTextAuto(path).Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] f = line.Split(',').Select(x => x.Trim().Trim('"').Trim()).ToArray();
                string cidr = f[0];
                int prefix = -1;
                int slash = cidr.IndexOf('/');
                if (slash >= 0)
                {
                    if (!int.TryParse(cidr.Substring(slash + 1), out prefix)) continue;
                    cidr = cidr.Substring(0, slash);
                }
                if (!IpUtil.TryParse(cidr, out var ip)) continue;
                int version = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 4 : 6;
                int bits = version == 4 ? 32 : 128;
                if (prefix < 0) prefix = bits;
                if (prefix > bits) continue;
                UInt128 mask = IpUtil.HostMask(prefix, bits);
                UInt128 start = IpUtil.ToNumber(ip) & ~mask;
                list.Add(new CustomNet
                {
                    Version = version,
                    Start = start,
                    End = start | mask,
                    Prefix = prefix,
                    Network = $"{IpUtil.FromNumber(start, version)}/{prefix}",
                    Label = f.Length > 1 ? f[1] : "",
                    Country = f.Length > 2 ? f[2].ToUpperInvariant() : "",
                });
            }
            // 가장 구체적인(좁은) 대역 우선
            return list.OrderByDescending(c => c.Prefix).ToList();
        }

        // ---------------------------------------------------------------- 조회

        public LookupResult Lookup(string text)
        {
            var res = new LookupResult { Input = text };
            if (!IpUtil.TryParse(text, out var ip)) return null;

            int v = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 4 : 6;
            UInt128 n = IpUtil.ToNumber(ip);
            res.Ip = ip.ToString();
            res.Version = v;
            res.Type = IpUtil.SpecialType(ip);

            foreach (var c in _custom) // 가장 구체적인 대역부터
            {
                if (c.Version != v || n < c.Start || n > c.End) continue;
                if (res.Custom == null)
                    res.Custom = new CustomHit { Network = c.Network, Label = c.Label, Country = c.Country };
                else if (string.IsNullOrEmpty(res.Custom.Country) && !string.IsNullOrEmpty(c.Country))
                    res.Custom.Country = c.Country; // 상위 대역 국가코드 상속
                if (!string.IsNullOrEmpty(res.Custom.Country)) break;
            }

            int t = v == 4 ? 0 : 1;
            if (_rir[t] != null && _rir[t].Find(n, out var rs, out var re, out var rv))
                res.Rir = new RirHit
                {
                    Registry = rv.reg, Country = rv.cc, Date = rv.date,
                    Range = $"{IpUtil.FromNumber(rs, v)} - {IpUtil.FromNumber(re, v)}",
                };
            if (_asn[t] != null && _asn[t].Find(n, out var s2, out var e2, out var av))
                res.Asn = new AsnHit
                {
                    Asn = av.asn, Country = av.cc, Name = av.name,
                    Range = $"{IpUtil.FromNumber(s2, v)} - {IpUtil.FromNumber(e2, v)}",
                };

            if (_geo[t] != null && _geo[t].Find(n, out _, out _, out var gc)) res.GeoCountry = gc;
            res.RegCountry = res.Rir?.Country;

            // 국가코드 결정: 사용자정의 > 실제 위치(DB-IP) > RIR 등록 > ASN 등록국가
            if (res.Custom != null && !string.IsNullOrEmpty(res.Custom.Country))
            { res.Country = res.Custom.Country; res.CountrySource = "사용자정의"; }
            else if (res.GeoCountry != null)
            { res.Country = res.GeoCountry; res.CountrySource = "실제 위치 (DB-IP)"; }
            else if (res.Rir != null)
            { res.Country = res.Rir.Country; res.CountrySource = $"등록 국가 (RIR {res.Rir.Registry})"; }
            else if (res.Asn != null && !string.IsNullOrEmpty(res.Asn.Country))
            { res.Country = res.Asn.Country; res.CountrySource = "ASN 등록국가"; }
            else if (res.Type != IpUtil.PublicType)
            { res.Country = "--"; res.CountrySource = res.Type; }
            else
            { res.Country = "ZZ"; res.CountrySource = "DB에 없음"; }
            return res;
        }

        // ---------------------------------------------------------------- 데이터 기준일

        public static string FileDate(string path)
        {
            if (Path.GetFileName(path).StartsWith("delegated-"))
            {
                try
                {
                    using var r = new StreamReader(path);
                    for (int i = 0; i < 50; i++)
                    {
                        string line = r.ReadLine();
                        if (line == null) break;
                        if (line.Length == 0 || line[0] == '#') continue;
                        string[] p = line.Split('|');
                        if (p.Length > 2 && DateTime.TryParseExact(p[2], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                            return d.ToString("yyyy-MM-dd");
                        break;
                    }
                }
                catch (IOException) { }
            }
            return File.Exists(path) ? File.GetLastWriteTime(path).ToString("yyyy-MM-dd") : "";
        }

        public static List<DataFileInfo> DescribeFiles(string dataDir)
        {
            var (r, a) = FindSources(dataDir);
            return FindGeo(dataDir).Concat(r).Concat(a).Select(p => new DataFileInfo { Name = Path.GetFileName(p), Date = FileDate(p) }).ToList();
        }

        // ---------------------------------------------------------------- 다운로드

        public static async Task<int> DownloadAllAsync(string dataDir, IProgress<string> log)
        {
            Directory.CreateDirectory(dataDir);
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ipinfo-offline/2.0");
            int ok = 0;
            foreach (var (name, url) in Downloads)
            {
                string dst = Path.Combine(dataDir, name);
                string tmp = dst + ".tmp";
                // DB-IP 는 파일 이름에 연-월이 들어감. 이번 달 파일이 아직 없으면 지난달 것을 받음.
                var urls = url.Contains("{0")
                    ? new[] { string.Format(CultureInfo.InvariantCulture, url, DateTime.Today),
                              string.Format(CultureInfo.InvariantCulture, url, DateTime.Today.AddMonths(-1)) }
                    : new[] { url };
                try
                {
                    HttpResponseMessage resp = null;
                    foreach (var u in urls)
                    {
                        log.Report("내려받는 중: " + u);
                        resp?.Dispose();
                        resp = await http.GetAsync(u, HttpCompletionOption.ResponseHeadersRead);
                        if (resp.StatusCode != HttpStatusCode.NotFound) break;
                    }
                    using (resp)
                    {
                        resp.EnsureSuccessStatusCode();
                        using var src = await resp.Content.ReadAsStreamAsync();
                        using var fs = File.Create(tmp);
                        await src.CopyToAsync(fs);
                    }
                    File.Move(tmp, dst, true);
                    log.Report($"  → 완료 ({new FileInfo(dst).Length / 1e6:0.0} MB)");
                    ok++;
                }
                catch (Exception ex)
                {
                    try { File.Delete(tmp); } catch { }
                    log.Report("  → 실패: " + ex.Message);
                }
            }
            return ok;
        }
    }
}
