// Explicit OPC DA COM interface definitions — no commercial SDK, no NuGet.
// Local use only. Read-only paths are used by the app; write interfaces are not defined.
using System;
using System.Runtime.InteropServices;

namespace GopOpcOkuyucu
{
    // OPC Foundation OPCEnum
    [ComImport, Guid("13486D51-4821-11D2-A494-3CB306C10000")]
    public class OpcServerList { }

    [ComImport, Guid("13486D50-4821-11D2-A494-3CB306C10000"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOPCServerList
    {
        void EnumClassesOfCategories(
            int cImplemented,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] Guid[] catidImpl,
            int cRequired,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] Guid[] catidReq,
            out IEnumGUID enumGuid);

        void GetClassDetails(
            [In] ref Guid clsid,
            [MarshalAs(UnmanagedType.LPWStr)] out string progId,
            [MarshalAs(UnmanagedType.LPWStr)] out string userType);

        void CLSIDFromProgID(
            [MarshalAs(UnmanagedType.LPWStr)] string progId,
            out Guid clsid);
    }

    [ComImport, Guid("0002E000-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumGUID
    {
        void Next(
            int celt,
            [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] Guid[] rgelt,
            out int pceltFetched);

        void Skip(int celt);
        void Reset();
        void Clone(out IEnumGUID enumGuid);
    }

    // OPC DA category GUIDs
    public static class OpcGuids
    {
        public static readonly Guid OPCDAServer10 =
            new Guid("63D5F430-CFE4-11D1-B2C8-0060083BA1FB");
        public static readonly Guid OPCDAServer20 =
            new Guid("63D5F432-CFE4-11D1-B2C8-0060083BA1FB");
        public static readonly Guid OPCDAServer30 =
            new Guid("CC603642-66D7-48F1-B69A-B625E736C46A");
    }

    public enum OPCDATASOURCE
    {
        OPC_DS_CACHE = 1,
        OPC_DS_DEVICE = 2
    }

    public enum OPCBROWSETYPE
    {
        OPC_BRANCH = 1,
        OPC_LEAF = 2,
        OPC_FLAT = 3
    }

    public enum OPCNAMESPACETYPE
    {
        OPC_NS_HIERARCHIAL = 1,
        OPC_NS_FLAT = 2
    }

    public enum OPCBROWSEDIRECTION
    {
        OPC_BROWSE_UP = 1,
        OPC_BROWSE_DOWN = 2,
        OPC_BROWSE_TO = 3
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct OPCITEMDEF
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string szAccessPath;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string szItemID;
        public int bActive;
        public int hClient;
        public int dwBlobSize;
        public IntPtr pBlob;
        public short vtRequestedDataType;
        public short wReserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct OPCITEMRESULT
    {
        public int hServer;
        public short vtCanonicalDataType;
        public short wReserved;
        public int dwAccessRights;
        public int dwBlobSize;
        public IntPtr pBlob;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct OPCITEMSTATE
    {
        public int hClient;
        public FILETIME ftTimeStamp;
        public short wQuality;
        public short wReserved;
        [MarshalAs(UnmanagedType.Struct)]
        public object vDataValue;
    }

    [ComImport, Guid("39C13A4D-011E-11D0-9675-0020AFD8ADB3"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOPCServer
    {
        void AddGroup(
            [MarshalAs(UnmanagedType.LPWStr)] string szName,
            [MarshalAs(UnmanagedType.Bool)] bool bActive,
            int dwRequestedUpdateRate,
            int hClientGroup,
            IntPtr pTimeBias,
            IntPtr pPercentDeadband,
            int dwLCID,
            out int phServerGroup,
            out int pRevisedUpdateRate,
            ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppUnk);

        void GetErrorString(
            int dwError,
            int dwLocale,
            [MarshalAs(UnmanagedType.LPWStr)] out string ppString);

        void GetGroupByName(
            [MarshalAs(UnmanagedType.LPWStr)] string szName,
            ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppUnk);

        void GetStatus(out IntPtr ppServerStatus);

        void RemoveGroup(
            int hServerGroup,
            [MarshalAs(UnmanagedType.Bool)] bool bForce);

        void CreateGroupEnumerator(
            int dwScope,
            ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppUnk);
    }

    [ComImport, Guid("39C13A4E-011E-11D0-9675-0020AFD8ADB3"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOPCItemMgt
    {
        void AddItems(
            int dwCount,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] OPCITEMDEF[] pItemArray,
            out IntPtr ppAddResults,
            out IntPtr ppErrors);

        void ValidateItems(
            int dwCount,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] OPCITEMDEF[] pItemArray,
            [MarshalAs(UnmanagedType.Bool)] bool bBlobUpdate,
            out IntPtr ppValidationResults,
            out IntPtr ppErrors);

        void RemoveItems(
            int dwCount,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] int[] phServer,
            out IntPtr ppErrors);

        void SetActiveState(
            int dwCount,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] int[] phServer,
            [MarshalAs(UnmanagedType.Bool)] bool bActive,
            out IntPtr ppErrors);

        void SetClientHandles(
            int dwCount,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] int[] phServer,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] int[] phClient,
            out IntPtr ppErrors);

        void SetDatatypes(
            int dwCount,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] int[] phServer,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] short[] pRequestedDatatypes,
            out IntPtr ppErrors);

        void CreateEnumerator(
            ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppUnk);
    }

