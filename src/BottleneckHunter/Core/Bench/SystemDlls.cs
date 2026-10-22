using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BottleneckHunter.Core.Bench;

/// <summary>
/// O app roda como administrador, então as DLLs do DirectX são carregadas somente de C:\Windows\System32
/// (caminho absoluto), nunca da pasta do app, da pasta atual ou do PATH, onde alguém poderia plantar uma cópia falsa.
/// </summary>
public static class SystemDlls
{
    private static readonly string[] Allowed = { "d3d11.dll", "dxgi.dll", "d3dcompiler_47.dll" };
    private static bool _done;

    public static void Register()
    {
        if (_done) return;
        _done = true;
        foreach (var asm in new[]
                 {
                     typeof(Vortice.Direct3D11.D3D11).Assembly,
                     typeof(Vortice.DXGI.DXGI).Assembly,
                     typeof(Vortice.D3DCompiler.Compiler).Assembly,
                 })
        {
            try
            {
                NativeLibrary.SetDllImportResolver(asm, Resolve);
            }
            catch (InvalidOperationException)
            {
                // Já havia um resolvedor registrado para este assembly.
            }
        }
    }

    private static IntPtr Resolve(string name, Assembly asm, DllImportSearchPath? path)
    {
        string file = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll";
        if (!Allowed.Contains(file, StringComparer.OrdinalIgnoreCase))
            return IntPtr.Zero; // Outras bibliotecas seguem a regra padrão do .NET.

        string full = Path.Combine(Environment.SystemDirectory, file);
        return NativeLibrary.Load(full);
    }
}
