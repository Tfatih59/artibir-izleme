using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace GopOpcOkuyucu
{
    public sealed class OpcBrowseOutcome
    {
        public List<string> ItemIds = new List<string>();
        public bool Truncated;
        public string Method;
        public string Error;
        public bool InterfaceMissing;
    }

    public sealed partial class OpcDaClient
    {
        private sealed class Da3Element
        {
            public string Name;
            public string ItemId;
            public int Flags;
        }

        private sealed class TurbineScheme
        {
            public string Prefix;
            public string Kind;
            public string Signal;
        }

        public OpcBrowseOutcome BrowseBestEffort(string filterSubstring, int maxCollect)
        {
            var outcome = new OpcBrowseOutcome();

            if (_browser != null)
            {
                try
                {
                    bool truncated;
                    List<string> items = BrowseHierarchical(filterSubstring, maxCollect, out truncated);
                    if (items.Count > 0)
                    {
                        outcome.ItemIds = items;
                        outcome.Truncated = truncated;
                        outcome.Method = "hiyerarsik";
                        return outcome;
                    }
                }
                catch (Exception ex)
                {
                    outcome.Error = ex.Message;
                }

                try
                {
                    bool truncated;
                    List<string> items = BrowseFlat(filterSubstring, maxCollect, out truncated);
                    if (items.Count > 0)
                    {
                        outcome.ItemIds = items;
                        outcome.Truncated = truncated;
                        outcome.Method = "duz";
                        return outcome;
                    }
                }
                catch (Exception ex)
                {
                    if (string.IsNullOrEmpty(outcome.Error))
                        outcome.Error = ex.Message;
                    else
                        outcome.Error = outcome.Error + "; " + ex.Message;
                }
            }

            if (_browser3 != null)
            {
                try
                {
                    bool truncated;
                    List<string> items = BrowseDa3(filterSubstring, maxCollect, out truncated);
                    if (items.Count > 0)
                    {
                        outcome.ItemIds = items;
                        outcome.Truncated = truncated;
                        outcome.Method = "da3";
                        return outcome;
                    }
                }
                catch (Exception ex)
                {
                    if (string.IsNullOrEmpty(outcome.Error))
                        outcome.Error = ex.Message;
                    else
                        outcome.Error = outcome.Error + "; " + ex.Message;
                }
            }

            if (_browser == null && _browser3 == null)
            {
                outcome.InterfaceMissing = true;
                outcome.Error = "Sunucu DA2/DA3 tarama arayuzu vermedi; bilinen Vestas etiketleri denenecek.";
            }

            try
            {
                List<string> ozyel = BuildOzyelKnownIds();
                if (!string.IsNullOrEmpty(filterSubstring))
                    ozyel = FilterIds(ozyel, filterSubstring);
                Console.WriteLine(string.Format("OZYEL etiketleri deneniyor ({0} adet)...", ozyel.Count));
                List<string> valid = ValidateItemIds(ozyel);
                if (valid.Count > 0)
                {
                    List<string> extras = ExpandValidatedPatterns(valid);
                    extras = SubtractIds(extras, valid);
                    if (!string.IsNullOrEmpty(filterSubstring))
                        extras = FilterIds(extras, filterSubstring);
                    if (extras.Count > 0)
                    {
                        Console.WriteLine(string.Format("Bulunan kaliba gore {0} ek etiket dogrulaniyor...", extras.Count));
                        List<string> extraValid = ValidateItemIds(extras);
                        foreach (string id in extraValid)
                            valid.Add(id);
                    }
                    outcome.ItemIds = UniquePreserve(valid);
                    outcome.Method = "bilinen_etiket";
                    outcome.Truncated = false;
                    return outcome;
                }

                List<string> probes = BuildVestasProbeIds();
                if (!string.IsNullOrEmpty(filterSubstring))
                    probes = FilterIds(probes, filterSubstring);

                Console.WriteLine(string.Format("Bilinen Vestas etiket adaylari deneniyor ({0} adet)...", probes.Count));
                valid = ValidateItemIds(probes);
                List<string> moreIds = ExpandValidatedPatterns(valid);
                moreIds = SubtractIds(moreIds, valid);
                if (!string.IsNullOrEmpty(filterSubstring))
                    moreIds = FilterIds(moreIds, filterSubstring);

                if (moreIds.Count > 0)
                {
                    Console.WriteLine(string.Format("Bulunan kaliba gore {0} ek etiket dogrulaniyor...", moreIds.Count));
                    List<string> extraValid = ValidateItemIds(moreIds);
                    foreach (string id in extraValid)
                        valid.Add(id);
                }

                if (valid.Count > 0)
                {
                    outcome.ItemIds = UniquePreserve(valid);
                    outcome.Method = "bilinen_etiket";
                    outcome.Truncated = false;
                    return outcome;
                }

                if (string.IsNullOrEmpty(outcome.Error))
                    outcome.Error = "Bilinen Vestas etiket adaylari sunucuda bulunamadi.";
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(outcome.Error))
                    outcome.Error = ex.Message;
                else
                    outcome.Error = outcome.Error + "; " + ex.Message;
            }

            return outcome;
        }

        public List<string> BrowseHierarchical(string filterSubstring, int maxCollect, out bool truncated)
        {
            truncated = false;
            var result = new List<string>();
            if (_browser == null)
                throw new InvalidOperationException("Sunucu adres alani tarama (browse) desteklemiyor veya henuz hazir degil.");

            ResetBrowseToRoot();
            BrowseHierRecursive(filterSubstring, maxCollect, result, ref truncated, 0);
            return result;
        }

        private void ResetBrowseToRoot()
        {
            try
            {
                _browser.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_TO, "");
                return;
            }
            catch
            {
            }

            for (int i = 0; i < 16; i++)
            {
                try
                {
                    _browser.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_UP, "");
                }
                catch
                {
                    break;
                }
            }
        }

        private void BrowseHierRecursive(string filterSubstring, int maxCollect, List<string> result, ref bool truncated, int depth)
        {
            if (depth > 12 || result.Count >= maxCollect)
            {
                if (result.Count >= maxCollect)
                    truncated = true;
                return;
            }

            List<string> leaves = CollectBrowseNames(OPCBROWSETYPE.OPC_LEAF);
            for (int i = 0; i < leaves.Count; i++)
            {
                string fullId = ResolveItemId(leaves[i]);
                if (string.IsNullOrEmpty(fullId))
                    continue;
                if (!string.IsNullOrEmpty(filterSubstring) &&
                    fullId.IndexOf(filterSubstring, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                result.Add(fullId);
                if (result.Count >= maxCollect)
                {
                    truncated = true;
                    return;
                }
            }

            List<string> branches = CollectBrowseNames(OPCBROWSETYPE.OPC_BRANCH);
            for (int i = 0; i < branches.Count; i++)
            {
                string branch = branches[i];
                try
                {
                    _browser.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_DOWN, branch);
                    BrowseHierRecursive(filterSubstring, maxCollect, result, ref truncated, depth + 1);
                }
                catch
                {
                }
                try
                {
                    _browser.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_UP, "");
                }
                catch
                {
                }
                if (truncated)
                    return;
            }
        }

        private List<string> CollectBrowseNames(OPCBROWSETYPE browseType)
        {
            var names = new List<string>();
            IEnumString enumString;
            try
            {
                _browser.BrowseOPCItemIDs(browseType, "", 0, 0, out enumString);
            }
            catch
            {
                return names;
            }

            if (enumString == null)
                return names;

            try
            {
                string[] buf = new string[64];
                while (true)
                {
                    int fetched;
                    enumString.Next(buf.Length, buf, out fetched);
                    if (fetched <= 0)
                        break;
                    for (int i = 0; i < fetched; i++)
                    {
                        if (!string.IsNullOrEmpty(buf[i]))
                            names.Add(buf[i]);
                    }
                }
            }
            finally
            {
                try { Marshal.ReleaseComObject(enumString); } catch { }
            }
            return names;
        }

        private string ResolveItemId(string raw)
        {
            try
            {
                string resolved;
                _browser.GetItemID(raw, out resolved);
                if (!string.IsNullOrEmpty(resolved))
                    return resolved;
            }
            catch
            {
            }
            return raw;
        }

        public List<string> BrowseDa3(string filterSubstring, int maxCollect, out bool truncated)
        {
            truncated = false;
            var result = new List<string>();
            if (_browser3 == null)
                throw new InvalidOperationException("OPC DA 3 tarama arayuzu yok.");
            BrowseDa3Recursive("", filterSubstring, maxCollect, 0, result, ref truncated);
            return result;
        }

        private void BrowseDa3Recursive(string itemId, string filterSubstring, int maxCollect, int depth, List<string> result, ref bool truncated)
        {
            if (depth > 12 || result.Count >= maxCollect)
            {
                if (result.Count >= maxCollect)
                    truncated = true;
                return;
            }

            string continuation = "";
            bool more = false;
            int guard = 0;
            do
            {
                guard++;
                if (guard > 40)
                    break;

                IntPtr pElements = IntPtr.Zero;
                int count = 0;
                more = false;
                int hr = _browser3.Browse(
                    itemId,
                    ref continuation,
                    80,
                    OPCBROWSEFILTER.OPC_BROWSE_FILTER_ALL,
                    "",
                    "",
                    false,
                    false,
                    0,
                    IntPtr.Zero,
                    out more,
                    out count,
                    out pElements);

                if (hr < 0)
                {
                    if (depth == 0 && result.Count == 0)
                        Marshal.ThrowExceptionForHR(hr);
                    break;
                }

                List<Da3Element> children = ReadDa3Elements(pElements, count);
                for (int i = 0; i < children.Count; i++)
                {
                    Da3Element el = children[i];
                    if ((el.Flags & OpcBrowseElementFlags.OPC_BROWSE_ISITEM) != 0 &&
                        !string.IsNullOrEmpty(el.ItemId))
                    {
                        if (string.IsNullOrEmpty(filterSubstring) ||
                            el.ItemId.IndexOf(filterSubstring, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            result.Add(el.ItemId);
                            if (result.Count >= maxCollect)
                            {
                                truncated = true;
                                return;
                            }
                        }
                    }

                    if ((el.Flags & OpcBrowseElementFlags.OPC_BROWSE_HASCHILDREN) != 0 &&
                        !string.IsNullOrEmpty(el.ItemId))
                    {
                        BrowseDa3Recursive(el.ItemId, filterSubstring, maxCollect, depth + 1, result, ref truncated);
                        if (truncated)
                            return;
                    }
                }
            }
            while (more || !string.IsNullOrEmpty(continuation));
        }

        private static List<Da3Element> ReadDa3Elements(IntPtr pElements, int count)
        {
            var list = new List<Da3Element>();
            if (pElements == IntPtr.Zero || count <= 0)
                return list;

            int ptrSize = IntPtr.Size;
            int elementSize = ptrSize + ptrSize + 4 + 4 + 4 + ptrSize;

            try
            {
                for (int i = 0; i < count; i++)
                {
                    IntPtr el = new IntPtr(pElements.ToInt64() + (long)i * elementSize);
                    IntPtr pName = Marshal.ReadIntPtr(el, 0);
                    IntPtr pItemId = Marshal.ReadIntPtr(el, ptrSize);
                    int flags = Marshal.ReadInt32(el, ptrSize * 2);
                    int numProps = Marshal.ReadInt32(el, ptrSize * 2 + 8);
                    IntPtr pProps = Marshal.ReadIntPtr(el, ptrSize * 2 + 12);

                    var de = new Da3Element();
                    de.Name = pName != IntPtr.Zero ? Marshal.PtrToStringUni(pName) : null;
                    de.ItemId = pItemId != IntPtr.Zero ? Marshal.PtrToStringUni(pItemId) : null;
                    de.Flags = flags;
                    list.Add(de);

                    if (pName != IntPtr.Zero) Marshal.FreeCoTaskMem(pName);
                    if (pItemId != IntPtr.Zero) Marshal.FreeCoTaskMem(pItemId);
                    if (pProps != IntPtr.Zero) Marshal.FreeCoTaskMem(pProps);
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(pElements);
            }

            return list;
        }

        public List<string> ValidateItemIds(IList<string> itemIds)
        {
            if (_server == null)
                throw new InvalidOperationException("Once sunucuya baglan.");

            var unique = UniquePreserve(itemIds);
            var valid = new List<string>();
            if (unique.Count == 0)
                return valid;

            GroupHandle group = EnsureGroup("_probe");
            const int batchSize = 40;
            int resultSize = Marshal.SizeOf(typeof(OPCITEMRESULT));

            for (int offset = 0; offset < unique.Count; offset += batchSize)
            {
                int n = Math.Min(batchSize, unique.Count - offset);
                var defs = new OPCITEMDEF[n];
                for (int i = 0; i < n; i++)
                {
                    defs[i] = new OPCITEMDEF
                    {
                        szAccessPath = "",
                        szItemID = unique[offset + i],
                        bActive = 0,
                        hClient = i + 1,
                        dwBlobSize = 0,
                        pBlob = IntPtr.Zero,
                        vtRequestedDataType = (short)VarEnum.VT_EMPTY,
                        wReserved = 0
                    };
                }

                IntPtr pResults;
                IntPtr pErrors;
                group.ItemMgt.ValidateItems(n, defs, false, out pResults, out pErrors);
                try
                {
                    for (int i = 0; i < n; i++)
                    {
                        int err = Marshal.ReadInt32(pErrors, i * sizeof(int));
                        if (err >= 0)
                            valid.Add(unique[offset + i]);
                    }

                    if (pResults != IntPtr.Zero)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            IntPtr resultPtr = new IntPtr(pResults.ToInt64() + (long)i * resultSize);
                            OPCITEMRESULT itemResult = (OPCITEMRESULT)Marshal.PtrToStructure(resultPtr, typeof(OPCITEMRESULT));
                            if (itemResult.pBlob != IntPtr.Zero)
                            {
                                try { Marshal.FreeCoTaskMem(itemResult.pBlob); } catch { }
                            }
                        }
                    }
                }
                finally
                {
                    if (pResults != IntPtr.Zero) Marshal.FreeCoTaskMem(pResults);
                    if (pErrors != IntPtr.Zero) Marshal.FreeCoTaskMem(pErrors);
                }
            }

            return valid;
        }

        public static List<string> BuildOzyelKnownIds()
        {
            var ids = new List<string>();
            ids.Add("OZYEL.IsConnected");
            ids.Add("OZYEL.FTPStatus");
            ids.Add("OZYEL.LastError");
            ids.Add("OZYEL.ConnectionStatus");
            ids.Add("OZYEL.ConnectionS");
            ids.Add("OZYEL.NamespaceType");
            ids.Add("OZYEL.NamespaceT");
            for (int n = 1; n <= 15; n++)
            {
                string wtg = "WTG" + n.ToString("00", CultureInfo.InvariantCulture);
                ids.Add("OZYEL." + wtg + ".Grid.Power");
                ids.Add("OZYEL." + wtg + ".System.OperationStateInt");
                ids.Add("OZYEL." + wtg + ".Ambient.WindSpeed");
            }
            return ids;
        }

        public static List<string> BuildVestasProbeIds()
        {
            string[] prefixes = new string[]
            {
                "",
                "DA."
            };

            string[] parks = new string[]
            {
                "GOP",
                "GOPRES",
                "GOP_RES",
                "GOP RES",
                "Gaziosmanpasa",
                "GaziosmanpasaRES",
                "Gaziosmanpasa RES",
                "Gaziosmanpasa_RES",
                "Gaziosmanpa" + "\u015F" + "a",
                "Gaziosmanpa" + "\u015F" + "a RES"
            };

            string[] turbines = new string[]
            {
                "WTG01",
                "T01",
                "T1",
                "WTG1"
            };

            string[] signals = new string[]
            {
                "ActivePower",
                "Active Power",
                "Grid Power",
                "Grid.ActivePower",
                "Grid Production",
                "Production",
                "WindSpeed",
                "Wind Speed",
                "Ambient WindSpeed",
                "Ambient.WindSpeed",
                "Status",
                "TurbineState"
            };

            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int p = 0; p < prefixes.Length; p++)
            {
                for (int k = 0; k < parks.Length; k++)
                {
                    for (int t = 0; t < turbines.Length; t++)
                    {
                        for (int s = 0; s < signals.Length; s++)
                        {
                            string id = prefixes[p] + parks[k] + "." + turbines[t] + "." + signals[s];
                            if (seen.Add(id))
                                ids.Add(id);
                        }
                    }
                }
            }

            // Park olmadan kisa adaylar
            for (int t = 0; t < turbines.Length; t++)
            {
                for (int s = 0; s < signals.Length; s++)
                {
                    string id = turbines[t] + "." + signals[s];
                    if (seen.Add(id))
                        ids.Add(id);
                }
            }

            return ids;
        }

        public static List<string> ExpandValidatedPatterns(List<string> valid)
        {
            var expanded = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < valid.Count; i++)
            {
                if (seen.Add(valid[i]))
                    expanded.Add(valid[i]);
            }

            string[] extraSignals = new string[]
            {
                "ActivePower",
                "Active Power",
                "Grid Power",
                "Grid.ActivePower",
                "Grid Production",
                "Production",
                "WindSpeed",
                "Wind Speed",
                "Ambient WindSpeed",
                "Ambient.WindSpeed",
                "Status",
                "TurbineState"
            };

            var schemes = new List<TurbineScheme>();
            for (int i = 0; i < valid.Count; i++)
            {
                TurbineScheme sch = TryParseTurbineItem(valid[i]);
                if (sch != null)
                    schemes.Add(sch);
            }

            var prefixKinds = new List<string>();
            var signalsByKey = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < schemes.Count; i++)
            {
                string key = schemes[i].Prefix + "|" + schemes[i].Kind;
                if (!signalsByKey.ContainsKey(key))
                {
                    prefixKinds.Add(key);
                    signalsByKey[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                if (!string.IsNullOrEmpty(schemes[i].Signal))
                    signalsByKey[key].Add(schemes[i].Signal);
                for (int s = 0; s < extraSignals.Length; s++)
                    signalsByKey[key].Add(extraSignals[s]);
            }

            for (int k = 0; k < prefixKinds.Count; k++)
            {
                string key = prefixKinds[k];
                int bar = key.IndexOf('|');
                string prefix = key.Substring(0, bar);
                string kind = key.Substring(bar + 1);
                foreach (string signal in signalsByKey[key])
                {
                    for (int n = 1; n <= 15; n++)
                    {
                        string turbine = FormatTurbine(kind, n);
                        if (turbine == null)
                            continue;
                        string id = prefix + turbine + "." + signal;
                        if (seen.Add(id))
                            expanded.Add(id);
                    }
                }
            }

            return expanded;
        }

        private static TurbineScheme TryParseTurbineItem(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            string[] parts = id.Split('.');
            if (parts.Length < 2)
                return null;

            for (int i = 0; i < parts.Length; i++)
            {
                string kind = ClassifyTurbineToken(parts[i]);
                if (kind == null)
                    continue;
                if (i + 1 >= parts.Length)
                    continue;

                var prefixParts = new List<string>();
                for (int j = 0; j < i; j++)
                    prefixParts.Add(parts[j]);
                string prefix = prefixParts.Count == 0 ? "" : string.Join(".", prefixParts.ToArray()) + ".";

                var signalParts = new List<string>();
                for (int j = i + 1; j < parts.Length; j++)
                    signalParts.Add(parts[j]);

                TurbineScheme sch = new TurbineScheme();
                sch.Prefix = prefix;
                sch.Kind = kind;
                sch.Signal = string.Join(".", signalParts.ToArray());
                return sch;
            }
            return null;
        }

        private static string ClassifyTurbineToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;
            if (token.StartsWith("WTG", StringComparison.OrdinalIgnoreCase) && token.Length > 3)
            {
                string rest = token.Substring(3);
                if (IsAllDigits(rest))
                    return rest.Length == 2 ? "WTG00" : "WTG";
            }
            if ((token[0] == 'T' || token[0] == 't') && token.Length > 1)
            {
                string rest = token.Substring(1);
                if (IsAllDigits(rest))
                    return rest.Length == 2 ? "T00" : "T";
            }
            return null;
        }

        private static string FormatTurbine(string kind, int n)
        {
            if (kind == "WTG00")
                return "WTG" + n.ToString("00", CultureInfo.InvariantCulture);
            if (kind == "WTG")
                return "WTG" + n.ToString(CultureInfo.InvariantCulture);
            if (kind == "T00")
                return "T" + n.ToString("00", CultureInfo.InvariantCulture);
            if (kind == "T")
                return "T" + n.ToString(CultureInfo.InvariantCulture);
            return null;
        }

        private static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (!char.IsDigit(s[i]))
                    return false;
            }
            return true;
        }

        private static List<string> FilterIds(IList<string> ids, string filter)
        {
            var list = new List<string>();
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i].IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    list.Add(ids[i]);
            }
            return list;
        }

        private static List<string> UniquePreserve(IList<string> ids)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(ids[i]))
                    continue;
                if (seen.Add(ids[i].Trim()))
                    list.Add(ids[i].Trim());
            }
            return list;
        }

        private static List<string> SubtractIds(List<string> all, List<string> already)
        {
            var have = new HashSet<string>(already, StringComparer.OrdinalIgnoreCase);
            var rest = new List<string>();
            for (int i = 0; i < all.Count; i++)
            {
                if (!have.Contains(all[i]))
                    rest.Add(all[i]);
            }
            return rest;
        }
    }
}
