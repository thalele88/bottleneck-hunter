namespace BottleneckHunter.Core.Bench;

/// <summary>
/// Shader do teste de GPU: desenha um fractal 3D (Mandelbulb) por "ray marching", com sombras suaves,
/// oclusão de ambiente e brilho. Cada pixel faz milhares de contas, então a placa de vídeo trabalha no máximo.
/// </summary>
internal static class GpuShader
{
    public const string Source = """
cbuffer Params : register(b0)
{
    float2 Res;
    float Time;
    float Steps;
};

struct VSOut
{
    float4 pos : SV_Position;
    float2 uv : TEXCOORD0;
};

// Um triângulo gigante que cobre a tela toda, sem precisar de vértices na memória.
VSOut VS(uint id : SV_VertexID)
{
    VSOut o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    o.uv = uv;
    return o;
}

float Power() { return 8.0 + 2.0 * sin(Time * 0.21); }

// Distância estimada até a superfície do fractal. trap guarda a "cor" do ponto.
float Distance(float3 p, out float trap)
{
    float3 z = p;
    float dr = 1.0;
    float r = 0.0;
    float n = Power();
    trap = 1e10;
    [loop] for (int i = 0; i < 9; i++)
    {
        r = length(z);
        if (r > 2.0) break;
        float theta = acos(clamp(z.z / r, -1.0, 1.0)) * n;
        float phi = atan2(z.y, z.x) * n;
        dr = pow(r, n - 1.0) * n * dr + 1.0;
        float zr = pow(r, n);
        z = zr * float3(sin(theta) * cos(phi), sin(theta) * sin(phi), cos(theta)) + p;
        trap = min(trap, dot(z, z));
    }
    return 0.5 * log(max(r, 1e-6)) * r / dr;
}

float Distance(float3 p) { float t; return Distance(p, t); }

float3 Normal(float3 p)
{
    const float2 k = float2(1, -1);
    const float h = 0.0006;
    return normalize(k.xyy * Distance(p + k.xyy * h) + k.yyx * Distance(p + k.yyx * h) +
                     k.yxy * Distance(p + k.yxy * h) + k.xxx * Distance(p + k.xxx * h));
}

float SoftShadow(float3 ro, float3 rd)
{
    float res = 1.0;
    float t = 0.01;
    [loop] for (int i = 0; i < 48; i++)
    {
        float d = Distance(ro + rd * t);
        res = min(res, 10.0 * d / t);
        t += clamp(d, 0.005, 0.2);
        if (res < 0.01 || t > 3.0) break;
    }
    return saturate(res);
}

float Occlusion(float3 p, float3 n)
{
    float occ = 0.0;
    float w = 1.0;
    [loop] for (int i = 1; i <= 6; i++)
    {
        float h = 0.02 * i;
        occ += (h - Distance(p + n * h)) * w;
        w *= 0.7;
    }
    return saturate(1.0 - 4.0 * occ);
}

float4 PS(VSOut input) : SV_Target
{
    float2 uv = (input.uv * Res - 0.5 * Res) / Res.y;
    uv.y = -uv.y;

    float a = Time * 0.15;
    float3 ro = float3(3.1 * cos(a), 1.1 * sin(Time * 0.11), 3.1 * sin(a));
    float3 fw = normalize(-ro);
    float3 rt = normalize(cross(float3(0, 1, 0), fw));
    float3 up = cross(fw, rt);
    float3 rd = normalize(uv.x * rt + uv.y * up + 1.7 * fw);

    float t = 0.0;
    float trap = 0.0;
    float glow = 0.0;
    bool hit = false;
    int steps = (int)Steps;
    [loop] for (int i = 0; i < steps; i++)
    {
        float d = Distance(ro + rd * t, trap);
        glow += 0.012 / (0.02 + d * d * 40.0);
        if (d < 0.0004 * t) { hit = true; break; }
        t += d * 0.9;
        if (t > 7.0) break;
    }

    float3 sky = lerp(float3(0.05, 0.06, 0.09), float3(0.16, 0.08, 0.03), saturate(uv.y + 0.6));
    float3 col = sky + glow * float3(1.0, 0.48, 0.1) * 0.08;

    if (hit)
    {
        float3 p = ro + rd * t;
        float3 n = Normal(p);
        float3 light = normalize(float3(0.6, 0.8, -0.4));
        float diff = saturate(dot(n, light)) * SoftShadow(p + n * 0.002, light);
        float ao = Occlusion(p, n);
        float spec = pow(saturate(dot(reflect(rd, n), light)), 24.0);
        float3 baseCol = lerp(float3(1.0, 0.48, 0.1), float3(0.3, 0.65, 1.0), saturate((sqrt(trap) - 0.8) * 3.5));
        col = baseCol * (0.15 * ao + 0.9 * diff) + spec * 0.6 * diff + float3(0.05, 0.07, 0.1) * ao;
    }

    // Vinheta e correção de gama.
    col *= 1.0 - 0.35 * dot(uv, uv);
    return float4(pow(saturate(col), 0.4545), 1.0);
}
""";
}
