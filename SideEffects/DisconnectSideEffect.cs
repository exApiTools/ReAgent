using System.Linq.Dynamic.Core.CustomTypeProviders;
using ReAgent.State;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using ExileCore;

namespace ReAgent.SideEffects;

[DynamicLinqType]
[Api]
public record DisconnectSideEffect : ISideEffect
{
    public SideEffectApplicationResult Apply(RuleState state)
    {
        try
        {
            KillTcpConnectionForProcess(ReAgent.ProcessID);
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"Unable to disconnect: {ex}");
        }

        return SideEffectApplicationResult.AppliedUnique;
    }

    public override string ToString() => "Disconnect";

    public static unsafe void KillTcpConnectionForProcess(int processId)
    {
        MibTcprowOwnerPid[] table;
        var afInet = 2;
        var buffSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref buffSize, true, afInet, TcpTableClass.TcpTableOwnerPidAll);
        var buffTable = NativeMemory.Alloc((nuint)buffSize);
        try
        {
            var ret = GetExtendedTcpTable((IntPtr)buffTable, ref buffSize, true, afInet, TcpTableClass.TcpTableOwnerPidAll);
            if (ret != 0)
                return;
            var tab = Marshal.PtrToStructure<MibTcptableOwnerPid>((IntPtr)buffTable);
            var rowPtr = (IntPtr)((long)buffTable + Marshal.SizeOf(tab.dwNumEntries));
            table = new MibTcprowOwnerPid[tab.dwNumEntries];
            for (var i = 0; i < tab.dwNumEntries; i++)
            {
                table[i] = Marshal.PtrToStructure<MibTcprowOwnerPid>(rowPtr);
                rowPtr = (IntPtr)((long)rowPtr + Marshal.SizeOf<MibTcprowOwnerPid>());
            }
        }
        finally
        {
            NativeMemory.Free(buffTable);
        }

        //Kill PoE Connection
        foreach (var connection in table.Where(t => t.owningPid == processId))
        {
            var poeConnection = connection;
            poeConnection.state = 12;
            var ptr = NativeMemory.Alloc((nuint)Marshal.SizeOf(poeConnection));
            Marshal.StructureToPtr(poeConnection, (nint)ptr, false);
            SetTcpEntry((IntPtr)ptr);
            NativeMemory.Free(ptr);
        }
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, TcpTableClass tblClass, uint reserved = 0);

    [DllImport("iphlpapi.dll")]
    private static extern int SetTcpEntry(IntPtr pTcprow);

    [StructLayout(LayoutKind.Sequential)]
    public struct MibTcprowOwnerPid
    {
        public uint state;
        public uint localAddr;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] localPort;

        public uint remoteAddr;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] remotePort;

        public uint owningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MibTcptableOwnerPid
    {
        public uint dwNumEntries;
        private readonly MibTcprowOwnerPid table;
    }

    private enum TcpTableClass
    {
        TcpTableBasicListener,
        TcpTableBasicConnections,
        TcpTableBasicAll,
        TcpTableOwnerPidListener,
        TcpTableOwnerPidConnections,
        TcpTableOwnerPidAll,
        TcpTableOwnerModuleListener,
        TcpTableOwnerModuleConnections,
        TcpTableOwnerModuleAll
    }
}