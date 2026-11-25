# GOP OPC-DA Okuyucu (ilk deneme)

> **v0.3 merkezi aktarım:** `--post` verilirse okuma JSON’u HTTPS ile merkeze gider. Anahtar ve adres: [POST.md](POST.md). `--post` yoksa aşağıdaki gibi yalnız yerel çalışır; ağa gönderim yapılmaz. Kaynak kod bu depoya olduğu gibi alınmıştır.

Gaziosmanpaşa RES santral PC’sinde, COPA-DATA **zenon** Runtime ile **aynı Windows oturumunda** çalıştırılacak **salt okunur** küçük bir konsol programı.

Bu paket **web prototipi değildir**. HTML arayüzüne dokunmaz. Excel’den veri aktarmaz. İnternete kapı açmaz. Değişken **yazmaz / setpoint vermez**.

Amaç: Yerel OPC-DA üzerinden zenon sunucusunu görmek, adres alanını taramak, birkaç değeri okumak. Çıktıyı (konsol veya `okuma-sonucu.json`) paylaşınca bir sonraki adımı konuşuruz.

Resmi COPA-DATA kontrol listesi (mutlaka bak):  
https://selfservice.copadata.com/portal/en/kb/articles/checklist-opcda-server

---

## Ne yapar / ne yapmaz

| Yapar | Yapmaz |
| --- | --- |
| Yerel OPC DA sunucularını listeler (OPCEnum) | DCOM / uzak OPC / firewall portu açmaz |
| zenon sunucusuna **yerel** bağlanır | Ağ üzerinden bağlantı denemez |
| Flat adres alanını tarar | Hiyerarşik ağaç varsaymaz (zenon flat) |
| `IOPCSyncIO` ile okur | Write / AsyncWrite / setpoint yok |
| `okuma-sonucu.json` yazar (exe yanı / çalışma dizini) | JSON’u bir yere göndermez |
| Her zenon projesi için ayrı OPC grubu kullanır | Aynı item’ı iki kez eklemez |

---

## Nereye kopyalayacaksın

- **zenon Service Engine (Runtime) zaten çalışan** Gaziosmanpaşa santral Windows PC’sine.
- Runtime’a **giriş yapmış aynı Windows kullanıcısı** ile.
- **Aynı masaüstü oturumu** — farklı Remote Desktop oturumundan deneme.

Bu Linux kutusundan veya başka bir makineden OPC’ye bağlanılmaz; bağlanılmamalı. COPA-DATA yalnız **yerel** OPC-DA erişimini destekler; zenon OPC DA sunucusuyla **DCOM kullanma**.

---

## Resmi ön kontroller (COPA-DATA checklist özeti)

1. **OPC Foundation OPC DA çekirdek bileşenleri** (x86 **ve** x64) kurulu olsun. zenon kurulumuna dahil değildir; zenon DVD/USB’deki third-party klasöründe de bulunur. Kurulumdan sonra **yeniden başlat**.
2. Sunucuyu kaydet: **yönetici** komut satırı → **32-bit** zenon kurulum klasörü → `zenOpcSrv /RegSrvD` → başarı mesajını doğrula. (Güvenlik nedeniyle zenon setup bunu otomatik kayıt etmez.)
3. `zenon6.ini` içinde `[VBA]` altında `EVENT=1` olsun — Service Engine’e dış API erişimi için gerekli.
4. **Service Engine (Runtime) ve bu okuyucu aynı Windows oturumu + aynı kullanıcı.** Farklı RDP oturumundan test etme; boş ikinci bir `zenOpcSrv` açılıp değişken görmeyebilirsin. Runtime Session 0 / SYSTEM altında servis olarak çalışıyorsa, normal masaüstü istemcisi yine boş sunucu başlatabilir — o durumda Runtime’ı etkileşimli oturumda çalıştırıp oradan dene.
5. **DCOM yapılandırma** veya OPC için **firewall portu açma**. Yerel deneme; uzak erişim hedefi değil.

