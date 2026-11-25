# Artıbir Enerji · Santral izleme

Gaziosmanpaşa RES, Yılmaz RES, Yeniköy RES ve Küçükköy GES için salt okunur işletme izleme uygulaması. Tek bir ASP.NET Core 8 uygulaması arayüzü, okuma API’lerini ve santral okuyucusunun ingest ucunu birlikte sunar.

Uzaktan kumanda yoktur: setpoint, start/stop veya iş emrinin sahaya iletilmesi yok. Vardiya ve arıza ekranları yalnız kayıt defteridir.

## Ne var

| Parça | Yer |
| --- | --- |
| Web + API | `src/Artibir.Izleme` |
| Şema ve migration | `src/Artibir.Izleme/Data/Migrations` |
| OPC okuyucu (kaynak, değiştirilmedi) | `collector/` |
| `--post` kullanımı | `collector/POST.md` |
| Örnek OPC JSON ve aylık Excel | `data/ornek/` |
| Testler | `src/Artibir.Izleme.Tests` |
| Azure yayın iş akışı | `.github/workflows/azure-app-service.yml` |

Gaziosmanpaşa (`gop`) canlı beslemeye açıktır: 15 Vestas V136, T1–T15. Yılmaz, Yeniköy (Siemens Gamesa, Çanakkale) ve Küçükköy GES katalogdadır ve arayüzde **örnek veri** olarak işaretlenir; uydurma MW yazılmaz, portföy toplamına katılmaz.

## Yerelde çalıştırma

Gerekli: .NET 8 SDK, PostgreSQL 14+ (önerilen 16).

```bash
sudo -u postgres psql -c "CREATE USER artibir WITH PASSWORD 'yerel-parola';"
sudo -u postgres psql -c "CREATE DATABASE artibir OWNER artibir;"

export ASPNETCORE_ENVIRONMENT=Development
export Auth__Mode=Dev
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=artibir;Username=artibir;Password=yerel-parola;SSL Mode=Prefer"
dotnet run --project src/Artibir.Izleme
```

Uygulama `http://localhost:5080` adresindedir. Açılışta migration uygulanır ve dört santral ile GOP türbinleri yazılır.

Geliştirme girişi (`Auth__Mode=Dev` ve `ASPNETCORE_ENVIRONMENT=Development`):

| Kullanıcı | Yetki | Parola |
| --- | --- | --- |
| deniz | Üst yönetim, dört santral | `artibir-dev` |
| ahmet | Üst yönetim, dört santral | `artibir-dev` |
| fatih | Yalnız Gaziosmanpaşa RES | `artibir-dev` |

Parola repoda bir sır olarak durmaz; kod, Development ortamında `Auth__DevPassword` boşsa bu değeri kullanır. Production ortamında `Auth__Mode=Dev` ise parola zorunludur ve boşsa giriş olmaz. Üretimde `Auth__Mode=Entra` olmalıdır.

Örnek okumayı içeri almak (okuyucunun gönderdiği gövde):

```bash
curl -sS -X POST "http://127.0.0.1:5080/api/ingest?key=YEREL_GOP_ANAHTARI" \
  -H "Content-Type: application/json" \
  --data-binary @data/ornek/okuma-2026-11-06-1509.json
```

Anahtar `Ingest__Keys__gop` ile aynı olmalıdır. Aynı dosya ikinci kez giderse yanıt `duplicate` ve HTTP 200’dür.

Testler:

```bash
dotnet test src/Artibir.Izleme.Tests/Artibir.Izleme.Tests.csproj
```

## Yapılandırma

Sırlar repoda yoktur. `appsettings.json` içindeki bağlantı dizesi, Entra kimlikleri ve ingest anahtarları boştur. Değerler yalnız ortam değişkeni veya Azure App Settings ile verilir. `appsettings.Development.json` `.gitignore` içindedir. Şablon: `src/Artibir.Izleme/appsettings.Development.example.json`.

