using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace BottleneckHunter.Core.Bench;

/// <summary>
/// Desenha o fractal 3D sem parar numa janela (HWND), numa thread própria, com Direct3D 11.
/// É o motor do benchmark e do stress de GPU. Detecta quando o driver de vídeo trava e reinicia
/// (o Windows chama isso de TDR), um sinal clássico de GPU instável ou fonte fraca.
/// </summary>
public sealed class GpuRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Params
    {
        public float ResX, ResY, Time, Steps;
    }

    private const int Strips = 8;
    private readonly IntPtr _hwnd;
    private readonly int _width, _height;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private long _frames;

    /// <summary>Passos do raio por pixel. Mais passos = mais trabalho por quadro.</summary>
    public int Steps { get; }
    public string AdapterName { get; private set; } = "";
    public bool Running { get; private set; }
    /// <summary>Verdadeiro quando o driver da GPU travou durante o teste.</summary>
    public bool DeviceLost { get; private set; }
    public string? Error { get; private set; }
    public double Fps { get; private set; }
    public long Frames => Interlocked.Read(ref _frames);
    public TimeSpan Elapsed => _clock.Elapsed;
    private readonly Stopwatch _clock = new();

    /// <summary>Chamado (em outra thread) quando a GPU está pronta ou quando algo dá errado.</summary>
    public event Action? Started;
    public event Action<string>? Failed;

    public GpuRenderer(IntPtr hwnd, int width, int height, int steps)
    {
        _hwnd = hwnd;
        _width = width;
        _height = height;
        Steps = steps;
    }

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "GpuStress" };
        _thread.Start();
    }

    public void Stop()
    {
        _cts.Cancel();
        if (_thread != null && _thread != Thread.CurrentThread) _thread.Join(3000);
    }

    public void Dispose() => Stop();

    private static IDXGIAdapter1? BestAdapter(IDXGIFactory2 factory)
    {
        // Preferimos a placa com mais memória dedicada (a dedicada em vez da integrada, nos notebooks).
        IDXGIAdapter1? best = null;
        ulong bestMem = 0;
        for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
        {
            var d = adapter.Description1;
            ulong mem = (ulong)d.DedicatedVideoMemory;
            if ((d.Flags & AdapterFlags.Software) == 0 && (best == null || mem > bestMem))
            {
                best?.Dispose();
                best = adapter;
                bestMem = mem;
            }
            else
            {
                adapter.Dispose();
            }
        }
        return best;
    }

    private void Loop()
    {
        IDXGIFactory2? factory = null;
        IDXGIAdapter1? adapter = null;
        ID3D11Device? device = null;
        ID3D11DeviceContext? ctx = null;
        IDXGISwapChain1? swap = null;
        ID3D11Texture2D? back = null;
        ID3D11RenderTargetView? rtv = null;
        ID3D11VertexShader? vs = null;
        ID3D11PixelShader? ps = null;
        ID3D11Buffer? cb = null;
        ID3D11RasterizerState? rs = null;

        try
        {
            SystemDlls.Register();
            factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
            adapter = BestAdapter(factory);
            AdapterName = adapter?.Description1.Description.Trim() ?? "";

            var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
            D3D11.D3D11CreateDevice(adapter, adapter == null ? DriverType.Hardware : DriverType.Unknown,
                DeviceCreationFlags.None, levels, out device, out ctx).CheckError();

            var desc = new SwapChainDescription1(
                (uint)_width, (uint)_height, Format.B8G8R8A8_UNorm, false,
                Usage.RenderTargetOutput, 2, Scaling.Stretch, SwapEffect.FlipDiscard, AlphaMode.Ignore, SwapChainFlags.None);
            swap = factory.CreateSwapChainForHwnd(device!, _hwnd, desc, null, null);
            factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAltEnter);

            back = swap.GetBuffer<ID3D11Texture2D>(0);
            rtv = device!.CreateRenderTargetView(back, null);

            var vsCode = Compiler.Compile(GpuShader.Source, "VS", "gpu-stress", "vs_4_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
            var psCode = Compiler.Compile(GpuShader.Source, "PS", "gpu-stress", "ps_4_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
            vs = device.CreateVertexShader(vsCode.Span, null);
            ps = device.CreatePixelShader(psCode.Span, null);
            cb = device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0), (SubresourceData?)null);

            ctx!.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.VSSetShader(vs);
            ctx.PSSetShader(ps);
            ctx.PSSetConstantBuffer(0, cb);
            ctx.RSSetViewport(new Viewport(0, 0, _width, _height));
            rs = device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { ScissorEnable = true });
            ctx.RSSetState(rs);

            Running = true;
            _clock.Start();
            Started?.Invoke();

            long lastFrames = 0;
            double lastT = 0;
            while (!_cts.IsCancellationRequested)
            {
                var p = new Params { ResX = _width, ResY = _height, Time = (float)_clock.Elapsed.TotalSeconds, Steps = Steps };
                ctx.UpdateSubresource(in p, cb);
                ctx.OMSetRenderTargets(rtv, null);

                // O quadro é desenhado em faixas, cada uma enviada separadamente. Assim, numa placa fraca,
                // nenhum pedaço de trabalho passa dos 2 s que o Windows espera antes de achar que a GPU travou.
                for (int strip = 0; strip < Strips; strip++)
                {
                    int top = _height * strip / Strips, bottom = _height * (strip + 1) / Strips;
                    ctx.RSSetScissorRect(new Vortice.RawRect(0, top, _width, bottom));
                    ctx.Draw(3, 0);
                    ctx.Flush();
                }

                var r = swap.Present(0, PresentFlags.None);
                if (r.Failure)
                {
                    if (r == Vortice.DXGI.ResultCode.DeviceRemoved || r == Vortice.DXGI.ResultCode.DeviceReset || r == Vortice.DXGI.ResultCode.DeviceHung)
                    {
                        DeviceLost = true;
                        Error = "O driver da placa de vídeo travou e foi reiniciado pelo Windows.";
                    }
                    else
                    {
                        Error = $"A GPU parou de responder (código 0x{r.Code:X8}).";
                    }
                    Failed?.Invoke(Error);
                    break;
                }

                long f = Interlocked.Increment(ref _frames);
                double now = _clock.Elapsed.TotalSeconds;
                if (now - lastT >= 0.5)
                {
                    Fps = (f - lastFrames) / (now - lastT);
                    lastFrames = f;
                    lastT = now;
                }
            }
        }
        catch (Exception ex)
        {
            Error = DescribeError(ex, device);
            if (device != null && device.DeviceRemovedReason.Failure) DeviceLost = true;
            Failed?.Invoke(Error);
        }
        finally
        {
            Running = false;
            _clock.Stop();
            ctx?.ClearState();
            ctx?.Flush();
            rs?.Dispose();
            cb?.Dispose();
            ps?.Dispose();
            vs?.Dispose();
            rtv?.Dispose();
            back?.Dispose();
            swap?.Dispose();
            ctx?.Dispose();
            device?.Dispose();
            adapter?.Dispose();
            factory?.Dispose();
        }
    }

    private static string DescribeError(Exception ex, ID3D11Device? device)
    {
        if (device != null && device.DeviceRemovedReason.Failure)
            return "O driver da placa de vídeo travou e foi reiniciado pelo Windows.";
        if (ex is DllNotFoundException)
            return "Não encontramos as bibliotecas do DirectX no Windows (d3d11.dll / d3dcompiler_47.dll).";
        return "Não foi possível iniciar o Direct3D 11: " + ex.Message;
    }
}