---

## Derleme (santral PC’sinde, x86)

zenon OPC DA sunucusu **32-bit** `zenOpcSrv` üzerinden kayıtlıdır. Okuyucuyu da **x86** derle.

### Kolay yol: `build.bat`

```bat
build.bat
```

Çıktı: `bin\Release\GopOpcOkuyucu.exe`

`build.bat`, .NET Framework 4.8’in 32-bit derleyicisini kullanır:

`C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe`

### Visual Studio

`GopOpcOkuyucu.csproj` dosyasını aç → Platform = **x86** → Release derle. Hedef: .NET Framework **4.8**.

### Elle csc (x86)

```bat
%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe /nologo /target:exe /platform:x86 /optimize+ /out:GopOpcOkuyucu.exe /reference:System.dll /reference:System.Core.dll Program.cs OpcDaClient.cs OpcDaInterfaces.cs Properties\AssemblyInfo.cs
```

NuGet / ticari OPC SDK gerekmez. OPC Automation (`OPC.Automation.1`) zorunlu değil; program doğrudan COM arabirimlerini kullanır.

---

## Çalıştırma

Runtime açıkken, **aynı oturumda**, exe’nun olduğu klasörde:

```bat
GopOpcOkuyucu.exe
```

Belirli sunucu ProgID:

```bat
GopOpcOkuyucu.exe --server "ProgID"
```

(ProgID’yi ilk çalıştırmanın listesinden kopyala. Bu pakette sabit bir zenon ProgID uydurulmadı — makinede ne kayıtlıysa o geçerli.)

İsimde geçenleri filtrele (ör. türbin ipucu):

```bat
GopOpcOkuyucu.exe --filter T07
```

Bilinen değişkenleri oku (`ProjeAdi.DegiskenAdi` — browse çıktısından kopyala; prototipteki `GOP_RES.*` örnekleri gerçek etiket değildir):

```bat
GopOpcOkuyucu.exe --read ProjeAdi.DegiskenAdi
GopOpcOkuyucu.exe --read Proje.A --read Proje.B --filter T07
```

Yardım:

```bat
GopOpcOkuyucu.exe --help
```

---

## Çıktıyı nasıl okursun

- **Sunucu listesi boş:** çekirdek bileşenler / kayıt / yeniden başlatma.
- **Tarama boş veya “henüz değişkenleri yüklüyor”:** çoğu zaman yanlış oturum/kullanıcı, kayıtsız sunucu, `EVENT=1` eksik, veya büyük projede sunucu hâlâ yükleniyor. **“Türbin yok” demek değildir.**
- **Quality Bad:** OPC kablosu kopuk olmak zorunda değil; zenon tarafında değişken **INVALID** olabilir. Program bunu da yazar.
- Son OPC istemcisi kapanınca `zenOpcSrv` kapanabilir — zenon’un beklenen davranışı.

Çalışma dizinine `okuma-sonucu.json` yazılır (ağa gönderilmez).

---

## Bize ne göndereceksin

Konsol çıktısını veya `okuma-sonucu.json` dosyasını yapıştır / ekran görüntüsü at.  
Şifre, kişisel veri, alakasız hesap bilgisi gönderme. Santral etiket adları ve değerleri beklenen kısım.

---

## Dosyalar

| Dosya | Açıklama |
| --- | --- |
| `Program.cs` | Komut satırı, deneme/yeniden bağlanma, JSON |
| `OpcDaClient.cs` | Listele, bağlan, tara, oku, gruplar |
| `OpcDaInterfaces.cs` | OPC DA COM arabirimleri (SDK yok) |
| `GopOpcOkuyucu.csproj` | VS / MSBuild projesi (x86) |
| `build.bat` | csc ile x86 derleme |
| `Properties/AssemblyInfo.cs` | Sürüm bilgisi |

Sürüm: 0.1.0 — ilk yerel okuma denemesi.