İç içe ayarlar Azure’da çift alt çizgi ile yazılır (`AzureAd__TenantId`).

## Ingest

`POST /api/ingest`

Kimlik: santral başına bir anahtar.

- Başlık (tercih edilen): `X-Api-Key`
- Sorgu (mevcut okuyucu özel başlık koyamadığı için): `?key=`

Gövde, okuyucunun `okuma-sonucu.json` dosyasıdır (`zaman`, `dongu`, `okunanlar[]` …). Ham satırlar yaklaşık 7 gün tutulur. Daha eskisi arka plan işiyle 10 dakikalık ortalama, minimum, maksimum ve durum sayaçlarına katlanır; ham satır silinir. Aynı gövde (yerel tampon / yeniden oynatma) içerik özeti ile yok sayılır.

OperationStateInt: **2 bakımda**, **3 çalışıyor**. Kötü kalite tek başına arıza kaydı açmaz.

## Excel geçmişi

Üst yönetim arayüzden (`Excel aktarımı`) veya `POST /api/admin/excel` ile aylık dosyaları yükler. Sayfa adı `TÜRBİN DATA`.

- Her türbin için kümülatif sayaç ve yanındaki `FARK MWh` (saatlik üretim).
- `TÜRBİN TOPLAM`, `SAYAÇ MWh`, `TÜRBİN&SAYAÇ FARKI`.
- Tarih çoğu zaman yalnız günün ilk satırındadır; saat sütunu bazen düz saat, bazen `DateTime` olarak gelir.
- FARK hücresi formülse ve kayıtlı sonuç yoksa üretim, ardışık sayaç farkının 1000’e bölümüdür (`MAX(0, …)`).
- Yıl ve ay verinin içinden okunur. İsteğe bağlı beklenen yıl/ay veya dosyanın birden fazla ayı kapsaması **uyuşmazlık** olarak işaretlenir; satırlar yine yazılır.
- Aynı dosya (SHA-256) tekrar yüklenirse atlanır. Düzeltilmiş dosya ilgili ayı baştan yazar.
- Tek istekte en fazla 80 dosya, dosya başına 40 MB. Dört-beş yıl bu sınırın içindedir; daha büyük yığınlar parça parça yüklenebilir.

## Yetki

Üretim: Microsoft Entra ID, kiracı **ARTIBIR GRUP**, birincil etki alanı `artibirgrup.com`.

Grup kimlikleri (object id) yapılandırmadadır:

| Ayar | Kim görür |
| --- | --- |
| `Authorization__ExecutiveGroupId` | Tüm santraller ve enerji mutabakatı |
| `Authorization__PlantGroupIds__gop` | Yalnız Gaziosmanpaşa |
| `Authorization__PlantGroupIds__yilmaz` | Yalnız Yılmaz |
| `Authorization__PlantGroupIds__yenikoy` | Yalnız Yeniköy |
| `Authorization__PlantGroupIds__kucukkoy` | Yalnız Küçükköy |

Üst yönetim grubundaki kullanıcı santral gruplarına da üye olsa bile her santrali görür. Finans / mutabakat sayfası yalnız üst yönetimdedir. Eşleme her oturumda `app_users` / `user_roles` tablosuna da yazılır; karar grup talebinden verilir. Grup eşlemesi değişince kullanıcının yeniden girmesi gerekir.

Grup talebi hiç gelmezse (overage) kullanıcı santral görmez. Çok sayıda grup üyeliği varsa Entra’ya grup filtresi koyun veya uygulamaya atanmış gruplarla sınırlayın. Graph ile overage çözümü bu sürümde yoktur.

## Azure’da IT’nin açacağı kaynaklar

Bölge önerisi: **West Europe**. İsimler örneklenmiştir; App Service ve PostgreSQL adları Azure’da global benzersiz olmalıdır.

### 1. Kaynak grubu

- Ad: `rg-artibir-izleme`
- Bölge: West Europe

### 2. PostgreSQL Flexible Server

