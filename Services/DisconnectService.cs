using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Principal;

namespace HBT.Services
{

/// <summary>
/// 拔线服务 — 通过 iphlpapi.dll 关闭炉石到游戏服务器的 TCP 连接。
/// 参考团子版 HDT 的 Reconnector 实现。
/// 需要管理员权限。
/// </summary>
public static class DisconnectService
{
    // ═══════════════════════════════════════
    //  iphlpapi P/Invoke
    // ═══════════════════════════════════════

    private enum TCP_TABLE_CLASS { TCP_TABLE_OWNER_MODULE_ALL = 8 }
    private enum MIB_TCP_STATE
    {
        ESTAB = 5,
        DELETE_TCB = 12
    }

    // 与团子版 Iphlpapi.MIB_TCPROW 完全一致
    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_TCPROW
    {
        public uint dwState;
        public uint dwLocalAddr;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] dwLocalPort;
        public uint dwRemoteAddr;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] dwRemotePort;
    }

    // 与团子版 Iphlpapi.MIB_TCPROW_OWNER_MODULE 完全一致
    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_MODULE
    {
        public uint dwState;
        public uint dwLocalAddr;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] dwLocalPort;
        public uint dwRemoteAddr;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] dwRemotePort;
        public uint dwOwningPid;
        public System.Runtime.InteropServices.ComTypes.FILETIME liCreateTimestamp;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public ulong[] dwOwningModuleInfo;

        public IPAddress RemoteAddress => new IPAddress(dwRemoteAddr);
        public ushort RemotePort => BitConverter.ToUInt16(new byte[2] { dwRemotePort[1], dwRemotePort[0] }, 0);
        public long TimestampTicks => ((long)liCreateTimestamp.dwHighDateTime << 32) + (uint)liCreateTimestamp.dwLowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPTABLE_OWNER_MODULE
    {
        public uint dwNumEntries;
        [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.Struct, SizeConst = 1)]
        public MIB_TCPROW_OWNER_MODULE[] table;
    }

    private const int AF_INET = 2;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, TCP_TABLE_CLASS tblClass, uint reserved = 0);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern int SetTcpEntry(IntPtr pTcprow);

    // ═══════════════════════════════════════
    //  状态管理
    // ═══════════════════════════════════════

    /// <summary>缓存的 TCP 连接行（进入战斗时获取）</summary>
    public static MIB_TCPROW? CachedTcpRow { get; set; }

    /// <summary>缓存 TcpRow 时的时间戳（用于重试时查找）</summary>
    public static long CachedTcpRowTimestamp { get; set; }

    /// <summary>是否正在重连状态</summary>
    public static bool IsReconnecting { get; set; }

    /// <summary>重连次数（每次游戏开始递增）</summary>
    public static int ReconnectCount { get; set; }

    /// <summary>是否正在拔线中</summary>
    public static bool IsDisconnecting { get; private set; }

    /// <summary>游戏是否已结束（结束后点击拔线直接显示失败）</summary>
    public static bool IsGameEnded { get; set; }

    /// <summary>炉石进程 ID（用于 TCP 表过滤）</summary>
    public static int HearthstoneProcessId { get; set; }

    // ═══════════════════════════════════════
    //  公开 API
    // ═══════════════════════════════════════

    /// <summary>检查是否以管理员权限运行</summary>
    public static bool IsElevated()
    {
        using (var identity = WindowsIdentity.GetCurrent())
        {
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// 获取炉石进程到服务器的 TCP 连接行（用于拔线）。
    /// 优先精确匹配 serverAddress:serverPort，失败则取最新的 ESTAB 连接。
    /// </summary>
    public static MIB_TCPROW? GetTcpRow(string serverAddress, uint serverPort)
    {
        try
        {
            var processes = Process.GetProcessesByName("Hearthstone");
            if (processes.Length == 0)
                return null;

            var hsPid = (uint)processes[0].Id;
            HearthstoneProcessId = (int)hsPid;

            var allConnections = GetAllTCPConnections();

            // 精确匹配 PID + 远程地址 + 端口
            var target = allConnections.FirstOrDefault(c =>
                c.dwOwningPid == hsPid &&
                c.RemoteAddress.ToString() == serverAddress &&
                c.RemotePort == (ushort)serverPort);

            if (target.dwOwningPid != 0)
            {
                CachedTcpRowTimestamp = target.TimestampTicks;
                return ToTcpRow(target);
            }

            // 仅按地址+端口匹配（不按 PID）
            var addrMatch = allConnections.FirstOrDefault(c =>
                c.RemoteAddress.ToString() == serverAddress &&
                c.RemotePort == (ushort)serverPort);
            if (addrMatch.dwOwningPid != 0)
            {
                CachedTcpRowTimestamp = addrMatch.TimestampTicks;
                return ToTcpRow(addrMatch);
            }

            // 回退：取炉石进程最新的 ESTAB 连接
            MIB_TCPROW_OWNER_MODULE? latest = null;
            foreach (var c in allConnections)
            {
                if (c.dwOwningPid == hsPid && c.dwState == (uint)MIB_TCP_STATE.ESTAB)
                {
                    if (latest == null || c.TimestampTicks > latest.Value.TimestampTicks)
                        latest = c;
                }
            }

            if (latest.HasValue)
                return ToTcpRow(latest.Value);

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取炉石进程最新的 ESTAB 连接（拔线失败后的重试用）。
    /// 在指定时间戳之前建立的最新连接。
    /// </summary>
    public static MIB_TCPROW? GetLastTcp(long beforeTimestamp)
    {
        try
        {
            var allConnections = GetAllTCPConnections();
            MIB_TCPROW_OWNER_MODULE? latest = null;

            foreach (var c in allConnections)
            {
                if (c.dwOwningPid == HearthstoneProcessId &&
                    c.dwState == (uint)MIB_TCP_STATE.ESTAB &&
                    c.TimestampTicks <= beforeTimestamp)
                {
                    if (latest == null || c.TimestampTicks > latest.Value.TimestampTicks)
                        latest = c;
                }
            }

            if (latest.HasValue)
                return ToTcpRow(latest.Value);

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Disconnect] GetLastTcp failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 执行拔线：将 TCP 连接状态设为 DELETE_TCB。
    /// 参考团子版 Reconnector.Disconnect。
    /// </summary>
    /// <returns>true=成功，false=失败</returns>
    public static bool Disconnect(MIB_TCPROW tcpRow)
    {
        if (IsDisconnecting) return false;

        try
        {
            IsDisconnecting = true;

            // state=12 = MIB_TCP_STATE_DELETE_TCB（强制 TCP 连接终止）
            tcpRow.dwState = (uint)MIB_TCP_STATE.DELETE_TCB;

            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MIB_TCPROW)));
            try
            {
                Marshal.StructureToPtr(tcpRow, ptr, false);
                int ret = SetTcpEntry(ptr);
                Console.WriteLine($"[Disconnect] SetTcpEntry returned: {ret}");
                return ret == 0;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Disconnect] Disconnect failed: {ex.Message}");
            return false;
        }
        finally
        {
            IsDisconnecting = false;
        }
    }

    /// <summary>
    /// 完整拔线流程（含重试）。
    /// 1. 用缓存的 TcpRow 断开
    /// 2. 失败时获取最新连接再断一次
    /// 参考团子版 DoReconnect 逻辑。
    /// </summary>
    public static string DisconnectWithRetry()
    {
        if (!IsElevated())
            return "需要管理员权限";

        if (IsDisconnecting)
            return "正在拔线中";

        if (!CachedTcpRow.HasValue)
            return "TcpRow 未缓存";

        var tcpRow = CachedTcpRow.Value;
        Console.WriteLine($"[Disconnect] 开始拔线: {GetTcpRowString(tcpRow)}");

        // 第一次尝试
        if (Disconnect(tcpRow))
        {
            IsReconnecting = true;
            Console.WriteLine("[Disconnect] 拔线成功");
            return null;
        }

        // 失败：获取最新连接再断一次
        Console.WriteLine("[Disconnect] 第一次拔线失败，尝试获取最新连接重试");
        var lastTcp = GetLastTcp(CachedTcpRowTimestamp);
        if (lastTcp.HasValue)
        {
            Console.WriteLine($"[Disconnect] 重试拔线: {GetTcpRowString(lastTcp.Value)}");
            if (Disconnect(lastTcp.Value))
            {
                IsReconnecting = true;
                Console.WriteLine("[Disconnect] 重试拔线成功");
                return null;
            }
        }

        return "拔线失败";
    }

    /// <summary>结束重连状态</summary>
    public static void EndReconnect()
    {
        IsReconnecting = false;
        CachedTcpRow = null;
        Console.WriteLine("[Disconnect] EndReconnect");
    }

    /// <summary>重置重连计数（新游戏开始时）</summary>
    public static void ResetReconnectCount()
    {
        ReconnectCount = 0;
    }

    /// <summary>格式化 TcpRow 为可读字符串</summary>
    public static string GetTcpRowString(MIB_TCPROW tcpRow)
    {
        var localAddr = new IPAddress(tcpRow.dwLocalAddr);
        var remoteAddr = new IPAddress(tcpRow.dwRemoteAddr);
        ushort localPort = BitConverter.ToUInt16(new byte[2] { tcpRow.dwLocalPort[1], tcpRow.dwLocalPort[0] }, 0);
        ushort remotePort = BitConverter.ToUInt16(new byte[2] { tcpRow.dwRemotePort[1], tcpRow.dwRemotePort[0] }, 0);
        return $"{localAddr}:{localPort} -> {remoteAddr}:{remotePort}";
    }

    // ═══════════════════════════════════════
    //  内部实现
    // ═══════════════════════════════════════

    private static MIB_TCPROW ToTcpRow(MIB_TCPROW_OWNER_MODULE row)
    {
        return new MIB_TCPROW
        {
            dwState = row.dwState,
            dwLocalAddr = row.dwLocalAddr,
            dwLocalPort = (byte[])row.dwLocalPort.Clone(),
            dwRemoteAddr = row.dwRemoteAddr,
            dwRemotePort = (byte[])row.dwRemotePort.Clone(),
        };
    }

    private static List<MIB_TCPROW_OWNER_MODULE> GetAllTCPConnections()
    {
        int buffSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref buffSize, false, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_MODULE_ALL);
        IntPtr tcpTablePtr = Marshal.AllocHGlobal(buffSize);

        try
        {
            var ret = GetExtendedTcpTable(tcpTablePtr, ref buffSize, false, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_MODULE_ALL);
            if (ret != 0)
                return new List<MIB_TCPROW_OWNER_MODULE>();

            uint rowCount = (uint)Marshal.ReadInt32(tcpTablePtr);
            int rowStructSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_MODULE));
            var rows = new MIB_TCPROW_OWNER_MODULE[rowCount];

            // 使用 Marshal.OffsetOf 获取 table 字段的实际偏移（处理对齐填充）
            IntPtr rowPtr = (IntPtr)((long)tcpTablePtr + Marshal.OffsetOf(typeof(MIB_TCPTABLE_OWNER_MODULE), "table").ToInt64());
            for (int i = 0; i < rowCount; i++)
            {
                rows[i] = (MIB_TCPROW_OWNER_MODULE)Marshal.PtrToStructure(rowPtr, typeof(MIB_TCPROW_OWNER_MODULE));
                rowPtr = (IntPtr)((long)rowPtr + rowStructSize);
            }

            return rows.ToList();
        }
        finally
        {
            Marshal.FreeHGlobal(tcpTablePtr);
        }
    }
}

}
