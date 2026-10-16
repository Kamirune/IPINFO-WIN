# IP 조회 (IPINFO-WIN)

폐쇄망용 오프라인 IP 조회 프로그램. IP를 넣으면 **국가코드**와 **소속(ASN/기관명)**을 보여 줍니다.
국가코드는 VirusTotal과 같은 **실제 위치 국가** 기준이며, 등록 국가가 다르면 함께 표시합니다.
C# · WPF (.NET 10), CODEC-WIN과 같은 방식으로 빌드·배포합니다.

## 빌드

```powershell
dotnet run                     # 개발 중 실행
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o dist
```

또는 `build.bat`을 더블클릭하면 같은 빌드를 하고 `data` 폴더까지 `dist`에 복사합니다.

`dist\ipinfo.exe` 하나가 나옵니다. .NET 실행 환경이 exe 안에 들어 있어 폐쇄망 PC에 아무것도 설치할 필요가 없습니다.

## 배포 (폐쇄망으로 옮길 것)

```
IP조회\
├─ ipinfo.exe
└─ data\
   ├─ dbip-country-lite.csv.gz   ← 실제 위치 국가 (DB-IP)
   ├─ delegated-afrinic-extended-latest
   ├─ delegated-apnic-extended-latest
   ├─ delegated-arin-extended-latest
   ├─ delegated-lacnic-extended-latest
   ├─ delegated-ripencc-extended-latest
   ├─ ip2asn-combined.tsv.gz
   └─ custom.csv        ← (선택) 내부 대역 이름표
```

데이터는 인터넷 PC에서 프로그램 오른쪽 위 **데이터 → 인터넷에서 최신 데이터 받기**로 받습니다.
프로그램은 exe 옆의 `data` 폴더를 찾고, 개발 중(`dotnet run`)에는 상위 폴더의 `data`도 찾아봅니다.

## 기능

- IP 하나: 국가코드를 도장처럼 크게, 소속·등록기관·할당 대역을 함께 표시
- 로그·엑셀 내용을 통째로 붙여넣기 → IP만 골라 중복 없이 조회 (`1.2.3.4:443`, `[v6]:80` 인식)
- 파일을 창에 끌어다 놓기 / 파일에서 불러오기 (UTF-8, CP949 모두 읽음)
- 여러 개 조회 시 국가별 걸러보기, 줄을 누르면 상세 정보
- CSV로 저장(엑셀에서 한글 그대로), 표 복사(엑셀에 바로 붙여넣기)
- 데이터 기준일 표시, 90일이 지나면 주황색으로 갱신 알림

## 국가코드 규칙

우선순위: **custom.csv → 실제 위치(DB-IP) → RIR 등록 국가 → ASN 등록국가**

- **실제 위치 국가**: 그 IP가 실제로 쓰이는 나라. VirusTotal 등 보안 서비스가 보여 주는 기준.
- **등록 국가**: 그 대역을 할당받은 기관이 등록된 나라. 클라우드는 본사 국가(예: Azure UAE 리전도 US)로 나옴.
- 둘이 다르면 화면에 "등록 US"처럼 함께 표시합니다. 클라우드·해외 리전일 가능성이 큽니다.
- 위치 DB는 추정값이라 VirusTotal(상용 DB)과 드물게 다를 수 있습니다.

| 표시 | 의미 |
|---|---|
| `KR`, `US` … | 정상 조회 |
| `--` | 사설/루프백/멀티캐스트 등 공인 IP가 아님 (custom.csv에 국가를 지정하면 그 값) |
| `ZZ` | 공인 IP인데 데이터에 없음 → 데이터 갱신 필요 |

## 내부 대역 이름 (data/custom.csv)

```
# CIDR,이름,국가코드(선택)
10.0.0.0/8,사내망 전체,KR
10.10.0.0/16,본사 개발팀
192.168.100.0/24,DMZ 서버팜
```

가장 좁은 대역의 이름이 표시되고, 국가코드를 비워두면 상위 대역의 것을 물려받습니다. 엑셀 기본 CSV(CP949)로 저장해도 읽습니다.

## 파일 구성

| 파일 | 내용 |
|---|---|
| `IpDb.cs` | 데이터 로딩·조회·다운로드 |
| `IpUtil.cs` | IP 파싱, 특수 주소 판별, 텍스트에서 IP 골라내기 |
| `Countries.cs` | 국가코드 → 한글 국가명 |
| `Stamps.cs` | 국가코드 도장 색·모양 |
| `ResultRow.cs` | 표 한 줄과 상세 정보 |
| `MainWindow.xaml(.cs)` | 메인 화면 |
| `DataWindow.xaml(.cs)` | 데이터 현황·갱신 창 |
| `App.xaml` | 색상·버튼·표 스타일 |

## 데이터 출처

- 실제 위치 국가: [IP Geolocation by DB-IP](https://db-ip.com) — IP to Country Lite, CC BY 4.0, 매월 갱신
- RIR 할당 파일: APNIC · ARIN · RIPE NCC · LACNIC · AFRINIC 공개 통계
- ip2asn: iptoasn.com (Public Domain)