- Ad: `psql-artibir-izleme`
- Sürüm: **16**
- İşlem: **Burstable, Standard_B1ms** (1 vCore, 2 GiB)
- Depolama: en az 32 GiB (7 günlük ham okuma büyüyebilir; izleyin)
- Yönetici kullanıcısı: IT seçer. Parola App Settings’e gider, repoya yazılmaz.
- Veritabanı: `artibir`
- Ağ: ilk kurulumda “Allow public access” ve Azure hizmetlerine izin. Üretimde tercihen özel uç nokta.
- SSL zorunlu.

Bağlantı dizesi:

```text
Host=<sunucu>.postgres.database.azure.com;Port=5432;Database=artibir;Username=<yonetici>;Password=<parola>;SSL Mode=Require;Trust Server Certificate=true
```

### 3. App Service

- Plan: **Linux, B1**
- Ad: `app-artibir-izleme` (örnek)
- Yığın: **.NET 8 (DOTNETCORE|8.0)**
- Başlangıç komutu: `dotnet Artibir.Izleme.dll`
- HTTPS Only: açık

Yayın: Deployment Center → GitHub → dal `main`. İş akışı `.github/workflows/azure-app-service.yml` testten sonra `azure/webapps-deploy` kullanır.

GitHub Actions sırları (değerler bu depoda yoktur):

- `AZURE_WEBAPP_NAME` — App Service adı
- `AZURE_WEBAPP_PUBLISH_PROFILE` — App Service’ten indirilen yayın profili

OIDC ile yayın tercih edilirse aynı iş akışındaki yorumdaki sırlar kullanılır: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_WEBAPP_NAME`. Yayın profili adımı kalkar; `azure/login@v2` eklenir.

### 4. App Settings

| Ad | Değer |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__DefaultConnection` | Yukarıdaki PostgreSQL dizesi |
| `Auth__Mode` | `Entra` |
| `AzureAd__Instance` | `https://login.microsoftonline.com/` |
| `AzureAd__TenantId` | ARTIBIR GRUP kiracı kimliği (GUID) |
| `AzureAd__ClientId` | Uygulama (client) kimliği |
| `AzureAd__ClientSecret` | İstemci parolası |
| `AzureAd__CallbackPath` | `/signin-oidc` |
| `Authorization__ExecutiveGroupId` | Üst yönetim güvenlik grubunun object id’si |
| `Authorization__PlantGroupIds__gop` | GOP grubu object id |
| `Authorization__PlantGroupIds__yilmaz` | Yılmaz grubu object id |
| `Authorization__PlantGroupIds__yenikoy` | Yeniköy grubu object id |
| `Authorization__PlantGroupIds__kucukkoy` | Küçükköy grubu object id |
| `Ingest__Keys__gop` | Uzun rastgele anahtar |
| `Ingest__Keys__yilmaz` | İleride, boş kalabilir |
| `Ingest__Keys__yenikoy` | İleride |
| `Ingest__Keys__kucukkoy` | İleride |
| `Retention__RawDays` | `7` |
| `WEBSITE_TIME_ZONE` | `Europe/Istanbul` |

`Ingest__Keys__*` boşsa ingest 503 döner; uç nokta açık kalmaz. Veri koruma anahtarları yazılabiliyorsa `/home/artibir-dataprotection` altına, değilse kullanıcının ev dizinine yazılır. App Service Linux’ta `/home` kalıcıdır. İsterseniz `DataProtection__Path` ile klasörü siz verin.

### 5. Entra uygulama kaydı

- Ad: `Artıbir İzleme`
- Desteklenen hesaplar: yalnız bu kiracı (ARTIBIR GRUP)
- Tür: Web
- Yeniden yönlendirme URI’leri:
  - `https://<app-adı>.azurewebsites.net/signin-oidc`
  - `https://izleme.artibirenerji.com.tr/signin-oidc`
  - Yerel deneme için `http://localhost:5080/signin-oidc`
