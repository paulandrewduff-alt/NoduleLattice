// ============================================================================
// FILE: NoduleLattice.Blazor/Components/Network/Network3D.razor.cs
// PURPOSE:
//   - Cache-bust JS import so renderer updates always take effect.
//   - Always push View changes even if step didn't move.
// ============================================================================

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using NoduleLattice.Blazor.Models;

namespace NoduleLattice.Blazor.Components.Network;

public partial class Network3D : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    // Increment this when you change wwwroot/js/network3d.js
    private const string JsVersion = "v=3";

    private IJSObjectReference? _module;
    private IJSObjectReference? _instance;

    private long _lastStep = -1;
    private string? _initError;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_initError is not null) return;

        if (firstRender)
        {
            try
            {
                _module = await JS.InvokeAsync<IJSObjectReference>("import", $"/js/network3d.js?{JsVersion}");
                _instance = await _module.InvokeAsync<IJSObjectReference>("createNetwork3D", _host);
            }
            catch (JSException ex)
            {
                _initError = $"JS init failed: {ex.Message}";
                Console.Error.WriteLine($"[Network3D] {_initError}");
                return;
            }
            catch (Exception ex)
            {
                _initError = $"JS init failed: {ex.GetType().Name}: {ex.Message}";
                Console.Error.WriteLine($"[Network3D] {_initError}\n{ex}");
                return;
            }
        }

        if (_module is null || _instance is null) return;
        if (Snapshot is null) return;

        var step = Snapshot.StepIndex;
        var view = View ?? new NetworkViewOptions();

        // If only view changed, we still want to repaint.
        if (step == _lastStep && View is null)
            return;

        _lastStep = step;

        try
        {
            await _module.InvokeVoidAsync("updateNetwork3D", _instance, Snapshot, view);
        }
        catch (JSDisconnectedException) { }
        catch (JSException ex)
        {
            Console.Error.WriteLine($"[Network3D] JS update failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Network3D] Update failed: {ex}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null && _instance is not null)
                await _module.InvokeVoidAsync("disposeNetwork3D", _instance);
        }
        catch { }

        try { if (_instance is not null) await _instance.DisposeAsync(); } catch { }
        try { if (_module is not null) await _module.DisposeAsync(); } catch { }
    }
}
