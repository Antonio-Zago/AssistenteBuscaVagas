using AssistenteBuscaVagas.AgenteColeta.Configuracao;
using Microsoft.Extensions.Options;

namespace AssistenteBuscaVagas.AgenteColeta.Agente;

/// <summary>
/// Controla os créditos do Firecrawl gastos no dia (UTC). É código, não prompt:
/// o LLM pode pedir mais buscas, mas não consegue gastar além do limite.
/// Fica em memória; se o serviço reiniciar, o contador zera (aceitável para estudo:
/// o limite mensal real continua no painel do Firecrawl).
/// </summary>
public sealed class OrcamentoDiario(IOptions<ColetaOptions> options, TimeProvider relogio)
{
    private readonly Lock _trava = new();
    private DateOnly _dia;
    private int _gastos;

    public int Limite => options.Value.Orcamento.CreditosPorDia;

    public int Restantes
    {
        get { lock (_trava) { VirarDia(); return Math.Max(0, Limite - _gastos); } }
    }

    /// <summary>Reserva créditos antes da chamada. Devolve false se não houver saldo.</summary>
    public bool TentarGastar(int creditos)
    {
        lock (_trava)
        {
            VirarDia();
            if (_gastos + creditos > Limite) return false;
            _gastos += creditos;
            return true;
        }
    }

    private void VirarDia()
    {
        var hoje = DateOnly.FromDateTime(relogio.GetUtcNow().UtcDateTime);
        if (hoje != _dia) { _dia = hoje; _gastos = 0; }
    }
}