- Oturum kapatma: `https://<app-adı>.azurewebsites.net/signout-callback-oidc` ve özel alan adı için aynı yol
- Sertifika ve parolalar: bir client secret. Değeri yalnız `AzureAd__ClientSecret`
- Belirteç yapılandırması: **groups** talebi, güvenlik grupları, **Group ID**, ID token’a eklensin
- Bildirim (manifest): `"groupMembershipClaims": "SecurityGroup"`
- API izni: Microsoft Graph `User.Read` (delegated), yönetici onayı kiracı politikasına göre
- Dört santral ve bir üst yönetim için ayrı güvenlik grupları. Object id’ler App Settings’e.

Kullanıcı uygulamaya “assignment required” ise gruplar uygulamaya atanır. Aksi halde kiracıdaki her hesap giriş yapar ama grubu yoksa santral görmez.

### 6. Özel alan adı (sonra) — Cloudflare

Örnek: `izleme.artibirenerji.com.tr`

1. App Service → Custom domains → alan adını ekle. Azure bir doğrulama kimliği verir.
2. Cloudflare DNS (önce proxy kapalı / DNS only, gri bulut):
   - `CNAME` `izleme` → `<app-adı>.azurewebsites.net`
   - `TXT` `asuid.izleme` → Azure’un verdiği doğrulama değeri
3. Azure doğrulaması geçince App Service Managed Certificate bağla.
4. Sertifika oluştuktan sonra Cloudflare proxy (turuncu bulut) açılabilir. Proxy erken açılırsa HTTP doğrulaması başarısız olabilir.
5. Entra yönlendirme URI’sine `https://izleme.artibirenerji.com.tr/signin-oidc` ekli olsun.

## Okuyucuyu bağlama

Santral PC’sinde, Vestas OPC ile aynı Windows oturumunda (`collector/POST.md`):

```bat
GopOpcOkuyucu.exe --tags taglar.txt --interval 10 --post "https://izleme.artibirenerji.com.tr/api/ingest?key=GOP_ANAHTARI"
```

Alan adı hazır değilse host `<app-adı>.azurewebsites.net` olur. Bağlantı santralden dışarıdır; santrale içeri port açılmaz.

## Okuma API’leri (oturum gerekir)

- `GET /api/me`, `GET /api/plants`
- `GET /api/plants/{kod}/live`
- `GET /api/plants/{kod}/power?range=today|week`
- `GET /api/plants/{kod}/production?range=today|week|month|year|years&bucket=hour|day|month|year`
- `GET /api/plants/{kod}/availability?range=week`
- `GET /api/plants/{kod}/shifts` ve `POST` (kayıt)
- `GET /api/plants/{kod}/faults` ve `POST` (kayıt)
- `GET /api/plants/{kod}/finance` — yalnız üst yönetim
- `POST /api/admin/excel` — yalnız üst yönetim

`range` yerine `from` / `to` (`yyyy-MM-dd` veya `yyyy-MM-dd HH:mm`, Türkiye saati) verilebilir.

Sağlık: `GET /health`

## Bilinçli sınırlar

- Yılmaz, Yeniköy ve Küçükköy için okuyucu ve Excel şeması bağlanmadı; ekranda örnek veri olarak dururlar.
- Üretim tahmini, iş emri, izin, organizasyon ve belge arşivi bu sürümde veri toplamaz. Prototipteki kurgusal kişi ve MW değerleri taşınmadı.
- Enerji mutabakatı MWh’dir. Tarife yoktur, tutar hesaplanmaz.
- Vestas availability sayacı, “çalışır durum oranı”ndan ayrıdır. Oran, `OperationStateInt = 3` örneklerinin payıdır.
- Günlük MWh, Excel sayacı yoksa POM kW ortalamasından yaklaşık integral olarak etiketlenir; uydurma eğri çizilmez.
- Entra grup overage (talebin sığmaması) Graph ile çözülmez.
- 7 gün × ~1000 etiket × 10 sn, B1ms için iri bir ham tablodur. Saklama `Retention__RawDays` ile kısalır; doluluk izlenmelidir.
