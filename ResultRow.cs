using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace IpInfo
{
    public sealed class Fact
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public bool Mono { get; set; }
    }

    /// <summary>화면 표의 한 줄</summary>
    public sealed class ResultRow
    {
        public LookupResult R { get; }

        public ResultRow(LookupResult r) { R = r; }

        public string Ip => R.Ip;
        public string Country => R.Country;
        public string CountryName => R.CountryName;
        public string Org => string.IsNullOrEmpty(R.OrgText) ? "-" : R.OrgText;
        public string Registry => R.Rir != null ? R.Rir.Registry : "-";
        public string RegDiff => R.RegDiff;
        public SolidColorBrush RegBrush => Stamps.BrushFor(R.RegDiff);
        public System.Windows.Visibility RegVisibility =>
            string.IsNullOrEmpty(R.RegDiff) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        public string TypeShort => Regex.Replace(R.Type ?? "", @"\(.*\)", "");
        public SolidColorBrush StampBrush => Stamps.BrushFor(R.Country);
        public string SourceText => "국가코드 출처: " + R.CountrySource;

        private List<Fact> _facts;
        public List<Fact> Facts => _facts ??= BuildFacts(R);

        public static List<Fact> BuildFacts(LookupResult r)
        {
            var f = new List<Fact>();
            if (r.Asn != null) f.Add(new Fact { Key = "소속", Value = $"AS{r.Asn.Asn}  {r.Asn.Name}" });
            if (r.Custom != null)
                f.Add(new Fact
                {
                    Key = "내부 대역",
                    Value = $"{(string.IsNullOrEmpty(r.Custom.Label) ? "이름 없음" : r.Custom.Label)}  ({r.Custom.Network})",
                });
            if (r.Rir != null)
            {
                string d = r.Rir.Date ?? "";
                string when = Regex.IsMatch(d, @"^\d{8}$") ? $", {d.Substring(0, 4)}-{d.Substring(4, 2)}-{d.Substring(6, 2)} 할당" : "";
                f.Add(new Fact { Key = "등록기관", Value = r.Rir.Registry + when });
                f.Add(new Fact { Key = "할당 대역", Value = r.Rir.Range, Mono = true });
            }
            else if (r.Asn != null)
            {
                f.Add(new Fact { Key = "라우팅 대역", Value = r.Asn.Range, Mono = true });
            }
            if (!string.IsNullOrEmpty(r.RegDiff))
                f.Add(new Fact
                {
                    Key = "등록 국가",
                    Value = $"{r.RegDiff} {Countries.NameOf(r.RegDiff)} ({r.Rir?.Registry} 등록). 실제 위치와 다릅니다. 클라우드·해외 리전일 가능성이 큽니다.",
                });
            f.Add(new Fact { Key = "구분", Value = r.Type });
            if (r.Asn != null && !string.IsNullOrEmpty(r.Asn.Country) && r.Asn.Country != r.Country && r.Asn.Country != r.RegCountry)
                f.Add(new Fact
                {
                    Key = "참고",
                    Value = $"ASN 등록 국가는 {r.Asn.Country}입니다. 클라우드·CDN은 실제 위치와 등록 국가가 다를 수 있습니다.",
                });
            if (r.Asn == null && r.Rir == null && r.Custom == null && r.Type == IpUtil.PublicType)
                f.Add(new Fact { Key = "참고", Value = "데이터에 없는 주소입니다. 데이터를 최신으로 갱신해 보세요." });
            return f;
        }
    }
}
