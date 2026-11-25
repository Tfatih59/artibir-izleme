using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace GopOpcOkuyucu
{
    public sealed class OpcServerInfo
    {
        public Guid Clsid { get; set; }
        public string ProgId { get; set; }
        public string UserType { get; set; }

        public override string ToString()
        {
            return string.Format("{0}  ({1})", ProgId ?? "(ProgID yok)", UserType ?? "");
        }
    }

    public sealed class OpcReadResult
    {
        public string ItemId { get; set; }
        public string ValueText { get; set; }
        public string QualityText { get; set; }
        public short QualityRaw { get; set; }
        public string TimestampText { get; set; }
        public int ErrorCode { get; set; }
        public string ErrorText { get; set; }
        public string Project { get; set; }
    }

    public sealed partial class OpcDaClient : IDisposable
    {
        private object _serverObj;
        private IOPCServer _server;
        private IOPCBrowseServerAddressSpace _browser;
        private IOPCBrowse _browser3;
        private IOPCItemIO _itemIo;
        private string _lastReadMethod;
        private readonly List<GroupHandle> _groups = new List<GroupHandle>();
        private bool _disposed;

        private sealed class GroupHandle
        {
            public string Project;
            public int ServerHandle;
            public object GroupObj;
            public IOPCItemMgt ItemMgt;
            public IOPCSyncIO SyncIo;
            public IOPCSyncIO2 SyncIo2;
            public readonly Dictionary<string, int> ItemServerHandles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<int, string> ClientToItem = new Dictionary<int, string>();
            public int NextClientHandle = 1;
        }

        public static List<OpcServerInfo> EnumerateLocalServers()
        {
            var list = new List<OpcServerInfo>();
            object enumObj = null;
            IOPCServerList serverList = null;
            try
            {
                enumObj = new OpcServerList();
                serverList = (IOPCServerList)enumObj;

                Guid[] cats = new Guid[]
                {
                    OpcGuids.OPCDAServer10,
                    OpcGuids.OPCDAServer20,
                    OpcGuids.OPCDAServer30
                };

                IEnumGUID enumGuid;
                try
                {
                    serverList.EnumClassesOfCategories(cats.Length, cats, 0, null, out enumGuid);
                }
                catch
                {
                    // Bazı sistemlerde 3.0 kategorisi yok; 1.0 + 2.0 dene.
                    cats = new Guid[] { OpcGuids.OPCDAServer10, OpcGuids.OPCDAServer20 };
                    serverList.EnumClassesOfCategories(cats.Length, cats, 0, null, out enumGuid);
                }

                if (enumGuid == null)
                    return list;

                var seen = new HashSet<Guid>();
                Guid[] buffer = new Guid[16];
                int fetched;
                while (true)
                {
                    enumGuid.Next(buffer.Length, buffer, out fetched);
                    if (fetched <= 0)
                        break;

                    for (int i = 0; i < fetched; i++)
                    {
                        Guid clsid = buffer[i];
                        if (!seen.Add(clsid))
                            continue;

                        string progId = null;
                        string userType = null;
                        try
                        {
                            serverList.GetClassDetails(ref clsid, out progId, out userType);
                        }
                        catch
                        {
                            progId = clsid.ToString("B");
                            userType = "";
                        }

                        list.Add(new OpcServerInfo
                        {
                            Clsid = clsid,
                            ProgId = progId,
                            UserType = userType
                        });
                    }
                }

                try { Marshal.ReleaseComObject(enumGuid); } catch { }
            }
            finally
            {
                if (serverList != null)
                {
                    try { Marshal.ReleaseComObject(serverList); } catch { }
                }
                else if (enumObj != null)
                {
                    try { Marshal.ReleaseComObject(enumObj); } catch { }
                }
            }

            list.Sort((a, b) => string.Compare(a.ProgId, b.ProgId, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public void Connect(Guid clsid)
        {
            DisconnectInternal();
            Type t = Type.GetTypeFromCLSID(clsid, true);
            _serverObj = Activator.CreateInstance(t);
            _server = (IOPCServer)_serverObj;
            AttachBrowsers();
        }

        public void ConnectByProgId(string progId)
        {
            DisconnectInternal();
            Type t = Type.GetTypeFromProgID(progId, true);
            _serverObj = Activator.CreateInstance(t);
            _server = (IOPCServer)_serverObj;
            AttachBrowsers();
        }

        private void AttachBrowsers()
        {
            _browser = null;
            _browser3 = null;
            try { _browser = QueryCom<IOPCBrowseServerAddressSpace>(_serverObj); }
            catch { _browser = null; }
            try { _browser3 = QueryCom<IOPCBrowse>(_serverObj); }
            catch { _browser3 = null; }
            try { _itemIo = QueryCom<IOPCItemIO>(_serverObj); }
            catch { _itemIo = null; }
        }

        public bool HasDa2Browser { get { return _browser != null; } }
        public bool HasDa3Browser { get { return _browser3 != null; } }
        public bool HasItemIo { get { return _itemIo != null; } }
        public string LastReadMethod { get { return _lastReadMethod; } }
        public bool HasAnyBrowser { get { return _browser != null || _browser3 != null; } }

        public List<string> BrowseFlat(string filterSubstring, int maxCollect, out bool truncated)
        {
            truncated = false;
            var result = new List<string>();
            if (_browser == null)
                throw new InvalidOperationException("Sunucu adres alanı tarama (browse) desteklemiyor veya henüz hazır değil.");

            OPCNAMESPACETYPE ns;
            try
            {
                _browser.QueryOrganization(out ns);
            }
            catch
            {
                ns = OPCNAMESPACETYPE.OPC_NS_FLAT;
            }

            // zenon OPC DA düz (flat) adres alanı kullanır.
            IEnumString enumString;
            string criteria = string.IsNullOrEmpty(filterSubstring) ? "" : "";
            _browser.BrowseOPCItemIDs(OPCBROWSETYPE.OPC_FLAT, criteria, 0, 0, out enumString);

            if (enumString == null)
                return result;

            try
            {
                string[] buf = new string[64];
                while (true)
                {
                    int fetched;
                    int hr = enumString.Next(buf.Length, buf, out fetched);
                    if (fetched <= 0)
                        break;

                    for (int i = 0; i < fetched; i++)
                    {
                        string id = buf[i];
                        if (string.IsNullOrEmpty(id))
                            continue;

                        string fullId = id;
                        try
                        {
                            string resolved;
                            _browser.GetItemID(id, out resolved);
                            if (!string.IsNullOrEmpty(resolved))
                                fullId = resolved;
                        }
                        catch
                        {
                            // GetItemID başarısız olursa ham id kullan.
                        }

                        if (!string.IsNullOrEmpty(filterSubstring) &&
                            fullId.IndexOf(filterSubstring, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        result.Add(fullId);
                        if (result.Count >= maxCollect)
                        {
                            truncated = true;
                            return result;
                        }
                    }

                    if (hr != 0) // S_FALSE = 1 typically when fewer returned
                    {
                        // Continue until fetched == 0
                    }
                }
            }
            finally
            {
                try { Marshal.ReleaseComObject(enumString); } catch { }
            }

            return result;
        }

        // Tek IOPCItemIO çağrısında ~1000 etiket bazı sunucularda düşer; 60'lık dilimler.
        private const int ItemIoBatchSize = 60;

        public List<OpcReadResult> ReadItems(IList<string> itemIds)
        {
            if (_server == null)
                throw new InvalidOperationException("Önce sunucuya bağlan.");

            var orderedUnique = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in itemIds)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                string id = raw.Trim();
                if (!seen.Add(id))
                    continue;
                orderedUnique.Add(id);
            }

            if (_itemIo != null)
            {
                try
                {
                    List<OpcReadResult> viaIo;
                    if (orderedUnique.Count > ItemIoBatchSize)
                    {
                        viaIo = ReadViaItemIoBatched(_itemIo, orderedUnique);
                        _lastReadMethod = "IOPCItemIO_batch";
                    }
                    else
                    {
                        viaIo = ReadViaItemIo(_itemIo, orderedUnique);
                        _lastReadMethod = "IOPCItemIO";
                    }
                    return viaIo;
                }
                catch (Exception exIo)
                {
                    if (orderedUnique.Count > 1)
                    {
                        try
                        {
                            List<OpcReadResult> singles = ReadViaItemIoOneByOne(_itemIo, orderedUnique);
                            bool anyCallOk = false;
                            foreach (OpcReadResult r in singles)
                            {
                                if (r.QualityText != "IOPCItemIO tekil hata")
                                {
                                    anyCallOk = true;
                                    break;
                                }
                            }
                            if (anyCallOk)
                            {
                                _lastReadMethod = "IOPCItemIO_tekil";
                                return singles;
                            }
                            throw new InvalidOperationException("IOPCItemIO tekil cagrilarin hepsi basarisiz.");
                        }
                        catch (Exception exOne)
                        {
                            _lastReadMethod = "IOPCItemIO_basarisiz";
                            // Grup yoluna düş; her iki hatayı da taşı.
                            try
                            {
                                List<OpcReadResult> viaGroup = ReadViaGroups(orderedUnique);
                                _lastReadMethod = "grup_ItemIO_sonra";
                                return viaGroup;
                            }
                            catch (Exception exGroup)
                            {
                                throw new InvalidOperationException(
                                    "IOPCItemIO: " + exIo.Message + " / tekil: " + exOne.Message + " / grup: " + exGroup.Message);
                            }
                        }
                    }
                    try
                    {
                        List<OpcReadResult> viaGroup = ReadViaGroups(orderedUnique);
                        _lastReadMethod = "grup_ItemIO_sonra";
                        return viaGroup;
                    }
                    catch (Exception exGroup)
                    {
                        throw new InvalidOperationException(
                            "IOPCItemIO: " + exIo.Message + " / grup: " + exGroup.Message);
                    }
                }
            }

            _lastReadMethod = "grup";
            return ReadViaGroups(orderedUnique);
        }

        private List<OpcReadResult> ReadViaGroups(List<string> orderedUnique)
        {
            // COPA-DATA: her zenon projesi için ayrı OPC grubu; aynı item iki kez eklenmez.
            var byProject = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in orderedUnique)
            {
                string project = GetProjectPrefix(id);
                List<string> bucket;
                if (!byProject.TryGetValue(project, out bucket))
                {
                    bucket = new List<string>();
                    byProject[project] = bucket;
                }
                bucket.Add(id);
            }

            var results = new List<OpcReadResult>();
            foreach (var kv in byProject)
            {
                GroupHandle group = EnsureGroup(kv.Key);
                results.AddRange(AddAndRead(group, kv.Value));
            }

            // Giriş sırasını koru
            var map = new Dictionary<string, OpcReadResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in results)
                map[r.ItemId] = r;

            var ordered = new List<OpcReadResult>();
            foreach (string id in orderedUnique)
            {
                OpcReadResult r;
                if (map.TryGetValue(id, out r))
                    ordered.Add(r);
            }
            return ordered;
        }

        private GroupHandle EnsureGroup(string project)
        {
            foreach (var g in _groups)
            {
                if (string.Equals(g.Project, project, StringComparison.OrdinalIgnoreCase))
                    return g;
            }

            string groupName = "GopOkuma_" + SanitizeName(project);
            int hServerGroup = 0;
            int revisedRate = 0;
            object groupObj = null;
            Exception addErr = null;

            Guid iidUnknown = new Guid("00000000-0000-0000-C000-000000000046");
            try
            {
                _server.AddGroup(
                    groupName,
                    true,
                    1000,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0,
                    out hServerGroup,
                    out revisedRate,
                    ref iidUnknown,
                    out groupObj);
            }
            catch (Exception ex)
            {
                addErr = ex;
                groupObj = null;
            }

            if (groupObj == null)
            {
                Guid iidItemMgt = typeof(IOPCItemMgt).GUID;
                try
                {
                    _server.AddGroup(
                        groupName + "B",
                        true,
                        1000,
                        0,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        0,
                        out hServerGroup,
                        out revisedRate,
                        ref iidItemMgt,
                        out groupObj);
                }
                catch (Exception ex2)
                {
                    string first = addErr != null ? addErr.Message : "bos";
                    throw new InvalidOperationException("AddGroup basarisiz: " + first + " / " + ex2.Message);
                }
            }

            if (groupObj == null)
                throw new InvalidOperationException("AddGroup bos grup dondurdu.");

            IOPCItemMgt itemMgt = QueryCom<IOPCItemMgt>(groupObj);
            if (itemMgt == null)
                throw new InvalidOperationException("OPC grubu IOPCItemMgt arayuzunu vermedi.");

            IOPCSyncIO syncIo = QueryCom<IOPCSyncIO>(groupObj);
            IOPCSyncIO2 syncIo2 = QueryCom<IOPCSyncIO2>(groupObj);
            if (syncIo == null && syncIo2 == null)
                throw new InvalidOperationException("OPC grubu IOPCSyncIO / IOPCSyncIO2 vermedi.");

            var handle = new GroupHandle
            {
                Project = project,
                ServerHandle = hServerGroup,
                GroupObj = groupObj,
                ItemMgt = itemMgt,
                SyncIo = syncIo,
                SyncIo2 = syncIo2
            };
            _groups.Add(handle);
            return handle;
        }

        private List<OpcReadResult> AddAndRead(GroupHandle group, List<string> itemIds)
        {
            var toAdd = new List<string>();
            var already = new List<string>();
            foreach (string id in itemIds)
            {
                if (group.ItemServerHandles.ContainsKey(id))
                    already.Add(id);
                else
                    toAdd.Add(id);
            }

            if (toAdd.Count > 0)
            {
                var defs = new OPCITEMDEF[toAdd.Count];
                for (int i = 0; i < toAdd.Count; i++)
                {
                    int client = group.NextClientHandle++;
                    defs[i] = new OPCITEMDEF
                    {
                        szAccessPath = "",
                        szItemID = toAdd[i],
                        bActive = 1,
                        hClient = client,
                        dwBlobSize = 0,
                        pBlob = IntPtr.Zero,
                        vtRequestedDataType = (short)VarEnum.VT_EMPTY,
                        wReserved = 0
                    };
                    group.ClientToItem[client] = toAdd[i];
                }

                IntPtr pResults;
                IntPtr pErrors;
                group.ItemMgt.AddItems(defs.Length, defs, out pResults, out pErrors);
                try
                {
                    int resultSize = Marshal.SizeOf(typeof(OPCITEMRESULT));
                    for (int i = 0; i < defs.Length; i++)
                    {
                        int err = Marshal.ReadInt32(pErrors, i * sizeof(int));
                        IntPtr resultPtr = new IntPtr(pResults.ToInt64() + i * resultSize);
                        var itemResult = (OPCITEMRESULT)Marshal.PtrToStructure(resultPtr, typeof(OPCITEMRESULT));
                        if (err >= 0 && itemResult.hServer != 0)
                        {
                            group.ItemServerHandles[toAdd[i]] = itemResult.hServer;
                        }
                        else
                        {
                            // Add başarısız — okumada hata olarak dönecek
                            group.ItemServerHandles[toAdd[i]] = 0;
                            group.ClientToItem[defs[i].hClient] = toAdd[i];
                        }

                        if (itemResult.pBlob != IntPtr.Zero)
                        {
                            try { Marshal.FreeCoTaskMem(itemResult.pBlob); } catch { }
                        }
                    }
                }
                finally
                {
                    if (pResults != IntPtr.Zero) Marshal.FreeCoTaskMem(pResults);
                    if (pErrors != IntPtr.Zero) Marshal.FreeCoTaskMem(pErrors);
                }
            }

            var all = new List<string>();
            all.AddRange(already);
            all.AddRange(toAdd);

            var serverHandles = new List<int>();
            var clientOrder = new List<string>();
            var failedAdd = new List<OpcReadResult>();

            foreach (string id in all)
            {
                int h;
                if (!group.ItemServerHandles.TryGetValue(id, out h) || h == 0)
                {
                    failedAdd.Add(new OpcReadResult
                    {
                        ItemId = id,
                        Project = group.Project,
                        ValueText = "",
                        QualityText = "AddItems başarısız",
                        QualityRaw = 0,
                        TimestampText = "",
                        ErrorCode = -1,
                        ErrorText = "Değişken OPC grubuna eklenemedi (yanlış ad veya henüz hazır değil)."
                    });
                    continue;
                }
                serverHandles.Add(h);
                clientOrder.Add(id);
            }

            var results = new List<OpcReadResult>();
            results.AddRange(failedAdd);

            if (serverHandles.Count == 0)
                return results;

            IntPtr pValues;
            IntPtr pReadErrors;
            if (group.SyncIo != null)
            {
                group.SyncIo.Read(
                    OPCDATASOURCE.OPC_DS_CACHE,
                    serverHandles.Count,
                    serverHandles.ToArray(),
                    out pValues,
                    out pReadErrors);
            }
            else
            {
                group.SyncIo2.Read(
                    OPCDATASOURCE.OPC_DS_CACHE,
                    serverHandles.Count,
                    serverHandles.ToArray(),
                    out pValues,
                    out pReadErrors);
            }

            try
            {
                // OPCITEMSTATE marshalling: vDataValue is VARIANT — use careful offset reading.
                // Structure layout with Pack=4: hClient(4) + FILETIME(8) + quality(2) + reserved(2) + VARIANT(16 on x86)
                // Safer approach: use custom unmarshaller for VARIANT fields.
                int stateSize = GetOpcItemStateSize();
                for (int i = 0; i < serverHandles.Count; i++)
                {
                    int err = Marshal.ReadInt32(pReadErrors, i * sizeof(int));
                    IntPtr statePtr = new IntPtr(pValues.ToInt64() + i * stateSize);
                    int hClient = Marshal.ReadInt32(statePtr, 0);
                    uint ftLow = (uint)Marshal.ReadInt32(statePtr, 4);
                    uint ftHigh = (uint)Marshal.ReadInt32(statePtr, 8);
                    short quality = Marshal.ReadInt16(statePtr, 12);

                    IntPtr variantPtr = new IntPtr(statePtr.ToInt64() + 16);
                    object value = null;
                    string valueText = "";
                    try
                    {
                        value = Marshal.GetObjectForNativeVariant(variantPtr);
                        valueText = FormatValue(value);
                    }
                    catch (Exception ex)
                    {
                        valueText = "(değer okunamadı: " + ex.Message + ")";
                    }
                    finally
                    {
                        try { NativeMethods.VariantClear(variantPtr); } catch { }
                    }

                    string itemId = clientOrder[i];
                    string itemFromClient;
                    if (group.ClientToItem.TryGetValue(hClient, out itemFromClient))
                        itemId = itemFromClient;

                    results.Add(new OpcReadResult
                    {
                        ItemId = itemId,
                        Project = group.Project,
                        ValueText = valueText,
                        QualityRaw = quality,
                        QualityText = FormatQuality(quality),
                        TimestampText = FormatFileTime(ftLow, ftHigh),
                        ErrorCode = err,
                        ErrorText = err < 0 ? string.Format("HRESULT 0x{0:X8}", err) : ""
                    });
                }
            }
            finally
            {
                if (pValues != IntPtr.Zero) Marshal.FreeCoTaskMem(pValues);
                if (pReadErrors != IntPtr.Zero) Marshal.FreeCoTaskMem(pReadErrors);
            }

            return results;
        }

        private static int GetOpcItemStateSize()
        {
            // OPCITEMSTATE: INT + FILETIME(8) + WORD + WORD + VARIANT
            // VARIANT is 16 bytes on both x86 and x64 for classic OPC.
            // Align to 4: total typically 32 on x86.
            return 4 + 8 + 2 + 2 + 16; // 32
        }

        public static string GetProjectPrefix(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return "_";
            int dot = itemId.IndexOf('.');
            if (dot <= 0)
                return itemId;
            return itemId.Substring(0, dot);
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "Proje";
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            string s = sb.ToString();
            if (s.Length > 40)
                s = s.Substring(0, 40);
            return s;
        }

        public static string FormatQuality(short quality)
        {
            int q = quality & 0xC0;
            string master;
            switch (q)
            {
                case 0xC0: master = "Good"; break;
                case 0x40: master = "Uncertain"; break;
                default: master = "Bad"; break;
            }

            string detail = "";
            if (master == "Bad")
            {
                int sub = (quality & 0x3C) >> 2;
                // 0x00 = non-specific, 0x01 = config error, 0x04 = sensor failure, etc.
                // zenon: Bad sıkça değişkenin INVALID olduğunu gösterir.
                detail = " — değişken INVALID olabilir; bağlantı kopukluğu demek zorunda değil";
            }

            return string.Format("{0} (0x{1:X4}){2}", master, (ushort)quality, detail);
        }

        private static string FormatValue(object value)
        {
            if (value == null || value == DBNull.Value)
                return "(null)";
            if (value is Array)
            {
                var arr = (Array)value;
                var parts = new List<string>();
                int n = Math.Min(arr.Length, 8);
                for (int i = 0; i < n; i++)
                    parts.Add(Convert.ToString(arr.GetValue(i), CultureInfo.InvariantCulture));
                if (arr.Length > 8)
                    parts.Add("...");
                return "[" + string.Join(", ", parts.ToArray()) + "]";
            }
            if (value is DateTime)
                return ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            if (value is bool)
                return ((bool)value) ? "true" : "false";
            if (value is double || value is float || value is decimal)
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        private static string FormatFileTime(uint low, uint high)
        {
            try
            {
                long fileTime = ((long)high << 32) | low;
                if (fileTime <= 0)
                    return "";
                DateTime dt = DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
                return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            catch
            {
                return "";
            }
        }

        private static T QueryCom<T>(object comObj) where T : class
        {
            if (comObj == null)
                return null;
            T typed = comObj as T;
            if (typed != null)
                return typed;
            IntPtr unk = IntPtr.Zero;
            IntPtr p = IntPtr.Zero;
            try
            {
                unk = Marshal.GetIUnknownForObject(comObj);
                Guid iid = typeof(T).GUID;
                int hr = Marshal.QueryInterface(unk, ref iid, out p);
                if (hr < 0 || p == IntPtr.Zero)
                    return null;
                return Marshal.GetObjectForIUnknown(p) as T;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (p != IntPtr.Zero)
                {
                    try { Marshal.Release(p); } catch { }
                }
                if (unk != IntPtr.Zero)
                {
                    try { Marshal.Release(unk); } catch { }
                }
            }
        }

        private List<OpcReadResult> ReadViaItemIoBatched(IOPCItemIO itemIo, List<string> itemIds)
        {
            var all = new List<OpcReadResult>();
            int batchSize = ItemIoBatchSize;
            for (int offset = 0; offset < itemIds.Count; offset += batchSize)
            {
                int n = Math.Min(batchSize, itemIds.Count - offset);
                var chunk = new List<string>(n);
                for (int i = 0; i < n; i++)
                    chunk.Add(itemIds[offset + i]);
                try
                {
                    all.AddRange(ReadViaItemIo(itemIo, chunk));
                }
                catch (Exception ex)
                {
                    // Dilim düşerse tek tek dene; yine olmazsa satır satır hata yaz.
                    try
                    {
                        all.AddRange(ReadViaItemIoOneByOne(itemIo, chunk));
                    }
                    catch (Exception)
                    {
                        for (int i = 0; i < chunk.Count; i++)
                        {
                            string id = chunk[i];
                            all.Add(new OpcReadResult
                            {
                                ItemId = id,
                                Project = GetProjectPrefix(id),
                                ValueText = "",
                                QualityText = "IOPCItemIO batch hata",
                                QualityRaw = 0,
                                TimestampText = "",
                                ErrorCode = -1,
                                ErrorText = ex.Message
                            });
                        }
                    }
                }
            }
            return all;
        }

        private List<OpcReadResult> ReadViaItemIoOneByOne(IOPCItemIO itemIo, List<string> itemIds)
        {
            var results = new List<OpcReadResult>();
            for (int i = 0; i < itemIds.Count; i++)
            {
                string id = itemIds[i];
                try
                {
                    List<string> one = new List<string>();
                    one.Add(id);
                    results.AddRange(ReadViaItemIo(itemIo, one));
                }
                catch (Exception ex)
                {
                    results.Add(new OpcReadResult
                    {
                        ItemId = id,
                        Project = GetProjectPrefix(id),
                        ValueText = "",
                        QualityText = "IOPCItemIO tekil hata",
                        QualityRaw = 0,
                        TimestampText = "",
                        ErrorCode = -1,
                        ErrorText = ex.Message
                    });
                }
            }
            return results;
        }

        private List<OpcReadResult> ReadViaItemIo(IOPCItemIO itemIo, List<string> itemIds)
        {
            int count = itemIds.Count;
            if (count == 0)
                return new List<OpcReadResult>();

            string[] ids = itemIds.ToArray();
            int[] maxAge = new int[count];
            for (int i = 0; i < count; i++)
                maxAge[i] = -1; // 0xFFFFFFFF = cache

            IntPtr pValues = IntPtr.Zero;
            IntPtr pQuals = IntPtr.Zero;
            IntPtr pTimes = IntPtr.Zero;
            IntPtr pErrors = IntPtr.Zero;
            itemIo.Read(count, ids, maxAge, out pValues, out pQuals, out pTimes, out pErrors);

            var results = new List<OpcReadResult>();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    int err = 0;
                    if (pErrors != IntPtr.Zero)
                        err = Marshal.ReadInt32(pErrors, i * 4);

                    short quality = 0;
                    if (pQuals != IntPtr.Zero)
                        quality = Marshal.ReadInt16(pQuals, i * 2);

                    uint ftLow = 0;
                    uint ftHigh = 0;
                    if (pTimes != IntPtr.Zero)
                    {
                        ftLow = (uint)Marshal.ReadInt32(pTimes, i * 8);
                        ftHigh = (uint)Marshal.ReadInt32(pTimes, i * 8 + 4);
                    }

                    string valueText = "";
                    if (pValues != IntPtr.Zero)
                    {
                        IntPtr variantPtr = new IntPtr(pValues.ToInt64() + ((long)i * 16));
                        try
                        {
                            object value = Marshal.GetObjectForNativeVariant(variantPtr);
                            valueText = FormatValue(value);
                        }
                        catch (Exception ex)
                        {
                            valueText = "(deger okunamadi: " + ex.Message + ")";
                        }
                        finally
                        {
                            try { NativeMethods.VariantClear(variantPtr); } catch { }
                        }
                    }

                    results.Add(new OpcReadResult
                    {
                        ItemId = itemIds[i],
                        Project = GetProjectPrefix(itemIds[i]),
                        ValueText = valueText,
                        QualityRaw = quality,
                        QualityText = FormatQuality(quality),
                        TimestampText = FormatFileTime(ftLow, ftHigh),
                        ErrorCode = err,
                        ErrorText = err < 0 ? string.Format("HRESULT 0x{0:X8}", err) : ""
                    });
                }
            }
            finally
            {
                if (pValues != IntPtr.Zero) Marshal.FreeCoTaskMem(pValues);
                if (pQuals != IntPtr.Zero) Marshal.FreeCoTaskMem(pQuals);
                if (pTimes != IntPtr.Zero) Marshal.FreeCoTaskMem(pTimes);
                if (pErrors != IntPtr.Zero) Marshal.FreeCoTaskMem(pErrors);
            }
            return results;
        }

        private void DisconnectInternal()
        {
            foreach (var g in _groups)
            {
                try
                {
                    if (_server != null && g.ServerHandle != 0)
                        _server.RemoveGroup(g.ServerHandle, true);
                }
                catch { }

                try
                {
                    if (g.GroupObj != null)
                        Marshal.ReleaseComObject(g.GroupObj);
                }
                catch { }
            }
            _groups.Clear();

            _browser = null;
            _browser3 = null;
            _itemIo = null;
            _server = null;
            if (_serverObj != null)
            {
                try { Marshal.ReleaseComObject(_serverObj); } catch { }
                _serverObj = null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            DisconnectInternal();
            GC.SuppressFinalize(this);
        }
    }

    internal static class NativeMethods
    {
        [DllImport("oleaut32.dll", PreserveSig = true)]
        public static extern int VariantClear(IntPtr pvarg);
    }
}
