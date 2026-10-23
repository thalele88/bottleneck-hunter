using System.Diagnostics;
using System.Runtime.InteropServices;
using static BottleneckHunter.Core.Optimize.NativeMethods;

namespace BottleneckHunter.Core.Optimize;

/// <summary>
/// Retrato da RAM dividido como o Windows enxerga:
/// em uso (programas), modificada (esperando ir para o disco), em espera (cache) e livre.
/// </summary>
public sealed record MemoryBreakdown(double TotalGb, double InUseGb, double ModifiedGb, double StandbyGb, double FreeGb, bool HasLists)
{
    public double UsedPercent => TotalGb > 0 ? InUseGb / TotalGb * 100 : 0;
}

/// <summary>Um programa e quanto de RAM ele ocupa (somando todos os processos com o mesmo nome).</summary>
public sealed record ProcessMemory(string Name, int Count, double Mb);

public sealed record MemoryCleanResult(MemoryBreakdown Before, MemoryBreakdown After, bool StandbyPurged, string? Error)
{
    /// <summary>Quanto saiu da memória "em uso".</summary>
    public double FreedMb => Math.Max(0, (Before.InUseGb - After.InUseGb) * 1024);
    /// <summary>Quanto do cache (lista em espera) foi esvaziado.</summary>
    public double CacheClearedMb => Math.Max(0, (Before.StandbyGb + Before.ModifiedGb - After.StandbyGb - After.ModifiedGb) * 1024);
}

/// <summary>
/// Limpeza de memória. Não fecha nenhum programa: só pede ao Windows que tire da RAM
/// as páginas que os programas não estão usando agora (elas voltam sozinhas se forem necessárias).
/// </summary>
public static class MemoryCleaner
{
    private const double GB = 1024d * 1024 * 1024;
    private static bool _privilegeTried;
    private static bool _hasPrivilege;

    /// <summary>O privilégio "SeProfileSingleProcess" é necessário para ler e limpar as listas de memória.</summary>
    private static bool EnsurePrivilege()
    {
        if (!_privilegeTried)
        {
            _hasPrivilege = EnablePrivilege("SeProfileSingleProcessPrivilege");
            EnablePrivilege("SeIncreaseQuotaPrivilege");
            _privilegeTried = true;
        }
        return _hasPrivilege;
    }

    public static MemoryBreakdown Read()
    {
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref mem)) return new MemoryBreakdown(0, 0, 0, 0, 0, false);

        double total = mem.ullTotalPhys / GB;
        double avail = mem.ullAvailPhys / GB;

        if (EnsurePrivilege() &&
            NtQuerySystemInformation(SystemMemoryListInformation, out var lists, Marshal.SizeOf<SYSTEM_MEMORY_LIST_INFORMATION>(), out _) == 0 &&
            lists.PageCountByPriority != null)
        {
            double page = Environment.SystemPageSize;
            double standby = lists.PageCountByPriority.Sum(p => (double)p) * page / GB;
            double free = ((double)lists.FreePageCount + lists.ZeroPageCount) * page / GB;
            double modified = (double)lists.ModifiedPageCount * page / GB;
            double inUse = Math.Max(0, total - standby - free - modified);
            return new MemoryBreakdown(total, inUse, modified, standby, free, true);
        }

        // Sem as listas detalhadas: "disponível" já inclui o cache, então mostramos só em uso x livre.
        return new MemoryBreakdown(total, total - avail, 0, 0, avail, false);
    }

    /// <summary>
    /// Limpa a memória. Rápida: esvazia o "conjunto de trabalho" dos programas.
    /// Completa: também grava a lista modificada no disco e esvazia o cache (lista em espera).
    /// </summary>
    public static MemoryCleanResult Clean(bool full)
    {
        var before = Read();
        string? error = null;
        bool purged = false;

        if (!TrySystemCommand(MemoryEmptyWorkingSets))
            TrimEachProcess();

        if (full)
        {
            TrySystemCommand(MemoryFlushModifiedList);
            purged = TrySystemCommand(MemoryPurgeStandbyList);
            if (!purged) error = "O Windows não permitiu esvaziar o cache. Abra o app como administrador.";
        }

        // O Windows leva um instante para recontar as páginas.
        Thread.Sleep(700);
        return new MemoryCleanResult(before, Read(), purged, error);
    }

    private static bool TrySystemCommand(int command)
    {
        if (!EnsurePrivilege()) return false;
        int cmd = command;
        return NtSetSystemInformation(SystemMemoryListInformation, ref cmd, sizeof(int)) == 0;
    }

    /// <summary>Plano B: pede a cada processo, um por um, que devolva a memória que não está usando.</summary>
    private static void TrimEachProcess()
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id > 4) EmptyWorkingSet(p.Handle);
            }
            catch
            {
                // Processos protegidos do sistema não deixam; tudo bem pular.
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    /// <summary>Os programas que mais ocupam RAM agora, agrupados pelo nome.</summary>
    public static List<ProcessMemory> TopProcesses(int count)
    {
        var totals = new Dictionary<string, (int Count, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id <= 4) continue;
                var name = p.ProcessName;
                var cur = totals.GetValueOrDefault(name);
                totals[name] = (cur.Count + 1, cur.Bytes + p.WorkingSet64);
            }
            catch
            {
                // O processo pode ter fechado no meio da leitura.
            }
            finally
            {
                p.Dispose();
            }
        }

        return totals
            .OrderByDescending(t => t.Value.Bytes)
            .Take(count)
            .Select(t => new ProcessMemory(t.Key, t.Value.Count, t.Value.Bytes / 1048576d))
            .ToList();
    }
}
