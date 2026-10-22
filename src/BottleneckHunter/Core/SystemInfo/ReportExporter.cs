using System.IO;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace BottleneckHunter.Core.SystemInfo;

/// <summary>Gera o relatório do PC em JSON (para outros programas) ou HTML (para abrir no navegador e mandar a alguém).</summary>
public static class ReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static string ToJson(SystemReport r) => JsonSerializer.Serialize(r, JsonOptions);

    public static void Save(SystemReport r, string path, bool json) =>
        File.WriteAllText(path, json ? ToJson(r) : ToHtml(r), new UTF8Encoding(false));

    public static string ToHtml(SystemReport r)
    {
        // Todo texto vindo do PC (nomes de peças, rótulos de volume) passa por Encode:
        // um nome malicioso não pode virar código no navegador.
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        static string Cls(Level l) => l.ToString().ToLowerInvariant();

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
          .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">")
          .Append($"<title>Relatório do PC: {E(r.ComputerName)}</title><style>{Css}</style></head><body><main>");

        sb.Append("<header><div class=\"logo\">BH</div><div><h1>Relatório do PC</h1>")
          .Append($"<p class=\"dim\">{E(r.ComputerName)} · gerado em {r.CreatedAt.ToString("dd/MM/yyyy 'às' HH:mm", Fmt.Br)} pelo Bottleneck Hunter {E(r.AppVersion)}</p></div></header>");

        sb.Append("<section class=\"card\"><h2>Resumo</h2><pre>").Append(E(ReportSections.Summary(r))).Append("</pre></section>");

        if (r.Findings.Count > 0)
        {
            sb.Append("<section class=\"card\"><h2>O que merece atenção</h2><ul class=\"findings\">");
            foreach (var f in r.Findings)
            {
                sb.Append($"<li class=\"{Cls(f.Level)}\"><span class=\"dot\"></span><div><b>{E(f.Title)}</b><p>{E(f.Detail)}</p>");
                if (f.HowToFix != null) sb.Append($"<p class=\"fix\"><b>Como resolver:</b> {E(f.HowToFix)}</p>");
                sb.Append("</div></li>");
            }
            sb.Append("</ul></section>");
        }

        sb.Append("<div class=\"grid\">");
        foreach (var s in ReportSections.All(r))
        {
            sb.Append($"<section class=\"card\"><h2>{E(s.Title)}</h2><p class=\"headline\">{E(s.Headline)}</p><table>");
            foreach (var row in s.Rows) sb.Append($"<tr><th>{E(row.Label)}</th><td>{E(row.Value)}</td></tr>");
            sb.Append($"</table><p class=\"explain\">{E(s.Explain)}</p></section>");
        }
        sb.Append("</div>");

        if (r.Memory.Modules.Count > 0)
        {
            sb.Append("<section class=\"card\"><h2>Pentes de memória</h2><table class=\"wide\"><tr><th>Slot</th><th>Tamanho</th><th>Tipo</th><th>Velocidade</th><th>Fabricante</th><th>Modelo</th></tr>");
            foreach (var m in r.Memory.Modules)
                sb.Append($"<tr><td>{E(Fmt.Or(m.Slot))}</td><td>{E(Fmt.Ram(m.SizeGb))}</td><td>{E(m.Type)}</td><td>{m.ConfiguredSpeedMts} MT/s</td><td>{E(Fmt.Or(m.Maker))}</td><td>{E(Fmt.Or(m.PartNumber))}</td></tr>");
            sb.Append("</table></section>");
        }

        foreach (var d in r.Disks)
        {
            sb.Append($"<section class=\"card\"><h2>Disco {d.Number}{(d.HoldsWindows ? " · Windows" : "")}</h2><p class=\"headline\">{E(d.Model)}</p><table>");
            foreach (var row in ReportSections.DiskRows(d)) sb.Append($"<tr><th>{E(row.Label)}</th><td>{E(row.Value)}</td></tr>");
            sb.Append("</table>");
            foreach (var v in d.Volumes.Where(v => v.TotalGb > 0))
            {
                var lvl = v.UsedPercent >= 90 ? "bad" : v.UsedPercent >= 80 ? "warn" : "good";
                sb.Append($"<div class=\"vol\"><span>{E(v.Letter)} {E(v.Label)}</span><span class=\"dim\">{E(Fmt.Gb(v.FreeGb))} livres de {E(Fmt.Gb(v.TotalGb))}</span></div>")
                  .Append($"<div class=\"bar\"><i class=\"{lvl}\" style=\"width:{Math.Clamp(v.UsedPercent, 0, 100).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}%\"></i></div>");
            }
            if (d.Smart.Count > 0)
            {
                sb.Append("<details><summary>Atributos S.M.A.R.T.</summary><table class=\"wide\"><tr><th>ID</th><th>Atributo</th><th>Atual</th><th>Pior</th><th>Limite</th><th>Bruto</th></tr>");
                foreach (var a in d.Smart)
                    sb.Append($"<tr class=\"{Cls(a.Level)}\" title=\"{E(a.Explain)}\"><td>{a.Id}</td><td>{E(a.Name)}</td><td>{a.Current}</td><td>{a.Worst}</td><td>{a.Threshold?.ToString() ?? "—"}</td><td>{a.Raw}</td></tr>");
                sb.Append("</table></details>");
            }
            sb.Append($"<p class=\"explain\">{E(ReportSections.DiskExplain)}</p></section>");
        }

        if (r.Errors.Count > 0)
            sb.Append("<section class=\"card\"><h2>Não foi possível ler</h2><ul>")
              .Append(string.Concat(r.Errors.Select(e => $"<li class=\"dim\">{E(e)}</li>")))
              .Append("</ul></section>");

        sb.Append("<footer class=\"dim\">Números de série não são incluídos neste relatório.</footer></main></body></html>");
        return sb.ToString();
    }

    private const string Css = """
        :root{--bg:#f4f5f7;--card:#fff;--text:#1b1e24;--dim:#5d6573;--border:#e2e5ea;--accent:#ff7a1a;--good:#1fa463;--warn:#d99a00;--bad:#e0344f;--track:#e8eaee}
        @media (prefers-color-scheme:dark){:root{--bg:#0e0f12;--card:#1a1d23;--text:#f2f3f5;--dim:#9aa1ad;--border:#2a2f38;--track:#262a32;--good:#3ddc84;--warn:#ffc93c;--bad:#ff5470}}
        *{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:15px/1.5 "Segoe UI",system-ui,sans-serif}
        main{max-width:1100px;margin:0 auto;padding:32px 16px}
        header{display:flex;gap:16px;align-items:center;margin-bottom:20px}.logo{width:52px;height:52px;border-radius:14px;background:var(--accent);color:#fff;font-weight:800;display:grid;place-items:center;font-size:20px}
        h1{margin:0;font-size:28px}h2{margin:0 0 6px;font-size:12px;letter-spacing:.06em;color:var(--dim);text-transform:uppercase}
        .dim{color:var(--dim)}.card{background:var(--card);border:1px solid var(--border);border-radius:14px;padding:18px;margin-bottom:14px;overflow-x:auto}
        .grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:14px}.grid .card{margin:0}.grid{margin-bottom:14px}
        .headline{font-size:18px;font-weight:600;margin:0 0 10px}.explain{color:var(--dim);font-size:13px;margin:10px 0 0;border-left:3px solid var(--accent);padding-left:10px}
        table{border-collapse:collapse;width:100%}th,td{text-align:left;padding:5px 8px 5px 0;border-bottom:1px solid var(--border);vertical-align:top}th{color:var(--dim);font-weight:500;width:42%}
        table.wide th{width:auto}pre{white-space:pre-wrap;margin:0;font:13px/1.6 Consolas,monospace}
        .findings{list-style:none;margin:0;padding:0}.findings li{display:flex;gap:12px;padding:10px 0;border-bottom:1px solid var(--border)}.findings p{margin:2px 0}.fix{color:var(--dim)}
        .dot{flex:none;width:12px;height:12px;border-radius:50%;margin-top:6px;background:var(--accent)}.good .dot{background:var(--good)}.warn .dot{background:var(--warn)}.bad .dot{background:var(--bad)}
        tr.bad td{color:var(--bad)}tr.warn td{color:var(--warn)}
        .vol{display:flex;justify-content:space-between;margin-top:12px;font-size:13px}.bar{height:8px;border-radius:4px;background:var(--track);overflow:hidden;margin-top:4px}.bar i{display:block;height:100%;background:var(--good)}.bar i.warn{background:var(--warn)}.bar i.bad{background:var(--bad)}
        details{margin-top:12px}summary{cursor:pointer;color:var(--accent);font-weight:600}footer{text-align:center;font-size:12px;margin-top:20px}
        """;
}
