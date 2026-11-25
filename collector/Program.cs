using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace GopOpcOkuyucu
{
    internal static class Program
    {
        private const int DefaultBrowsePreview = 40;
        private const int DefaultReadCount = 15;
        private const int MaxBrowseCollect = 5000;
        private const int MaxConnectAttempts = 6;
        private const int RetryDelayMs = 15000;
        private const string DefaultTagsFileName = "taglar.txt";

        private static volatile bool s_stopRequested;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                return Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Hata: " + ex.Message);
                Console.Error.WriteLine();
                Console.Error.WriteLine("İpucu: Bu program yalnız zenon Runtime ile aynı Windows oturumunda ve aynı kullanıcıda çalışmalı.");
                Console.Error.WriteLine("Resmi kontrol listesi: https://selfservice.copadata.com/portal/en/kb/articles/checklist-opcda-server");
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            string serverProgId = null;
            string filter = null;
            var readItems = new List<string>();
            string tagsFileArg = null;
            int intervalSec = 0;
            string postUrl = null;
            bool help = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--help" || a == "-h" || a == "/?")
                {
                    help = true;
                }
                else if (a == "--server" && i + 1 < args.Length)
                {
                    serverProgId = args[++i];
                }
                else if (a == "--filter" && i + 1 < args.Length)
                {
                    filter = args[++i];
                }
                else if (a == "--read" && i + 1 < args.Length)
                {
                    readItems.Add(args[++i]);
                }
                else if (a == "--tags" && i + 1 < args.Length)
                {
                    tagsFileArg = args[++i];
                }
                else if (a == "--interval" && i + 1 < args.Length)
                {
                    if (!int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out intervalSec) || intervalSec < 0)
                    {
                        Console.Error.WriteLine("--interval saniye olarak 0 veya pozitif tam sayı olmalı.");
                        return 2;
                    }
                }
                else if (a == "--post" && i + 1 < args.Length)
                {
                    postUrl = args[++i];
                }
                else
                {
                    Console.Error.WriteLine("Bilinmeyen argüman: " + a);
                    PrintUsage();
                    return 2;
                }
            }

            if (help)
            {
                PrintUsage();
                return 0;
            }

            string tagsPath = ResolveTagsPath(tagsFileArg);
            List<string> fromFile = LoadTagsFile(tagsPath);
            if (fromFile.Count > 0)
            {
                Console.WriteLine(string.Format(
                    "Etiket dosyası: {0} ({1} satır)",
                    tagsPath,
                    fromFile.Count));
            }
            else if (!string.IsNullOrEmpty(tagsFileArg))
            {
                Console.Error.WriteLine("Etiket dosyası okunamadı veya boş: " + tagsPath);
                return 2;
            }

            // Dosyadan gelenler + isteğe bağlı --read ekleri
            var toRead = new List<string>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in fromFile)
            {
                if (seenIds.Add(id))
                    toRead.Add(id);
            }
            foreach (string id in readItems)
            {
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                string trimmed = id.Trim();
                if (seenIds.Add(trimmed))
                    toRead.Add(trimmed);
            }

            Console.WriteLine("GOP OPC-DA Okuyucu v0.3 — salt okunur");
            if (intervalSec > 0)
                Console.WriteLine(string.Format("Sürekli mod: her {0} sn", intervalSec));
            if (!string.IsNullOrEmpty(postUrl))
                Console.WriteLine("HTTPS POST: " + postUrl);
            Console.WriteLine("Yerel sunucular listeleniyor (OPCEnum)...");
            Console.WriteLine();

            List<OpcServerInfo> servers;
            try
            {
                servers = OpcDaClient.EnumerateLocalServers();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("OPC sunucu listesi alınamadı: " + ex.Message);
                Console.Error.WriteLine("OPC Foundation OPC DA çekirdek bileşenleri kurulu mu? (x86 ve x64)");
                Console.Error.WriteLine("Kurulumdan sonra yeniden başlatma gerekir.");
                AnnounceJson(WriteJson(null, null, null, filter, "liste_alinamadi", ex.Message, servers: null, tagsPath: tagsPath, cycle: 0, lastSuccess: null, postUrl: postUrl));
                return 1;
            }

            if (servers.Count == 0)
            {
                Console.WriteLine("Yerelde hiç OPC DA sunucusu görünmüyor.");
                Console.WriteLine("- OPC Foundation çekirdek bileşenleri kurulu mu?");
                Console.WriteLine("- zenOpcSrv /RegSrvD 32-bit zenon klasöründen yönetici olarak çalıştırıldı mı?");
                Console.WriteLine("- Bu oturum, Runtime'ın oturumu mu?");
                AnnounceJson(WriteJson(null, null, null, filter, "sunucu_yok", "Yerelde hiç OPC DA sunucusu görünmedi.", servers, tagsPath, 0, null, postUrl));
                return 1;
            }

            Console.WriteLine("Bulunan yerel OPC DA sunucuları:");
            for (int i = 0; i < servers.Count; i++)
            {
                Console.WriteLine(string.Format("  [{0}] {1}", i + 1, servers[i]));
            }
            Console.WriteLine();

            OpcServerInfo chosen = ChooseServer(servers, serverProgId);
            if (chosen == null)
            {
                AnnounceJson(WriteJson(null, null, null, filter, "sunucu_secilemedi", "Otomatik sunucu seçilemedi; --server ile ProgID ver.", servers, tagsPath, 0, null, postUrl));
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine("Bağlanılıyor: " + chosen.ProgId);
            Console.WriteLine("(zenon büyük projelerde değişkenleri yüklerken uzun sürebilir; gerekirse yeniden denenecek.)");
            Console.WriteLine();

            s_stopRequested = false;
            Console.CancelKeyPress += OnCancelKeyPress;

            using (var client = new OpcDaClient())
            {
                Exception lastConnectError = null;
                bool connected = false;
                for (int attempt = 1; attempt <= MaxConnectAttempts; attempt++)
                {
                    try
                    {
                        if (chosen.Clsid != Guid.Empty)
                            client.Connect(chosen.Clsid);
                        else
                            client.ConnectByProgId(chosen.ProgId);
                        connected = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastConnectError = ex;
                        Console.WriteLine(string.Format(
                            "Deneme {0}/{1}: bağlantı/başlatma henüz olmadı ({2})",
                            attempt, MaxConnectAttempts, ex.Message));
                        if (attempt < MaxConnectAttempts)
                        {
                            Console.WriteLine("Sunucu henüz değişkenleri yüklüyor olabilir — " + (RetryDelayMs / 1000) + " sn bekleniyor...");
                            Thread.Sleep(RetryDelayMs);
                        }
                    }
                }

                if (!connected)
                {
                    Console.Error.WriteLine("Sunucuya bağlanılamadı: " + (lastConnectError != null ? lastConnectError.Message : ""));
                    Console.Error.WriteLine("Kontrol et: aynı oturum/kullanıcı, OPC sunucusu kayıtlı mı, Runtime açık mı?");
                    AnnounceJson(WriteJson(chosen.ProgId, null, null, filter, "baglanti_yok", lastConnectError != null ? lastConnectError.Message : "Sunucuya bağlanılamadı.", servers, tagsPath, 0, null, postUrl));
                    return 1;
                }

                Console.WriteLine("Bağlantı kuruldu.");
                Console.WriteLine();

                List<string> browsed = null;
                string taramaYontemi = null;

                // Etiket dosyası / --read varsa tarama zorunlu değil (VOB browse desteklemiyor).
                if (toRead.Count == 0)
                {
                    Console.WriteLine(string.Format(
                        "DA2 tarama: {0}, DA3 tarama: {1}, ItemIO: {2}",
                        client.HasDa2Browser ? "var" : "yok",
                        client.HasDa3Browser ? "var" : "yok",
                        client.HasItemIo ? "var" : "yok"));

                    OpcBrowseOutcome browseOutcome = null;
                    Exception lastBrowseError = null;
                    int browseAttempts = client.HasAnyBrowser ? 2 : 1;
                    for (int attempt = 1; attempt <= browseAttempts; attempt++)
                    {
                        try
                        {
                            Console.WriteLine(string.Format(
                                "Adres alanı taranıyor{0}...",
                                string.IsNullOrEmpty(filter) ? "" : " — filtre: " + filter));
                            browseOutcome = client.BrowseBestEffort(filter, MaxBrowseCollect);
                            browsed = browseOutcome.ItemIds;
                            taramaYontemi = browseOutcome.Method;
                            lastBrowseError = null;
                            if (browsed != null && browsed.Count > 0)
                                break;
                            if (browseOutcome.InterfaceMissing)
                                break;
                            if (attempt < browseAttempts && !string.IsNullOrEmpty(browseOutcome.Error))
                            {
                                Console.WriteLine(string.Format(
                                    "Deneme {0}/{1}: {2}",
                                    attempt, browseAttempts, browseOutcome.Error));
                                Console.WriteLine("10 sn bekleniyor...");
                                Thread.Sleep(10000);
                                continue;
                            }
                            break;
                        }
                        catch (Exception ex)
                        {
                            lastBrowseError = ex;
                            Console.WriteLine(string.Format(
                                "Deneme {0}/{1}: tarama başarısız ({2})",
                                attempt, browseAttempts, ex.Message));
                            if (attempt < browseAttempts)
                            {
                                Console.WriteLine("10 sn bekleniyor...");
                                Thread.Sleep(10000);
                            }
                        }
                    }

                    if (browseOutcome != null && !string.IsNullOrEmpty(browseOutcome.Method))
                        Console.WriteLine("Tarama yöntemi: " + browseOutcome.Method);

                    if (browsed == null || browsed.Count == 0)
                    {
                        string reason = lastBrowseError != null
                            ? lastBrowseError.Message
                            : (browseOutcome != null ? browseOutcome.Error : "Etiket listesi alınamadı.");
                        Console.WriteLine("Tarama sonuç vermedi: " + reason);
                        Console.WriteLine("taglar.txt veya --read / --tags kullan.");
                        AnnounceJson(WriteJson(chosen.ProgId, browsed, new List<OpcReadResult>(), filter, "tarama_yok", reason, servers, tagsPath, 0, null, postUrl, taramaYontemi));
                        return 1;
                    }

                    int n = browsed.Count <= 80 ? browsed.Count : Math.Min(DefaultReadCount, browsed.Count);
                    for (int i = 0; i < n; i++)
                        toRead.Add(browsed[i]);
                    Console.WriteLine(string.Format("{0} değişken okunuyor (tarama önizlemesi)...", n));
                }
                else
                {
                    Console.WriteLine(string.Format(
                        "ItemIO: {0} — {1} etiket okunacak (dosya/CLI; tarama atlandı).",
                        client.HasItemIo ? "var" : "yok",
                        toRead.Count));
                    taramaYontemi = "atlandi_etiket_dosyasi";
                }

                string lastSuccess = null;
                int cycle = 0;
                int exitCode = 0;

                do
                {
                    cycle++;
                    if (intervalSec > 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine(string.Format("===== Döngü {0} @ {1} =====",
                            cycle,
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
                    }

                    List<OpcReadResult> results = null;
                    Exception lastReadError = null;
                    for (int attempt = 1; attempt <= 3; attempt++)
                    {
                        try
                        {
                            results = client.ReadItems(toRead);
                            lastReadError = null;
                            break;
                        }
                        catch (Exception ex)
                        {
                            lastReadError = ex;
                            Console.WriteLine(string.Format(
                                "Okuma denemesi {0}/3 başarısız ({1}: {2})",
                                attempt, ex.GetType().Name, ex.Message));
                            if (attempt < 3)
                                Thread.Sleep(RetryDelayMs);
                        }
                    }

                    if (lastReadError != null)
                    {
                        Console.Error.WriteLine("Okuma yapılamadı: " + lastReadError.Message);
                        string jsonFail = WriteJson(
                            chosen.ProgId, browsed, new List<OpcReadResult>(), filter,
                            "okuma_yok",
                            lastReadError.GetType().Name + ": " + lastReadError.Message,
                            servers, tagsPath, cycle, lastSuccess, postUrl, taramaYontemi, client.LastReadMethod);
                        AnnounceJson(jsonFail);
                        exitCode = 1;
                        if (intervalSec <= 0)
                            break;
                    }
                    else
                    {
                        int goodCount = CountGood(results);
                        string plantPower = FindValue(results, "OZYEL.POM01.Electrical.ActivePower");
                        double sumGrid;
                        int gridCount = SumGridPower(results, out sumGrid);

                        if (intervalSec <= 0)
                        {
                            Console.WriteLine("Okuma sonuçları (özet; ayrıntı JSON'da):");
                            Console.WriteLine("(Quality Bad: çoğu zaman bağlantı kopukluğu değil, değişkenin INVALID oluşudur.)");
                            Console.WriteLine();
                            int preview = Math.Min(40, results.Count);
                            for (int i = 0; i < preview; i++)
                            {
                                OpcReadResult r = results[i];
                                Console.WriteLine("  " + r.ItemId + " = " + r.ValueText + " [" + r.QualityText + "]");
                                if (!string.IsNullOrEmpty(r.ErrorText))
                                    Console.WriteLine("    hata: " + r.ErrorText);
                            }
                            if (results.Count > preview)
                                Console.WriteLine(string.Format("  ... +{0} satır daha (JSON)", results.Count - preview));
                            Console.WriteLine();
                        }

                        Console.WriteLine(string.Format(
                            "Özet: Good={0}/{1}, POM ActivePower={2}, Grid.Power toplamı={3} ({4} türbin)",
                            goodCount,
                            results.Count,
                            string.IsNullOrEmpty(plantPower) ? "-" : plantPower,
                            gridCount > 0 ? sumGrid.ToString("0.###", CultureInfo.InvariantCulture) : "-",
                            gridCount));
                        Console.WriteLine("Okuma yöntemi: " + (client.LastReadMethod ?? "-"));

                        if (goodCount > 0)
                            lastSuccess = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

                        string durum = goodCount > 0 ? "okundu" : "okundu_good_yok";
                        string jsonPath = WriteJson(
                            chosen.ProgId, browsed, results, filter, durum, null,
                            servers, tagsPath, cycle, lastSuccess, postUrl, taramaYontemi, client.LastReadMethod);
                        AnnounceJson(jsonPath);

                        if (!string.IsNullOrEmpty(postUrl))
                        {
                            try
                            {
                                PostJson(postUrl, File.ReadAllText(jsonPath, Encoding.UTF8));
                                Console.WriteLine("POST tamam: " + postUrl);
                            }
                            catch (Exception exPost)
                            {
                                Console.WriteLine("POST başarısız: " + exPost.Message);
                            }
                        }
                    }

                    if (intervalSec <= 0 || s_stopRequested)
                        break;

                    // Bekleme: Ctrl+C ile erken çıkış
                    int waited = 0;
                    int slice = 200;
                    int totalMs = intervalSec * 1000;
                    while (waited < totalMs && !s_stopRequested)
                    {
                        Thread.Sleep(slice);
                        waited += slice;
                    }
                }
                while (!s_stopRequested && intervalSec > 0);

                Console.WriteLine();
                Console.WriteLine("Not: Son OPC istemcisi kapanınca bazı sunucular kapanabilir — beklenen davranış olabilir.");
                Console.WriteLine("Bağlantı temiz kapatılıyor...");
                if (s_stopRequested)
                    Console.WriteLine("Ctrl+C ile durduruldu.");
                Console.WriteLine("Bitti.");
                return exitCode;
            }
        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            s_stopRequested = true;
            Console.WriteLine();
            Console.WriteLine("Durdurma istendi — döngü bitince çıkılacak...");
        }

        private static OpcServerInfo ChooseServer(List<OpcServerInfo> servers, string serverProgId)
        {
            if (!string.IsNullOrEmpty(serverProgId))
            {
                foreach (var s in servers)
                {
                    if (string.Equals(s.ProgId, serverProgId, StringComparison.OrdinalIgnoreCase))
                        return s;
                }
                Console.WriteLine("Uyarı: --server değeri listede yok, yine de bağlanmayı deneyeceğim: " + serverProgId);
                return new OpcServerInfo { ProgId = serverProgId, UserType = "(liste dışı)", Clsid = Guid.Empty };
            }

            var known = new List<OpcServerInfo>();
            var vestasMatches = new List<OpcServerInfo>();
            foreach (var s in servers)
            {
                if (IsKnownPlantServer(s))
                    known.Add(s);
                if (IsVestasServer(s))
                    vestasMatches.Add(s);
            }

            if (vestasMatches.Count == 1)
            {
                Console.WriteLine("Otomatik seçilen sunucu: " + vestasMatches[0].ProgId);
                return vestasMatches[0];
            }
            if (known.Count == 1)
            {
                Console.WriteLine("Otomatik seçilen sunucu: " + known[0].ProgId);
                return known[0];
            }
            if (known.Count == 0)
            {
                Console.WriteLine("Adında zenon/VESTAS geçen sunucu otomatik seçilemedi.");
                Console.WriteLine("Yukarıdaki listeden ProgID'yi alıp şöyle çalıştır:");
                Console.WriteLine("  GopOpcOkuyucu.exe --server \"ProgID\"");
                return null;
            }

            Console.WriteLine("Birden fazla uygun sunucu var. Hangisini kullanacağını belirt:");
            foreach (var s in known)
                Console.WriteLine("  " + s.ProgId);
            Console.WriteLine();
            Console.WriteLine("Örnek: GopOpcOkuyucu.exe --server \"" + known[0].ProgId + "\"");
            return null;
        }

        private static string ResolveTagsPath(string tagsFileArg)
        {
            if (!string.IsNullOrEmpty(tagsFileArg))
            {
                if (Path.IsPathRooted(tagsFileArg))
                    return tagsFileArg;
                return Path.Combine(AppBaseDir(), tagsFileArg);
            }
            return Path.Combine(AppBaseDir(), DefaultTagsFileName);
        }

        private static string AppBaseDir()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(dir))
                dir = Environment.CurrentDirectory;
            return dir;
        }

        private static List<string> LoadTagsFile(string path)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return list;

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line == null)
                    continue;
                line = line.Trim();
                if (line.Length == 0)
                    continue;
                if (line[0] == '#')
                    continue;
                // Satır içi yorum: "ID  # not"
                int hash = line.IndexOf('#');
                if (hash >= 0)
                    line = line.Substring(0, hash).Trim();
                if (line.Length == 0)
                    continue;
                list.Add(line);
            }
            return list;
        }

        private static int CountGood(List<OpcReadResult> results)
        {
            int n = 0;
            if (results == null)
                return 0;
            for (int i = 0; i < results.Count; i++)
            {
                string q = results[i].QualityText;
                if (q != null && q.IndexOf("Good", StringComparison.OrdinalIgnoreCase) >= 0)
                    n++;
            }
            return n;
        }

        private static string FindValue(List<OpcReadResult> results, string itemId)
        {
            if (results == null)
                return null;
            for (int i = 0; i < results.Count; i++)
            {
                if (string.Equals(results[i].ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                    return results[i].ValueText;
            }
            return null;
        }

        private static int SumGridPower(List<OpcReadResult> results, out double sum)
        {
            sum = 0;
            int count = 0;
            if (results == null)
                return 0;
            for (int i = 0; i < results.Count; i++)
            {
                OpcReadResult r = results[i];
                if (r.ItemId == null)
                    continue;
                // OZYEL.WTGnn.Grid.Power
                if (r.ItemId.IndexOf(".Grid.Power", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (r.ItemId.IndexOf(".WTG", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                double v;
                if (TryParseDouble(r.ValueText, out v))
                {
                    sum += v;
                    count++;
                }
            }
            return count;
        }

        private static bool TryParseDouble(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text))
                return false;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.GetCultureInfo("tr-TR"), out value);
        }

        private static void PostJson(string url, string jsonBody)
        {
            // TLS 1.2 (eski varsayılan yetersiz kalabilir)
            try
            {
                ServicePointManager.SecurityProtocol =
                    (SecurityProtocolType)3072 // Tls12
                    | (SecurityProtocolType)768  // Tls11
                    | SecurityProtocolType.Tls;
            }
            catch
            {
                // Bazı ortamlarda enum set edilemeyebilir; yoksay.
            }

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.Timeout = 30000;
            req.ReadWriteTimeout = 30000;
            byte[] body = Encoding.UTF8.GetBytes(jsonBody);
            req.ContentLength = body.Length;
            using (Stream s = req.GetRequestStream())
            {
                s.Write(body, 0, body.Length);
            }
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            {
                // gövdeyi okumaya gerek yok; durum yeterli
                int code = (int)resp.StatusCode;
                if (code < 200 || code >= 300)
                    throw new InvalidOperationException("HTTP " + code);
            }
        }

        private static void AnnounceJson(string jsonPath)
        {
            Console.WriteLine();
            Console.WriteLine("Yerel JSON yazıldı: " + jsonPath);
            Console.WriteLine("Dosya, exe ile aynı klasördedir (okuma-sonucu.json).");
        }

        private static string JsonFilePath()
        {
            return Path.Combine(AppBaseDir(), "okuma-sonucu.json");
        }

        private static bool IsVestasServer(OpcServerInfo s)
        {
            return ContainsIgnoreCase(s.ProgId, "vestas") || ContainsIgnoreCase(s.UserType, "vestas");
        }

        private static bool IsKnownPlantServer(OpcServerInfo s)
        {
            return ContainsIgnoreCase(s.ProgId, "zenon")
                || ContainsIgnoreCase(s.UserType, "zenon")
                || ContainsIgnoreCase(s.UserType, "copa-data")
                || ContainsIgnoreCase(s.UserType, "copadata")
                || ContainsIgnoreCase(s.ProgId, "copa")
                || IsVestasServer(s);
        }

        private static string WriteJson(
            string server,
            List<string> browsed,
            List<OpcReadResult> results,
            string filter,
            string durum,
            string hata,
            List<OpcServerInfo> servers,
            string tagsPath,
            int cycle,
            string lastSuccess,
            string postUrl,
            string taramaYontemi = null,
            string okumaYontemi = null)
        {
            string path = JsonFilePath();
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"aciklama\": \"Yerel salt-okunur OPC-DA okuma. Varsayılan olarak ağa gönderilmedi; yalnız --post verilirse HTTPS POST yapılır.\",");
            sb.AppendLine("  \"surum\": \"0.3.0.0\",");
            sb.Append("  \"durum\": ").Append(JsonString(durum)).AppendLine(",");
            sb.Append("  \"hata\": ").Append(JsonString(hata)).AppendLine(",");
            sb.Append("  \"sunucu\": ").Append(JsonString(server)).AppendLine(",");
            sb.Append("  \"etiketDosyasi\": ").Append(JsonString(tagsPath)).AppendLine(",");
            sb.Append("  \"dongu\": ").Append(cycle).AppendLine(",");
            sb.Append("  \"sonBasariliZaman\": ").Append(JsonString(lastSuccess)).AppendLine(",");
            sb.Append("  \"postUrl\": ").Append(JsonString(postUrl)).AppendLine(",");
            sb.Append("  \"taramaYontemi\": ").Append(JsonString(taramaYontemi)).AppendLine(",");
            sb.Append("  \"okumaYontemi\": ").Append(JsonString(okumaYontemi)).AppendLine(",");
            sb.Append("  \"zaman\": ").Append(JsonString(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).AppendLine(",");
            sb.Append("  \"filtre\": ").Append(JsonString(filter)).AppendLine(",");
            sb.AppendLine("  \"sunucular\": [");
            if (servers != null)
            {
                for (int i = 0; i < servers.Count; i++)
                {
                    var s = servers[i];
                    sb.Append("    {\"progId\": ").Append(JsonString(s.ProgId));
                    sb.Append(", \"ad\": ").Append(JsonString(s.UserType)).Append("}");
                    if (i < servers.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }
            }
            sb.AppendLine("  ],");
            sb.Append("  \"tarananAdet\": ").Append(browsed != null ? browsed.Count : 0).AppendLine(",");
            sb.AppendLine("  \"tarananOnizleme\": [");
            if (browsed != null)
            {
                int n = Math.Min(DefaultBrowsePreview, browsed.Count);
                for (int i = 0; i < n; i++)
                {
                    sb.Append("    ").Append(JsonString(browsed[i]));
                    if (i < n - 1) sb.Append(",");
                    sb.AppendLine();
                }
            }
            sb.AppendLine("  ],");
            int good = CountGood(results);
            sb.Append("  \"okunanAdet\": ").Append(results != null ? results.Count : 0).AppendLine(",");
            sb.Append("  \"goodAdet\": ").Append(good).AppendLine(",");
            sb.AppendLine("  \"okunanlar\": [");
            if (results != null)
            {
                for (int i = 0; i < results.Count; i++)
                {
                    var r = results[i];
                    sb.AppendLine("    {");
                    sb.Append("      \"itemId\": ").Append(JsonString(r.ItemId)).AppendLine(",");
                    sb.Append("      \"proje\": ").Append(JsonString(r.Project)).AppendLine(",");
                    sb.Append("      \"deger\": ").Append(JsonString(r.ValueText)).AppendLine(",");
                    sb.Append("      \"kalite\": ").Append(JsonString(r.QualityText)).AppendLine(",");
                    sb.Append("      \"kaliteHam\": ").Append(r.QualityRaw).AppendLine(",");
                    sb.Append("      \"zaman\": ").Append(JsonString(r.TimestampText)).AppendLine(",");
                    sb.Append("      \"hata\": ").Append(JsonString(r.ErrorText)).AppendLine();
                    sb.Append("    }");
                    if (i < results.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        private static string JsonString(string s)
        {
            if (s == null)
                return "null";
            var sb = new StringBuilder();
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static bool ContainsIgnoreCase(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
                return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Kullanım (v0.3):");
            Console.WriteLine("  GopOpcOkuyucu.exe");
            Console.WriteLine("  GopOpcOkuyucu.exe --tags taglar.txt");
            Console.WriteLine("  GopOpcOkuyucu.exe --tags taglar.txt --interval 10");
            Console.WriteLine("  GopOpcOkuyucu.exe --tags taglar.txt --interval 10 --post https://ornek.invalid/ingest");
            Console.WriteLine("  GopOpcOkuyucu.exe --server \"ProgID\"");
            Console.WriteLine("  GopOpcOkuyucu.exe --read OZYEL.WTG01.Grid.Power");
            Console.WriteLine("  GopOpcOkuyucu.exe --filter T07");
            Console.WriteLine();
            Console.WriteLine("  --tags     Etiket dosyası (UTF-8, satır başına bir ItemID, # yorum). Varsayılan: exe yanındaki taglar.txt");
            Console.WriteLine("  --read     Ek tek etiket (birden fazla kez verilebilir)");
            Console.WriteLine("  --interval Saniye; >0 ise sürekli oku ve okuma-sonucu.json üzerine yaz. Ctrl+C ile çık.");
            Console.WriteLine("  --post     İsteğe bağlı HTTPS POST URL (varsayılan kapalı; endpoint uydurulmaz)");
            Console.WriteLine();
            Console.WriteLine("Salt okunur. Setpoint / yazma yok. Yalnızca yerel OPC-DA.");
        }
    }
}