    [ComImport, Guid("39C13A4F-011E-11D0-9675-0020AFD8ADB3"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOPCGroupStateMgt
    {
        void GetState(
            out int pUpdateRate,
            [MarshalAs(UnmanagedType.Bool)] out bool pActive,
            [MarshalAs(UnmanagedType.LPWStr)] out string ppName,
            out int pTimeBias,
            out float pPercentDeadband,
            out int pLCID,
            out int phClientGroup,
            out int phServerGroup);

        void SetState(
            IntPtr pRequestedUpdateRate,
            out int pRevisedUpdateRate,
            IntPtr pActive,
            IntPtr pTimeBias,
            IntPtr pPercentDeadband,
            IntPtr pLCID,
            IntPtr phClientGroup);

        void SetName([MarshalAs(UnmanagedType.LPWStr)] string szName);

        void CloneGroup(
            [MarshalAs(UnmanagedType.LPWStr)] string szName,
            ref Guid riid,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppUnk);
    }

    [ComImport, Guid("39C13A52-011E-11D0-9675-0020AFD8ADB3"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOPCSyncIO
    {
        // Read only — Write is intentionally not exposed by this application.
        void Read(
            OPCDATASOURCE dwSource,
            int dwCount,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] int[] phServer,
            out IntPtr ppItemValues,
            out IntPtr ppErrors);
    }

    // OPC DA 3.0 — inherits IOPCSyncIO. Write/WriteVQT are declared for vtable layout only; never called.
    [ComImport, Guid("730F5F0F-55B1-4c81-9E18-FF8A0904E1FA"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOPCSyncIO2
    {
        void Read(
            OPCDATASOURCE dwSource,
            int dwCount,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] int[] phServer,
            out IntPtr ppItemValues,
            out IntPtr ppErrors);

        void Write(
            int dwCount,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] int[] phServer,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] object[] pItemValues,
            out IntPtr ppErrors);

        void ReadMaxAge(
            int dwCount,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] int[] phServer,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] int[] pdwMaxAge,
            out IntPtr ppvValues,
            out IntPtr ppwQualities,
            out IntPtr ppftTimeStamps,
            out IntPtr ppErrors);

        void WriteVQT(
            int dwCount,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] int[] phServer,
            IntPtr pItemVQT,
            out IntPtr ppErrors);
    }

    // OPC DA 3.0 — read items by ID without creating a group.
    [ComImport, Guid("85C0B427-2893-4cbc-BD78-E5FC5146F08F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOPCItemIO
    {
        void Read(
            int dwCount,
            [In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr, SizeParamIndex = 0)] string[] pszItemIDs,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] int[] pdwMaxAge,
            out IntPtr ppvValues,
            out IntPtr ppwQualities,
            out IntPtr ppftTimeStamps,
            out IntPtr ppErrors);

        void WriteVQT(
            int dwCount,
            [In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr, SizeParamIndex = 0)] string[] pszItemIDs,
            IntPtr pItemVQT,
            out IntPtr ppErrors);
    }

    [ComImport, Guid("39C13A4C-011E-11D0-9675-0020AFD8ADB3"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOPCBrowseServerAddressSpace
    {
        void QueryOrganization(out OPCNAMESPACETYPE pNameSpaceType);

        void ChangeBrowsePosition(
            OPCBROWSEDIRECTION dwBrowseDirection,
            [MarshalAs(UnmanagedType.LPWStr)] string szString);

        void BrowseOPCItemIDs(
            OPCBROWSETYPE dwBrowseFilterType,
            [MarshalAs(UnmanagedType.LPWStr)] string szFilterCriteria,
            short vtDataTypeFilter,
            int dwAccessRightsFilter,
            out IEnumString ppIEnumString);

        void GetItemID(
            [MarshalAs(UnmanagedType.LPWStr)] string szItemDataID,
            [MarshalAs(UnmanagedType.LPWStr)] out string szItemID);

        void BrowseAccessPaths(
            [MarshalAs(UnmanagedType.LPWStr)] string szItemID,
            out IEnumString ppIEnumString);
    }

    [ComImport, Guid("00000101-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumString
    {
        [PreserveSig]
        int Next(
            int celt,
            [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr, SizeParamIndex = 0)]
            string[] rgelt,
            out int pceltFetched);

        void Skip(int celt);
        void Reset();
        void Clone(out IEnumString enumString);
    }

    public enum OPCBROWSEFILTER
    {
        OPC_BROWSE_FILTER_ALL = 1,
        OPC_BROWSE_FILTER_BRANCHES = 2,
        OPC_BROWSE_FILTER_ITEMS = 3
    }

    public static class OpcBrowseElementFlags
    {
        public const int OPC_BROWSE_HASCHILDREN = 1;
        public const int OPC_BROWSE_ISITEM = 2;
    }

    // OPC DA 3.0 browse — some servers expose this instead of DA 2.0 browse.
    [ComImport, Guid("39227004-A18F-4B57-8B0A-5235670F4468"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOPCBrowse
    {
        void GetProperties(
            int dwItemCount,
            [In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr, SizeParamIndex = 0)] string[] pszItemIDs,
            [MarshalAs(UnmanagedType.Bool)] bool bReturnPropertyValues,
            int dwPropertyCount,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] int[] pdwPropertyIDs,
            out IntPtr ppItemProperties);

        [PreserveSig]
        int Browse(
            [MarshalAs(UnmanagedType.LPWStr)] string szItemID,
            [MarshalAs(UnmanagedType.LPWStr)] ref string pszContinuationPoint,
            int dwMaxElementsReturned,
            OPCBROWSEFILTER dwBrowseFilter,
            [MarshalAs(UnmanagedType.LPWStr)] string szElementNameFilter,
            [MarshalAs(UnmanagedType.LPWStr)] string szVendorFilter,
            [MarshalAs(UnmanagedType.Bool)] bool bReturnAllProperties,
            [MarshalAs(UnmanagedType.Bool)] bool bReturnPropertyValues,
            int dwPropertyCount,
            IntPtr pdwPropertyIDs,
            [MarshalAs(UnmanagedType.Bool)] out bool pbMoreElements,
            out int pdwCount,
            out IntPtr ppBrowseElements);
    }
}
