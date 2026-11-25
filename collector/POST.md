# Okuyucuyu merkeze bağlama (`--post`)

Kaynak kod değişmedi. `GopOpcOkuyucu.exe` hâlâ salt okunurdur: OPC’ye yazmaz, setpoint vermez, türbin başlatmaz veya durdurmaz.

`--post` verilmezse program yalnızca yerel `okuma-sonucu.json` yazar ve ağa hiçbir şey göndermez.

## Komut

Santral PC’sinde, zenon / Vestas OPC ile aynı oturumda:

```bat
GopOpcOkuyucu.exe --tags taglar.txt --interval 10 --post "https://izleme.artibirenerji.com.tr/api/ingest?key=ANAHTAR"
```

- `ANAHTAR`, Azure App Settings’teki `Ingest__Keys__gop` değeridir. Repoda yoktur.
- Adres santralden dışarı doğrudur. Santral PC’sine dışarıdan port açılmaz.
- Aralık saniye cinsindendir. `10`, yaklaşık 10 saniyede bir okuyup aynı JSON’u POST eder.
- Program özel HTTP başlığı ekleyemez. Bu yüzden anahtar sorgu dizesindedir: `?key=`.
- Sunucu `X-Api-Key` başlığını da kabul eder. Mevcut exe bu başlığı koymaz; ileride bir ara katman kullanırsa başlık tercih edilir (sorgu dizesi bazı HTTP günlüklerine düşebilir).
- Aynı dosyanın yeniden gönderilmesi (yerel tampon / tekrar) güvenlidir. Sunucu gövdenin özetini tutar ve ikinci kez `200` ile `duplicate` döner; okuyucu bunu hata saymaz.
- Başarısız POST yalnızca konsola yazılır. OPC okuması ve yerel JSON sürer.

Gaziosmanpaşa için anahtar `gop` santraline aittir. Başka santral okuyucusu kendi anahtarıyla aynı uç noktaya gider (`Ingest__Keys__yilmaz` vb.).

## Ne gönderilir

`okuma-sonucu.json` ile aynı gövde: `zaman`, `dongu`, `sunucu`, `okunanlar[]` (`itemId`, `deger`, `kalite`, `kaliteHam`, `zaman`). Ayrıntı örnek dosyada: `data/ornek/okuma-2026-11-06-1509.json`.

## Hızlı deneme (merkez tarafı)

```bash
curl -sS -X POST "http://127.0.0.1:5080/api/ingest?key=ANAHTAR" \
  -H "Content-Type: application/json" \
  --data-binary @data/ornek/okuma-2026-11-06-1509.json
```

Beklenen: `{"status":"stored",...}` ve aynı komutun hemen ardından `{"status":"duplicate",...}`.
