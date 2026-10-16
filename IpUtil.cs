using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace IpInfo
{
    /// <summary>IP 주소 파싱·변환과 로그 텍스트에서 IP 골라내기</summary>
    public static class IpUtil
    {
        // ---------------------------------------------------------------- 파싱

        /// <summary>IPv4 점 표기(a.b.c.d)만 엄격하게 파싱. "1" 이나 "1.2" 같은 축약형은 거부.</summary>
        public static bool TryParseV4(ReadOnlySpan<char> s, out uint value)
        {
            value = 0;
            int part = 0, digits = 0, dots = 0;
            foreach (char c in s)
            {
                if (c >= '0' && c <= '9')
                {
                    part = part * 10 + (c - '0');
                    if (++digits > 3 || part > 255) return false;
                }
                else if (c == '.')
                {
                    if (digits == 0 || ++dots > 3) return false;
                    value = (value << 8) | (uint)part;
                    part = 0; digits = 0;
                }
                else return false;
            }
            if (dots != 3 || digits == 0) return false;
            value = (value << 8) | (uint)part;
            return true;
        }

        /// <summary>IPv4·IPv6 문자열을 파싱. IPv4-mapped IPv6 는 IPv4 로, 영역 표기(%eth0)는 제거.</summary>
        public static bool TryParse(string text, out IPAddress ip)
        {
            ip = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Trim().Trim('[', ']');
            int pct = s.IndexOf('%');
            if (pct >= 0) s = s.Substring(0, pct);

            if (s.IndexOf(':') < 0)
            {
                if (!TryParseV4(s, out uint v4)) return false;
                ip = FromV4(v4);
                return true;
            }
            if (!IPAddress.TryParse(s, out var a) || a.AddressFamily != AddressFamily.InterNetworkV6) return false;
            if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
            else if (a.ScopeId != 0) a = new IPAddress(a.GetAddressBytes());
            ip = a;
            return true;
        }

        public static IPAddress FromV4(uint v) =>
            new IPAddress(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });

        public static UInt128 ToNumber(IPAddress ip)
        {
            UInt128 n = 0;
            foreach (byte b in ip.GetAddressBytes()) n = (n << 8) | b;
            return n;
        }

        public static string FromNumber(UInt128 n, int version)
        {
            int len = version == 4 ? 4 : 16;
            var b = new byte[len];
            for (int i = len - 1; i >= 0; i--) { b[i] = (byte)(n & 0xFF); n >>= 8; }
            return new IPAddress(b).ToString();
        }

        /// <summary>프리픽스 길이에 대한 호스트 부분 마스크 (bits = 32 또는 128)</summary>
        public static UInt128 HostMask(int prefix, int bits)
        {
            int host = bits - prefix;
            if (host <= 0) return 0;
            if (host >= 128) return UInt128.MaxValue;
            return (UInt128.One << host) - 1;
        }

        // ---------------------------------------------------------------- 특수 주소

        private static readonly (uint net, int bits, string name)[] SpecialV4 =
        {
            (0x00000000, 8, "특수용도(This network)"),
            (0x0A000000, 8, "사설(Private)"),
            (0x64400000, 10, "CGNAT 공유주소(100.64.0.0/10)"),
            (0x7F000000, 8, "루프백(Loopback)"),
            (0xA9FE0000, 16, "링크로컬(Link-local)"),
            (0xAC100000, 12, "사설(Private)"),
            (0xC0000000, 24, "특수용도(IETF)"),
            (0xC0000200, 24, "문서용(Documentation)"),
            (0xC0A80000, 16, "사설(Private)"),
            (0xC6120000, 15, "벤치마크용(Benchmarking)"),
            (0xC6336400, 24, "문서용(Documentation)"),
            (0xCB007100, 24, "문서용(Documentation)"),
            (0xE0000000, 4, "멀티캐스트(Multicast)"),
            (0xF0000000, 4, "예약(Reserved)"),
        };

        public const string PublicType = "공인(Public)";

        public static string SpecialType(IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            if (b.Length == 4)
            {
                uint v = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
                if (v == 0) return "미지정(Unspecified)";
                foreach (var (net, bits, name) in SpecialV4)
                {
                    uint mask = bits == 0 ? 0 : uint.MaxValue << (32 - bits);
                    if ((v & mask) == net) return name;
                }
                return PublicType;
            }
            if (ip.Equals(IPAddress.IPv6None)) return "미지정(Unspecified)";
            if (ip.Equals(IPAddress.IPv6Loopback)) return "루프백(Loopback)";
            if (b[0] == 0xFE && (b[1] & 0xC0) == 0x80) return "링크로컬(Link-local)";
            if ((b[0] & 0xFE) == 0xFC) return "사설(Private)";
            if (b[0] == 0xFF) return "멀티캐스트(Multicast)";
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return "문서용(Documentation)";
            if ((b[0] & 0xE0) != 0x20) return "특수용도(Non-global)";
            return PublicType;
        }

        // ---------------------------------------------------------------- 텍스트에서 IP 골라내기

        private static readonly Regex TokenSplit = new Regex(@"[^0-9A-Za-z:.\[\]%]+", RegexOptions.Compiled);
        // 시각(12:30:45), MAC 주소(aa:bb:cc:dd:ee:ff) 는 잘못된 IP로 치지 않음
        private static readonly Regex NotIpLike = new Regex(
            @"^(\d{1,2}:\d{2}(:\d{2})?([.,]\d+)?|([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2})$", RegexOptions.Compiled);

        private static bool ParseCandidate(string t, out IPAddress ip)
        {
            if (TryParse(t, out ip)) return true;
            string s = t.TrimEnd('.');
            if (s.StartsWith("[")) // [v6]:port
            {
                int j = s.IndexOf(']');
                if (j > 0) return TryParse(s.Substring(1, j - 1), out ip);
            }
            if (CountOf(s, ':') == 1 && CountOf(s, '.') == 3) // v4:port
                return TryParse(s.Substring(0, s.IndexOf(':')), out ip);
            if (TryParse(s, out ip)) return true;
            return TryParse(s.TrimEnd(':'), out ip);
        }

        private static int CountOf(string s, char c)
        {
            int n = 0;
            foreach (char x in s) if (x == c) n++;
            return n;
        }

        private static bool LooksLikeIp(string t)
        {
            if (NotIpLike.IsMatch(t)) return false;
            bool hasDigit = false;
            foreach (char c in t) if (c >= '0' && c <= '9') { hasDigit = true; break; }
            return hasDigit && (CountOf(t, '.') >= 3 || CountOf(t, ':') >= 2);
        }

        /// <summary>순서를 지키며 중복 없이 IP를 골라냄. skipped 는 IP처럼 보이지만 잘못된 항목.</summary>
        public static List<string> ExtractIps(string text, out List<string> skipped, int limit = 100000)
        {
            var ips = new List<string>();
            skipped = new List<string>();
            var seen = new HashSet<string>();
            foreach (string t in TokenSplit.Split(text ?? ""))
            {
                if (t.Length == 0) continue;
                if (!ParseCandidate(t, out var ip))
                {
                    if (skipped.Count < 20 && LooksLikeIp(t)) skipped.Add(t);
                    continue;
                }
                string s = ip.ToString();
                if (seen.Add(s))
                {
                    ips.Add(s);
                    if (ips.Count >= limit) break;
                }
            }
            return ips;
        }
    }
}
